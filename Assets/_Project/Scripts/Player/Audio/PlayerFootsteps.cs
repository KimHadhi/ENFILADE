using System;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Footsteps from the body's own feet, the way GMod's Anim-Based Footsteps does it: a foot that has lifted clear of
/// the ground and comes back down is one step. Steps therefore land exactly when a foot does, at any speed, while
/// turning on the spot or shuffling out of a landing.
/// - Reads the feet after the leg IK (PlayerBody), each against the ground under it, standing, crouching or on a beam.
/// - Each foot measures its own last contact. A fresh upward excursion above the supporting foot arms it; returning
///   near that contact or passing the bottom of a full swing lands it. This survives different clip heights and
///   skipped minima at low fps.
/// - Standing still remeasures both contacts. Each foot steps at most once per minStepInterval.
/// - Plays the Step cues of Tables/SoundCues on the ground's surface: StepSneak crouched, StepWalk, StepRun when
///   faster; a foot lifting off at speed scuffs (StepRelease). Landings themselves sound from PlayerMovementAudio, so
///   the feet start out planted when they touch down.
/// Numbers: Player.csv row Footsteps.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(260)]
public sealed class PlayerFootsteps : MonoBehaviour
{
    [SerializeField, Min(.005f), Tooltip("Lift needed above this foot's last contact and the supporting foot's ground-relative height (m).")]
    private float liftHeight = .04f;
    [SerializeField, Min(0f), Tooltip("A lifted foot lands when its ankle comes back within this of its planted height (m).")]
    private float contactHeight = .02f;
    [SerializeField, Min(0f), Tooltip("A descending foot's rebound within this of its last contact also lands it (m). A full swing can follow a higher new contact.")]
    private float settleHeight = .08f;
    [SerializeField, Min(0f), Tooltip("Shortest time between two steps of one foot (s).")]
    private float minStepInterval = .1f;
    [SerializeField, Min(0f), Tooltip("Steps below this ground speed are walking steps, faster ones running steps (m/s). Crouched steps sneak.")]
    private float runSpeed = 3f;
    [SerializeField, Min(0f), Tooltip("A foot lifting off at this ground speed or faster scuffs the ground (m/s).")]
    private float releaseSpeed = 3f;
    [SerializeField, Min(.1f), Tooltip("Standing still this long measures the planted ankle height again (s).")]
    private float calibrateTime = .5f;

    /// <summary>One foot coming down.</summary>
    public readonly struct Step
    {
        public readonly bool Left;
        public readonly Vector3 Point;
        public readonly string Surface;
        /// <summary>Ground speed at the step (m/s).</summary>
        public readonly float Speed;

        public Step(bool left, Vector3 point, string surface, float speed) { Left = left; Point = point; Surface = surface; Speed = speed; }
    }

    /// <summary>A foot came down (for camera step punches and the like).</summary>
    public event Action<Step> Stepped;

    /// <summary>The surface of the last step, for the debug HUD.</summary>
    public string LastSurface { get; private set; } = Surfaces.Default;

    private struct Foot
    {
        public bool Lifted, Descending;
        public float Height, Low, Peak, Trough, Cooldown;

        public Foot(float height)
        {
            Height = Low = Peak = Trough = height;
            Cooldown = 0f;
            Lifted = Descending = false;
        }
    }

    // Ignore sub-millimetre wobble at the bottom; use displacement rather than a frame's velocity.
    private const float ContactRebound = .002f, StoppedSpeed = .03f;

    private PlayerMovement player;
    private PlayerBody body;
    private CharacterSounds sounds;
    private Foot left, right;
    private float stillTime;
    private bool tracking, trackingBeam;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        body = GetComponent<PlayerBody>();
    }

    private void OnDisable() => tracking = false;

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (GamePause.IsPaused || dt <= 0f || player == null || body == null) return;
        if (!body.TryReadFeet(out var leftFoot, out var rightFoot))
        {
            tracking = false;
            stillTime = 0f;
            return;
        }
        // Both ankles rise with the pelvis, especially in crouch clips. Only the higher foot can lift off.
        float supportHeight = Mathf.Min(leftFoot.Height, rightFoot.Height);
        bool onBeam = player.Abilities.Fixtures.BalanceActive;
        if (!tracking || onBeam != trackingBeam)
        {
            // Touching down or coming out of a vault: the feet start planted, the landing has its own sound.
            left = new Foot(leftFoot.Height);
            right = new Foot(rightFoot.Height);
            // An initially raised foot has not planted at its starting height. Both beam feet share this flat support.
            if (onBeam) left.Low = right.Low = Mathf.Min(supportHeight, body.RestAnkleHeight);
            trackingBeam = onBeam;
            tracking = true;
            return;
        }
        Calibrate(leftFoot.Height, rightFoot.Height, dt);
        Track(ref left, leftFoot, true, dt, supportHeight);
        Track(ref right, rightFoot, false, dt, supportHeight);
    }

    private void Track(ref Foot foot, in PlayerBody.FootReading reading, bool isLeft, float dt, float supportHeight)
    {
        float height = reading.Height;
        foot.Height = height;
        foot.Cooldown -= dt;
        if (!foot.Lifted)
        {
            foot.Low = Mathf.Min(foot.Low, height);
            if (height - foot.Low < liftHeight || height - supportHeight < liftHeight) return;
            foot.Lifted = true;
            foot.Descending = false;
            foot.Peak = foot.Trough = height;
            LiftOff(reading);
            return;
        }
        foot.Peak = Mathf.Max(foot.Peak, height);
        if (!foot.Descending)
        {
            if (foot.Peak - height < contactHeight) return;
            foot.Descending = true;
            foot.Trough = height;
        }
        foot.Trough = Mathf.Min(foot.Trough, height);
        // A sampled frame may jump straight from descending to rising. Keep the trough instead of clearing descent.
        // A full swing can also settle above the old contact when a new gait has a higher ankle baseline.
        bool settled = height - foot.Trough >= ContactRebound
            && (foot.Trough - foot.Low <= settleHeight || foot.Peak - foot.Trough >= liftHeight);
        bool down = height - foot.Low <= contactHeight || settled;
        if (!down || foot.Cooldown > 0f) return;
        foot.Lifted = false;
        foot.Descending = false;
        foot.Low = foot.Trough;
        foot.Cooldown = minStepInterval;
        Land(reading, isLeft);
    }

    private void Land(in PlayerBody.FootReading reading, bool isLeft)
    {
        float speed = GroundSpeed();
        string surface = Surfaces.Of(reading.Surface);
        string cue = player.CurrentState == MovementState.Crouching ? "StepSneak" : speed < runSpeed ? "StepWalk" : "StepRun";
        Vector3 point = reading.GroundPoint;
        LastSurface = surface;
        var fixtures = player.Abilities.Fixtures;
        // Walking beams use the fixture's step cue; running retains its ordinary footstep sound.
        if (player.CurrentState != MovementState.Balance) Sounds()?.Play(cue, point, surface);
        if (fixtures.BalanceActive) fixtures.BalanceFootDown(isLeft);
        Stepped?.Invoke(new Step(isLeft, point, surface, speed));
    }

    private void LiftOff(in PlayerBody.FootReading reading)
    {
        if (player.CurrentState == MovementState.Crouching || GroundSpeed() < releaseSpeed) return;
        Sounds()?.Play("StepRelease", reading.GroundPoint, Surfaces.Of(reading.Surface));
    }

    // Each foot has its own contact height; idle and locomotion clips need not match the model's rest pose.
    private void Calibrate(float leftHeight, float rightHeight, float dt)
    {
        bool still = player.CurrentState == MovementState.Standing && GroundSpeed() < .1f
            && Mathf.Abs(leftHeight - left.Height) < StoppedSpeed * dt && Mathf.Abs(rightHeight - right.Height) < StoppedSpeed * dt;
        stillTime = still ? stillTime + dt : 0f;
        if (stillTime < calibrateTime) return;
        left = new Foot(leftHeight);
        right = new Foot(rightHeight);
    }

    private float GroundSpeed() => new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;

    private CharacterSounds Sounds() => sounds != null ? sounds : sounds = GetComponent<CharacterSounds>();
}
