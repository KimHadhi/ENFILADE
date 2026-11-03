using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Puts the body's own hands where they belong after the clip has posed the arms:
/// - on the held gun's grips (grip points per weapon in Weapons.csv), wherever the camera-driven gun pose puts it;
/// - on the fist targets while fists are out (FistsView), except running forward or barging, where the clip swings them;
/// - on the ledge edge, a hand either side, while hanging;
/// - on real ladder rungs, keeping one hand planted while the other reaches above it;
/// - round a swing bar, as the body swings with it (PlayerBodyHands.Bar);
/// - on the edge of what the player vaults or climbs while the arm can reach it (PlayerBodyHands.Edge), and on top of it, never through it;
/// - on a skirt, never inside it, when the clip lets an empty hand hang where the skirt is (PlayerBodyHands.Skirt);
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
    [SerializeField, Min(0f), Tooltip("Skin clearance at a ledge or bar, after measuring the character's finger pads (m).")]
    private float fingerSurfaceClearance = .001f;
    [SerializeField, Range(0f, 90f), Tooltip("How far out of the palm's plane a thumb at ease sits, turned under the palm (degrees).")]
    private float thumbUnderPalm = 55f;
    [SerializeField, Range(0f, 60f), Tooltip("How far each outer thumb joint bends toward the palm at ease (degrees).")]
    private float thumbBend = 20f;

    private enum Hold { Gun, Fists, Ledge, Ladder, Bar }
    private const int HoldCount = 5;
    // How quickly a hand comes up onto a top, in arm lengths per second. Eased, since a top can appear under a wrist all at once.
    private const float RestLiftSpeed = 6f;
    // Moving at least this share of the speed along the body's facing counts as running forward.
    private const float RunAlignment = .7f;

    private PlayerMovement player;
    private PlayerBody body;
    private WeaponController weapons;
    private FistsView fists;
    private Animator boundAnimator;
    private ArmIK left, right;
    private readonly float[] weights = new float[HoldCount];
    private readonly float[] restLifts = new float[2];
    private float fistsReady, leftOffGun;

    /// <summary>How far the hands have gone up as fists (0-1).</summary>
    internal float FistsWeight => weights[(int)Hold.Fists];

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
        System.Array.Clear(skirtPushes, 0, skirtPushes.Length);
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

        Place(right, false);
        Place(left, true);
        float open = 1f - Mathf.Max(weights[(int)Hold.Gun], weights[(int)Hold.Fists]);
        left.RelaxThumb(1f - Mathf.Max(weights[(int)Hold.Gun] * (1f - leftOffGun), weights[(int)Hold.Fists]));
        right.RelaxThumb(open);
        CurlLedgeFingers(left, true);
        CurlLedgeFingers(right, false);
        CurlLadderFingers(left, true);
        CurlLadderFingers(right, false);
        CurlBarFingers(left);
        CurlBarFingers(right);
        if (alive && Climbing(state)) RestOnObstacle(dt);
        if (alive) RestOnSkirt(dt);
    }

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
    // them up at once and keeps them there for a moment.
    private bool FistsUp(float dt)
    {
        var melee = player.MeleeCombat;
        fistsReady = Mathf.Max(0f, fistsReady - dt);
        if (fists.IsPunching || (melee != null && (melee.IsCharging || melee.IsGuarding || melee.IsParryActive || melee.IsGuardBreaking)))
            fistsReady = fistsReadyTime;
        if (fistsReady > 0f) return true;
        if (body.PlaysDashPush) return false;
        Vector3 run = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up);
        float speed = run.magnitude;
        return speed < fistsRunSpeed || Vector3.Dot(run, transform.forward) < speed * RunAlignment;
    }

    // Mixes the holds by weight, so a change of hold slides the hand across instead of snapping it.
    private void Place(ArmIK arm, bool isLeft)
    {
        float total = 0f;
        Vector3 position = Vector3.zero, hint = Vector3.zero;
        Quaternion rotation = Quaternion.identity;
        for (int i = 0; i < HoldCount; i++)
        {
            float weight = weights[i];
            if ((Hold)i == Hold.Ladder) weight *= LadderHandWeight(isLeft);
            if ((Hold)i == Hold.Fists) weight *= 1f - body.PunchArmShare(isLeft);
            // In a one-handed vault the left hand leaves the gun for the edge, the right stays on it.
            if ((Hold)i == Hold.Gun && isLeft) weight *= 1f - leftOffGun;
            if ((Hold)i == Hold.Ledge && !isLeft) weight *= 1f - leftOffGun;
            if (weight <= 0f || !Target((Hold)i, arm, isLeft, out Vector3 p, out Quaternion r, out Vector3 h)) continue;
            if ((Hold)i == Hold.Ledge) weight *= EdgeShare(arm, isLeft, p);
            if (weight <= 0f) continue;
            total += weight;
            position += p * weight;
            hint += h * weight;
            rotation = total <= weight ? r : Quaternion.Slerp(rotation, r, weight / total);
        }
        heldShares[isLeft ? 0 : 1] = Mathf.Clamp01(total);
        if (total <= 0f) return;
        arm.Solve(position / total, rotation, hint / total, Mathf.Clamp01(total), 1f, shoulderReach);
    }

    private bool Target(Hold hold, ArmIK arm, bool isLeft, out Vector3 position, out Quaternion rotation, out Vector3 hint)
    {
        position = hint = Vector3.zero;
        rotation = Quaternion.identity;
        Vector3 side = transform.right * (isLeft ? -1f : 1f), up = transform.up, forward = transform.forward;
        Vector3 shoulder = arm.Upper.position;
        switch (hold)
        {
            case Hold.Gun:
            {
                var root = weapons.WeaponRoot;
                var data = weapons.EquippedData;
                if (root == null || data == null) return false;
                position = root.TransformPoint(isLeft ? data.LeftGrip : data.RightGrip);
                rotation = arm.HandRotation(root.TransformDirection(isLeft ? data.LeftGripFingers : data.RightGripFingers),
                    root.TransformDirection(isLeft ? data.LeftGripPalm : data.RightGripPalm));
                // The trigger elbow drops out to the side; the support elbow tucks under the gun.
                hint = shoulder + (isLeft ? side * .1f + forward * .15f - up * .4f : side * .25f - forward * .1f - up * .35f);
                return true;
            }
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
        }
        return false;
    }

    // A hand that would sink into the obstacle's top rests on it instead, its elbow staying where the clip has it.
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
