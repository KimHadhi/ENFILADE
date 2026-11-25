using UnityEngine;

// Data/Resources/Tables/Player/Player.csv sets these by field name (rows Posture and Move; AbilitySettings uses row Ability), so a
// renamed field needs its table key renamed too. The table wins; inspector values only fill cells left empty.
public sealed partial class PlayerMovement
{
    [Header("Locomotion Speeds")]
    // Walking, jogging, running and sprinting, standing or crouched, go as fast as their clips carry the body each way:
    // the clips' own (MovementGait), not fields here.
    [SerializeField, Min(0f)] internal float proneSpeed = 1.6f;
    [SerializeField, Min(0f)] internal float supineSpeed = 1.6f;
    [Tooltip("Going down from on their feet, how long the body falls before the crawl can start (s): BIPER P_Die_F's body "
        + "centre reaches the floor at 0.96 s and its hips settle by 1.03 s (PlayerBody.Downed).")]
    [SerializeField, Min(0f)] internal float downedFallTime = 1f;
    [Tooltip("Seconds Shift takes from the run to the sprint, or in walk mode from the walk to the jog (MovementGait).")]
    [SerializeField, Min(0.01f)] internal float sprintRampUpDuration = 0.1f;

    [Header("Heights & Eye Offsets")]
    [Tooltip("선택 사항. 비워두면 플레이어 자식의 CrouchPivot을 자동 탐색합니다. 별도 피벗을 쓸 때만 지정하세요.")]
    [SerializeField] internal Transform crouchPivot;
    [SerializeField] internal float crouchOffset = -0.63f;
    [SerializeField] internal float slideOffset = -0.98f;
    [SerializeField] internal float proneOffset = -1.23f;
    [SerializeField] internal float supineOffset = -1.13f;

    [SerializeField, Min(0.3f)] internal float crouchColliderHeight = 0.9f;
    [Tooltip("앉기 허용 및 자동 포복 판정에 필요한 추가 머리 공간. 기본 0.15m로 높이 1m 통로에서는 포복을 유지합니다.")]
    [SerializeField, Min(0f)] internal float crouchClearanceMargin = 0.15f;
    [Tooltip("앉아 이동할 때 낮은 통로를 미리 확인하는 거리. 포복으로 지나갈 공간과 경로가 있을 때만 전환합니다.")]
    [SerializeField, Min(0.1f)] internal float proneEntryLookAhead = 0.6f;
    [SerializeField, Min(0.3f)] internal float slideColliderHeight = 0.6f;
    [SerializeField, Min(0.2f)] internal float proneColliderHeight = 0.4f;
    [SerializeField, Min(0f)] internal float transitionSharpness = 14f;
    [SerializeField, Min(0f)] internal float slideTransitionSharpness = 8.5f;

    [Header("Sliding Physics")]
    [SerializeField, Min(1f)] internal float slideMinStartSpeed = 4f;
    [SerializeField, Min(0f)] internal float slideFriction = 4.2f;
    [SerializeField, Min(0f)] internal float diveSlideFriction = 9f;
    [SerializeField, Min(0f)] internal float supineSlideFriction = 9f;
    [SerializeField, Range(0.05f, 0.8f)] internal float airSlideFrictionMultiplier = 0.25f;
    [SerializeField, Min(0f)] internal float slopeSlideGravity = 28f;
    [SerializeField, Min(0f)] internal float slideEndSpeed = 1.5f;
    [Tooltip("Sliding with the keys held, the fastest the slope takes it (m/s; SlideMove).")]
    [SerializeField, Min(1f)] internal float slideMaxSpeed = 9f;
    [SerializeField, Min(1f)] internal float diveSlideCancelMaxSpeed = 2.0f;
    [Tooltip("Sliding on the back, Shift with a move key or Space gets up once the slide is this slow (m/s) and slideCancelMinDuration old.")]
    [SerializeField, Min(1f)] internal float supineSlideCancelMaxSpeed = 5.0f;
    [SerializeField, Min(0f)] internal float slideBrakeDeceleration = 18f;
    [SerializeField, Min(0f)] internal float landingSlideMinSpeed = 4f;
    [SerializeField, Min(0.1f)] internal float slideCancelMinDuration = 0.5f;
    [SerializeField, Min(0f)] internal float slideCooldown = 0.4f;
    [SerializeField, Min(0f)] internal float slideSteerSharpness = 4.0f;
    [Tooltip("Sliding, the mouse pushed on past the view's right limit (Body.slideLookLimits) turns the slide after it, up to this fast (degrees per second). 0 stops the view at the limit as before.")]
    [SerializeField, Min(0f)] internal float slideLookSteerRate = 90f;
    [SerializeField, Min(0.1f)] internal float groundSnapDistance = 0.5f;
    [SerializeField, Min(0.01f)] internal float slideJumpMinDuration = 0.1f;

    [Header("Dive pose (a lying body off an edge)")]
    [Tooltip("다이브 시작 후 기존 눈높이/몸 높이를 유지하는 시간. 먼저 위로 솟는 궤적을 보여 줍니다.")]
    [SerializeField, Min(0f)] internal float divePoseDelay = 0.03f;
    [Tooltip("도약 후 엎드린 높이까지 몸과 시점을 부드럽게 낮추는 시간. 수평 이동 속도와는 별개입니다.")]
    [SerializeField, Min(0.05f)] internal float divePoseDuration = 0.5f;

    [Header("Diving In-Air Flight Physics")]
    [SerializeField, Range(0.5f, 1.2f)] internal float diveGravityScale = 0.9f;
    [SerializeField, Min(0f)] internal float diveAirResistance = 0.12f;
    [Tooltip("다이브 도약 후 수평 속도를 유지하는 시간. 충돌과 착지는 정상 처리합니다.")]
    [SerializeField, Range(0.2f, 0.5f)] internal float diveSpeedHoldDuration = 0.25f;
    [Tooltip("공중 감속이 0에서 설정값까지 서서히 강해지는 시간. 이 시점에 급제동하지 않습니다.")]
    [SerializeField, Min(0.01f)] internal float diveBurstDuration = 0.1f;
    [SerializeField, Min(0f)] internal float diveCarrySpeed = 2.2f;
    [SerializeField, Min(0.1f)] internal float diveBurstBraking = 2.6f;

    [Header("Air Movement & Jump")]
    [SerializeField, Min(0f)] internal float groundSharpness = 14f;
    [Tooltip("A change of way on the ground up to this far round (degrees) keeps the pace: the velocity turns. Further, it slows "
        + "through a stop as a body reversing does.")]
    [SerializeField, Range(0f, 180f)] internal float groundKeepPaceTurn = 120f;
    [Tooltip("Out of a one-off that runs on faster than the gait the keys ask for (a slide's getting up, a roll, a landing on the "
        + "run), how quickly the body comes down to the gait (m/s a second), the legs' gait with it (MovementGait.Carry).")]
    [SerializeField, Min(.1f)] internal float runOutCoast = 2.5f;
    [Tooltip("Out of a one-off that runs on slower than the gait the keys ask for (a roll's run-out), how quickly the body speeds "
        + "up to it (m/s a second), the legs' gait with it (MovementGait.Carry).")]
    [SerializeField, Min(.1f)] internal float runOutAccel = 4.5f;
    [SerializeField, Min(0f)] internal float airControlSharpness = 5.0f;
    [SerializeField, Min(0f)] internal float jumpHeight = 1.3f;
    [SerializeField] internal float gravity = -24f;
    [SerializeField, Range(0f, 0.2f)] internal float coyoteTime = 0.1f;
    [SerializeField, Range(0f, 0.3f)] internal float jumpBufferTime = 0.15f;

    [Header("Impact & Destruction")]
    [SerializeField, Min(0f)] internal float doorBreakSpeedThreshold = 6.0f;
    [SerializeField, Range(0.1f, 1f)] internal float doorBreakSpeedRetention = 0.85f;

    [Header("Collision & Layers")]
    [SerializeField] internal LayerMask groundLayers = ~0;


}
