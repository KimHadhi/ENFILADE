using UnityEngine;

// Keep serialized field names on the original component so existing scenes/prefabs retain their values.
public sealed partial class PlayerMovement
{
    [Header("Locomotion Speeds")]
    [SerializeField, Min(0f)] internal float walkSpeed = 5.2f;
    [SerializeField, Min(0f)] internal float sprintSpeed = 8.4f;
    [SerializeField, Min(0f)] internal float crouchSpeed = 3.0f;
    [SerializeField, Min(0f)] internal float proneSpeed = 1.6f;
    [SerializeField, Min(0f)] internal float supineSpeed = 1.6f;
    [SerializeField, Min(0.01f)] internal float sprintRampUpDuration = 0.1f;

    [Header("FOV & Visual Compensation")]
    [SerializeField, Range(1f, 1.3f)] internal float strafeCompensation = 1.15f;

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
    [SerializeField, Min(1f)] internal float slideMinStartSpeed = 5.3f;
    [SerializeField, Min(0f)] internal float slideKickBoost = 1.0f;
    [SerializeField, Min(0f)] internal float slideFriction = 4.2f;
    [SerializeField, Min(0f)] internal float diveSlideFriction = 5.2f;
    [SerializeField, Min(0f)] internal float supineSlideFriction = 5.6f;
    [SerializeField, Range(0.05f, 0.8f)] internal float airSlideFrictionMultiplier = 0.25f;
    [SerializeField, Min(0f)] internal float slopeSlideGravity = 28f;
    [SerializeField, Min(0f)] internal float slideEndSpeed = 1.5f;
    [SerializeField, Min(1f)] internal float slideCancelMaxSpeed = 5.0f;
    [SerializeField, Min(1f)] internal float diveSlideCancelMaxSpeed = 2.0f;
    [SerializeField, Min(0f)] internal float slideBrakeDeceleration = 18f;
    [SerializeField, Min(0f)] internal float landingSlideMinSpeed = 6.5f;
    [SerializeField, Min(0.1f)] internal float slideCancelMinDuration = 0.5f;
    [SerializeField, Min(0f)] internal float slideCooldown = 0.4f;
    [SerializeField, Min(0f)] internal float slideSteerSharpness = 4.0f;
    [SerializeField, Min(0.1f)] internal float groundSnapDistance = 0.5f;
    [SerializeField, Min(0.01f)] internal float slideJumpMinDuration = 0.1f;
    [SerializeField, Min(0.2f)] internal float slideKickJumpHeight = 1.05f;

    [Header("Airborne & Ground Launch Dive")]
    [Tooltip("다이브 시작 순간의 수평 속도. Burst Duration 이후 감속하므로 지속 이동 속도가 아닙니다.")]
    [SerializeField, Min(5f)] internal float groundDiveLaunchSpeed = 11.5f;
    [Tooltip("지상 다이브 발사 시 상향 포물선 도약력")]
    [SerializeField, Min(1f)] internal float groundDiveVerticalPop = 4.6f;
    [SerializeField, Min(0f)] internal float divePopPerSpeed = 0.12f;
    [SerializeField, Min(0f)] internal float maxAdditionalDivePop = 2.5f;

    [Header("Diving In-Air Flight Physics")]
    [SerializeField, Range(0.5f, 1.2f)] internal float diveGravityScale = 0.9f;
    [SerializeField, Range(0.5f, 1.0f)] internal float diveMomentumRetention = 0.88f;
    [SerializeField, Min(0f)] internal float diveAirResistance = 0.12f;
    [Tooltip("빠른 회피 추진력을 유지하는 짧은 구간. 이후 Carry Speed 쪽으로 감속합니다.")]
    [SerializeField, Min(0.01f)] internal float diveBurstDuration = 0.1f;
    [SerializeField, Min(0f)] internal float diveCarrySpeed = 2.8f;
    [SerializeField, Min(0.1f)] internal float diveBurstBraking = 8f;
    [Tooltip("착지 직후 다이브 재발동을 막는 시간. 시점 조작은 계속 가능합니다.")]
    [SerializeField, Min(0f)] internal float diveLandingRecovery = 0.35f;

    [Header("Head-First Slide")]
    [SerializeField, Min(0f)] internal float diveSlideKickBoost = 3.8f;
    [SerializeField, Min(0f)] internal float maxLandingSlideSpeed = 10.0f;

    [Header("Air Movement & Jump")]
    [SerializeField, Min(0f)] internal float groundSharpness = 14f;
    [SerializeField, Min(0f)] internal float airControlSharpness = 5.0f;
    [SerializeField, Min(0f)] internal float jumpHeight = 1.3f;
    [SerializeField] internal float gravity = -24f;
    [SerializeField, Range(0f, 0.2f)] internal float coyoteTime = 0.1f;
    [SerializeField, Range(0f, 0.3f)] internal float jumpBufferTime = 0.15f;

    [Header("Impact & Destruction")]
    [SerializeField, Min(0f)] internal float doorBreakSpeedThreshold = 6.0f;
    [SerializeField, Range(0.1f, 1f)] internal float doorBreakSpeedRetention = 0.85f;
    [SerializeField, Min(0f)] internal float wallBounceBounciness = 0.5f;
    [SerializeField, Min(0f)] internal float supineBounceThreshold = 7.0f;

    [Header("Collision & Layers")]
    [SerializeField] internal LayerMask groundLayers = ~0;


}
