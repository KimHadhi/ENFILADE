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
    [Tooltip("지상 다이브 런칭 수평 속도")]
    [SerializeField, Min(5f)] internal float groundDiveLaunchSpeed = 8.5f;
    [Tooltip("지상 다이브 발사 시 상향 포물선 도약력")]
    [SerializeField, Min(1f)] internal float groundDiveVerticalPop = 6.5f;
    [SerializeField, Min(0f)] internal float divePopPerSpeed = 0.12f;
    [SerializeField, Min(0f)] internal float maxAdditionalDivePop = 2.5f;

    [Header("Diving In-Air Flight Physics")]
    [SerializeField, Range(0.5f, 1.2f)] internal float diveGravityScale = 0.72f;
    [SerializeField, Range(0.5f, 1.0f)] internal float diveMomentumRetention = 0.88f;
    [SerializeField, Min(0f)] internal float diveAirResistance = 0.12f;

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
