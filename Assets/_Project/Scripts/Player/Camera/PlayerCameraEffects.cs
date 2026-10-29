using UnityEngine;
using UnityEngine.Rendering.Universal;

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

    [Header("Move & Slide Tilt")]
    [SerializeField] private bool enableMoveTilt = true;
    [SerializeField, Range(0f, 3f)] private float tiltAngle = 0.8f;
    [Tooltip("슬라이드 중 A/D 조향 시 기울어지는 각도 배율")]
    [SerializeField, Range(1f, 4f)] private float slideTiltMultiplier = 2.5f;
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

    [Header("Land & Action Impacts (스프링 반동)")]
    [SerializeField, Range(0f, 0.2f)] private float landKickAmount = 0.06f;
    [SerializeField, Min(0f)] private float springStiffness = 260f;
    [SerializeField, Min(0f)] private float springDamping = 22f;
    [Tooltip("닷지, 롤링, 벽 충돌 시 카메라 흔들림 복원력")]
    [SerializeField, Min(0f)] private float impulseStiffness = 150f;
    [SerializeField, Min(0f)] private float impulseDamping = 18f;

    private Camera cam;
    private float baseFov;
    private float currentFov;
    private Vector3 initialBobTargetPos;
    private float currentTiltRoll;
    private Vector3 baseCameraPosition;
    private Transform effectsPivot;
    private PlayerBody body;
    private PlayerMovementPunch movementPunch;
    internal Vector3 CameraLocalHome => baseCameraPosition;
    internal Vector3 CameraAnchorPosition => targetCamera.parent.TransformPoint(baseCameraPosition);
    internal Vector3 LeanDisplacement { get; private set; }
    private ObstacleTraversalState punchSource;
    private int punchSerial;
    private Vector2 traversalPunch, punchVelocity;
    private float divePitch;

    private float bobPhase;
    private float currentBobWeight;
    private float currentBobY;

    private bool wasGroundedLastFrame = true;
    private float lastFallSpeed;
    private float lastHorizontalSpeed;
    private float impactDisplacementY;
    private float impactVelocityY;

    // 일회성 액션 임펄스 (Dodge, Roll, Collision)
    private Vector3 impulseRotDisplacement;
    private Vector3 impulseRotVelocity;
    private float impulseFovDisplacement;
    private float impulseFovVelocity;

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
        // The player's view renders URP post-processing: the pipeline profile's grade, and volumes such as a long fall's warp.
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        baseCameraPosition = targetCamera.localPosition;

        baseFov = GameSettings.Current.VerticalFieldOfView;
        currentFov = baseFov;

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
        if (movement == null || !movement.IsInitialized || cam == null || effectsPivot == null || verticalBobTarget == null)
            return;

        float dt = Time.deltaTime;
        if (GamePause.IsPaused || dt <= 0f) return;

        UpdateCollisionImpacts();
        UpdateDynamicFov(dt);
        UpdateTilt(dt);
        UpdateLandingImpact(dt);
        UpdateVerticalL4DBob(dt);
        UpdateTraversalPunch(dt);
        UpdateDivePresentation(dt);
        UpdateActionImpulses(dt);

        float finalOffsetY = (currentBobY + impactDisplacementY) * Shake;
        verticalBobTarget.localPosition = new Vector3(initialBobTargetPos.x, initialBobTargetPos.y + finalOffsetY, initialBobTargetPos.z);

        ApplyCameraRotation();
        ApplyLeanPosition();
    }

    /// <summary>
    /// 스텝 닷지, 낙하 롤링, 넉백 등 외부 상태 머신에서 카메라에 일회성 임펄스를 가할 때 호출합니다.
    /// </summary>
    public void ApplyCameraImpulse(Vector3 rotationImpulse, float fovImpulse = 0f)
    {
        impulseRotVelocity += rotationImpulse;
        impulseFovVelocity += fovImpulse;
    }

    private void UpdateCollisionImpacts()
    {
        float currentHSpeed = movement.HorizontalVelocity.magnitude;

        // 슬라이드, 다이브 중 벽이나 부서지는 문에 강하게 충돌했을 때 속도 급감을 감지하여 카메라 타격감 발생
        if (lastHorizontalSpeed - currentHSpeed > 6.0f)
        {
            float impactSeverity = Mathf.Clamp01((lastHorizontalSpeed - currentHSpeed - 6.0f) / 10f);
            ApplyCameraImpulse(new Vector3(-12f * impactSeverity, 0f, Random.Range(-4f, 4f) * impactSeverity));
        }
        lastHorizontalSpeed = currentHSpeed;
    }

    private void UpdateActionImpulses(float dt)
    {
        // 범용 스프링 물리 (Dodge, Roll, Damage)
        Vector3 rotSpringForce = -impulseStiffness * impulseRotDisplacement;
        Vector3 rotDampingForce = -impulseDamping * impulseRotVelocity;
        impulseRotVelocity += (rotSpringForce + rotDampingForce) * dt;
        impulseRotDisplacement += impulseRotVelocity * dt;

        float fovSpringForce = -impulseStiffness * impulseFovDisplacement;
        float fovDampingForce = -impulseDamping * impulseFovVelocity;
        impulseFovVelocity += (fovSpringForce + fovDampingForce) * dt;
        impulseFovDisplacement += impulseFovVelocity * dt;

        if (impulseRotDisplacement.sqrMagnitude < 0.0001f && impulseRotVelocity.sqrMagnitude < 0.0001f)
        {
            impulseRotDisplacement = Vector3.zero;
            impulseRotVelocity = Vector3.zero;
        }
        if (Mathf.Abs(impulseFovDisplacement) < 0.01f && Mathf.Abs(impulseFovVelocity) < 0.01f)
        {
            impulseFovDisplacement = 0f;
            impulseFovVelocity = 0f;
        }
    }

    private void ApplyLeanPosition()
    {
        Vector3 origin = targetCamera.parent.TransformPoint(baseCameraPosition);
        // A lean peeks sideways without dragging the eye down as camera roll increases.
        Vector3 offset = movement.transform.right * movement.LeanOffset;
        float distance = offset.magnitude;
        if (distance > 0.001f && Physics.SphereCast(origin, 0.1f, offset / distance, out RaycastHit hit,
            distance + 0.02f, movement.groundLayers, QueryTriggerInteraction.Ignore))
            offset *= Mathf.Clamp01((hit.distance - 0.02f) / distance);
        LeanDisplacement = offset;
        if (body == null) body = movement.GetComponent<PlayerBody>();
        // The visible body composes the final eye view after these effects. Without a body, keep the pivot camera.
        if (body == null || !body.isActiveAndEnabled || body.BodyAnimator == null || body.View != targetCamera)
            targetCamera.position = origin + offset;
        movement.GetComponent<PlayerLeanHitboxes>()?.Sync(offset);
    }

    // The Esc menu's screen shake scales bob, landing, impacts, punches and movement tilt; Q/E lean is aiming, not shake.
    private static float Shake => GameSettings.Current.screenShake;

    private void ApplyCameraRotation()
    {
        float shake = Shake;
        if (movementPunch == null) movementPunch = movement.GetComponent<PlayerMovementPunch>();
        Vector3 steps = movementPunch != null && movementPunch.isActiveAndEnabled ? movementPunch.Angles : Vector3.zero;
        effectsPivot.localRotation = Quaternion.Euler(
            (traversalPunch.x + divePitch + impulseRotDisplacement.x + steps.x) * shake,
            (impulseRotDisplacement.y + steps.y) * shake,
            (currentTiltRoll + traversalPunch.y + impulseRotDisplacement.z + steps.z) * shake + movement.LeanRoll
        );
    }

    private void UpdateTraversalPunch(float dt)
    {
        var traversal = movement.StateMachine.Current as ObstacleTraversalState;
        if (traversal != null && traversal.UsesReferenceMotion
            && (punchSource != traversal || punchSerial != traversal.EntrySerial))
        {
            punchSource = traversal; punchSerial = traversal.EntrySerial;
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
        impulseRotDisplacement = impulseRotVelocity = Vector3.zero;
        divePitch = impulseFovDisplacement = impulseFovVelocity = 0f;
    }

    private void UpdateDivePresentation(float dt)
    {
        float target = 0f;
        if (movement.IsDiving)
        {
            float forward = Vector3.Dot(movement.HorizontalVelocity.normalized, movement.transform.forward);
            target = forward * 3f * movement.Actions.Dive.BurstWeight;
        }
        divePitch = Mathf.Lerp(divePitch, target, 1f - Mathf.Exp(-16f * Mathf.Max(0f, dt)));
    }

    private void UpdateDynamicFov(float dt)
    {
        if (cam == null) return;

        // The Esc menu's field of view is the base; the speed bonus and ADS zoom work from it. A change applies at
        // once (also while paused, as a live preview) instead of easing in with the speed bonus.
        float previousBase = baseFov;
        baseFov = GameSettings.Current.VerticalFieldOfView;
        currentFov += baseFov - previousBase;
        Vector3 hVel = movement.Velocity;
        hVel.y = 0f;
        float currentSpeed = movement.StateMachine.Current is ObstacleTraversalState traversal
            ? traversal.EntrySpeed : hVel.magnitude;

        float speedRatio = Mathf.InverseLerp(fovThresholdSpeed, fovMaxSpeed, currentSpeed);
        float targetFov = baseFov + (enableDynamicFov ? speedRatio * maxFovBonus : 0f);
        float t = 1f - Mathf.Exp(-fovSharpness * dt);
        currentFov = Mathf.Lerp(currentFov, targetFov, t);

        RefreshWeaponFov();
    }

    // WeaponController finalizes ADS/obstruction after the camera's lean pose. Recompose
    // in that same frame without advancing the movement FOV or impulse springs twice.
    public void RefreshWeaponFov()
    {
        if (cam == null || movement == null) return;
        // ADS already has its own transition timer. Compose after smoothing movement FOV
        // so a second smoothing pass cannot delay the zoom behind the weapon's sight pose.
        var weapon = movement.GetComponent<WeaponController>();
        float aim = weapon != null && weapon.EquippedData != null && !weapon.IsHighReady ? weapon.AimBlend : 0f;
        float aimedFov = aim > 0f ? Mathf.Min(baseFov, weapon.EquippedData.AdsFov) : currentFov;
        cam.fieldOfView = Mathf.Lerp(currentFov, aimedFov, aim)
            + impulseFovDisplacement * Shake * Mathf.Lerp(1f, .25f, aim);
    }

    private void UpdateTilt(float dt)
    {
        float targetTilt = 0f;

        if (movement.IsDiving && enableDiveTilt)
        {
            Vector3 flightDir = movement.HorizontalVelocity.normalized;
            float sideDot = Vector3.Dot(flightDir, movement.transform.right);
            targetTilt = -sideDot * maxDiveRollAngle;

            float t = 1f - Mathf.Exp(-diveTiltSharpness * dt);
            currentTiltRoll = Mathf.Lerp(currentTiltRoll, targetTilt, t);
            return;
        }

        if (enableMoveTilt)
        {
            Vector2 input = movement.CurrentInput;
            targetTilt = -input.x * tiltAngle;

            // 슬라이드 중 조향 시 카메라 틸트를 증폭하여 코너링 느낌 부여
            if (movement.IsSliding)
                targetTilt *= slideTiltMultiplier;

            float t = 1f - Mathf.Exp(-tiltSharpness * dt);
            currentTiltRoll = Mathf.Lerp(currentTiltRoll, targetTilt, t);
        }
        else
        {
            currentTiltRoll = Mathf.MoveTowards(currentTiltRoll, 0f, 15f * dt);
        }
    }

    private void UpdateVerticalL4DBob(float dt)
    {
        if (!enableHeadbob || movement.IsDiving || movement.IsSliding || movement.IsParkour)
        {
            currentBobY = Mathf.MoveTowards(currentBobY, 0f, 2f * dt);
            return;
        }

        Vector3 hVel = movement.ActualVelocity;
        hVel.y = 0f;
        float actualSpeed = hVel.magnitude;

        bool isMovingGrounded = movement.IsGrounded && actualSpeed > 0.5f;
        float targetWeight = isMovingGrounded ? 1f : 0f;

        float weightT = 1f - Mathf.Exp(-bobWeightSharpness * dt);
        currentBobWeight = Mathf.Lerp(currentBobWeight, targetWeight, weightT);

        float targetBobY = 0f;
        if (currentBobWeight > 0.001f)
        {
            float sprintRatio = Mathf.InverseLerp(6.0f, 10.0f, actualSpeed);
            float activeStride = Mathf.Lerp(walkStride, sprintStride, sprintRatio);

            float phaseStep = (actualSpeed / Mathf.Max(0.5f, activeStride)) * Mathf.PI * 2f;
            bobPhase = Mathf.Repeat(bobPhase + phaseStep * dt, Mathf.PI * 2f);

            float wave = Mathf.Sin(bobPhase);
            float dynamicBobAmount = bobAmountY * Mathf.Lerp(1f, sprintBobMultiplier, sprintRatio);

            targetBobY = wave * dynamicBobAmount * currentBobWeight;
        }

        currentBobY = Mathf.Lerp(currentBobY, targetBobY, 15f * dt);
    }

    private void UpdateLandingImpact(float dt)
    {
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

        impactVelocityY += totalAccel * dt;
        impactDisplacementY += impactVelocityY * dt;

        if (Mathf.Abs(impactDisplacementY) < 0.0005f && Mathf.Abs(impactVelocityY) < 0.001f)
        {
            impactDisplacementY = 0f;
            impactVelocityY = 0f;
        }
    }
}
