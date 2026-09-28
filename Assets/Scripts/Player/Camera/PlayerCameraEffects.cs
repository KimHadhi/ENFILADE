using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class PlayerCameraEffects : MonoBehaviour
{
    [Header("Optional References (비우면 자동 탐색)")]
    [Tooltip("선택 사항. 비우면 CrouchPivot/BobPivot, LookPivot, 카메라 순서로 자동 탐색합니다.")]
    [SerializeField] private Transform verticalBobTarget;
    [Tooltip("선택 사항. 비우면 자식 Camera를 자동 탐색합니다. 여러 카메라가 있을 때 지정하세요.")]
    [SerializeField] private Transform targetCamera;
    [Tooltip("선택 사항. 비우면 부모의 PlayerMovement를 자동 탐색합니다.")]
    [SerializeField] private PlayerMovement movement;

    [Header("Dynamic FOV")]
    [SerializeField] private bool enableDynamicFov = true;
    [SerializeField, Range(0f, 25f)] private float maxFovBonus = 10f;
    [SerializeField, Min(0f)] private float fovSharpness = 8f;
    [SerializeField, Min(0f)] private float fovThresholdSpeed = 5f;
    [SerializeField, Min(0f)] private float fovMaxSpeed = 10f;

    [Header("Move Tilt (지상 이동 시 미세 롤)")]
    [SerializeField] private bool enableMoveTilt = true;
    [SerializeField, Range(0f, 3f)] private float tiltAngle = 0.8f;
    [SerializeField, Min(0f)] private float tiltSharpness = 10f;

    [Header("Dive Tilt (맥스 페인 다이빙 롤)")]
    [SerializeField] private bool enableDiveTilt = true;
    [Tooltip("다이빙 시 몸을 던진 방향으로 화면이 기울어지는 최대 각도")]
    [SerializeField, Range(5f, 20f)] private float maxDiveRollAngle = 12f;
    [SerializeField, Min(0f)] private float diveTiltSharpness = 12f;

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
    private float currentTiltRoll;
    private Vector3 baseCameraPosition;
    private Transform effectsPivot;
    private ObstacleTraversalState punchSource;
    private int punchSerial;
    private Vector2 traversalPunch, punchVelocity;
    private float divePitch;

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
        if (cam == null) return;
        baseCameraPosition = targetCamera.localPosition;
        if (cam != null)
        {
            baseFov = cam.fieldOfView;
            currentFov = baseFov;
        }

        if (movement == null)
            movement = GetComponentInParent<PlayerMovement>();

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
        // Mouse aim belongs to PlayerLook. Camera effects get their own additive
        // transform so Euler subtraction cannot fight aim, recoil or animation.
        effectsPivot = targetCamera.parent != null && targetCamera.parent.name == "CameraEffectsPivot"
            ? targetCamera.parent : null;
        if (effectsPivot == null)
        {
            var pivot = new GameObject("CameraEffectsPivot");
            effectsPivot = pivot.transform;
            effectsPivot.SetParent(targetCamera.parent, false);
            targetCamera.SetParent(effectsPivot, false);
        }
    }

    private void LateUpdate()
    {
        if (movement == null || cam == null || effectsPivot == null || verticalBobTarget == null)
            return;

        UpdateDynamicFov();
        UpdateTilt();
        UpdateLandingImpact();
        UpdateVerticalL4DBob();
        UpdateTraversalPunch(Time.deltaTime);
        UpdateDivePresentation(Time.deltaTime);

        float finalOffsetY = currentBobY + impactDisplacementY;
        verticalBobTarget.localPosition = new Vector3(initialBobTargetPos.x, initialBobTargetPos.y + finalOffsetY, initialBobTargetPos.z);

        ApplyCameraRotation();
        Vector3 cameraPosition = targetCamera.localPosition;
        cameraPosition.x = baseCameraPosition.x + movement.LeanOffset;
        targetCamera.localPosition = cameraPosition;
    }

    private void ApplyCameraRotation()
    {
        effectsPivot.localRotation = Quaternion.Euler(traversalPunch.x + divePitch, 0f,
            currentTiltRoll + movement.LeanRoll + traversalPunch.y);
    }

    private void UpdateTraversalPunch(float dt)
    {
        var traversal = movement.StateMachine.Current as ObstacleTraversalState;
        if (traversal != null && traversal.UsesReferenceMotion
            && (punchSource != traversal || punchSerial != traversal.EntrySerial))
        {
            punchSource = traversal; punchSerial = traversal.EntrySerial;
            // Vault2 pitches up; Vault1 rolls. Match the Lua's small one-shot
            // impulse, without adding it again on each movement frame.
            punchVelocity += (traversal.IsVault ? new Vector2(-1f, 0f) : new Vector2(0f, -1f)) * 20f;
        }
        dt = Mathf.Max(0f, dt);
        Vector2 spring = punchVelocity + 10f * traversalPunch;
        float decay = Mathf.Exp(-10f * dt);
        traversalPunch = (traversalPunch + spring * dt) * decay;
        punchVelocity = (punchVelocity - 10f * spring * dt) * decay;
    }

    private void OnDisable()
    {
        if (effectsPivot != null) effectsPivot.localRotation = Quaternion.identity;
        traversalPunch = punchVelocity = Vector2.zero;
        divePitch = 0f;
    }

    private void UpdateDivePresentation(float dt)
    {
        // Small, additive launch cue. It never owns mouse aim or the player's yaw.
        float target = 0f;
        if (movement.IsDiving)
        {
            float forward = Vector3.Dot(movement.HorizontalVelocity.normalized, movement.transform.forward);
            target = forward * 3f * movement.Actions.Dive.BurstWeight;
        }
        divePitch = Mathf.Lerp(divePitch, target, 1f - Mathf.Exp(-16f * Mathf.Max(0f, dt)));
    }

    private void UpdateDynamicFov()
    {
        if (!enableDynamicFov || cam == null)
            return;

        Vector3 hVel = movement.Velocity;
        hVel.y = 0f;
        // Vault position correction is not sprint acceleration. Preserve entry FOV.
        float currentSpeed = movement.StateMachine.Current is ObstacleTraversalState traversal
            ? traversal.EntrySpeed : hVel.magnitude;

        float speedRatio = Mathf.InverseLerp(fovThresholdSpeed, fovMaxSpeed, currentSpeed);
        float targetFov = baseFov + (speedRatio * maxFovBonus);

        float t = 1f - Mathf.Exp(-fovSharpness * Time.deltaTime);
        currentFov = Mathf.Lerp(currentFov, targetFov, t);
        cam.fieldOfView = currentFov;
    }

    private void UpdateTilt()
    {
        float targetTilt = 0f;

        // 다이빙 중인 경우: 고정된 몸통 축 기준으로 카메라가 좌우로 꺾인 비율 연산
        if (movement.IsDiving && enableDiveTilt)
        {
            Vector3 flightDir = movement.HorizontalVelocity.normalized;
            float sideDot = Vector3.Dot(flightDir, movement.transform.right);
            targetTilt = -sideDot * maxDiveRollAngle;

            float t = 1f - Mathf.Exp(-diveTiltSharpness * Time.deltaTime);
            currentTiltRoll = Mathf.Lerp(currentTiltRoll, targetTilt, t);
            return;
        }

        if (enableMoveTilt)
        {
            Vector2 input = movement.CurrentInput;
            targetTilt = -input.x * tiltAngle;

            float t = 1f - Mathf.Exp(-tiltSharpness * Time.deltaTime);
            currentTiltRoll = Mathf.Lerp(currentTiltRoll, targetTilt, t);
        }
        else
        {
            currentTiltRoll = Mathf.MoveTowards(currentTiltRoll, 0f, 15f * Time.deltaTime);
        }
    }

    private void UpdateVerticalL4DBob()
    {
        if (!enableHeadbob || movement.IsDiving || movement.IsSliding || movement.IsParkour)
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
        // Scripted traversal descents are not falling impacts.
        if (movement.IsParkour) lastFallSpeed = 0f;
        bool isGrounded = movement.IsGrounded;
        float currentVerticalVel = movement.Velocity.y;

        if (!movement.IsParkour && !isGrounded && currentVerticalVel < 0f)
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
