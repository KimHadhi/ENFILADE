using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Puts the body's own hands where they belong after the clip has posed the arms:
/// - on the held gun: KINEMATION FPS Animation Framework has already put both hands on it (FPSBody), so that hold only
///   counts, and the other holds take the hands from it as they come in;
/// - on the fist targets while fists are out (FistsView), except running forward or barging, where the clip swings them;
/// - on the ledge edge, a hand either side, while hanging;
/// - on real ladder rungs, keeping one hand planted while the other reaches above it;
/// - round a swing bar, as the body swings with it (PlayerBodyHands.Bar);
/// - on the edge of what the player vaults or climbs while the arm can reach it (PlayerBodyHands.Edge), and on top of it, never through it;
///   over a high wall, a hand that has pushed off stays no higher than the top until the clip has played out;
/// - with the thumb at ease, measured from the character's own hand, unless it is a fist or on a gun.
/// Parkour states take the hands off the gun, except as TraversalHands says: a vault or a waist-high climb keeps it in
/// the right hand while the left plants, a step up keeps both on it, and the rest put it away (WeaponController).
/// Runs after the weapon has posed itself, so the hands never trail it.
/// Numbers come from Tables/Player.csv, row Hands.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(320)]
public sealed partial class PlayerBodyHands : MonoBehaviour
{
    [SerializeField, Min(.01f), Tooltip("How quickly the hands move between holds (gun, fists, ledge, free) (s).")]
    private float blendTime = .15f;
    [SerializeField, Range(0f, 30f), Tooltip("How far the shoulder turns toward a hold that is out of the arm's reach (degrees).")]
    private float shoulderReach = 15f;
    [SerializeField, Min(0f), Tooltip("Distance between the hands along a ledge, as a share of the character's shoulder width.")]
    private float ledgeHandSpacing = 1f;
    [SerializeField, Min(0f), Tooltip("Running forward faster than this, empty hands swing with the run clip instead of holding the fists up (m/s).")]
    private float fistsRunSpeed = 3f;
    [SerializeField, Min(0f), Tooltip("After a punch, a charge, a guard or a guard break the fists stay up this long, running or not (s).")]
    private float fistsReadyTime = 1.5f;
    [SerializeField, Min(0f), Tooltip("Empty hands come up into fists only once the player has not been running forward for this long (s), "
        + "so a moment's slowing or turning (a landing, the end of a vault, a look round) doesn't flick them up and down.")]
    private float fistsRaiseDelay = .3f;
    [SerializeField, Min(0f), Tooltip("Skin clearance at a ledge or bar, after measuring the character's finger pads (m).")]
    private float fingerSurfaceClearance = .001f;
    [SerializeField, Range(0f, 90f), Tooltip("How far out of the palm's plane a thumb at ease sits, turned under the palm (degrees).")]
    private float thumbUnderPalm = 55f;
    [SerializeField, Range(0f, 60f), Tooltip("How far each outer thumb joint bends toward the palm at ease (degrees).")]
    private float thumbBend = 20f;

    private enum Hold { Gun, Fists, Ledge, Ladder, Bar, Zipline }
    private const int HoldCount = 6;
    // How quickly a hand comes up onto a top, in arm lengths per second. Eased, since a top can appear under a wrist all at once.
    private const float RestLiftSpeed = 6f;
    // At least this share of the input (this PC) or of the speed along the facing (another PC's copy) is forward, running forward.
    private const float RunAlignment = .7f;

    private PlayerMovement player;
    private PlayerBody body;
    private WeaponController weapons;
    private FistsView fists;
    private Animator boundAnimator;
    private ArmIK left, right;
    private readonly float[] weights = new float[HoldCount];
    private readonly float[] restLifts = new float[2];
    private readonly float[] holding = new float[2];
    private float fistsReady, leftOffGun, notRunning, stance;

    /// <summary>How far the hands have gone up as fists (0-1).</summary>
    internal float FistsWeight => weights[(int)Hold.Fists];

    /// <summary>How far the right hand is on the gun (0-1). It eases out in climbs, on ladders and bars, and stays on in a
    /// one-handed vault (only the left leaves). FPSBody weighs FPS Animation Framework's upper body by it.</summary>
    internal float GunWeight => weights[(int)Hold.Gun];

    /// <summary>How far both hands are on the gun (0-1): in a one-handed vault the left leaves it for the edge.</summary>
    internal float BothHandsOnGun => weights[(int)Hold.Gun] * (1f - leftOffGun);

    /// <summary>
    /// How much of the upper body FPS Animation Framework holds the gun with (0-1): both hands on it, standing or crouched
    /// (walking, running and jumps included). In every other move (a vault, a slide, a dive, a roll, a heavy landing) the
    /// move's own armed clip poses the upper body, as it did before KINEMATION (0.7.34), and the hands are put on the gun
    /// here, where FPSBody keeps it (user, 2026-11-07: "움직임은 클립이").
    /// </summary>
    internal float FrameworkShare => BothHandsOnGun * stance;

    // Standing or crouched, the shooting stances FPS Animation Framework's profiles are made for.
    private static bool ShootingStance(MovementState state) => state == MovementState.Standing || state == MovementState.Crouching;

    /// <summary>How much of a hand something holds this frame (0-1): the gun, the fists, a ledge, a rung or a bar.</summary>
    internal float Holding(bool isLeft) => holding[isLeft ? 0 : 1];

    /// <summary>How far the wrist sits above a surface the palm rests flat on (m), measured from the character's hand.</summary>
    internal float PalmDepth => right != null ? right.PalmDepth : left != null ? left.PalmDepth : 0f;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        body = GetComponent<PlayerBody>();
    }

    private void OnDestroy()
    {
        if (fists != null && body != null) fists.Thrown -= body.ThrowPunch;
    }

    // Called by PlayerBody before any clip is evaluated, so lengths, skin and bone axes come from the rest pose.
    internal void BindAnimator(Animator animator)
    {
        boundAnimator = animator;
        gripSkin = body != null ? body.Skin : null;
        // Nothing of the old body is kept if making the new arms fails part way.
        left = right = null;
        left = ArmIK.Create(animator, true);
        right = ArmIK.Create(animator, false);
        shoulderSpan = left != null && right != null ? Vector3.Distance(left.Upper.position, right.Upper.position) : 0f;
        left?.PrepareBarGrip(Ladder.BarThickness * .5f, ladderPalmClearance, fingerSurfaceClearance);
        right?.PrepareBarGrip(Ladder.BarThickness * .5f, ladderPalmClearance, fingerSurfaceClearance);
        left?.PrepareThumb(thumbUnderPalm, thumbBend);
        right?.PrepareThumb(thumbUnderPalm, thumbBend);
        LetGo();
    }

    /// <summary>Takes the hands off whatever they hold.</summary>
    internal void LetGo()
    {
        left?.ClearLedgeSkin();
        right?.ClearLedgeSkin();
        System.Array.Clear(weights, 0, HoldCount);
        ResetEdge();
        heldLadder = null;
        wasOnLadder = ladderTopExit = false;
        wasSliding = false;
        ladderSlideBlend = 0f;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (GamePause.IsPaused || player == null || body == null || dt <= 0f) return;
        var animator = body.BodyAnimator;
        if (animator != boundAnimator)
            BindAnimator(animator);
        if (left == null || right == null || body.IsLimp) return;
        left.ClearLedgeSkin();
        right.ClearLedgeSkin();
        if (weapons == null) weapons = GetComponent<WeaponController>();
        if (fists == null)
        {
            fists = GetComponentInChildren<FistsView>();
            if (fists != null) fists.Thrown += body.ThrowPunch;
        }

        var state = player.CurrentState;
        bool alive = !body.IsDead, armed = alive && HoldsWeapons(state);
        bool gun = weapons != null && weapons.ShowsWeapon && weapons.EquippedData != null;
        var use = TraversalHands.For(player);
        // A vault keeps the gun in the right hand while the left plants; a step up keeps both on it.
        bool oneHanded = alive && gun && use == TraversalHandUse.LeftHand;
        leftOffGun = Mathf.MoveTowards(leftOffGun, oneHanded ? 1f : 0f, dt / blendTime);
        stance = Mathf.MoveTowards(stance, alive && ShootingStance(state) ? 1f : 0f, dt / blendTime);
        Ease(Hold.Gun, gun && (armed || oneHanded || (alive && use == TraversalHandUse.Gun)), dt);
        Ease(Hold.Fists, armed && fists != null && fists.IsActive && FistsUp(dt), dt);
        TrackEdgeHands(state);
        Ease(Hold.Ledge, alive && WantsEdge(state), dt);
        bool onLadder = alive && state == MovementState.Ladder && player.Abilities.Fixtures.Ladder != null
            && player.Abilities.Fixtures.Ladder.isActiveAndEnabled;
        var exitLadder = player.Abilities.Traversal.Obstacle.ExitLadder;
        bool leavingLadder = alive && state == MovementState.Mantle && exitLadder != null && exitLadder.isActiveAndEnabled;
        UpdateLadder(onLadder, leavingLadder, dt);
        Ease(Hold.Ladder, onLadder || (leavingLadder && (LadderHandWeight(true) > 0f || LadderHandWeight(false) > 0f)), dt);
        Ease(Hold.Bar, alive && OnBar(state), dt);
        Ease(Hold.Zipline, alive && OnZipline(state), dt);
        PrepareZipline();

        using (PlaceMarker.Auto())
        {
            Place(right, false);
            Place(left, true);
        }
        using (FingersMarker.Auto())
        {
            float open = 1f - Mathf.Max(weights[(int)Hold.Gun], weights[(int)Hold.Fists]);
            left.RelaxThumb(1f - Mathf.Max(weights[(int)Hold.Gun] * (1f - leftOffGun), weights[(int)Hold.Fists]));
            right.RelaxThumb(open);
            CurlLedgeFingers(left, true);
            CurlLedgeFingers(right, false);
            CurlLadderFingers(left, true);
            CurlLadderFingers(right, false);
            CurlBarFingers(left);
            CurlBarFingers(right);
            CurlZiplineFingers(left);
            CurlZiplineFingers(right);
            CurlGunFingers(left, true, dt);
            CurlGunFingers(right, false, dt);
        }
        using (RestMarker.Auto())
        {
            if (alive && Climbing(state)) RestOnObstacle(dt);
            if (alive && state == MovementState.Mantle) ProbeLadderExit();
            KeepPushedHandsDown(dt);
        }
    }

    // Named spans, so the profiler and HitchProbe can tell which part of the hands' work a stall is in. The fingers' span
    // takes in HandGrip and its first-contact HandSkinCorrection.Prepare.
    private static readonly Unity.Profiling.ProfilerMarker PlaceMarker = new Unity.Profiling.ProfilerMarker("PlayerBodyHands.Place"),
        FingersMarker = new Unity.Profiling.ProfilerMarker("PlayerBodyHands.Fingers"), RestMarker = new Unity.Profiling.ProfilerMarker("PlayerBodyHands.Rest");

    private void Ease(Hold hold, bool on, float dt)
        => weights[(int)hold] = Mathf.MoveTowards(weights[(int)hold], on ? 1f : 0f, dt / blendTime);

    // Shooting is allowed on the feet, in the air, sliding, diving and lying; climbing moves need the hands.
    internal static bool HoldsWeapons(MovementState state)
    {
        switch (state)
        {
            case MovementState.Vault:
            case MovementState.Mantle:
            case MovementState.LedgeHang:
            case MovementState.LedgeJump:
            case MovementState.WallClimb:
            case MovementState.WallKick:
            case MovementState.LandingRoll:
            case MovementState.HeavyLanding:
            case MovementState.BarGrab:
            case MovementState.BarSwing:
            case MovementState.LacheRelease:
            case MovementState.Ladder:
            case MovementState.Zipline:
                return false;
            default:
                return true;
        }
    }

    // Empty hands follow the clip while running forward or barging (the dash push and its play-out). A fist move puts
    // them up at once and keeps them there for a moment. Otherwise they come up only once the player has stopped running
    // forward for fistsRaiseDelay: around a vault the speed dips and the view turns for a moment, and the guard flicked up
    // and down with every one, open-handed in front of the face (user's video, 2026-11-06).
    private bool FistsUp(float dt)
    {
        var melee = player.MeleeCombat;
        fistsReady = Mathf.Max(0f, fistsReady - dt);
        if (fists.IsPunching || (melee != null && (melee.IsCharging || melee.IsGuarding || melee.IsParryActive || melee.IsGuardBreaking)))
            fistsReady = fistsReadyTime;
        if (fistsReady > 0f) return true;
        if (body.PlaysDashPush) { notRunning = 0f; return false; }
        notRunning = RunsForward() ? 0f : notRunning + dt;
        return notRunning >= fistsRaiseDelay;
    }

    // Running forward at a run. This PC's player by what they ask for (W, mostly, at the run's own speed): the way the body
    // actually moves swings round behind the view as it turns, and stalls against the next obstacle before a vault, and
    // either alone had the fists flick up mid-run. Another PC's copy by how it moves.
    private bool RunsForward()
    {
        if (player.IsNetworkDriven)
        {
            Vector3 moving = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up);
            float speed = moving.magnitude;
            return speed >= fistsRunSpeed && Vector3.Dot(moving, transform.forward) >= speed * RunAlignment;
        }
        Vector2 input = player.CurrentInput;
        return Vector3.ProjectOnPlane(player.HorizontalVelocity, Vector3.up).magnitude >= fistsRunSpeed
            && input.y > .1f && input.y >= input.magnitude * RunAlignment;
    }

    // Mixes the holds by weight, so a change of hold slides the hand across instead of snapping it.
    private void Place(ArmIK arm, bool isLeft)
    {
        float total = 0f, elsewhere = 0f;
        Vector3 position = Vector3.zero, hint = Vector3.zero;
        Quaternion rotation = Quaternion.identity;
        // FPS Animation Framework has already put the hands on its gun (FPSBody): that hold counts, with nothing to solve.
        for (int i = 0; i < HoldCount; i++)
        {
            float weight = weights[i];
            if ((Hold)i == Hold.Ladder) weight *= LadderHandWeight(isLeft);
            if ((Hold)i == Hold.Fists) weight *= 1f - body.PunchArmShare(isLeft);
            // In a one-handed vault the left hand leaves the gun for the edge, the right stays on it.
            if ((Hold)i == Hold.Gun && isLeft) weight *= 1f - leftOffGun;
            if ((Hold)i == Hold.Ledge && !isLeft) weight *= 1f - leftOffGun;
            if ((Hold)i == Hold.Gun)
            {
                // Where FPS Animation Framework holds the upper body its hands are already on the gun and only count; where
                // the move's clip has it, the hand is put on the gun here, as before KINEMATION (FrameworkShare).
                weight = Mathf.Max(0f, weight);
                float framework = Mathf.Min(weight, FrameworkShare), solved = weight - framework;
                elsewhere += framework;
                if (solved <= 0f || !GunGrip(arm, isLeft, out Vector3 gp, out Quaternion gr, out Vector3 gh)) { elsewhere += solved; continue; }
                total += solved;
                position += gp * solved;
                hint += gh * solved;
                rotation = total <= solved ? gr : Quaternion.Slerp(rotation, gr, solved / total);
                continue;
            }
            if (weight <= 0f || !Target((Hold)i, arm, isLeft, out Vector3 p, out Quaternion r, out Vector3 h)) continue;
            if ((Hold)i == Hold.Ledge) weight *= EdgeShare(arm, isLeft, p);
            if (weight <= 0f) continue;
            total += weight;
            position += p * weight;
            hint += h * weight;
            rotation = total <= weight ? r : Quaternion.Slerp(rotation, r, weight / total);
        }
        holding[isLeft ? 0 : 1] = Mathf.Clamp01(total + elsewhere);
        if (total <= 0f) return;
        arm.Solve(position / total, rotation, hint / total, Mathf.Clamp01(total), 1f, shoulderReach);
    }

    // A hand on the gun where FPSBody keeps it while the move's clip has the upper body: the grip as FPS Animation Framework
    // last held it; the trigger elbow drops out to the side, the support elbow tucks under the gun (as before KINEMATION).
    private bool GunGrip(ArmIK arm, bool isLeft, out Vector3 position, out Quaternion rotation, out Vector3 hint)
    {
        Vector3 side = transform.right * (isLeft ? -1f : 1f), up = transform.up, forward = transform.forward;
        hint = arm.Upper.position + (isLeft ? side * .1f + forward * .15f - up * .4f : side * .25f - forward * .1f - up * .35f);
        var framework = FPSBody.Holding(weapons);
        if (framework != null && framework.HandOnGun(isLeft, out position, out rotation)) return true;
        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    private bool Target(Hold hold, ArmIK arm, bool isLeft, out Vector3 position, out Quaternion rotation, out Vector3 hint)
    {
        position = hint = Vector3.zero;
        rotation = Quaternion.identity;
        Vector3 side = transform.right * (isLeft ? -1f : 1f), up = transform.up, forward = transform.forward;
        Vector3 shoulder = arm.Upper.position;
        switch (hold)
        {
            case Hold.Fists:
            {
                var fist = isLeft ? fists.LeftFist : fists.RightFist;
                if (fist == null) return false;
                position = fist.position;
                rotation = arm.HandRotation(fist.forward, -fist.up);
                hint = shoulder + side * .2f - forward * .05f - up * .35f;
                return true;
            }
            case Hold.Ledge:
                return LedgeHandTarget(arm, isLeft, out position, out rotation, out hint);
            case Hold.Ladder:
                return LadderTarget(arm, isLeft, out position, out rotation, out hint);
            case Hold.Bar:
                return BarTarget(arm, isLeft, out position, out rotation, out hint);
            case Hold.Zipline:
                return ZiplineTarget(arm, isLeft, out position, out rotation, out hint);
        }
        return false;
    }

    // A hand that would sink into the obstacle's top rests on it instead, its elbow staying where the clip has it.
    // Temporary probe (user, 2026-11-08: the climb-off's hands don't match, "like there's no IK"): through a climb off a
    // ladder's top, where the hands' middle is against where the warp holds it, how far each hand is from the floor's
    // edge, and the rung hold's weights. A hand on the hold reads near (0, 0, 0) while the clip's hands are down.
    private float ladderProbeAt;
    private void ProbeLadderExit()
    {
        var traversal = player.Abilities.Traversal;
        if (traversal.Obstacle.ExitLadder == null || traversal.ObstacleClipTime < 0f || Time.time - ladderProbeAt < .1f) return;
        ladderProbeAt = Time.time;
        Vector2 support = traversal.ObstacleSupport;
        float time = traversal.ObstacleClipTime;
        Vector3 middle = (left.Hand.position + right.Hand.position) * .5f;
        ParkourProbeLog.Line($"climb-off hands: clip {time:0.00} {(time >= support.x && time <= support.y ? "DOWN" : "up")} | middle - hold "
            + $"{(middle - traversal.ObstacleContact).ToString("F3")} | left {left.Hand.position.ToString("F2")} right {right.Hand.position.ToString("F2")} "
            + $"edge {traversal.Obstacle.Edge.ToString("F2")} | short {traversal.EdgeShortfall:0.000} | rung weights {weights[(int)Hold.Ladder]:0.00} "
            + $"L {LadderHandWeight(true):0.00} R {LadderHandWeight(false):0.00} | feet {player.transform.position.ToString("F2")}");
    }

    private void RestOnObstacle(float dt)
    {
        var surface = player.Abilities.Traversal.Obstacle.Surface;
        if (surface == null) return;
        Rest(left, true);
        Rest(right, false);

        void Rest(ArmIK arm, bool isLeft)
        {
            // The floor correction must not pull a supporting hand off its ladder rung during the top exit, nor the gun hand off the gun.
            if (weights[(int)Hold.Ladder] * LadderHandWeight(isLeft) > .001f) return;
            if (!isLeft && weights[(int)Hold.Gun] > .001f) return;
            Vector3 wrist = arm.Hand.position;
            float lift = !surface.Raycast(new Ray(wrist + Vector3.up * .5f, Vector3.down), out RaycastHit top, .6f) ? 0f
                : Mathf.Max(0f, top.point.y + arm.PalmDepth + fingerSurfaceClearance - wrist.y);
            int hand = isLeft ? 0 : 1;
            restLifts[hand] = Mathf.MoveTowards(restLifts[hand], lift, RestLiftSpeed * arm.Reach * dt);
            if (restLifts[hand] > 0f)
                arm.Solve(wrist + Vector3.up * restLifts[hand], arm.Hand.rotation, arm.Elbow, 1f, 0f, shoulderReach);
        }
    }
}
