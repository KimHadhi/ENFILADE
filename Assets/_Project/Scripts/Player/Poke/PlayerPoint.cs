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
    private Animator armed;
    private bool held, mirroredPointing, mirroredNoPoke;

    /// <summary>This player points now: their held F on their own PC, their pose's mark on every other.</summary>
    public bool Pointing => movement != null && (movement.IsNetworkDriven ? mirroredPointing : held);

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

    /// <summary>The marks this PC's player's pose carries for the point (MovementMirror.Capture).</summary>
    internal static PlayerPose.Marks MarksOf(PlayerMovement who)
    {
        var marks = PlayerPose.Marks.None;
        if (who != null && who.TryGetComponent(out PlayerPoint point) && point.held) marks |= PlayerPose.Marks.Pointing;
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
    /// climbing, the gun not aimed, reloaded, drawn or looked over, the fists not guarding or winding up. A shot or a
    /// punch ends a point outright (PlayerPing), so the hand never has to choose.
    /// </summary>
    internal static bool CanPoint(PlayerMovement who, WeaponController weapon)
    {
        if (who == null || !who.IsInitialized || who.IsDead || who.IsDowned || who.IsHeld || who.IsParkour || who.CombatInputLocked)
            return false;
        var state = who.CurrentState;
        if (state != MovementState.Standing && state != MovementState.Crouching) return false;
        if (weapon != null && (weapon.AimBlend > .3f || weapon.IsSwitching || weapon.IsInspecting
            || (weapon.Runtime != null && weapon.Runtime.IsReloading))) return false;
        var melee = who.MeleeCombat;
        return melee == null || !melee.IsActive || !(melee.IsGuarding || melee.IsCharging || melee.IsGuardBreaking);
    }
}
