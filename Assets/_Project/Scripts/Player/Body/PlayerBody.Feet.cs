using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The body's feet on the ground: FootPlanting (our GMod IK Foot port) with this body's table numbers, on while standing
/// or crouching on the ground, landings included, and before the eyes place the camera, so the view dips with the hips.
/// The feet after it are read by PlayerFootsteps (TryReadFeet), so steps sound when a foot really comes down.
/// Presentation only: nothing here moves the player. Numbers come from Tables/Player.csv, row Body.
/// </summary>
public sealed partial class PlayerBody
{
    /// <summary>One foot after this frame's leg IK, for footstep sounds.</summary>
    internal readonly struct FootReading
    {
        /// <summary>The ankle (the foot bone) in world space.</summary>
        public readonly Vector3 Ankle;
        /// <summary>World height of the ground under the foot (m).</summary>
        public readonly float Ground;
        /// <summary>What that ground is; null when the foot has no support.</summary>
        public readonly Collider Surface;
        public bool HasSupport => Surface != null;

        public FootReading(Vector3 ankle, float ground, Collider surface) { Ankle = ankle; Ground = ground; Surface = surface; }

        /// <summary>How far the ankle is above the ground under it (m).</summary>
        public float Height => Ankle.y - Ground;
        public Vector3 GroundPoint => new Vector3(Ankle.x, Ground, Ankle.z);
    }

    [Header("Feet on the ground")]
    [SerializeField, Min(0f), Tooltip("Furthest the hips drop to let a foot reach lower ground (m).")]
    private float maxHipDrop = .3f;
    [SerializeField, Min(.05f), Tooltip("How far above and below the capsule's floor each foot looks for ground (m).")]
    private float footReach = .5f;
    [SerializeField, Min(.01f), Tooltip("How quickly the hips and feet settle onto lower ground (s). Higher ground is taken at once.")]
    private float footSettleTime = .08f;
    [SerializeField, Range(0f, 60f), Tooltip("Most a planted foot tilts to match a slope (degrees).")]
    private float maxFootTilt = 30f;
    [SerializeField, Range(0f, 180f), Tooltip("Most a foot turns about the shin, either way, in any clip (degrees).")]
    private float maxAnkleTurn = 20f;
    [SerializeField, Range(0f, 180f), Tooltip("Most a foot rolls onto its side, either way, in any clip (degrees).")]
    private float maxAnkleRoll = 25f;

    private FootPlanting feet;

    // The legs as the feet measured them, for what else keeps them out of things (a tuck, a heave, a wall).
    private FootPlanting.Leg leftLeg => feet?.Left;
    private FootPlanting.Leg rightLeg => feet?.Right;

    /// <summary>The feet after this frame's leg IK, on the ground.</summary>
    internal bool TryReadFeet(out FootReading left, out FootReading right)
    {
        bool reading = feet != null && feet.OnGround && body != null;
        left = reading ? new FootReading(feet.Left.Foot.position, feet.Left.Ground, feet.Left.Surface) : default;
        right = reading ? new FootReading(feet.Right.Foot.position, feet.Right.Ground, feet.Right.Surface) : default;
        return reading;
    }

    // The body stands on the soles it draws, its root soleLift under the capsule's floor (StandOnSoles).
    private void SetUpFeet() => feet = FootPlanting.Measure(animator, body, transform, restForward, soleLift);

    private void PlantFeet(float dt)
    {
        if (feet == null) return;
        // A paired execution's clip has the feet where it was made beside the zombie (PlayerBody.Execution).
        bool onFeet = !dead && !playingOut && !heaving && !PlaysPaired && player.IsGrounded
            && (state == MovementState.Standing || state == MovementState.Crouching);
        // Running, the stride stretched to the ground the body covers (PlayerBody.Locomotion), so the feet don't skate.
        feet.Plant(onFeet, transform.position.y, player.groundLayers,
            new FootPlanting.Tuning(maxHipDrop, footReach, footSettleTime, maxFootTilt, maxAnkleTurn, maxAnkleRoll, blendTime), dt,
            onFeet ? StrideNow : FootPlanting.Stride.None);
    }

    // Select the player in the Scene view: the line is the ground each foot found, the sphere where its ankle goes.
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) feet?.DrawGizmos();
    }
}
