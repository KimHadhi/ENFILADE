using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Beatrun's movement view punches, on the Source engine's punch spring.
/// - Each footstep nods the view a little and rolls it toward the foot coming down, so the roll alternates. Steps roll
///   less the faster the run, and kick harder while the run is still building speed. A gun in the hands steadies the
///   nod, and the roll too unless sprinting with Shift. Crouched steps don't punch.
/// - A jump while moving forward tips the view up. A landing nods it down and rolls it, 2:1, by the fall speed; a hard
///   or heavy landing adds Beatrun's hard-land jolt.
/// - Each ladder rung rolls the view toward the hand going up. Catching a ladder in the air jolts it, and stopping a
///   slide down one nods it.
/// - Balance beam timing uses a steady view; its input judgments never inject a camera punch.
/// PlayerCameraEffects adds Angles to the camera with its other effects, scaled by the Esc menu's screen shake.
/// Numbers: Player.csv row Punch.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerMovementPunch : MonoBehaviour
{
    [Header("Spring")]
    [SerializeField, Min(0f), Tooltip("Pull back to rest (Source: 65).")]
    private float stiffness = 65f;
    [SerializeField, Min(0f), Tooltip("Share of the turning speed lost per second (Source: 9).")]
    private float damping = 9f;
    [SerializeField, Min(0f), Tooltip("A punch of 1 degree starts the view turning this fast (deg/s) (Source: 20).")]
    private float kickSpeed = 20f;
    [SerializeField, Min(0f), Tooltip("Scales every movement punch (Beatrun's view bob intensity, 1 = 20).")]
    private float intensity = 1f;

    [Header("Steps")]
    [SerializeField, Tooltip("Step punch: nod (down); roll (toward the foot) in degrees.")]
    private Vector2 stepPunch = new Vector2(.45f, .5f);
    [SerializeField, Tooltip("Step roll factor at a standstill; at top run speed.")]
    private Vector2 stepRollBySpeed = new Vector2(1.25f, .5f);
    [SerializeField, Tooltip("Step strength while the run builds speed; at top run speed.")]
    private Vector2 stepStrength = new Vector2(.5f, .25f);
    [SerializeField, Tooltip("Share of the step nod; roll left with a gun out. The roll stays whole while sprinting with Shift.")]
    private Vector2 armedStep = new Vector2(.25f, .25f);

    [Header("Jumps and landings")]
    [SerializeField, Tooltip("A jump while moving forward tips the view by this (degrees; negative looks up).")]
    private float jumpPitch = -2f;
    [SerializeField, Min(0f), Tooltip("Slower jumps don't tip the view (m/s).")]
    private float jumpMinSpeed = 1.9f;
    [SerializeField, Tooltip("Landing punch per landingPerSpeed: nod (down); roll in degrees.")]
    private Vector2 landPunch = new Vector2(3f, 1.5f);
    [SerializeField, Min(0f), Tooltip("Landing punch scale per m/s of fall speed. A 1.3 m jump lands at about 8 m/s.")]
    private float landingPerSpeed = .09f;
    [SerializeField, Min(0f), Tooltip("Gentler touchdowns don't punch (m/s).")]
    private float landMinSpeed = 1.5f;
    [SerializeField, Tooltip("A hard or heavy landing (MovementLanding's tier) kicks this on top: nod (down); turn (right); "
        + "roll in degrees. Beatrun's hard land: 10, 2, 5.")]
    private Vector3 hardLandPunch = new Vector3(10f, 2f, 5f);

    [Header("Ladders")]
    [SerializeField, Tooltip("Each rung rolls the view by this, its side by the hand going up (degrees).")]
    private float ladderStepRoll = 1.5f;
    [SerializeField, Tooltip("Catching a ladder in the air kicks this: nod (down); turn (right); roll in degrees. Beatrun's: 15, 5, -15.")]
    private Vector3 ladderCatchPunch = new Vector3(15f, 5f, -15f);
    [SerializeField, Tooltip("Stopping a slide down a ladder nods the view by this (degrees).")]
    private float ladderSlideStopNod = 5f;

    // Source clamps the punch short of turning the view over.
    private const float MaxAngle = 89f, RestEnergy = 1e-6f;

    private PlayerMovement player;
    private PlayerFootsteps footsteps;
    private WeaponController weapons;
    private Vector3 angles, velocity;
    private float side = 1f;

    /// <summary>The punch this frame in degrees: x nods down, y turns right, z rolls.</summary>
    public Vector3 Angles => angles;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void OnEnable()
    {
        if (player == null) return;
        player.Landed += OnLanded;
        player.Jumped += OnJumped;
        player.StateChanged += OnStateChanged;
        player.LadderStepped += OnLadderStepped;
        player.LadderSlideStopped += OnLadderSlideStopped;
        footsteps = GetComponent<PlayerFootsteps>();
        if (footsteps != null) footsteps.Stepped += OnStepped;
    }

    private void OnDisable()
    {
        if (player != null)
        {
            player.Landed -= OnLanded;
            player.Jumped -= OnJumped;
            player.StateChanged -= OnStateChanged;
            player.LadderStepped -= OnLadderStepped;
            player.LadderSlideStopped -= OnLadderSlideStopped;
        }
        if (footsteps != null) footsteps.Stepped -= OnStepped;
        angles = velocity = Vector3.zero;
    }

    /// <summary>Kicks the view by these degrees (x nods down, y turns right, z rolls); the spring brings it back.</summary>
    public void Kick(Vector3 degrees) => velocity += degrees * (kickSpeed * intensity);

    private void Update()
    {
        float dt = Time.deltaTime;
        if (GamePause.IsPaused || dt <= 0f) return;
        if (angles.sqrMagnitude + velocity.sqrMagnitude <= RestEnergy)
        {
            angles = velocity = Vector3.zero;
            return;
        }
        // Source's DecayPunchAngle: move, damp, then spring back.
        angles += velocity * dt;
        velocity *= Mathf.Max(0f, 1f - damping * dt);
        velocity -= angles * Mathf.Clamp(stiffness * dt, 0f, 2f);
        angles = new Vector3(Mathf.Clamp(angles.x, -MaxAngle, MaxAngle), Mathf.Clamp(angles.y, -MaxAngle, MaxAngle),
            Mathf.Clamp(angles.z, -MaxAngle, MaxAngle));
    }

    private void OnStepped(PlayerFootsteps.Step step)
    {
        side = step.Left ? 1f : -1f;
        if (player.CurrentState == MovementState.Crouching || player.Abilities.Fixtures.BalanceActive) return;
        float top = player.TopRunSpeed;
        float share = Mathf.Clamp01(step.Speed / Mathf.Max(.1f, top));
        float strength = player.RunSpeed < top - .25f ? stepStrength.x : stepStrength.y;
        float roll = Mathf.Lerp(stepRollBySpeed.x, stepRollBySpeed.y, share);
        float nodShare = 1f, rollShare = 1f;
        if (Armed())
        {
            nodShare = armedStep.x;
            if (player.SprintProgress < .5f) rollShare = armedStep.y;
        }
        Kick(new Vector3(stepPunch.x * nodShare, 0f, stepPunch.y * roll * rollShare * side) * strength);
    }

    private void OnJumped()
    {
        Vector3 run = player.HorizontalVelocity;
        if (run.magnitude < jumpMinSpeed || Vector3.Dot(run, transform.forward) < 0f) return;
        Kick(new Vector3(jumpPitch, 0f, 0f));
    }

    // A landing roll pitches the view on its own. A hard or heavy one jolts it on top of the usual landing punch.
    private void OnLanded(float impact)
    {
        if (impact < landMinSpeed || player.CurrentState == MovementState.LandingRoll) return;
        float scale = impact * landingPerSpeed;
        Kick(new Vector3(landPunch.x, 0f, landPunch.y * side) * scale);
        if (player.LastLandingTier != MovementLanding.Tier.Light)
            Kick(new Vector3(hardLandPunch.x, hardLandPunch.y, hardLandPunch.z * side));
        side = -side;
    }

    private void OnStateChanged(MovementState from, MovementState to)
    {
        if (to == MovementState.Ladder && player.Abilities.Fixtures.LadderCaught) Kick(ladderCatchPunch);
    }

    private void OnLadderStepped(bool leftHand) => Kick(new Vector3(0f, 0f, leftHand ? ladderStepRoll : -ladderStepRoll));

    private void OnLadderSlideStopped() => Kick(new Vector3(ladderSlideStopNod, 0f, 0f));

    private bool Armed()
    {
        if (weapons == null) weapons = GetComponent<WeaponController>();
        return weapons != null && weapons.EquippedData != null;
    }
}
