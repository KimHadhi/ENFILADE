using System;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The camera out of the body, for this PC's own player only:
/// - Rookie mode (애송이 모드, Esc → 카메라; off by default, since the game is played through the eyes): while a parkour
///   move the player left ticked is under way (vaults and climbs, ladders up and down, landing rolls, dodges, ziplines,
///   slides, swing bars) the camera eases out of the eyes to behind the head and over the right shoulder, so the whole
///   body shows, and eases back into the eyes after (user, 2026-11-11: "파쿠르 할 때 … 3인칭 되는 기능 넣기, 애송이
///   모드로"). The mouse still turns the view: the camera circles the head with the look. A wall in the way brings it in
///   at once and lets it out again slowly. A short move keeps it out a moment (leastOut) and moves one after another keep
///   it out (linger), so it never flickers in and out. Aiming down the sights brings it straight back.
/// - The execution cinematic (on by default): through an execution or a stealth kill the camera leaves the body and shows
///   the two from outside: low from the side as it begins (from in front of the victim for a stealth kill, so the sneak up
///   behind it shows), then close in on the victim the moment the blow lands, as the kill's short hit stop holds time.
///   When it ends the view cuts to over the shoulder and glides back into the eyes (user: "나중에 처형할 때 시네마틱
///   카메라 연출 효과 넣기").
/// Out and back in, the camera goes beside the head first and only then behind it, so it never passes through the head.
/// It runs after the body has put the camera on the eyes (FrameOrder.ThirdPersonCamera) and before the weapons, which aim
/// along the camera's ray from level with the eyes as they do in the F5 view; the gun, the fists and every reach stay on
/// the eyes (PlayerView). It stands aside in the F5 view and when dead (the ragdoll and spectating have the camera then).
/// Numbers: Tables/ThirdPerson, rows Rookie and Execution.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.ThirdPersonCamera)]
public sealed class ThirdPersonCamera : MonoBehaviour
{
    public const string TableName = "ThirdPerson", RookieRow = "Rookie", ExecutionRow = "Execution";

    [Serializable]
    public sealed class FollowSettings
    {
        [Min(.5f), Tooltip("How far behind the head the camera sits (m).")]
        public float distance = 2.4f;
        [Tooltip("How far above the eyes (m).")]
        public float height = .25f;
        [Tooltip("How far to the right, over the shoulder (m). 0 is straight behind.")]
        public float side = .45f;
        [Min(.05f), Tooltip("Seconds to ease out of the eyes.")]
        public float outTime = .35f;
        [Min(.05f), Tooltip("Seconds to ease back into them.")]
        public float backTime = .45f;
        [Min(0f), Tooltip("Seconds it stays out after a move ends, so moves one after another keep it out.")]
        public float linger = .3f;
        [Min(0f), Tooltip("Least seconds out once a move takes it out, so a short one (a dodge) is not in and out at once.")]
        public float leastOut = .7f;
        [Range(0f, 1f), Tooltip("Share of the head's own movement the circled point follows (a roll ducks the head). 0 keeps to "
            + "the eye height the capsule sets.")]
        public float headShare = .35f;
        [Min(0f), Tooltip("How quickly the circled point catches up (1/s). 0 snaps.")]
        public float pivotRate = 14f;
        [Min(.05f), Tooltip("The camera's size against walls (m).")]
        public float wallRadius = .18f;
        [Min(0f), Tooltip("How quickly it moves back out once a wall no longer stands in the way (1/s).")]
        public float wallRelease = 5f;
        [Range(0f, 1f), Tooltip("Aiming down the sights past this much brings it back to the eyes.")]
        public float aimBack = .3f;
    }

    [Serializable]
    public sealed class CinematicSettings
    {
        [Min(.5f), Tooltip("Before the blow: how far to the side of the two the camera stands (m).")]
        public float sideDistance = 2.6f;
        [Tooltip("Before the blow: how high above the ground (m). Low looks up at them.")]
        public float lowHeight = .45f;
        [Tooltip("Before the blow: the height it looks at, between the two (m).")]
        public float lookHeight = 1.1f;
        [Tooltip("Before the blow: how fast it circles round toward the player's front (degrees per second).")]
        public float orbitSpeed = 28f;
        [Min(.5f), Tooltip("A stealth kill: how far in front of the victim (m).")]
        public float frontDistance = 2.2f;
        [Tooltip("A stealth kill: how far to the side of straight in front (m).")]
        public float frontSide = .9f;
        [Min(.3f), Tooltip("As the blow lands: how close to the victim's upper body (m).")]
        public float closeDistance = 1.2f;
        [Tooltip("As the blow lands: how far it moves in while the blow plays out (m).")]
        public float closePush = .3f;
        [Tooltip("As the blow lands: how far above the victim's upper body (m).")]
        public float closeLift = .12f;
        [Tooltip("As the blow lands: how far back from the victim toward the player (m), so the blow coming in shows.")]
        public float closeBack = .35f;
        [Min(.05f), Tooltip("The camera's size against walls (m).")]
        public float wallRadius = .2f;
        [Min(.5f), Tooltip("The longest a cinematic may run, should the execution's end never come (s).")]
        public float longest = 3f;
    }

    [SerializeField] private FollowSettings follow = new FollowSettings();
    [SerializeField] private CinematicSettings cinematic = new CinematicSettings();

    // Below this share of the way out the camera counts as in the eyes: the head shows and is cut as in first person. Past
    // it the camera is a hand's width beside the head, clear of glasses and hair.
    private const float InEyes = .25f;
    // The body camera's own size against walls near the eyes (m), as BodyCamera.Radius comes to at most field of views.
    private const float EyeRadius = .05f;
    // Of the way out (0 eyes, 1 behind): the part spent going beside the head, and from where it starts going behind.
    private const float SideSpan = .6f, BackFrom = .3f;
    private const string EnemyLayer = "Enemy";
    private static int sideSwap; // the cinematic takes turns between the sides when both have room

    private readonly CameraClearance clearance = new CameraClearance();
    private CharacterController capsule;
    private PlayerMovement player;
    private PlayerBody body;
    private PlayerExecution execution;
    private WeaponController weapons;
    private Transform view, lookPivot;

    // Rookie mode: how far out (0 eyes, 1 behind), how long out, and how long it still stays.
    private float weight, outFor, lingerLeft;
    private Vector3 pivot;
    private bool pivotKnown, showing;
    // The room walls leave: the share of the way to the shoulder, and the metres behind it. In at once, out slowly.
    private float sideRoom = 1f, backRoom = float.PositiveInfinity;

    // The execution cinematic.
    private MeleeCombatTarget victim;
    private bool filming, landed, stealth;
    private float filmTime, landedAt;
    private int filmSide;
    private Vector3 victimFocus, landedAlong, landedRight;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        GameTables.BindNested(TableName, RookieRow, this, follow);
        GameTables.BindNested(TableName, ExecutionRow, this, cinematic);
    }

    private void OnEnable() => Subscribe();

    private void OnDisable()
    {
        if (execution != null)
        {
            execution.Started -= OnExecutionStarted;
            execution.Impact -= OnExecutionImpact;
            execution.Ended -= OnExecutionEnded;
            execution = null;
        }
        StopFilming();
        weight = outFor = lingerLeft = 0f;
        Show(false);
    }

    private void Subscribe()
    {
        if (execution != null) return;
        execution = GetComponent<PlayerExecution>();
        if (execution == null) return;
        execution.Started += OnExecutionStarted;
        execution.Impact += OnExecutionImpact;
        execution.Ended += OnExecutionEnded;
    }

    private void LateUpdate()
    {
        if (GamePause.IsPaused || Time.deltaTime <= 0f) return;
        if (!Bind() || !CanLeaveEyes())
        {
            // Gone back at once: the ragdoll, spectating or the F5 view take the camera from here.
            StopFilming();
            weight = outFor = lingerLeft = 0f;
            pivotKnown = false;
            Show(false);
            return;
        }
        float dt = Time.deltaTime;
        if (filming && Film(dt)) return;
        FollowMoves(dt);
    }

    private bool Bind()
    {
        if (player == null || !player.IsLocal || !player.IsInitialized) return false;
        if (body == null) body = GetComponent<PlayerBody>();
        if (body == null || !body.isActiveAndEnabled || body.BodyAnimator == null) return false;
        if (view == null) view = body.View;
        if (view == null || view.parent == null) return false;
        if (lookPivot == null)
            lookPivot = view.parent.name == "CameraEffectsPivot" && view.parent.parent != null ? view.parent.parent : view.parent;
        if (weapons == null) weapons = GetComponent<WeaponController>();
        Subscribe();
        return true;
    }

    private bool CanLeaveEyes() => !body.IsThirdPerson && !body.IsDead && !body.IsLimp && !player.IsDead;

    // ---- Rookie mode ----

    private void FollowMoves(float dt)
    {
        bool wanted = WantsOut();
        lingerLeft = wanted ? follow.linger : Mathf.Max(0f, lingerLeft - dt);
        bool aiming = Aiming();
        bool stay = !aiming && (wanted || lingerLeft > 0f || (weight > 0f && outFor < follow.leastOut));
        float target = stay ? 1f : 0f;
        float time = target > weight ? follow.outTime : follow.backTime * (aiming ? .5f : 1f);
        weight = Mathf.MoveTowards(weight, target, dt / Mathf.Max(.01f, time));
        outFor = weight > 0f ? outFor + dt : 0f;
        if (weight <= 0f)
        {
            pivotKnown = false;
            sideRoom = 1f;
            backRoom = float.PositiveInfinity;
            Show(false);
            return;
        }
        PlaceBehind(weight, dt);
    }

    // The moves the player ticked under Esc → 카메라 → 애송이 모드.
    private bool WantsOut()
    {
        var s = GameSettings.Current;
        if (!s.rookieMode) return false;
        switch (player.CurrentState)
        {
            case MovementState.Vault:
            case MovementState.LedgeHang:
            case MovementState.LedgeJump:
            case MovementState.WallClimb:
            case MovementState.WallKick:
                return s.rookieVault;
            // Climbing off a ladder's top is the ladder's end, not a climb of its own.
            case MovementState.Mantle:
                return player.Abilities.Traversal.Obstacle.ExitLadder != null ? s.rookieLadder : s.rookieVault;
            case MovementState.Ladder:
                return s.rookieLadder;
            case MovementState.LandingRoll:
                return s.rookieLandingRoll;
            case MovementState.StepDodge:
                return s.rookieDodge;
            case MovementState.Zipline:
                return s.rookieZipline;
            case MovementState.Sliding:
            case MovementState.HeadFirstSliding:
            case MovementState.SupineSliding:
                return s.rookieSlide;
            case MovementState.BarGrab:
            case MovementState.BarSwing:
            case MovementState.LacheRelease:
                return s.rookieBars;
            default:
                return false;
        }
    }

    private bool Aiming() => weapons != null && weapons.EquippedData != null && !weapons.IsHighReady && weapons.AimBlend > follow.aimBack;

    /// <summary>
    /// <paramref name="w"/> of the way from the eyes to behind the head: beside the head over its first part, behind it
    /// from BackFrom on, so the path round never cuts through the head. Both legs are swept against walls, the first from a
    /// point inside the movement capsule, which is never inside a wall the player is not (the face against a ladder's wall
    /// would be). The camera's size against walls grows with the way out from the body camera's own small one, so close to
    /// the eyes it is where the body camera, which cleared it already, put it.
    /// </summary>
    private void PlaceBehind(float w, float dt)
    {
        Vector3 eye = view.position;
        Quaternion eyeTurn = view.rotation, look = lookPivot.rotation;
        // The point it circles: the eye height the capsule sets, with a share of the head's own movement, eased.
        Vector3 wantedPivot = Vector3.Lerp(lookPivot.position, body.EyePosition, follow.headShare);
        pivot = pivotKnown ? Vector3.Lerp(pivot, wantedPivot, Rate(follow.pivotRate, dt)) : wantedPivot;
        pivotKnown = true;

        float aside = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(w / SideSpan));
        float behind = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((w - BackFrom) / (1f - BackFrom)));
        float radius = Mathf.Lerp(EyeRadius, follow.wallRadius, aside);
        LayerMask walls = Walls;
        Vector3 anchor = SafeAnchor(pivot);
        // Squeezed through a gap (a vault), the capsule itself may touch the obstacle: then from the eyes.
        if (!clearance.IsClear(anchor, anchor, radius, walls, transform)) anchor = eye;
        Vector3 shoulderOffset = look * new Vector3(follow.side, follow.height, 0f);
        // The room beside the head, measured all the way out every frame so a wall that goes lets the camera out slowly.
        Vector3 fullShoulder = pivot + shoulderOffset;
        float sideAllowed = Share(anchor, clearance.Resolve(anchor, fullShoulder, follow.wallRadius, walls, transform), fullShoulder);
        sideRoom = sideAllowed < sideRoom ? sideAllowed : Mathf.Lerp(sideRoom, sideAllowed, Rate(follow.wallRelease, dt));
        Vector3 start = Vector3.Lerp(eye, pivot, aside);
        Vector3 shoulder = clearance.Resolve(anchor, start + shoulderOffset * Mathf.Min(aside, sideRoom), radius, walls, transform);

        Vector3 back = look * Vector3.back;
        float room = Vector3.Distance(shoulder, clearance.Resolve(shoulder, shoulder + back * follow.distance, follow.wallRadius, walls, transform));
        backRoom = room < backRoom ? room : Mathf.Lerp(backRoom, room, Rate(follow.wallRelease, dt));
        Vector3 position = clearance.Resolve(shoulder, shoulder + back * Mathf.Min(follow.distance * behind, backRoom), radius, walls,
            transform);
        // Out of the eyes the view turns with the look and the camera effects (punches, tilts), not the head's clip.
        view.SetPositionAndRotation(position, Quaternion.Slerp(eyeTurn, view.parent.rotation, aside));
        Show(w > InEyes);
    }

    // A point on the movement capsule's axis at the height of `near`, kept within the capsule's straight part.
    private Vector3 SafeAnchor(Vector3 near)
    {
        if (capsule == null) capsule = GetComponent<CharacterController>();
        if (capsule == null) return near;
        Vector3 local = transform.InverseTransformPoint(near);
        float half = Mathf.Max(0f, capsule.height * .5f - capsule.radius);
        local.x = capsule.center.x;
        local.z = capsule.center.z;
        local.y = Mathf.Clamp(local.y, capsule.center.y - half, capsule.center.y + half);
        return transform.TransformPoint(local);
    }

    // ---- The execution cinematic ----

    private void OnExecutionStarted(MeleeCombatTarget target)
    {
        if (target == null || !GameSettings.Current.executionCinematic || player == null || !player.IsLocal) return;
        if (body == null || body.IsThirdPerson) return;
        victim = target;
        filming = true;
        landed = false;
        filmTime = landedAt = 0f;
        stealth = target.TryGetComponent(out ZombieMind mind) && mind.Unaware;
        victimFocus = Vector3.zero;
        Focus(target, ref victimFocus);
        filmSide = PickSide();
        weight = outFor = lingerLeft = 0f;
        pivotKnown = false;
    }

    // The victim may go at once (a corpse copy takes its place), so the close shot keeps where it was struck.
    private void OnExecutionImpact(MeleeCombatTarget target)
    {
        if (!filming) return;
        landed = true;
        landedAt = filmTime;
        Line(out _, out landedAlong, out landedRight);
        if (target != null) Focus(target, ref victimFocus);
    }

    private void OnExecutionEnded()
    {
        if (!filming) return;
        StopFilming();
        // A cut to over the shoulder, then the glide back into the eyes from there.
        weight = 1f;
        outFor = follow.leastOut;
        lingerLeft = 0f;
        pivotKnown = false;
        sideRoom = 1f;
        backRoom = float.PositiveInfinity;
    }

    private void StopFilming()
    {
        filming = landed = false;
        victim = null;
    }

    private bool Film(float dt)
    {
        filmTime += dt;
        if ((!landed && (victim == null || !victim.isActiveAndEnabled)) || filmTime > cinematic.longest)
        {
            OnExecutionEnded();
            return false;
        }
        Vector3 position, focus;
        if (!landed)
        {
            Line(out Vector3 middle, out Vector3 along, out Vector3 right);
            focus = middle + Vector3.up * cinematic.lookHeight;
            position = LeadIn(middle, along, right, filmSide, filmTime);
        }
        else
        {
            // Close on the victim from the other side, a little back toward the player, moving in as the blow plays out.
            float t = Mathf.Clamp01((filmTime - landedAt) / .6f);
            focus = victimFocus;
            position = focus - landedRight * (filmSide * (cinematic.closeDistance - cinematic.closePush * Mathf.SmoothStep(0f, 1f, t)))
                - landedAlong * cinematic.closeBack + Vector3.up * cinematic.closeLift;
        }
        position = clearance.Resolve(focus, position, cinematic.wallRadius, Walls, transform);
        Vector3 toward = focus - position;
        view.SetPositionAndRotation(position, toward.sqrMagnitude > .0001f ? Quaternion.LookRotation(toward, Vector3.up) : view.rotation);
        Show(true);
        return true;
    }

    // Before the blow: low and to one side of the two, circling slowly toward the player's front; for a stealth kill,
    // in front of the unaware victim and a little to one side, so the player rising behind it shows.
    private Vector3 LeadIn(Vector3 middle, Vector3 along, Vector3 right, int side, float time)
    {
        if (stealth)
            return VictimAt() + along * cinematic.frontDistance + right * (side * cinematic.frontSide) + Vector3.up * cinematic.lowHeight;
        Vector3 outward = Quaternion.AngleAxis(-side * cinematic.orbitSpeed * time, Vector3.up) * (right * side);
        return middle + outward * cinematic.sideDistance + Vector3.up * cinematic.lowHeight;
    }

    // The side with more room before a wall; when both have room, the sides take turns.
    private int PickSide()
    {
        Line(out Vector3 middle, out Vector3 along, out Vector3 right);
        Vector3 focus = middle + Vector3.up * cinematic.lookHeight;
        float RoomOn(int side)
        {
            Vector3 wanted = LeadIn(middle, along, right, side, 0f);
            return Share(focus, clearance.Resolve(focus, wanted, cinematic.wallRadius, Walls, transform), wanted);
        }
        float rightRoom = RoomOn(1), leftRoom = RoomOn(-1);
        if (Mathf.Abs(rightRoom - leftRoom) > .15f) return rightRoom > leftRoom ? 1 : -1;
        return (sideSwap++ & 1) == 0 ? 1 : -1;
    }

    // The ground between the player and the victim, the way from the player to it and that way's right.
    private void Line(out Vector3 middle, out Vector3 along, out Vector3 right)
    {
        Vector3 me = transform.position, them = VictimAt();
        along = Vector3.ProjectOnPlane(them - me, Vector3.up);
        if (along.sqrMagnitude < .0001f) along = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        along = along.sqrMagnitude > .0001f ? along.normalized : Vector3.forward;
        right = Vector3.Cross(Vector3.up, along);
        middle = (me + them) * .5f;
        middle.y = Mathf.Min(me.y, them.y);
    }

    private Vector3 VictimAt() => victim != null ? victim.transform.position : transform.position + transform.forward;

    // The victim's upper body: a little above its collider's middle, so low when it lies down. Taken as the execution starts
    // and again as the blow lands, unless the body has already given up its collider by then.
    private static void Focus(MeleeCombatTarget target, ref Vector3 focus)
    {
        var shape = target.GetComponentInChildren<Collider>();
        if (shape != null && shape.enabled)
        {
            var bounds = shape.bounds;
            focus = bounds.center + Vector3.up * (bounds.extents.y * .45f);
        }
        else if (focus == Vector3.zero) focus = target.transform.position + Vector3.up * 1.3f;
    }

    // ---- Shared ----

    // Walls stop the camera; enemies, the player's own colliders and anything under it don't.
    private LayerMask Walls => player.groundLayers & ~LayerMask.GetMask(EnemyLayer);

    private void Show(bool outside)
    {
        if (showing == outside || body == null) return;
        showing = outside;
        body.ShowFromOutside(outside);
    }

    // How far from `from` the cleared point got, as a share of the way to `wanted`.
    private static float Share(Vector3 from, Vector3 cleared, Vector3 wanted)
    {
        float full = Vector3.Distance(from, wanted);
        return full > .001f ? Mathf.Clamp01(Vector3.Distance(from, cleared) / full) : 1f;
    }

    private static float Rate(float rate, float dt) => rate <= 0f ? 1f : 1f - Mathf.Exp(-rate * dt);
}
