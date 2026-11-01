using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Puts the body's own hands where they belong after the clip has posed the arms:
/// - on the held gun's grips (grip points per weapon in Weapons.csv), wherever the camera-driven gun pose puts it;
/// - on the fist targets while fists are out (FistsView), except running forward or barging, where the clip swings them;
/// - on the ledge edge, a hand either side, while hanging;
/// - on real ladder rungs, keeping one hand planted while the other reaches above it;
/// - on top of a vaulted or mantled obstacle, never through it.
/// Parkour states take the hands off the gun. Runs after the weapon has posed itself, so the hands never trail it.
/// Numbers come from Tables/Player.csv, row Hands.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(320)]
public sealed partial class PlayerBodyHands : MonoBehaviour
{
    [SerializeField, Min(.01f), Tooltip("How quickly the hands move between holds (gun, fists, ledge, free) (s).")]
    private float blendTime = .15f;
    [SerializeField, Range(0f, 30f), Tooltip("How far the shoulder turns toward a hold that is out of the arm's reach (degrees).")]
    private float shoulderReach = 15f;
    [SerializeField, Min(0f), Tooltip("Distance between the hands along a ledge (m).")]
    private float ledgeHandSpacing = .36f;
    [SerializeField, Min(0f), Tooltip("Wrist height above an obstacle's top when a hand rests on it during a vault or mantle (m).")]
    private float palmHeight = .04f;
    [SerializeField, Min(0f), Tooltip("Running forward faster than this, empty hands swing with the run clip instead of holding the fists up (m/s).")]
    private float fistsRunSpeed = 3f;
    [SerializeField, Min(0f), Tooltip("After a punch, a charge, a guard or a guard break the fists stay up this long, running or not (s).")]
    private float fistsReadyTime = 1.5f;
    [SerializeField, Min(0f), Tooltip("Skin clearance at a ledge or bar, after measuring the character's finger pads (m).")]
    private float fingerSurfaceClearance = .001f;

    private enum Hold { Gun, Fists, Ledge, Ladder }
    private const int HoldCount = 4;
    // Moving at least this share of the speed along the body's facing counts as running forward.
    private const float RunAlignment = .7f;

    private PlayerMovement player;
    private PlayerBody body;
    private WeaponController weapons;
    private FistsView fists;
    private Animator boundAnimator;
    private ArmIK left, right;
    private readonly float[] weights = new float[HoldCount];
    private float fistsReady;

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
        left?.PrepareBarGrip(Ladder.BarThickness * .5f, ladderPalmClearance, fingerSurfaceClearance);
        right?.PrepareBarGrip(Ladder.BarThickness * .5f, ladderPalmClearance, fingerSurfaceClearance);
        System.Array.Clear(weights, 0, HoldCount);
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
        if (left == null || right == null) return;
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
        Ease(Hold.Gun, armed && weapons != null && weapons.ShowsWeapon && weapons.EquippedData != null, dt);
        Ease(Hold.Fists, armed && fists != null && fists.IsActive && FistsUp(dt), dt);
        Ease(Hold.Ledge, alive && state == MovementState.LedgeHang && player.Abilities.Traversal.Ledge.Surface != null, dt);
        bool onLadder = alive && state == MovementState.Ladder && player.Abilities.Fixtures.Ladder != null
            && player.Abilities.Fixtures.Ladder.isActiveAndEnabled;
        var exitLadder = player.Abilities.Traversal.Obstacle.ExitLadder;
        bool leavingLadder = alive && state == MovementState.Mantle && exitLadder != null && exitLadder.isActiveAndEnabled;
        UpdateLadder(onLadder, leavingLadder, dt);
        Ease(Hold.Ladder, onLadder || (leavingLadder && (LadderHandWeight(true) > 0f || LadderHandWeight(false) > 0f)), dt);

        Place(right, false);
        Place(left, true);
        CurlLedgeFingers(left, true);
        CurlLedgeFingers(right, false);
        CurlLadderFingers(left, true);
        CurlLadderFingers(right, false);
        if (alive && (state == MovementState.Vault || state == MovementState.Mantle)) RestOnObstacle();
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
            if (weight <= 0f || !Target((Hold)i, arm, isLeft, out Vector3 p, out Quaternion r, out Vector3 h)) continue;
            total += weight;
            position += p * weight;
            hint += h * weight;
            rotation = total <= weight ? r : Quaternion.Slerp(rotation, r, weight / total);
        }
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
                position = root.TransformPoint(isLeft ? data.LeftGrip : data.RightGrip) + ThirdPersonShift();
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
                position = fist.position + ThirdPersonShift();
                rotation = arm.HandRotation(fist.forward, -fist.up);
                hint = shoulder + side * .2f - forward * .05f - up * .35f;
                return true;
            }
            case Hold.Ledge:
                return LedgeHandTarget(arm, isLeft, out position, out rotation, out hint);
            case Hold.Ladder:
                return LadderTarget(arm, isLeft, out position, out rotation, out hint);
        }
        return false;
    }

    // In the F5 view the camera, and the gun and fist targets under it, sit behind the player; bring them back to
    // the eyes so the hands do not reach for the camera.
    private Vector3 ThirdPersonShift()
    {
        var view = body.View;
        return body.IsThirdPerson && view != null ? body.EyePosition - view.position : Vector3.zero;
    }

    // A hand that would sink into the obstacle's top rests on it instead.
    private void RestOnObstacle()
    {
        var surface = player.Abilities.Traversal.Obstacle.Surface;
        if (surface == null) return;
        Rest(left, true);
        Rest(right, false);

        void Rest(ArmIK arm, bool isLeft)
        {
            // The floor correction must not pull a supporting hand off its ladder rung during the top exit.
            if (weights[(int)Hold.Ladder] * LadderHandWeight(isLeft) > .001f) return;
            Vector3 wrist = arm.Hand.position;
            if (!surface.Raycast(new Ray(wrist + Vector3.up * .5f, Vector3.down), out RaycastHit top, .6f)) return;
            float lift = top.point.y + palmHeight - wrist.y;
            if (lift > 0f)
                arm.Solve(wrist + Vector3.up * lift, arm.Hand.rotation, wrist - transform.up * .3f + transform.forward * .1f, 1f, 0f, shoulderReach);
        }
    }
}
