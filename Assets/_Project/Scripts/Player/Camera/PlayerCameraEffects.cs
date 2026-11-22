using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The movement and combat feel on the camera's turn, on top of KINEMATION's first-person camera (the camera rides the
/// body's eyes, PlayerBody; KINEMATION's gun shakes and camera motion go on last, FPSBodyCamera): Beatrun's movement
/// punches (PlayerMovementPunch), the vault and climb punches, the dive's pitch, the move and slide tilt, collision and
/// action impulses, and combat's head moves (CombatHeadMotion). It also sets the field of view (settings, speed, aiming).
/// The camera's place is the eyes' alone: the clips carry the head's bob and a landing's dip, and the body's lean the peek.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(FrameOrder.CameraEffects)]
public sealed class PlayerCameraEffects : MonoBehaviour
{
    [Header("Optional References (비우면 자동 탐색)")]
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

    [Header("Action Impacts (스프링 반동)")]
    [Tooltip("닷지, 롤링, 벽 충돌 시 카메라 흔들림 복원력")]
    [SerializeField, Min(0f)] private float impulseStiffness = 150f;
    [SerializeField, Min(0f)] private float impulseDamping = 18f;

    private Camera cam;
    private float baseFov;
    private float currentFov;
    private float currentTiltRoll;
    private Vector3 baseCameraPosition;
    private Transform effectsPivot, eyes;
    private PlayerBody body;
    private PlayerMovementPunch movementPunch;
    private WeaponController weapon;
    internal Vector3 CameraLocalHome => baseCameraPosition;
    internal Vector3 CameraAnchorPosition => targetCamera.parent.TransformPoint(baseCameraPosition);
    /// <summary>Where combat's head moves put the eyes this frame (CombatHeadMotion.Offset, in the world). The body adds it too.</summary>
    internal Vector3 HeadDisplacement { get; private set; }
    private CombatHeadMotion headMotion;
    private ObstacleTraversalState punchSource;
    private int punchSerial;
    private Vector2 traversalPunch, punchVelocity;
    private float divePitch;

    private float lastHorizontalSpeed;

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
        if (movement == null || !movement.IsInitialized || cam == null || effectsPivot == null)
            return;

        float dt = Time.deltaTime;
        if (GamePause.IsPaused || dt <= 0f) return;

        UpdateCollisionImpacts();
        UpdateDynamicFov(dt);
        UpdateTilt(dt);
        UpdateTraversalPunch(dt);
        UpdateDivePresentation(dt);
        UpdateActionImpulses(dt);

        ApplyCameraRotation();
        ApplyLeanPosition();
        // Without a visible body this is the camera's final pose; PlayerBody moves the eyes again when it places it.
        if (eyes == null) eyes = PlayerView.EyesOf(movement);
        PlayerView.Follow(eyes, targetCamera);
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
        // A dodge or a roll throws the body and lets it go as its clip does: no collision (it jolted the view at every dodge).
        if (movement.CurrentState == PlayerMovement.MovementState.StepDodge || movement.CurrentState == PlayerMovement.MovementState.LandingRoll)
        {
            lastHorizontalSpeed = 0f;
            return;
        }

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
        // Combat's head moves (a slip's dip): right, up and forward of the body, on top of everything, as asked.
        var head = HeadMotion;
        HeadDisplacement = head != null ? movement.transform.TransformDirection(head.Offset) : Vector3.zero;
        if (body == null) body = movement.GetComponent<PlayerBody>();
        // The visible body composes the final eye view after these effects and moves the leaning hitboxes to where its eyes
        // went (BodyLean). Without a body, the pivot camera goes sideways by the lean.
        if (body != null && body.isActiveAndEnabled && body.BodyAnimator != null && body.View == targetCamera) return;
        Vector3 origin = targetCamera.parent.TransformPoint(baseCameraPosition);
        Vector3 offset = movement.transform.right * movement.LeanOffset;
        float distance = offset.magnitude;
        if (distance > 0.001f && Physics.SphereCast(origin, 0.1f, offset / distance, out RaycastHit hit,
            distance + 0.02f, movement.groundLayers, QueryTriggerInteraction.Ignore))
            offset *= Mathf.Clamp01((hit.distance - 0.02f) / distance);
        targetCamera.position = origin + offset + HeadDisplacement;
        movement.GetComponent<PlayerLeanHitboxes>().OrNull()?.Sync(offset);
    }

    // How far down the sights the held gun is (0 at the hip or high ready, 1 aimed), for the zoom and the still view.
    private float Aim
    {
        get
        {
            if (weapon == null && movement != null) weapon = movement.GetComponent<WeaponController>();
            return weapon != null && weapon.EquippedData != null && !weapon.IsHighReady ? weapon.AimBlend : 0f;
        }
    }

    // The Esc menu's screen shake scales impacts, punches and movement tilt; Q/E lean is aiming, not shake.
    private static float Shake => GameSettings.Current.screenShake;

    // Combat's head moves for this PC, added to the PC's player when it spawns (PlayerPerfectDefense); none on other PCs.
    private CombatHeadMotion HeadMotion
    {
        get
        {
            if (headMotion == null) movement.TryGetComponent(out headMotion);
            return headMotion != null && headMotion.isActiveAndEnabled ? headMotion : null;
        }
    }

    private void ApplyCameraRotation()
    {
        float shake = Shake;
        if (movementPunch == null) movementPunch = movement.GetComponent<PlayerMovementPunch>();
        Vector3 steps = movementPunch != null && movementPunch.isActiveAndEnabled ? movementPunch.Angles : Vector3.zero;
        // Down the sights the view holds still as a marksman's does (Rainbow Six Siege): the footsteps' nods and the
        // strafing tilt fade out with the aim (user, 2026-11-09: "흔들거려서").
        float still = 1f - Aim;
        steps *= still;
        // Combat's head turns (the back elbow's glance over the shoulder) are moves of the head, not shake: never scaled.
        // The look and the player's facing stay where they are.
        var head = HeadMotion;
        Vector3 glance = head != null ? head.Angles : Vector3.zero;
        effectsPivot.localRotation = Quaternion.Euler(
            (traversalPunch.x + divePitch + impulseRotDisplacement.x + steps.x) * shake + glance.x,
            (impulseRotDisplacement.y + steps.y) * shake + glance.y,
            (currentTiltRoll * still + traversalPunch.y + impulseRotDisplacement.z + steps.z) * shake + movement.LeanRoll + glance.z
        );
    }

    private void UpdateTraversalPunch(float dt)
    {
        var traversal = movement.StateMachine.Current as ObstacleTraversalState;
        // Not climbing off a ladder's top: warped now (WarpedTraversal), it counts as a straight-line move, but it starts
        // from hanging still and its kick shook the view as the climb began (user, 2026-11-08).
        if (traversal != null && traversal.MovesInStraightLine && movement.Abilities.Traversal.Obstacle.ExitLadder == null
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
        HeadDisplacement = Vector3.zero;
        LookScale = 1f;
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
        float aim = Aim;
        float aimedFov = currentFov;
        float turn = 1f;
        if (aim > 0f)
        {
            // The aimed view is the shown sight's (GunMounts.AimedFov, GunParts.csv): a sight that magnifies narrows the
            // whole view, so the world round the scope is as large as the world in its lens; any other leaves the gun's
            // own aimed field (Weapons.csv adsFov). It was the gun's field whatever the sight, so a 4x scope showed the
            // plain view with a 4x circle in it (user, 2026-11-18: "일반 화면 + 확대 화면 2개로 보임").
            float own = Mathf.Min(baseFov, weapon.EquippedData.AdsFov);
            var gun = weapon.HeldGun;
            aimedFov = gun != null ? gun.AimedFov(baseFov, own) : own;
            // The mouse through a sight that magnifies the whole view is the player's own setting for that magnification
            // (ScopeLook, the settings' controls tab): at 100% the view turns by 1 / magnification of the mouse's turn, so
            // a target crosses the screen as fast as it does unaimed. The gun's own aimed field never slowed the mouse,
            // and still doesn't.
            if (gun != null) turn = ScopeLook.Turn(gun.SightPart, GameSettings.Current);
        }
        LookScale = Mathf.Lerp(1f, turn, aim);
        cam.fieldOfView = Mathf.Lerp(currentFov, aimedFov, aim)
            + impulseFovDisplacement * Shake * Mathf.Lerp(1f, .25f, aim);
    }

    /// <summary>How much of the mouse's turn the view takes this frame (1 unless aimed through a sight that magnifies):
    /// PlayerLook multiplies its sensitivity by it.</summary>
    public float LookScale { get; private set; } = 1f;

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
}
