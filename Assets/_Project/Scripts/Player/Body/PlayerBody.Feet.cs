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
    [SerializeField, Range(1f, 90f), Tooltip("A kept foot steps once the legs have turned this far over it (degrees), when no turn on the spot leads the steps.")]
    private float footHoldTwist = 25f;
    [SerializeField, Min(.05f), Tooltip("How long a foot's step takes, up off its place and down in its new one (s).")]
    private float footStepTime = .22f;
    [SerializeField, Min(0f), Tooltip("How high a stepping foot comes up at the middle of its step (m).")]
    private float footStepHeight = .08f;
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

    // What moved the hips sideways this frame that is no step (the lean), for the feet to take back out.
    private Vector3 hipsCarried;

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
        Vector3 travel = player.ActualVelocity;
        // The legs turning on the spot no longer carry the feet round with them (as up to 0.9.278, over 40° a second): the
        // feet stay where they stand and step round themselves (FootPlanting.Stand), at any speed of turn.
        // By the body's one still-or-moving flag (Locomotion: a gap between its two speeds), and never through a run's turn,
        // whose clip steps the feet itself as it brakes and sets off. Until 0.9.403 the feet had a speed of their own
        // (Body.footHoldSpeed .15 m/s, no gap), and a run's turn braking through it switched them between standing (the
        // ground read under each foot) and walking (the ground each came down on) frame by frame: on stairs a foot's ground
        // went -34, -2, -18 cm in three frames (the feet record of 2026-11-24 21:3x; the user: "급하게 turn 해서 turn 애니메이션
        // 나오고 다시 걷기 시작할 때 발이 … 살짝 꺾이는").
        bool stands = onFeet && still && turnClip == null;
        var hold = new FootPlanting.Hold(stands, hipsCarried, yaw, footHoldTwist, footHoldDrop, footHoldRelease, footStepTime, footStepHeight,
            turnPlanner.Lead, turnPlanner.Share);
        // Running, the stride stretched to the ground the body covers (PlayerBody.Locomotion), so the feet don't skate.
        // Walking or running, each foot keeps the ground it came down on and swings to where it will come down, timed by the
        // stride (FootPlanting.Walk).
        feet.Plant(onFeet, transform.position.y, player.groundLayers,
            new FootPlanting.Tuning(maxHipDrop, footReach, footSettleTime, maxFootTilt, maxAnkleTurn, maxAnkleRoll, blendTime), dt,
            onFeet ? StrideNow : FootPlanting.Stride.None, hold, new FootPlanting.Steps(travel, strideCycle, phase));
        RecordFeet(dt, onFeet);
    }

    // 발 기록 (Logs/feet.txt, "발 기록 봐 줘"): a second, a line a frame, from when the body runs at FeetRecordSpeed or faster or
    // the capsule's floor jumps FeetRecordPop or more in a frame (a step up or down a stair): what the feet stood on and how the
    // hips and the eyes followed, so a foot that jumps, slides or can't reach shows in numbers without playing it (the user,
    // 2026-11-24: "달리다가 … 발이 텔레포트하거나 디딜 곳을 못 찾거나 … 계단을 뛰어 오를 때"; the experts' review).
    private const string FeetRecord = "feet";
    private const string FeetRecordHeading = "발 기록 (PlayerBody.RecordFeet): 4m/s 넘게 달리거나 캡슐 바닥이 한 프레임에 3cm 넘게 뛸 때부터 1초 동안 "
        + "프레임마다 한 줄: 지난 초, 속도(m/s), 캡슐 바닥 높이와 이 프레임 변화(cm), 고른 바닥과 엉덩이 바닥이 캡슐 바닥에서 떨어진 정도(cm), "
        + "보폭 때문에 내린 엉덩이(cm); 발마다 디딤(D)·흔들림(S), 땅 높이(캡슐 바닥 기준 cm)와 이 프레임 변화(cm), 발목 목표가 땅에서 뜬 높이(cm), "
        + "IK 뒤 실제 발목이 목표에서 벗어난 거리(cm), 엉덩이에서 목표까지가 다리 길이의 몇 %인지, 받침 비율, 디딘 동안 실제 발목이 땅 위로 "
        + "미끄러진 빠르기(cm/s); 지난 프레임 카메라 높이 변화(cm)";
    private const float FeetRecordFor = 1f, FeetRecordSpeed = 4f, FeetRecordPop = .03f;
    private float feetRecordLeft, feetRecordFrom, feetFloorWas, feetCameraWas, leftGroundWas, rightGroundWas;
    private Vector3 leftAnkleWas, rightAnkleWas;
    private bool feetRecordKnown;
    private readonly System.Text.StringBuilder feetLine = new System.Text.StringBuilder();

    private void RecordFeet(float dt, bool onFeet)
    {
        if (!PlayRecord.On || feet == null || player == null || !player.IsLocal || dt <= 0f) return;
        float floor = transform.position.y, pop = feetRecordKnown ? floor - feetFloorWas : 0f;
        float speed = new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;
        float camera = bodyCamera.HasPlace ? bodyCamera.Placed.y : 0f;
        if (feetRecordLeft <= 0f && onFeet && (speed >= FeetRecordSpeed || Mathf.Abs(pop) >= FeetRecordPop))
        {
            feetRecordLeft = FeetRecordFor;
            feetRecordFrom = Time.time;
            PlayRecord.Line(FeetRecord, FeetRecordHeading, $"-- {state} {speed:0.0} m/s, 캡슐 {pop * 100f:+0;-0}cm");
        }
        if (feetRecordLeft > 0f && feetRecordKnown)
        {
            feetRecordLeft -= dt;
            feetLine.Clear().Append($"+{Time.time - feetRecordFrom:0.00} {speed:0.0}m/s 캡슐 {floor:0.00} {pop * 100f:+0;-0}cm ")
                .Append($"고른 {(feet.SmoothFloor - floor) * 100f:+0;-0} 엉덩이 {(feet.HipFloor - floor) * 100f:+0;-0} 보폭 {feet.StrideDrop * 100f:0}");
            Leg("왼", feet.Left, leftGroundWas, leftAnkleWas);
            Leg("오", feet.Right, rightGroundWas, rightAnkleWas);
            feetLine.Append($" | 카메라 {(camera - feetCameraWas) * 100f:+0;-0}cm");
            PlayRecord.Line(FeetRecord, FeetRecordHeading, feetLine.ToString());
        }
        feetFloorWas = floor;
        feetCameraWas = camera;
        leftGroundWas = feet.Left.Ground;
        rightGroundWas = feet.Right.Ground;
        leftAnkleWas = feet.Left.Foot.position;
        rightAnkleWas = feet.Right.Foot.position;
        feetRecordKnown = true;

        void Leg(string name, FootPlanting.Leg leg, float groundWas, Vector3 ankleWas)
        {
            Vector3 ankle = leg.Foot.position;
            float length = (leg.Lower.position - leg.Upper.position).magnitude + (ankle - leg.Lower.position).magnitude;
            float reach = (leg.Target - leg.Upper.position).magnitude / Mathf.Max(.01f, length);
            Vector3 slid = ankle - ankleWas;
            float slip = leg.Down ? new Vector2(slid.x, slid.z).magnitude / dt : 0f;
            feetLine.Append($" | {name} {(leg.Down ? "D" : "S")} 땅 {(leg.Ground - floor) * 100f:+0;-0} {(leg.Ground - groundWas) * 100f:+0;-0} ")
                .Append($"목표 {(leg.Target.y - leg.Ground) * 100f:0} 어긋남 {(ankle - leg.Target).magnitude * 100f:0} 닿음 {reach * 100f:0}% ")
                .Append($"받침 {leg.SupportWeight:0.0} 미끄러짐 {slip * 100f:0}");
        }
    }

    // Select the player in the Scene view: the line is the ground each foot found, the sphere where its ankle goes.
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) feet?.DrawGizmos();
    }
}
