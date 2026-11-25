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

    [SerializeField, Min(0f), Tooltip("The hips come down at most this far for a leg to reach where the lean's shift left its foot (m).")]
    private float footHoldDrop = .05f;
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

    /// <summary>The legs as the feet measured them, after this frame's leg IK (MoveTest reads their soles).</summary>
    internal bool TryReadLegs(out FootPlanting.Leg left, out FootPlanting.Leg right)
    {
        left = feet?.Left;
        right = feet?.Right;
        return left != null && right != null && feet.OnGround;
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
        // The body stands while it neither travels nor turns: the feet read the ground under them (FootPlanting.Probe), and
        // never through a run's turn, whose clip steps the feet as it brakes and sets off. What the lean moved the hips by is
        // taken out of the feet in any case. By the body's one still-or-moving flag (Locomotion: a gap between its two
        // speeds). Until 0.9.403 the feet had a speed of their own (Body.footHoldSpeed .15 m/s, no gap), and a run's turn
        // braking through it switched them between standing and walking frame by frame: on stairs a foot's ground went -34,
        // -2, -18 cm in three frames (the feet record of 2026-11-24 21:3x). Every foot is where its clip has it (0.9.430;
        // until then a foot that was down kept its place and stepped by itself, FootPlanting).
        bool stands = onFeet && still && turnClip == null;
        var stance = new FootPlanting.Stance(stands, hipsCarried, footHoldDrop);
        // The drawn body's travel: the capsule's and the slack's (PlayerBody.Slack).
        Vector3 travel = player.ActualVelocity + SlackSpeed;
        // Where the clip had the ankles before the ground moved them, for the feet record.
        leftClipAnkle = feet.Left.Foot.position;
        rightClipAnkle = feet.Right.Foot.position;
        feet.Plant(onFeet, transform.position.y, player.groundLayers,
            new FootPlanting.Tuning(maxHipDrop, footReach, footSettleTime, maxFootTilt, maxAnkleTurn, maxAnkleRoll, blendTime), dt,
            stance, new FootPlanting.Steps(travel, strideCycle, phase));
        RecordFeet(dt, onFeet);
    }

    // 발 기록 (Logs/feet.txt, "발 기록 봐 줘"): a line a frame while the legs walk or run or the keys ask them to (and a second
    // after), and for a second from when the body runs at FeetRecordSpeed or faster, the capsule's floor jumps FeetRecordPop or
    // more in a frame (a step up or down a stair) or a run's turn or a turn on the spot plays: what the feet stood on, how the hips and the eyes
    // followed, and what the legs looked like and what made them so (the clips and the switch easing between them; each foot
    // off its hip, its knee's bend, which way it points, how far the IK moved it off the clip's place), so a foot that jumps or
    // slides or a leg that looks wrong shows in numbers without playing it (the user, 2026-11-24: "달리다가 … 발이 텔레포트하거나
    // 디딜 곳을 못 찾거나 … 계단을 뛰어 오를 때"; the experts' review). A foot slides when the end of its sole that is on the
    // ground (its heel or its toe tip, within SoleOnGround of the ground under it) goes over it. Until 0.9.426 the slide was the
    // ankle's while the clip's sole was low, which a foot rolling from heel to toe on its place moves too, and walking and
    // jogging (WASD under 4 m/s) were not recorded at all: the record read a third of the frames as sliding, 0.9.425 rebuilt
    // the movement on that and the legs went wrong (the user, 2026-11-25: "발목? 걍 다리가 존나 이상했는데 너 문제 파악을
    // 존나 확실히 잘 해야 할 거야").
    private const string FeetRecord = "feet";
    private const string FeetRecordHeading = "발 기록 (PlayerBody.RecordFeet): 다리가 걷거나 달리거나 키가 다리를 부르는 동안(그리고 멈춘 뒤 1초), "
        + "4m/s 넘게 달리거나 캡슐 바닥이 한 프레임에 3cm 넘게 뛰거나 달리다 돌기·제자리 돌기가 나올 때부터 1초 동안 프레임마다 한 줄: 지난 초, 속도(m/s), "
        + "키가 다리에게 가라는 쪽·다리가 가는 쪽(발을 디딜 때 바뀜)·실제로 가는 쪽·몸(골반)이 보는 쪽(시선 기준 도), 시선이 도는 빠르기(도/초), 걸음 자리(0 왼발 디딤, .5 오른발), "
        + "클립 사이를 잇는 관성이 남은 시간(초), 무게가 큰 클립 셋; 캡슐 바닥 높이와 이 프레임 변화(cm), 고른 바닥과 엉덩이 바닥이 캡슐 바닥에서 "
        + "떨어진 정도(cm), 다리가 닿게 내린 엉덩이(cm); 발마다 디딤(D)·흔들림(S), 땅에 닿은 발바닥 끝(뒤꿈치·발끝, 땅에서 2cm 안이 아니면 뜸)과 "
        + "그 끝이 땅에서 뜬 높이(cm), 그 끝이 땅 위로 미끄러진 빠르기(cm/s, 지난 프레임에도 같은 끝이 닿아 있었을 때만), 발목이 제 엉덩이 "
        + "관절에서 앞(+)·바깥(+)으로 떨어진 거리(cm), 무릎이 굽은 각도(0 곧음), 발끝이 몸이 보는 쪽에서 바깥(+)으로 돈 각도, IK가 발목을 "
        + "클립 자리에서 옮긴 거리(cm), 땅 높이(캡슐 바닥 기준 cm)와 이 프레임 변화(cm), 발목 목표가 땅에서 뜬 높이(cm), IK 뒤 실제 발목이 "
        + "목표에서 벗어난 거리(cm), 엉덩이에서 목표까지가 다리 길이의 몇 %인지; 지난 프레임 카메라 높이 변화(cm)";
    // A sole's end within this of the ground under it is on the ground (m).
    private const float FeetRecordFor = 1f, FeetRecordSpeed = 4f, FeetRecordPop = .03f, SoleOnGround = .02f;
    private float feetRecordLeft, feetRecordFrom, feetFloorWas, feetCameraWas, feetViewWas, leftGroundWas, rightGroundWas;
    // Where the clip had each ankle before the feet were planted this frame; where the end of each sole on the ground was last
    // frame, and which end (0 none, 1 the heel, 2 the toe tip).
    private Vector3 leftClipAnkle, rightClipAnkle, leftSoleWas, rightSoleWas;
    private int leftSoleEndWas, rightSoleEndWas;
    private bool feetRecordKnown;
    private readonly System.Text.StringBuilder feetLine = new System.Text.StringBuilder();

    private void RecordFeet(float dt, bool onFeet)
    {
        if (!PlayRecord.On || feet == null || player == null || !player.IsLocal || dt <= 0f) return;
        float floor = transform.position.y, pop = feetRecordKnown ? floor - feetFloorWas : 0f;
        float speed = new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;
        float camera = bodyCamera.HasPlace ? bodyCamera.Placed.y : 0f;
        float view = transform.eulerAngles.y, viewTurn = feetRecordKnown ? Mathf.DeltaAngle(feetViewWas, view) / dt : 0f;
        int leftEnd = SoleEnd(feet.Left, leftSoleEndWas, out Vector3 leftSole, out float leftOver);
        int rightEnd = SoleEnd(feet.Right, rightSoleEndWas, out Vector3 rightSole, out float rightOver);
        // The legs walk or run or the keys ask them to: every frame, and a second after. Anything else starts a second.
        bool walks = walkedFrame == Time.frameCount || player.StrideFrame == Time.frameCount && player.Stride.sqrMagnitude > .01f;
        string why = walks ? "걷기·달리기" : turnClip != null ? "달리다 돌기" : standTurn != null ? "제자리 돌기" : speed >= FeetRecordSpeed ? "빠르게 달리기"
            : Mathf.Abs(pop) >= FeetRecordPop ? "캡슐 바닥 뜀" : null;
        if (onFeet && why != null)
        {
            if (feetRecordLeft <= 0f)
            {
                feetRecordFrom = Time.time;
                PlayRecord.Line(FeetRecord, FeetRecordHeading, $"-- {why}: {state} {speed:0.0} m/s, 캡슐 {pop * 100f:+0;-0}cm");
            }
            if (walks || feetRecordLeft <= 0f) feetRecordLeft = FeetRecordFor;
        }
        if (feetRecordLeft > 0f && feetRecordKnown)
        {
            feetRecordLeft -= dt;
            feetLine.Clear().Append($"+{Time.time - feetRecordFrom:0.00} {speed:0.0}m/s 키 {HeadingOf(player.Stride):0}° ")
                .Append($"다리 {travelHeading:0}° 가는 쪽 {HeadingOf(player.ActualVelocity):0}° 몸 {yaw:0}° 시선 {viewTurn:0}°/s 걸음 {phase:0.00} ")
                .Append($"관성 {bodyInertia.Left:0.00} |");
            AppendTopClips(feetLine);
            feetLine.Append($" | 캡슐 {floor:0.00} {pop * 100f:+0;-0}cm ")
                .Append($"고른 {(feet.SmoothFloor - floor) * 100f:+0;-0} 엉덩이 {(feet.HipFloor - floor) * 100f:+0;-0} 내림 {feet.ReachDropNow * 100f:0}");
            Leg("왼", feet.Left, true, leftGroundWas, leftClipAnkle, leftEnd, leftSole, leftOver, leftSoleEndWas, leftSoleWas);
            Leg("오", feet.Right, false, rightGroundWas, rightClipAnkle, rightEnd, rightSole, rightOver, rightSoleEndWas, rightSoleWas);
            feetLine.Append($" | 카메라 {(camera - feetCameraWas) * 100f:+0;-0}cm");
            PlayRecord.Line(FeetRecord, FeetRecordHeading, feetLine.ToString());
        }
        feetFloorWas = floor;
        feetCameraWas = camera;
        feetViewWas = view;
        leftGroundWas = feet.Left.Ground;
        rightGroundWas = feet.Right.Ground;
        leftSoleWas = leftSole;
        rightSoleWas = rightSole;
        leftSoleEndWas = leftEnd;
        rightSoleEndWas = rightEnd;
        feetRecordKnown = true;

        void Leg(string name, FootPlanting.Leg leg, bool left, float groundWas, Vector3 clipAnkle, int end, Vector3 sole, float over,
            int endWas, Vector3 soleWas)
        {
            Vector3 ankle = leg.Foot.position;
            float length = (leg.Lower.position - leg.Upper.position).magnitude + (ankle - leg.Lower.position).magnitude;
            float reach = (leg.Target - leg.Upper.position).magnitude / Mathf.Max(.01f, length);
            string slid = end != 0 && end == endWas ? (new Vector2(sole.x - soleWas.x, sole.z - soleWas.z).magnitude / dt * 100f).ToString("0") : "-";
            // Where the foot is off its own hip and which way it points, in the frame the hips face; outward is + on either side.
            var facing = Quaternion.Euler(0f, body.eulerAngles.y, 0f);
            Vector3 off = Quaternion.Inverse(facing) * (ankle - leg.Upper.position);
            float outward = left ? -1f : 1f;
            float knee = Vector3.Angle(leg.Lower.position - leg.Upper.position, ankle - leg.Lower.position);
            Vector3 toes = Vector3.ProjectOnPlane(leg.Foot.TransformPoint(leg.Tip) - leg.Foot.TransformPoint(leg.Heel), Vector3.up);
            float points = toes.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(facing * Vector3.forward, toes, Vector3.up) * outward : 0f;
            feetLine.Append($" | {name} {(leg.Down ? "D" : "S")} {(end == 1 ? "뒤꿈치" : end == 2 ? "발끝" : "뜸")} {over * 100f:0} 미끄러짐 {slid} ")
                .Append($"앞 {off.z * 100f:+0;-0} 바깥 {off.x * outward * 100f:+0;-0} 무릎 {knee:0}° 발 {points:+0;-0}° IK {(ankle - clipAnkle).magnitude * 100f:0} ")
                .Append($"땅 {(leg.Ground - floor) * 100f:+0;-0} {(leg.Ground - groundWas) * 100f:+0;-0} ")
                .Append($"목표 {(leg.Target.y - leg.Ground) * 100f:0} 어긋남 {(ankle - leg.Target).magnitude * 100f:0} 닿음 {reach * 100f:0}%");
        }
    }

    // Which end of the leg's sole is on the ground (1 the heel, 2 the toe tip; 0 neither within SoleOnGround of the ground under
    // it), where it is (world) and how high over that ground (m): the end it stood on last frame (`was`) while it still does, so a
    // foot flat on the ground keeps one end and its slide reads off one point; else the lower end.
    private static int SoleEnd(FootPlanting.Leg leg, int was, out Vector3 point, out float over)
    {
        Vector3 heel = leg.Foot.TransformPoint(leg.HeelSole), tip = leg.Foot.TransformPoint(leg.TipSole);
        float heelOver = heel.y - leg.Ground, tipOver = tip.y - leg.Ground;
        int end = was == 1 && heelOver <= SoleOnGround ? 1 : was == 2 && tipOver <= SoleOnGround ? 2 : heelOver <= tipOver ? 1 : 2;
        point = end == 1 ? heel : tip;
        over = end == 1 ? heelOver : tipOver;
        return over <= SoleOnGround ? end : 0;
    }

    // Select the player in the Scene view: the line is the ground each foot found, the sphere where its ankle goes.
    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) feet?.DrawGizmos();
    }
}
