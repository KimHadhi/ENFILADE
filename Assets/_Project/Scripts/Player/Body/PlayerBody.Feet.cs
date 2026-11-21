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

    [Header("Feet keep their place")]
    [SerializeField, Min(0f), Tooltip("The body stands (its feet keep their places) while it travels slower than this over the ground (m/s).")]
    private float footHoldSpeed = .15f;
    [SerializeField, Min(0f), Tooltip("... and its legs turn slower than this (degrees a second). Legs turning quicker carry the feet round with them, as before.")]
    private float footHoldTurnRate = 40f;
    [SerializeField, Range(1f, 90f), Tooltip("A kept foot lets go once the legs have turned this far over it (degrees), and takes its new place: a step.")]
    private float footHoldTwist = 25f;
    [SerializeField, Min(0f), Tooltip("The hips come down at most this far for a leg to reach a kept foot they have gone over away from (m). Further, the foot lets go.")]
    private float footHoldDrop = .05f;
    [SerializeField, Min(.01f), Tooltip("How long a foot that let go takes to ease to where the clip has it (s).")]
    private float footHoldRelease = .12f;

    [Header("What the feet stand in")]
    [SerializeField, Min(0f), Tooltip("Heels at least this high (m, at the size the body is drawn) sound like heels: the feet play "
        + "the '@Heels' versions of their cues in SoundCues.csv. Measured from the dressed look (DrawnHeels), any character or shoe.")]
    private float heelsFrom = .03f;

    private FootPlanting feet;
    // How high the heels the body stands in are, as dressing left it (m at its size; 0 for flat shoes and bare feet).
    private float heelHeight;

    /// <summary>How high the heels this body stands in are (m), measured as it was dressed; 0 for flat shoes and bare feet.</summary>
    internal float HeelHeight => heelHeight;
    /// <summary>What the feet stand in, as the sounds name it (DrawnHeels.Footwear), or null for ordinary shoes.</summary>
    internal string Footwear => DrawnHeels.FootwearOf(heelHeight, heelsFrom);

    // Reads the heels off the look as dressed, before any clip has posed it (Build), and has the feet sound like them. A
    // character's own shoes, the wardrobe's and a modder's avatar are read alike, so nothing is listed per shoe (the user,
    // 2026-11-21: "기본이 하이힐인 애들도 있을 거 아니야 … 모드로 캐릭터를 추가할 거라서 알아서").
    private void MeasureHeels()
    {
        heelHeight = body != null ? DrawnHeels.Of(body, animator) * body.localScale.y : 0f;
        if (TryGetComponent(out CharacterSounds sounds)) sounds.Footwear = Footwear;
    }

    // What moved the hips sideways this frame that is no step (the lean), for the feet to take back out; and the legs'
    // yaw last frame, for how fast they turn.
    private Vector3 hipsCarried;
    private float lastFeetYaw;
    private bool hasFeetYaw;

    /// <summary>
    /// How far each ankle is from the place it keeps, level (m), after this frame's leg IK; zero for a foot that keeps
    /// none. And whether it keeps one. For the console's /lean, which measures that a lean does not carry the feet.
    /// </summary>
    internal bool TryReadKeptFeet(out bool leftKept, out bool rightKept, out float leftOff, out float rightOff)
    {
        leftKept = rightKept = false;
        leftOff = rightOff = 0f;
        if (feet == null || !feet.OnGround) return false;
        leftKept = feet.Left.Kept;
        rightKept = feet.Right.Kept;
        leftOff = leftKept ? Level(feet.Left.Foot.position - feet.Left.Place) : 0f;
        rightOff = rightKept ? Level(feet.Right.Foot.position - feet.Right.Place) : 0f;
        return true;

        static float Level(Vector3 way) => new Vector2(way.x, way.z).magnitude;
    }

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
        // The body stands while it neither travels nor turns: then each foot that is down keeps its place on the ground,
        // whatever moves the hips over it (FootPlanting.Hold). What the lean moved the hips by is taken out of the feet
        // in any case. The turn is the legs' own, the model's: standing, they keep their facing while the view turns
        // (TurnOnTheSpot) and the feet under them stay; the player's own facing is the view's and says nothing of the feet.
        float yaw = body.eulerAngles.y;
        float turnRate = hasFeetYaw && dt > 0f ? Mathf.Abs(Mathf.DeltaAngle(lastFeetYaw, yaw)) / dt : 0f;
        lastFeetYaw = yaw;
        hasFeetYaw = true;
        Vector3 travel = player.ActualVelocity;
        // A turn on the spot's clip steps the feet round itself (TurnOnTheSpot): kept feet fought its steps, held where its
        // lead-in and settle turn slowly and let go where its middle turns fast, so the feet stuck and jumped.
        bool stands = onFeet && new Vector2(travel.x, travel.z).magnitude < footHoldSpeed && turnRate < footHoldTurnRate
            && stepping == null;
        var hold = new FootPlanting.Hold(stands, hipsCarried, yaw, footHoldTwist, footHoldDrop, footHoldRelease);
        // Running, the stride stretched to the ground the body covers (PlayerBody.Locomotion), so the feet don't skate.
        feet.Plant(onFeet, transform.position.y, player.groundLayers,
            new FootPlanting.Tuning(maxHipDrop, footReach, footSettleTime, maxFootTilt, maxAnkleTurn, maxAnkleRoll, blendTime), dt,
            onFeet ? StrideNow : FootPlanting.Stride.None, hold);
    }

    // Select the player in the Scene view: the line is the ground each foot found, the sphere where its ankle goes.
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) feet?.DrawGizmos();
    }
}
