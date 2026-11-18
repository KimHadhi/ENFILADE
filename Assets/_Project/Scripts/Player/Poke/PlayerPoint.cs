using UnityEngine;

/// <summary>
/// Holding F to point (the user, 2026-11-12: "F가 핑이잖아? 핑 찍을 때 남는 손으로 손가락질 하는 모션 있고 F 꾹 누르고
/// 있으면 GTA5에서 무슨 키 누르고 있으면 계속 손가락질할 수 있잖아? 그런 것처럼"): whether a player points now, and
/// whether they let friends poke them. On this PC's player PlayerPing holds it while F is held past Poke.csv
/// Point.holdAfter with nothing else for F to do (a tap stays a ping); on every other PC their pose's marks say it
/// (PlayerPose.Marks Pointing and NoPoke, MovementMirror), and the way they point is the look the pose already carries,
/// so a point costs the network nothing more. A tap's ping points at what it pinged instead: their PC names the foe and
/// the spot on it with the ping (PlayerNetworkCombat, Pinged), and the marks PingPoint and PointAway say that the point is
/// a ping's and that turning away cut it short, so a friend's copy points at the same foe and drops at the same moment.
/// The arm itself is PointArm, on whatever body they wear.
/// <para>
/// Whether the left hand is free to point is not worked out here: the point asks the hands' owner for it every frame
/// it would point (HandOwner; Tables/Player/HandUses.csv point.hold for a held F, point.ping for a ping's moment, which
/// the left hand may give while the right aims), on this PC's player and on a friend's copy alike. Until 2026-11-18 a
/// list here (CanPoint) asked the body, the gun and the grenade one by one, and knew nothing of a syringe or a tool.
/// </para>
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerPoint : MonoBehaviour
{
    // How much longer than its drop (Point.turnOut) a ping's point that turning away cut short stays marked on the pose
    // (s): a few of the pose's network ticks, so a friend's copy sees the mark however short the drop is, and still has it
    // at the end of its own drop, however late the mark came.
    private const float AwayMarked = .2f;

    private PlayerMovement movement;
    private PlayerBody body;
    private WeaponController weapon;
    private HandOwner hands;
    private Animator armed;
    // The use the left hand is this point's under now (HandUses.PointHold or PointPing); null: it does not point.
    private string row;
    // pinned: the console's /point keeps this PC's player pointing without F, to look at the hand while tuning it.
    private bool held, pinned, mirroredPointing, mirroredNoPoke, mirroredPingPoint, mirroredAway;
    // A tap's ping points for a moment (Flash): until when, and at what (the pinged foe, the spot on it). On a friend's
    // copy, the foe and the spot their PC named (Pinged), and when it did.
    private float flashUntil = float.NegativeInfinity, pingedAt = float.NegativeInfinity;
    private Transform flashAt;
    private Vector3 flashLocal;
    // A ping's point that turning away cut short is going back until then (TurnAway).
    private float awayUntil = float.NegativeInfinity;

    /// <summary>
    /// This player points now: their held F (or /point, or a ping's moment) on their own PC, their pose's mark on every
    /// other.
    /// </summary>
    public bool Pointing => movement != null && (movement.IsNetworkDriven ? mirroredPointing : row != null);

    /// <summary>The use the left hand is this point's under now, for the arm that poses it (PointArm); null: none.</summary>
    internal string Row => row;

    /// <summary>F is held for a point on this PC's player (PlayerPing), whether or not the hand has been given yet.</summary>
    internal bool Held => held;

    /// <summary>
    /// A held point could have the left hand now: PlayerPing asks before it holds, and ends the hold when it no longer
    /// can (aiming, a reload, a draw, a throw, a syringe, a tool, the gun's bash: whatever outranks point.hold).
    /// </summary>
    internal bool CanHold => Free(HandUses.PointHold);

    // ---- The left hand (HandOwner) ----

    private HandOwner Hands => hands != null ? hands : hands = HandOwner.Of(movement);

    // The hand could be this point's under `use`: nothing stronger has it or wants it, the body is in a state the arm
    // can be up in, and the melee slot's own hands are at rest.
    private bool Free(string use) => movement != null && Hands != null && Hands.Could(use, this) && Hands.Stands(use) && !MeleeBusy();

    // The melee slot's own hands at work (a guard, a wind-up, a guard break with the fists or the knife out). Their
    // rows are not in the hands' table yet (they come with the melee slot's), so the point still asks the fists itself.
    private bool MeleeBusy()
    {
        var melee = movement.MeleeCombat;
        return melee != null && melee.IsActive && (melee.IsGuarding || melee.IsCharging || melee.IsGuardBreaking);
    }

    // What the hand would point for now. On this PC's player a held F or the console's /point (point.hold), else a
    // ping's moment (point.ping); on a friend's copy what their pose says, their PC having decided it.
    private string Wanted()
    {
        if (movement.IsNetworkDriven) return !mirroredPointing ? null : mirroredPingPoint ? HandUses.PointPing : HandUses.PointHold;
        return held || pinned ? HandUses.PointHold : Time.time < flashUntil ? HandUses.PointPing : null;
    }

    // The left hand asked for, or given back, as what the player does now says: every frame, and at once when a press
    // or a ping changes it. A no leaves the hand alone and is asked again the next frame, so /point rests through
    // aiming, a reload or a throw and comes back after, and a ping's moment comes back if there is any of it left.
    private void Ask()
    {
        if (movement == null || Hands == null) return;
        string want = Wanted();
        if (want != null && !movement.IsNetworkDriven && (!hands.Stands(want) || MeleeBusy())) want = null;
        if (want != null && hands.Take(want, this, false))
        {
            row = want;
            return;
        }
        if (row != null) hands.Give(this);
        row = null;
    }

    private void OnDisable()
    {
        if (row != null && hands != null) hands.Give(this);
        row = null;
    }

    /// <summary>
    /// An F tap's ping points at what it pinged for Poke.csv Ping.point seconds (the user, 2026-11-12: "적 핑 찍을 때
    /// 가리키는 애니메이션이 있었으면"): the same drawn hand as a held F, at <paramref name="point"/> on
    /// <paramref name="at"/> as it moves; friends see it through the pose's point mark. Aimed down the sights it is a
    /// quick tap of the left hand alone, Ping.pointAimed seconds, and the gun stays on the aim (the user, 2026-11-12: "줌
    /// 한 상태로 핑 찍으면 그 총 잠깐 애니메이션 나오는 게 너무 조준을 방해함. 짧게 만들거나 아니면 걍 왼손만 움직이게
    /// 해"). False when the hand isn't free (a reload, a draw, a throw, busy fists) or the time is 0: then nothing gestures
    /// and the mark and its sound say it alone; the gun's nod that stood in before is gone.
    /// </summary>
    internal bool Flash(Transform at, Vector3 point)
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        bool aimed = weapon != null && weapon.AimBlend > AimedAt;
        float seconds = aimed ? PokeTables.Number("Ping.pointAimed", .35f) : PokeTables.Number("Ping.point", .6f);
        if (seconds <= 0f || !Free(HandUses.PointPing)) return false;
        flashUntil = Time.time + seconds;
        flashAt = at;
        flashLocal = at != null ? at.InverseTransformPoint(point) : point;
        Ask();
        return true;
    }

    /// <summary>A shot or a punch takes the hand back from a ping's moment at once (PlayerPing), as from a held point.</summary>
    internal void StopFlash()
    {
        flashUntil = float.NegativeInfinity;
        Ask();
    }

    /// <summary>
    /// Where a ping's point aims while it lasts and nothing else holds the point (a held F or /point aim where the player
    /// looks): the pinged spot, following the foe as it moves. On a friend's copy, while their pose says the point is a
    /// ping's, the spot their PC named (Pinged).
    /// </summary>
    internal bool PingSpot(out Vector3 point)
    {
        point = default;
        if (!AimsAtPing) return false;
        point = flashAt.TransformPoint(flashLocal);
        return true;
    }

    // The point aims at the ping: on this PC's player while its moment lasts and no held F or /point has the hand; on a
    // friend's copy while their pose says so and their PC named the foe lately (a ping's moment is under a second, so an
    // older name belongs to an earlier ping and the copy points along the look until the new one comes).
    private bool AimsAtPing
    {
        get
        {
            if (movement == null || flashAt == null) return false;
            if (movement.IsNetworkDriven)
                return mirroredPointing && mirroredPingPoint && Time.time - pingedAt <= PokeTables.Number("Ping.point", .6f) + 1f;
            return !held && !pinned && Time.time < flashUntil;
        }
    }

    /// <summary>
    /// The ping's point is over: the view turned so far from what it pinged that the shoulder can't carry the arm round to
    /// it (PointArm, on this PC's player; the user, 2026-11-14: "일정 각도 이상 화면(고개) 돌려서 꺾으면 빠르게 핑하던
    /// 손가락 끝내고 돌아오게 가능?"). The hand goes back over Poke.csv Point.turnOut from where it was, and the pose
    /// says so (PointAway), so friends' copies drop it the same way.
    /// </summary>
    internal void TurnAway()
    {
        if (movement == null || movement.IsNetworkDriven) return;
        flashUntil = float.NegativeInfinity;
        awayUntil = Time.time + Mathf.Max(0f, PokeTables.Number("Point.turnOut", .1f)) + AwayMarked;
    }

    /// <summary>
    /// A ping's point that turning away cut short is going back (PointArm drops it over Point.turnOut): on this PC's player
    /// since TurnAway, on a friend's copy as their pose says. Over as soon as the player points again.
    /// </summary>
    internal bool TurnedAway => movement != null && !Pointing && (movement.IsNetworkDriven ? mirroredAway : Time.time < awayUntil);

    /// <summary>
    /// A friend pinged this foe (PlayerNetworkCombat, on every PC but theirs), <paramref name="local"/> being the spot on
    /// it their hand points at, in the foe's own frame: their copy here points at that spot on this PC's copy of the foe
    /// while their pose says the point is a ping's (PlayerPose.Marks.PingPoint), as their own hand does.
    /// </summary>
    internal static void Pinged(PlayerMovement who, Component foe, Vector3 local)
    {
        if (who == null || foe == null || !who.IsNetworkDriven) return;
        if (!who.TryGetComponent(out PlayerPoint point)) point = who.gameObject.AddComponent<PlayerPoint>();
        point.flashAt = foe.transform;
        point.flashLocal = local;
        point.pingedAt = Time.time;
    }

    /// <summary>They let friends poke them: their own setting on their PC, their pose's mark on every other.</summary>
    public bool Pokable => movement == null || (movement.IsNetworkDriven ? !mirroredNoPoke : GameSettings.Current.letOthersPoke);

    private void Awake() => movement = GetComponent<PlayerMovement>();

    // A body that points gets the arm that does it (PointArm), on whatever model it wears now; one that never points
    // never has one.
    private void Update()
    {
        Ask();
        if (!Pointing) return;
        if (body == null && !TryGetComponent(out body)) return;
        var model = body.BodyAnimator;
        if (model == null || model == armed) return;
        armed = model;
        if (!model.TryGetComponent(out PointArm _)) model.gameObject.AddComponent<PointArm>();
    }

    /// <summary>This PC's player starts or stops pointing (PlayerPing).</summary>
    internal void Hold(bool pointing)
    {
        if (held == pointing) return;
        held = pointing;
        Ask();
    }

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
        if (who != null && who.TryGetComponent(out PlayerPoint point))
        {
            if (point.Pointing) marks |= PlayerPose.Marks.Pointing;
            if (point.Pointing && point.AimsAtPing) marks |= PlayerPose.Marks.PingPoint;
            if (point.TurnedAway) marks |= PlayerPose.Marks.PointAway;
        }
        if (!GameSettings.Current.letOthersPoke) marks |= PlayerPose.Marks.NoPoke;
        return marks;
    }

    /// <summary>
    /// Another PC's player as their pose says (MovementMirror.Apply, every frame): pointing, pokes off, the point a ping's,
    /// a ping's point that turning away cut short going back.
    /// </summary>
    internal static void Mirror(PlayerMovement who, bool pointing, bool noPoke, bool pingPoint, bool away)
    {
        if (who == null) return;
        if (!who.TryGetComponent(out PlayerPoint point))
        {
            if (!pointing && !noPoke && !away) return;
            point = who.gameObject.AddComponent<PlayerPoint>();
        }
        point.mirroredPointing = pointing;
        point.mirroredNoPoke = noPoke;
        point.mirroredPingPoint = pingPoint;
        point.mirroredAway = away;
    }

    // How far into aiming down the sights the gun counts as aimed, for how long a ping's point lasts (WeaponController.AimBlend).
    private const float AimedAt = .3f;
}
