using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class PlayerCameraEffects : MonoBehaviour
{
    [Header("References")]
    [Tooltip("상하 보폭 밥/착지 충격을 적용할 전용 피벗 (BobPivot 권장)")]
    [SerializeField] private Transform verticalBobTarget;
    [Tooltip("실제 카메라 트랜스폼 (FOV 및 Roll 틸트 전용)")]
    [SerializeField] private Transform targetCamera;
    [Tooltip("플레이어 이동 컴포넌트")]
    [SerializeField] private PlayerMovement movement;

    [Header("Dynamic FOV")]
    [SerializeField] private bool enableDynamicFov = true;
    [SerializeField, Range(0f, 25f)] private float maxFovBonus = 10f;
    [SerializeField, Min(0f)] private float fovSharpness = 8f;
    [SerializeField, Min(0f)] private float fovThresholdSpeed = 5f;
    [SerializeField, Min(0f)] private float fovMaxSpeed = 10f;

    [Header("Move Tilt (A/D 화면 롤 기울임)")]
    [SerializeField, Range(0f, 3f)] private float tiltAngle = 0.8f;
    [SerializeField, Min(0f)] private float tiltSharpness = 12f;

    [Header("Headbob (레포데 스타일 Y축 수직 리프트)")]
    [SerializeField] private bool enableHeadbob = true;
    [SerializeField, Min(1f)] private float walkStride = 2.4f;
    [SerializeField, Min(1f)] private float sprintStride = 3.8f;
    [SerializeField, Range(0f, 0.08f)] private float bobAmountY = 0.03f;
    [SerializeField, Range(0.5f, 1.5f)] private float sprintBobMultiplier = 0.8f;
    [SerializeField, Min(0f)] private float bobWeightSharpness = 6f;

    [Header("Land Impact (착지 쿠션 Y축 침하)")]
    [SerializeField, Range(0f, 0.2f)] private float landKickAmount = 0.06f;
    [SerializeField, Min(0f)] private float springStiffness = 260f;
    [SerializeField, Min(0f)] private float springDamping = 22f;

    private Camera cam;
    private float baseFov;
    private float currentFov;
    private Vector3 initialBobTargetPos;
    private Quaternion initialCamRot;
    private float currentTiltRoll;

    private float bobPhase;
    private float currentBobWeight;
    private float currentBobY;

    private bool wasGroundedLastFrame = true;
    private float lastFallSpeed;
    private float impactDisplacementY;
    private float impactVelocityY;

    private void Awake()
    {
        if (targetCamera == null)
        {
            Camera c = GetComponentInChildren<Camera>();
            if (c != null) targetCamera = c.transform;
            else targetCamera = transform;
        }

        cam = targetCamera.GetComponent<Camera>();
        if (cam != null)
        {
            baseFov = cam.fieldOfView;
            currentFov = baseFov;
        }

        if (movement == null)
            movement = GetComponentInParent<PlayerMovement>();

        // [핵심 격리] CrouchPivot을 덮어쓰지 않도록 BobPivot 우선 탐색
        if (verticalBobTarget == null)
        {
            var bob = transform.Find("CrouchPivot/BobPivot");
            if (bob != null) verticalBobTarget = bob;
            else
            {
                var look = transform.Find("CrouchPivot/LookPivot");
                verticalBobTarget = look != null ? look : targetCamera;
            }
        }

        initialBobTargetPos = verticalBobTarget.localPosition;
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

        // 1. BobPivot만 수직 Y 미세 바운스 적용 (CrouchPivot에 영향 0)
        float finalOffsetY = currentBobY + impactDisplacementY;
        verticalBobTarget.localPosition = new Vector3(initialBobTargetPos.x, initialBobTargetPos.y + finalOffsetY, initialBobTargetPos.z);

        // 2. Camera만 Roll 틸트 적용
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

        Vector3 hVel = movement.ActualVelocity;
        hVel.y = 0f;
        float actualSpeed = hVel.magnitude;

        bool isMovingGrounded = movement.IsGrounded && actualSpeed > 0.5f;
        float targetWeight = isMovingGrounded ? 1f : 0f;

        float weightT = 1f - Mathf.Exp(-bobWeightSharpness * Time.deltaTime);
        currentBobWeight = Mathf.Lerp(currentBobWeight, targetWeight, weightT);

        float targetBobY = 0f;
        if (currentBobWeight > 0.001f)
        {
            float sprintRatio = Mathf.InverseLerp(6.0f, 10.0f, actualSpeed);
            float activeStride = Mathf.Lerp(walkStride, sprintStride, sprintRatio);

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
            float impactForce = Mathf.Clamp(lastFallSpeed / 18f, 0.2f, 1f) * landKickAmount;
            impactVelocityY -= impactForce * 40f;
            lastFallSpeed = 0f;
        }

        wasGroundedLastFrame = isGrounded;

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