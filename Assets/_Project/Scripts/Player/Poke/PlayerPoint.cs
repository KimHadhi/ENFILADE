using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Holding F to point (the user, 2026-11-12: "F가 핑이잖아? 핑 찍을 때 남는 손으로 손가락질 하는 모션 있고 F 꾹 누르고
/// 있으면 GTA5에서 무슨 키 누르고 있으면 계속 손가락질할 수 있잖아? 그런 것처럼"): whether a player points now, and
/// whether they let friends poke them. On this PC's player PlayerPing holds it while F is held past Poke.csv
/// Point.holdAfter with nothing else for F to do (a tap stays a ping); on every other PC their pose's two marks say it
/// (PlayerPose.Marks Pointing and NoPoke, MovementMirror), and the way they point is the look the pose already carries,
/// so a point costs the network nothing more. The arm itself is PointArm, on whatever body they wear.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerPoint : MonoBehaviour
{
    private PlayerMovement movement;
    private PlayerBody body;
    private WeaponController weapon;
    private Animator armed;
    // pinned: the console's /point keeps this PC's player pointing without F, to look at the hand while tuning it.
    private bool held, pinned, mirroredPointing, mirroredNoPoke;
    // A tap's ping points for a moment (Flash): until when, and at what (the pinged foe, the spot on it).
    private float flashUntil = float.NegativeInfinity;
    private Transform flashAt;
    private Vector3 flashLocal;

    /// <summary>
    /// This player points now: their held F (or /point, or a ping's moment) on their own PC, their pose's mark on every
    /// other.
    /// </summary>
    public bool Pointing => movement != null && (movement.IsNetworkDriven ? mirroredPointing : held || Pinned || Flashing);

    // /point holds while the hand is free (it rests through aiming, a reload, a throw and comes back after).
    private bool Pinned => pinned && HandFree;

    // A ping's moment, while the hand stays free for it.
    private bool Flashing => Time.time < flashUntil && HandFree;

    private bool HandFree
    {
        get
        {
            if (weapon == null) weapon = GetComponent<WeaponController>();
            return CanPoint(movement, weapon);
        }
    }

    /// <summary>
    /// An F tap's ping points at what it pinged for Poke.csv Ping.point seconds (the user, 2026-11-12: "적 핑 찍을 때
    /// 가리키는 애니메이션이 있었으면"): the same drawn hand as a held F, at <paramref name="point"/> on
    /// <paramref name="at"/> as it moves; friends see it through the pose's point mark. False when the hand isn't free
    /// (aiming, a reload, a throw, busy fists) or Ping.point is 0: then the gun's nod stands in, as before.
    /// </summary>
    internal bool Flash(Transform at, Vector3 point)
    {
        float seconds = PokeTables.Number("Ping.point", .6f);
        if (seconds <= 0f || !HandFree) return false;
        flashUntil = Time.time + seconds;
        flashAt = at;
        flashLocal = at != null ? at.InverseTransformPoint(point) : point;
        return true;
    }

    /// <summary>
    /// Where a ping's point aims on this PC's player while it lasts and nothing else holds the point (a held F or /point
    /// aim where the player looks): the pinged spot, following the foe as it moves.
    /// </summary>
    internal bool FlashTarget(out Vector3 point)
    {
        point = default;
        if (movement == null || movement.IsNetworkDriven || held || pinned || Time.time >= flashUntil) return false;
        if (flashAt == null) return false;
        point = flashAt.TransformPoint(flashLocal);
        return true;
    }

    /// <summary>They let friends poke them: their own setting on their PC, their pose's mark on every other.</summary>
    public bool Pokable => movement == null || (movement.IsNetworkDriven ? !mirroredNoPoke : GameSettings.Current.letOthersPoke);

    private void Awake() => movement = GetComponent<PlayerMovement>();

    // A body that points gets the arm that does it (PointArm), on whatever model it wears now; one that never points
    // never has one.
    private void Update()
    {
        if (!Pointing) return;
        if (body == null && !TryGetComponent(out body)) return;
        var model = body.BodyAnimator;
        if (model == null || model == armed) return;
        armed = model;
        if (!model.TryGetComponent(out PointArm _)) model.gameObject.AddComponent<PointArm>();
    }

    /// <summary>This PC's player starts or stops pointing (PlayerPing).</summary>
    internal void Hold(bool pointing) => held = pointing;

    /// <summary>
    /// The console's /point: this PC's player keeps pointing without F held (friends see it too), so the hand can be looked
    /// at, from F5 as well, while Poke.csv's Point.* numbers are changed and saved: the running game reads them again at
    /// once (GameTablesWatcher). Again lets go. Returns what the console prints.
    /// </summary>
    internal static string Command(string args)
    {
        var who = PlayerRoster.Local;
        if (who == null) return Texts.Get("point.pinned.none");
        if (!who.TryGetComponent(out PlayerPoint point)) point = who.gameObject.AddComponent<PlayerPoint>();
        point.pinned = !point.pinned;
        return Texts.Get(point.pinned ? "point.pinned.on" : "point.pinned.off");
    }

    /// <summary>The marks this PC's player's pose carries for the point (MovementMirror.Capture).</summary>
    internal static PlayerPose.Marks MarksOf(PlayerMovement who)
    {
        var marks = PlayerPose.Marks.None;
        if (who != null && who.TryGetComponent(out PlayerPoint point) && point.Pointing) marks |= PlayerPose.Marks.Pointing;
        if (!GameSettings.Current.letOthersPoke) marks |= PlayerPose.Marks.NoPoke;
        return marks;
    }

    /// <summary>Another PC's player as their pose says (MovementMirror.Apply, every frame).</summary>
    internal static void Mirror(PlayerMovement who, bool pointing, bool noPoke)
    {
        if (who == null) return;
        if (!who.TryGetComponent(out PlayerPoint point))
        {
            if (!pointing && !noPoke) return;
            point = who.gameObject.AddComponent<PlayerPoint>();
        }
        point.mirroredPointing = pointing;
        point.mirroredNoPoke = noPoke;
    }

    /// <summary>
    /// This PC's player's hand is free to point: alive, up, standing or crouched (walking included), not held or busy
    /// climbing, the gun not aimed, reloaded, drawn or looked over, the off hand not on a G throw, the fists not guarding
    /// or winding up. A shot or a punch ends a point outright (PlayerPing), so the hand never has to choose.
    /// </summary>
    internal static bool CanPoint(PlayerMovement who, WeaponController weapon)
    {
        if (who == null || !who.IsInitialized || who.IsDead || who.IsDowned || who.IsHeld || who.IsParkour || who.CombatInputLocked)
            return false;
        var state = who.CurrentState;
        if (state != MovementState.Standing && state != MovementState.Crouching) return false;
        if (weapon != null && (weapon.AimBlend > .3f || weapon.IsSwitching || weapon.IsInspecting
            || (weapon.Runtime != null && weapon.Runtime.IsReloading))) return false;
        // A G throw has the off hand first (the manager, 2026-11-12): it ends a point like a shot does.
        if (PlayerGrenades.HandBusy(who)) return false;
        var melee = who.MeleeCombat;
        return melee == null || !melee.IsActive || !(melee.IsGuarding || melee.IsCharging || melee.IsGuardBreaking);
    }
}
