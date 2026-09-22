using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class PlayerCameraEffects : MonoBehaviour
{
    [Header("References")]
    [Tooltip("상하 시선 피벗 (LookPivot) 또는 전용 BobPivot")]
    [SerializeField] private Transform verticalBobTarget;
    [Tooltip("실제 카메라 트랜스폼")]
    [SerializeField] private Transform targetCamera;
    [Tooltip("플레이어 이동 컴포넌트")]
    [SerializeField] private PlayerMovement movement;

    [Header("Dynamic FOV")]
    [SerializeField] private bool enableDynamicFov = true;
    [SerializeField, Range(0f, 25f)] private float maxFovBonus = 10f;
    [SerializeField, Min(0f)] private float fovSharpness = 8f;
    [SerializeField, Min(0f)] private float fovThresholdSpeed = 5f;
    [SerializeField, Min(0f)] private float fovMaxSpeed = 10f;

    [Header("Move Tilt (순수 A/D 화면 기울임)")]
    [Tooltip("A/D 이동 시 카메라 롤 각도 (0.5~1.0도 권장)")]
    [SerializeField, Range(0f, 3f)] private float tiltAngle = 0.8f;
    [SerializeField, Min(0f)] private float tiltSharpness = 12f;

    [Header("Headbob (레포데 스타일 순수 Y축 수직 리프트)")]
    [SerializeField] private bool enableHeadbob = true;
    [Tooltip("걷기 보폭 거리 (m)")]
    [SerializeField, Min(1f)] private float walkStride = 2.4f;
    [Tooltip("달리기 보폭 거리 (m)")]
    [SerializeField, Min(1f)] private float sprintStride = 3.8f;
    [Tooltip("위아래 시점 오르내림 높이 (m). 레포데 기준 0.025~0.035m 추천")]
    [SerializeField, Range(0f, 0.08f)] private float bobAmountY = 0.03f;
    [Tooltip("달릴 때 상하 폭 배율")]
    [SerializeField, Range(0.5f, 1.5f)] private float sprintBobMultiplier = 0.8f;
    [Tooltip("출발/정지 전환 속도")]
    [SerializeField, Min(0f)] private float bobWeightSharpness = 6f;

    [Header("Land Impact (순수 Y축 수직 침하)")]
    [Tooltip("착지 시 시점이 아래로 가라앉는 깊이 (m)")]
    [SerializeField, Range(0f, 0.2f)] private float landKickAmount = 0.06f;
    [SerializeField, Min(0f)] private float springStiffness = 260f;
    [SerializeField, Min(0f)] private float springDamping = 22f;

    private Camera cam;
    private float baseFov;
    private float currentFov;

    private Vector3 initialTargetPos;
    private Quaternion initialCamRot;

    private float currentTiltRoll;

    // 레포데 스타일 거리 위상
    private float bobPhase;
    private float currentBobWeight;
    private float currentBobY;

    // 착지 수직 스프링
    private bool wasGroundedLastFrame = true;
    private float lastFallSpeed;
    private float impactDisplacementY;
    private float impactVelocityY;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = transform;

        cam = targetCamera.GetComponent<Camera>();
        if (cam == null)
            cam = targetCamera.GetComponentInChildren<Camera>();

        if (cam != null)
        {
            baseFov = cam.fieldOfView;
            currentFov = baseFov;
        }

        if (movement == null)
            movement = GetComponentInParent<PlayerMovement>();

        // 상하 바운스를 적용할 타겟: LookPivot을 기본으로 자동 탐색
        if (verticalBobTarget == null)
        {
            var lookPivot = transform.Find("LookPivot");
            if (lookPivot == null && transform.parent != null)
                lookPivot = transform.parent.Find("LookPivot");

            verticalBobTarget = lookPivot != null ? lookPivot : targetCamera;
        }

        initialTargetPos = verticalBobTarget.localPosition;
        initialCamRot = targetCamera.localRotation;
    }

    private void LateUpdate()
    {
        if (movement == null || targetCamera == null || verticalBobTarget == null)
            return;

        UpdateDynamicFov();
        UpdateTilt();
        UpdateLandingImpact();
        UpdateVerticalL4DBob();

        // 1. [핵심] 고개를 끄덕이지 않고 순수하게 Y축 높이만 오르내림 (레포데 메커니즘)
        float finalOffsetY = currentBobY + impactDisplacementY;
        verticalBobTarget.localPosition = new Vector3(initialTargetPos.x, initialTargetPos.y + finalOffsetY, initialTargetPos.z);

        // 2. 카메라는 Pitch 회전 간섭 0, 오직 A/D 시의 좌우 롤(Roll) 틸트만 부드럽게 반영
        targetCamera.localRotation = initialCamRot * Quaternion.Euler(0f, 0f, currentTiltRoll);
    }

    private void UpdateDynamicFov()
    {
        if (!enableDynamicFov || cam == null)
            return;

        Vector3 hVel = movement.Velocity;
        hVel.y = 0f;
        float currentSpeed = hVel.magnitude;

        float speedRatio = Mathf.InverseLerp(fovThresholdSpeed, fovMaxSpeed, currentSpeed);
        float targetFov = baseFov + (speedRatio * maxFovBonus);

        float t = 1f - Mathf.Exp(-fovSharpness * Time.deltaTime);
        currentFov = Mathf.Lerp(currentFov, targetFov, t);
        cam.fieldOfView = currentFov;
    }

    private void UpdateTilt()
    {
        Vector3 localVel = movement.transform.InverseTransformDirection(movement.Velocity);
        float lateralRatio = 0f;

        if (movement.Velocity.magnitude > 0.1f)
            lateralRatio = Mathf.Clamp(localVel.x / movement.Velocity.magnitude, -1f, 1f);

        float targetTilt = -lateralRatio * tiltAngle;
        float t = 1f - Mathf.Exp(-tiltSharpness * Time.deltaTime);
        currentTiltRoll = Mathf.Lerp(currentTiltRoll, targetTilt, t);
    }

    private void UpdateVerticalL4DBob()
    {
        if (!enableHeadbob)
        {
            currentBobY = Mathf.MoveTowards(currentBobY, 0f, 2f * Time.deltaTime);
            return;
        }

        // [핵심] 입력 속도가 아닌 실제 물리 변위 속도를 측정!
        // 벽에 막혀 제자리에서 멈춰있으면 actualSpeed가 0이 되어 보빙이 발생하지 않음
        Vector3 hVel = movement.ActualVelocity;
        hVel.y = 0f;
        float actualSpeed = hVel.magnitude;

        // 실제 이동 중일 때만 보빙 가중치를 올림
        bool isMovingGrounded = movement.IsGrounded && actualSpeed > 0.5f;
        float targetWeight = isMovingGrounded ? 1f : 0f;

        float weightT = 1f - Mathf.Exp(-bobWeightSharpness * Time.deltaTime);
        currentBobWeight = Mathf.Lerp(currentBobWeight, targetWeight, weightT);

        float targetBobY = 0f;

        if (currentBobWeight > 0.001f)
        {
            float sprintRatio = Mathf.InverseLerp(6.0f, 10.0f, actualSpeed);
            float activeStride = Mathf.Lerp(walkStride, sprintStride, sprintRatio);

            // 실제 이동한 거리(속도)만큼만 주기 진행
            float phaseStep = (actualSpeed / Mathf.Max(0.5f, activeStride)) * Mathf.PI * 2f;
            bobPhase = Mathf.Repeat(bobPhase + phaseStep * Time.deltaTime, Mathf.PI * 2f);

            float wave = Mathf.Sin(bobPhase);
            float dynamicBobAmount = bobAmountY * Mathf.Lerp(1f, sprintBobMultiplier, sprintRatio);

            targetBobY = wave * dynamicBobAmount * currentBobWeight;
        }

        currentBobY = Mathf.Lerp(currentBobY, targetBobY, 15f * Time.deltaTime);
    }

    private void UpdateLandingImpact()
    {
        bool isGrounded = movement.IsGrounded;
        float currentVerticalVel = movement.Velocity.y;

        if (!isGrounded && currentVerticalVel < 0f)
        {
            lastFallSpeed = Mathf.Abs(currentVerticalVel);
        }

        if (isGrounded && !wasGroundedLastFrame && lastFallSpeed > 4.5f)
        {
            // 착지 시 고개가 꺾이지 않고 시점 전체가 아래로 쿠션처럼 쿵 가라앉음
            float impactForce = Mathf.Clamp(lastFallSpeed / 18f, 0.2f, 1f) * landKickAmount;
            impactVelocityY -= impactForce * 40f;
            lastFallSpeed = 0f;
        }

        wasGroundedLastFrame = isGrounded;

        // 수직 Y축 스프링 물리 연산
        float springForce = -springStiffness * impactDisplacementY;
        float dampingForce = -springDamping * impactVelocityY;
        float totalAccel = springForce + dampingForce;

        impactVelocityY += totalAccel * Time.deltaTime;
        impactDisplacementY += impactVelocityY * Time.deltaTime;

        if (Mathf.Abs(impactDisplacementY) < 0.0005f && Mathf.Abs(impactVelocityY) < 0.001f)
        {
            impactDisplacementY = 0f;
            impactVelocityY = 0f;
        }
    }
}