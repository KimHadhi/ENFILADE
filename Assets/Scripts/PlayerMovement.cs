using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-100)]
public sealed class PlayerMovement : MonoBehaviour
{
    public enum MovementState
    {
        Standing,
        Crouching,
        Sliding,        // 지상 엉덩이 슬라이딩
        Diving,         // 전방 공중 다이빙 (배가 아래)
        SupineDiving,   // 후방 공중 다이빙 (등이 아래)
        DiveSliding,    // 배 깔고 미끄러지는 헤드퍼스트 슬라이딩
        SupineSliding,  // 등 깔고 뒤로 미끄러지는 백 슬라이딩
        Proning,        // 일반 엎드리기 (포복)
        Supine          // 백 크롤 (누운 자세, S계열 후진만 가능)
    }

    [Header("Locomotion Speeds")]
    [SerializeField, Min(0f)] private float walkSpeed = 5.2f;
    [SerializeField, Min(0f)] private float sprintSpeed = 8.4f;
    [SerializeField, Min(0f)] private float crouchSpeed = 3.0f;
    [SerializeField, Min(0f)] private float proneSpeed = 1.6f;
    [SerializeField, Min(0f)] private float supineSpeed = 1.6f;
    [SerializeField, Min(0.01f)] private float sprintRampUpDuration = 0.1f;

    [Header("FOV & Visual Compensation")]
    [SerializeField, Range(1f, 1.3f)] private float strafeCompensation = 1.15f;

    [Header("Heights & Eye Offsets")]
    [SerializeField] private Transform crouchPivot;
    [SerializeField] private float crouchOffset = -0.63f;
    [SerializeField] private float slideOffset = -0.98f;
    [SerializeField] private float proneOffset = -1.23f;
    [SerializeField] private float supineOffset = -1.13f;

    [SerializeField, Min(0.3f)] private float crouchColliderHeight = 0.9f;
    [SerializeField, Min(0.3f)] private float slideColliderHeight = 0.6f;
    [SerializeField, Min(0.2f)] private float proneColliderHeight = 0.4f;
    [SerializeField, Min(0f)] private float transitionSharpness = 14f;
    [SerializeField, Min(0f)] private float slideTransitionSharpness = 8.5f;

    [Header("Sliding Physics")]
    [SerializeField, Min(0f)] private float slideKickBoost = 1.0f;
    [SerializeField, Min(0f)] private float slideFriction = 6.5f;
    [Tooltip("공중에 떴을 때 적용할 슬라이딩 마찰력 배율 (기본 25% = 0.25)")]
    [SerializeField, Range(0.05f, 0.8f)] private float airSlideFrictionMultiplier = 0.25f;
    [SerializeField, Min(0f)] private float slopeSlideGravity = 28f;
    [SerializeField, Min(0f)] private float slideEndSpeed = 2.0f;
    [SerializeField, Min(0f)] private float slideCooldown = 0.3f;
    [SerializeField, Min(0f)] private float slideSteerSharpness = 4.0f;
    [SerializeField, Min(0.1f)] private float groundSnapDistance = 0.5f;
    [Tooltip("슬라이딩 시작 후 점프가 가능해지기까지의 최소 시간 (선입력 버퍼 연동)")]
    [SerializeField, Min(0.01f)] private float slideJumpMinDuration = 0.1f;

    [Header("Diving & Jump-Kicking")]
    [SerializeField, Min(0.5f)] private float diveJumpHeight = 1.35f;
    [Tooltip("전방 다이빙(Diving) 시 중력 배율 (기본 낙하감)")]
    [SerializeField, Range(0.3f, 1.5f)] private float diveGravityScale = 0.72f;
    [Tooltip("후방/배면 다이빙(SupineDiving) 시 중력 배율 (체공 시간 증대)")]
    [SerializeField, Range(0.1f, 1.2f)] private float supineDiveGravityScale = 0.45f;
    [SerializeField, Range(0.5f, 1.0f)] private float diveMomentumRetention = 0.85f;
    [SerializeField, Min(1f)] private float minDiveSpeed = 5.5f;
    [SerializeField, Min(5f)] private float maxDiveSpeed = 8.5f;
    [SerializeField, Min(0f)] private float airDiveVerticalPop = 4.0f;
    [Tooltip("다이빙 중 공기 저항으로 인한 초당 미세 감속량")]
    [SerializeField, Min(0f)] private float diveAirResistance = 0.4f;
    [Tooltip("다이빙 중 시선 쪽으로 궤적이 완만하게 따라오는 조향 감도")]
    [SerializeField, Min(0.1f)] private float diveLookControlSharpness = 1.4f;
    [Tooltip("다이빙 중 조향이 허용되는 현재 진행 방향 기준 최대 각도 (좌우 허용각, 기본 60도)")]
    [SerializeField, Range(30f, 85f)] private float diveSteerMaxAngle = 60f;

    [Header("Head-First Slide")]
    [SerializeField, Min(0f)] private float diveSlideKickBoost = 3.8f;
    [SerializeField, Min(0f)] private float landingSlideFriction = 4.0f;
    [SerializeField, Min(0f)] private float maxLandingSlideSpeed = 10.0f;

    [Header("Air Movement & Jump")]
    [SerializeField, Min(0f)] private float groundSharpness = 14f;
    [SerializeField, Min(0f)] private float airControlSharpness = 5.0f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.3f;
    [SerializeField] private float gravity = -24f;
    [SerializeField, Range(0f, 0.2f)] private float coyoteTime = 0.1f;
    [SerializeField, Range(0f, 0.3f)] private float jumpBufferTime = 0.15f;

    [Header("Collision & Layers")]
    [SerializeField] private LayerMask groundLayers = ~0;

    private CharacterController controller;
    private InputAction moveAction;
    private InputAction sprintAction;
    private InputAction jumpAction;
    private InputAction crouchAction;
    private InputAction proneAction;

    private MovementState currentState = MovementState.Standing;
    private MovementState previousState = MovementState.Standing;
    private float currentStateDuration;

    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private Vector3 slideDirection;
    private float currentSlideSpeed;
    private float slideCooldownTimer;

    private float sprintProgress;
    private bool movementEnabled = true;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private bool isGrounded;
    private Vector3 groundNormal = Vector3.up;
    private float jumpUngroundTimer;

    private float standingColliderHeight;
    private float defaultStepOffset;

    private float baseCameraY;
    private float currentOffset;

    private Vector3 previousPosition;
    private Vector3 actualVelocity;

    public MovementState CurrentState => currentState;
    public MovementState PreviousState => previousState;
    public float CurrentStateDuration => currentStateDuration;
    public bool IsGrounded => isGrounded;
    public bool IsSliding => currentState == MovementState.Sliding || currentState == MovementState.DiveSliding || currentState == MovementState.SupineSliding;
    public bool IsDiving => currentState == MovementState.Diving || currentState == MovementState.SupineDiving;
    public bool IsCrouching => currentState == MovementState.Crouching;
    public bool IsProning => currentState == MovementState.Proning || currentState == MovementState.Supine;
    public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;
    public Vector3 ActualVelocity => actualVelocity;
    public Vector2 CurrentInput => moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

    private bool InputAvailable => isActiveAndEnabled && movementEnabled && Time.timeScale > 0f && Application.isFocused;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        groundLayers &= ~(1 << gameObject.layer);

        var actions = InputSystem.actions;
        if (actions != null)
        {
            moveAction = actions.FindAction("Player/Move", throwIfNotFound: false);
            sprintAction = actions.FindAction("Player/Sprint", throwIfNotFound: false);
            jumpAction = actions.FindAction("Player/Jump", throwIfNotFound: false);
            crouchAction = actions.FindAction("Player/Crouch", throwIfNotFound: false);
            proneAction = actions.FindAction("Player/Prone", throwIfNotFound: false);
        }

        standingColliderHeight = controller.height;
        defaultStepOffset = controller.stepOffset;
        controller.center = new Vector3(0f, standingColliderHeight * 0.5f, 0f);

        if (crouchPivot == null)
            crouchPivot = transform.Find("CrouchPivot");

        if (crouchPivot != null)
        {
            baseCameraY = crouchPivot.localPosition.y;
            currentOffset = 0f;
        }

        previousPosition = transform.position;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        coyoteTimer -= dt;
        jumpBufferTimer -= dt;
        slideCooldownTimer -= dt;
        jumpUngroundTimer -= dt;
        currentStateDuration += dt;

        CheckGroundedAndSlope();
        ProcessSprintRampUp(dt);
        ProcessStateTransitions();
        ProcessJump();

        if (IsSliding)
        {
            UpdateSlidePhysics(dt);
        }
        else
        {
            CalculateStandardMovement(dt);
        }

        ApplyGravity(dt);

        Vector3 move = (horizontalVelocity + Vector3.up * verticalVelocity) * dt;
        controller.Move(move);

        if (IsSliding)
        {
            CheckMultiRayWallCollision();
        }

        if (dt > 0f)
        {
            actualVelocity = (transform.position - previousPosition) / dt;
            previousPosition = transform.position;
        }

        UpdateHeightsAndTransform(dt);
    }

    private void ChangeState(MovementState newState)
    {
        if (currentState == newState) return;
        previousState = currentState;
        currentState = newState;
        currentStateDuration = 0f;
    }

    private void CheckGroundedAndSlope()
    {
        if (jumpUngroundTimer > 0f)
        {
            isGrounded = false;
            groundNormal = Vector3.up;
            return;
        }

        bool wasGroundedBefore = isGrounded;
        Vector3 sphereCenter = transform.position + controller.center - Vector3.up * (controller.height * 0.5f - controller.radius);

        bool sphereHit = Physics.SphereCast(sphereCenter, controller.radius * 0.9f, Vector3.down, out RaycastHit hit, 0.25f, groundLayers, QueryTriggerInteraction.Ignore);
        bool hasPhysicalContact = sphereHit || controller.isGrounded;

        if (hasPhysicalContact)
        {
            bool centerHasGround = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit rayHit, controller.height * 0.5f + 0.35f, groundLayers, QueryTriggerInteraction.Ignore);

            if (centerHasGround)
            {
                isGrounded = true;
                groundNormal = rayHit.normal;
                coyoteTimer = coyoteTime;
            }
            else
            {
                if (IsSliding || IsDiving)
                {
                    isGrounded = false;
                    groundNormal = Vector3.up;
                }
                else
                {
                    isGrounded = true;
                    groundNormal = sphereHit ? hit.normal : Vector3.up;
                    coyoteTimer = coyoteTime;
                }
            }
        }
        else
        {
            isGrounded = false;
            groundNormal = Vector3.up;
        }

        if (!wasGroundedBefore && isGrounded)
        {
            float landingSpeed = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).magnitude;

            if (currentState == MovementState.SupineDiving)
            {
                if (landingSpeed > slideEndSpeed) StartLandingSlide(MovementState.SupineSliding, landingSpeed, -transform.forward, false);
                else ChangeState(MovementState.Supine);
            }
            else if (currentState == MovementState.Diving)
            {
                if (landingSpeed > slideEndSpeed) StartLandingSlide(MovementState.DiveSliding, landingSpeed, transform.forward, false);
                else ChangeState(MovementState.Proning);
            }
            else if (!IsSliding)
            {
                bool crouchHeld = InputAvailable && crouchAction != null && crouchAction.IsPressed();
                if (crouchHeld && landingSpeed > (walkSpeed * 0.9f) && slideCooldownTimer <= 0f)
                {
                    StartSlide(landingSpeed);
                }
            }
        }
    }

    private void ProcessSprintRampUp(float dt)
    {
        Vector2 input = CurrentInput;
        bool sprintHeld = sprintAction != null && sprintAction.IsPressed();
        bool canSprintState = currentState == MovementState.Standing || currentState == MovementState.Crouching;
        bool wantsToSprint = InputAvailable && isGrounded && sprintHeld && input.y > 0.1f && canSprintState;

        if (wantsToSprint)
        {
            sprintProgress = Mathf.MoveTowards(sprintProgress, 1f, dt / sprintRampUpDuration);
        }
        else if (!IsSliding && !IsDiving)
        {
            sprintProgress = Mathf.MoveTowards(sprintProgress, 0f, dt / (sprintRampUpDuration * 1.5f));
        }
    }

    private void ProcessStateTransitions()
    {
        bool crouchPressed = InputAvailable && crouchAction != null && crouchAction.IsPressed();
        bool crouchJustPressed = InputAvailable && crouchAction != null && crouchAction.WasPressedThisFrame();
        bool proneJustPressed = InputAvailable && (
            (proneAction != null && proneAction.WasPressedThisFrame()) ||
            (Keyboard.current != null && Keyboard.current.zKey.wasPressedThisFrame)
        );

        bool sprintHeld = InputAvailable && sprintAction != null && sprintAction.IsPressed();
        bool wantsSprintForward = sprintHeld && CurrentInput.y > 0.1f;
        float horizontalSpeed = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).magnitude;

        if (!isGrounded)
        {
            if (currentState == MovementState.Sliding && currentSlideSpeed <= slideEndSpeed)
            {
                StopSlide();
                return;
            }

            if (IsDiving)
            {
                if (horizontalSpeed <= slideEndSpeed && Mathf.Abs(verticalVelocity) < 0.5f)
                {
                    ChangeState(currentState == MovementState.SupineDiving ? MovementState.Supine : MovementState.Proning);
                    return;
                }
            }

            if (currentState == MovementState.Supine || currentState == MovementState.SupineSliding)
            {
                ChangeState(MovementState.SupineDiving);
                return;
            }
            else if (currentState == MovementState.Proning || currentState == MovementState.DiveSliding)
            {
                ChangeState(MovementState.Diving);
                return;
            }

            // 공중 시선 전환 (전방 vs 후방 판별)
            if (IsDiving && horizontalVelocity.sqrMagnitude > 0.1f)
            {
                float velocityDot = Vector3.Dot(horizontalVelocity.normalized, transform.forward);
                if (velocityDot < -0.2f || CurrentInput.y < -0.1f)
                {
                    if (currentState != MovementState.SupineDiving) ChangeState(MovementState.SupineDiving);
                }
                else
                {
                    if (currentState != MovementState.Diving) ChangeState(MovementState.Diving);
                }
                return;
            }

            if (!IsDiving && proneJustPressed && horizontalSpeed > walkSpeed * 0.85f)
            {
                Vector3 moveDir = horizontalVelocity.sqrMagnitude > 0.1f ? horizontalVelocity.normalized : transform.forward;
                float launchSpeed = Mathf.Clamp(horizontalSpeed, minDiveSpeed, maxDiveSpeed);

                horizontalVelocity = moveDir * launchSpeed;
                verticalVelocity = Mathf.Max(verticalVelocity + airDiveVerticalPop, airDiveVerticalPop);
                ChangeState(MovementState.Diving);
                return;
            }

            return;
        }

        switch (currentState)
        {
            case MovementState.Standing:
                bool isFullSprint = (sprintProgress >= 0.9f) || (horizontalSpeed > sprintSpeed * 0.8f);
                if (proneJustPressed && isGrounded)
                {
                    if (isFullSprint && slideCooldownTimer <= 0f)
                    {
                        StartLandingSlide(MovementState.DiveSliding, horizontalSpeed, transform.forward, true);
                    }
                    else
                    {
                        ChangeState(CurrentInput.y < -0.1f ? MovementState.Supine : MovementState.Proning);
                    }
                }
                else if (crouchJustPressed && isGrounded && isFullSprint && slideCooldownTimer <= 0f)
                {
                    StartSlide(horizontalSpeed);
                }
                else if (crouchPressed && isGrounded)
                {
                    ChangeState(MovementState.Crouching);
                }
                break;

            case MovementState.Crouching:
                if (proneJustPressed && isGrounded)
                {
                    ChangeState(CurrentInput.y < -0.1f ? MovementState.Supine : MovementState.Proning);
                }
                else if (wantsSprintForward && !HasCeilingAbove(standingColliderHeight))
                {
                    ChangeState(MovementState.Standing);
                }
                else if (!crouchPressed && !HasCeilingAbove(standingColliderHeight))
                {
                    ChangeState(MovementState.Standing);
                }
                break;

            case MovementState.Proning:
            case MovementState.Supine:
                if (wantsSprintForward && !HasCeilingAbove(standingColliderHeight))
                {
                    ChangeState(MovementState.Standing);
                }
                else if (crouchJustPressed && !HasCeilingAbove(crouchColliderHeight))
                {
                    ChangeState(MovementState.Crouching);
                }
                else if (proneJustPressed && !HasCeilingAbove(crouchColliderHeight))
                {
                    ChangeState(crouchPressed || HasCeilingAbove(standingColliderHeight) ? MovementState.Crouching : MovementState.Standing);
                }
                break;

            case MovementState.Sliding:
                if (currentSlideSpeed <= slideEndSpeed) StopSlide();
                break;

            case MovementState.DiveSliding:
                if (currentSlideSpeed <= slideEndSpeed)
                {
                    if (wantsSprintForward && !HasCeilingAbove(standingColliderHeight))
                        ChangeState(MovementState.Standing);
                    else
                        ChangeState(MovementState.Proning);
                }
                break;

            case MovementState.SupineSliding:
                if (currentSlideSpeed <= slideEndSpeed)
                {
                    ChangeState(MovementState.Supine);
                }
                break;
        }
    }

    private void StartSlide(float currentSpeed)
    {
        ChangeState(MovementState.Sliding);
        sprintProgress = 0f;

        Vector2 input = CurrentInput;
        Vector3 inputDir = (transform.right * input.x + transform.forward * input.y).normalized;
        Vector3 momentumDir = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).normalized;

        slideDirection = momentumDir.sqrMagnitude > 0.01f ? momentumDir : (inputDir.sqrMagnitude > 0.01f ? inputDir : transform.forward);
        currentSlideSpeed = Mathf.Max(currentSpeed, sprintSpeed) + slideKickBoost;
        slideCooldownTimer = slideCooldown;
    }

    private void StartLandingSlide(MovementState slideState, float currentSpeed, Vector3 fallbackDir, bool applyBoost)
    {
        ChangeState(slideState);
        sprintProgress = 0f;

        Vector3 momentumDir = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).normalized;
        slideDirection = momentumDir.sqrMagnitude > 0.01f ? momentumDir : fallbackDir;

        float boost = applyBoost ? diveSlideKickBoost : 0f;
        currentSlideSpeed = Mathf.Min(currentSpeed + boost, maxLandingSlideSpeed);
        slideCooldownTimer = slideCooldown;
    }

    private bool HasGroundAhead(Vector3 direction, float distanceAhead, float checkDepth)
    {
        Vector3 checkOrigin = transform.position + (direction * distanceAhead) + Vector3.up * 0.1f;
        return Physics.Raycast(checkOrigin, Vector3.down, checkDepth + 0.1f, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void UpdateSlidePhysics(float dt)
    {
        Vector2 input = CurrentInput;
        if (Mathf.Abs(input.x) > 0.1f)
        {
            Vector3 steerDir = transform.right * input.x;
            slideDirection = Vector3.Slerp(slideDirection, (slideDirection + steerDir * 0.5f).normalized, slideSteerSharpness * dt);
        }

        Vector3 slopeDirection = Vector3.ProjectOnPlane(slideDirection, groundNormal).normalized;
        float slopeDot = Vector3.Dot(slopeDirection, Vector3.down);
        float baseFriction = (currentState == MovementState.Sliding) ? slideFriction : landingSlideFriction;

        float activeFriction = isGrounded ? baseFriction : (baseFriction * airSlideFrictionMultiplier);

        if (isGrounded && slopeDot > 0.05f)
        {
            currentSlideSpeed += slopeDot * slopeSlideGravity * dt;
        }
        else
        {
            float uphillResistance = (isGrounded && slopeDot < -0.05f) ? Mathf.Abs(slopeDot) * 15f : 0f;
            currentSlideSpeed -= (activeFriction + uphillResistance) * dt;
        }

        currentSlideSpeed = Mathf.Max(currentSlideSpeed, 0f);
        horizontalVelocity = slopeDirection * currentSlideSpeed;

        if (isGrounded)
        {
            bool groundAhead = HasGroundAhead(slideDirection, controller.radius + 0.25f, groundSnapDistance);
            if (groundAhead)
            {
                verticalVelocity = (slopeDot > 0.05f) ? -5f : -2f;
            }
            else
            {
                if (verticalVelocity < 0f) verticalVelocity = 0f;
            }
        }
    }

    private void StopSlide()
    {
        bool crouchPressed = InputAvailable && crouchAction != null && crouchAction.IsPressed();
        bool sprintHeld = InputAvailable && sprintAction != null && sprintAction.IsPressed();
        bool wantsSprintForward = sprintHeld && CurrentInput.y > 0.1f;

        if (!isGrounded)
        {
            ChangeState(crouchPressed ? MovementState.Crouching : MovementState.Standing);
            return;
        }

        if (HasCeilingAbove(crouchColliderHeight)) ChangeState(MovementState.Proning);
        else if (wantsSprintForward && !HasCeilingAbove(standingColliderHeight)) ChangeState(MovementState.Standing);
        else if (crouchPressed || HasCeilingAbove(standingColliderHeight)) ChangeState(MovementState.Crouching);
        else ChangeState(MovementState.Standing);
    }

    private void CheckMultiRayWallCollision()
    {
        if (slideCooldown - slideCooldownTimer < 0.05f) return;

        float topRayY = (currentState == MovementState.Sliding) ? 0.55f : 0.35f;
        float[] checkHeights = new float[] { 0.15f, (0.15f + topRayY) * 0.5f, topRayY };
        float rayDistance = controller.radius + 0.28f;

        for (int i = 0; i < checkHeights.Length; i++)
        {
            Vector3 rayOrigin = transform.position + Vector3.up * checkHeights[i];
            if (Physics.Raycast(rayOrigin, slideDirection, out RaycastHit hit, rayDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                if (Vector3.Dot(hit.normal, -slideDirection) > 0.4f)
                {
                    currentSlideSpeed = 0f;
                    if (currentState == MovementState.DiveSliding) ChangeState(MovementState.Proning);
                    else if (currentState == MovementState.SupineSliding) ChangeState(MovementState.Supine);
                    else StopSlide();
                    return;
                }
            }
        }
    }

    private void ProcessJump()
    {
        if (InputAvailable && jumpAction != null && jumpAction.WasPressedThisFrame())
            jumpBufferTimer = jumpBufferTime;

        if (jumpBufferTimer <= 0f || coyoteTimer <= 0f) return;

        if (currentState == MovementState.Proning || currentState == MovementState.Supine)
        {
            if (!HasCeilingAbove(crouchColliderHeight))
            {
                ChangeState(HasCeilingAbove(standingColliderHeight) ? MovementState.Crouching : MovementState.Standing);
                jumpBufferTimer = 0f;
            }
            return;
        }

        if (currentState == MovementState.DiveSliding || currentState == MovementState.SupineSliding || IsDiving)
            return;

        if (HasCeilingAbove(standingColliderHeight)) return;

        if (currentState == MovementState.Sliding)
        {
            if (currentStateDuration < slideJumpMinDuration)
            {
                return;
            }

            Vector3 launchDirection = slideDirection.normalized;
            float retainedSpeed = currentSlideSpeed * diveMomentumRetention;
            float launchSpeed = Mathf.Clamp(retainedSpeed, minDiveSpeed, maxDiveSpeed);

            horizontalVelocity = launchDirection * launchSpeed;
            verticalVelocity = Mathf.Sqrt(2f * diveJumpHeight * Mathf.Abs(gravity));

            ChangeState(MovementState.Diving);

            jumpUngroundTimer = 0.15f;
            isGrounded = false;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            return;
        }
        else if (currentState == MovementState.Crouching)
        {
            ChangeState(MovementState.Standing);
        }

        verticalVelocity = Mathf.Sqrt(2f * jumpHeight * Mathf.Abs(gravity));
        jumpUngroundTimer = 0.15f;
        isGrounded = false;
        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
    }

    private void CalculateStandardMovement(float dt)
    {
        if (IsDiving)
        {
            float currentAirSpeed = horizontalVelocity.magnitude;
            if (currentAirSpeed > 0.01f)
            {
                currentAirSpeed = Mathf.Max(currentAirSpeed - (diveAirResistance * dt), 0f);

                Vector3 currentDir = horizontalVelocity.normalized;

                Vector3 lookForward = transform.forward;
                lookForward.y = 0f;
                if (lookForward.sqrMagnitude > 0.001f) lookForward.Normalize();
                else lookForward = currentDir;

                Vector3 lookRight = transform.right;
                lookRight.y = 0f;
                lookRight.Normalize();

                Vector2 input = CurrentInput;
                Vector3 candidateTarget = (lookForward + lookRight * (input.x * 0.45f)).normalized;

                float angle = Vector3.Angle(currentDir, candidateTarget);
                Vector3 finalSteerTarget = (angle <= diveSteerMaxAngle) ? candidateTarget : currentDir;

                horizontalVelocity = Vector3.MoveTowards(
                    horizontalVelocity,
                    finalSteerTarget * currentAirSpeed,
                    diveLookControlSharpness * currentAirSpeed * dt
                );
            }
            return;
        }

        Vector2 inputStandard = CurrentInput;
        float inputMagnitude = Mathf.Clamp01(inputStandard.magnitude);
        float runningSpeed = Mathf.Lerp(walkSpeed, sprintSpeed, sprintProgress);
        float targetBaseSpeed;

        switch (currentState)
        {
            case MovementState.Proning: targetBaseSpeed = proneSpeed; break;
            case MovementState.Supine:
                targetBaseSpeed = supineSpeed;
                if (inputStandard.y > -0.01f) { inputStandard = Vector2.zero; inputMagnitude = 0f; }
                break;
            case MovementState.Crouching: targetBaseSpeed = crouchSpeed; break;
            default: targetBaseSpeed = runningSpeed; break;
        }

        Vector3 inputDirection = (transform.right * inputStandard.x + transform.forward * inputStandard.y).normalized;
        float lateralWeight = Mathf.Abs(inputStandard.x);
        float visualBoost = Mathf.Lerp(1f, strafeCompensation, lateralWeight);

        float baseSpeed = targetBaseSpeed * visualBoost;
        Vector3 targetVelocity = inputDirection * (baseSpeed * inputMagnitude);

        if (isGrounded)
        {
            float t = 1f - Mathf.Exp(-groundSharpness * dt);
            horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVelocity, t);
            if (horizontalVelocity.sqrMagnitude < 0.0001f) horizontalVelocity = Vector3.zero;
        }
        else
        {
            float currentAirSpeed = horizontalVelocity.magnitude;
            if (currentAirSpeed > baseSpeed)
            {
                if (inputMagnitude > 0.01f)
                {
                    horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, inputDirection * currentAirSpeed, airControlSharpness * currentAirSpeed * dt);
                }
            }
            else
            {
                float t = 1f - Mathf.Exp(-airControlSharpness * dt);
                horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVelocity, t);
            }
        }
    }

    private void ApplyGravity(float dt)
    {
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        else
        {
            float currentGravity = gravity;

            if (currentState == MovementState.SupineDiving)
            {
                currentGravity *= supineDiveGravityScale;
            }
            else if (currentState == MovementState.Diving)
            {
                currentGravity *= diveGravityScale;
            }

            verticalVelocity += currentGravity * dt;
        }
        verticalVelocity = Mathf.Max(verticalVelocity, -50f);
    }

    private void HasCeilingAbove_Helper() { }

    private bool HasCeilingAbove(float targetHeightToCheck)
    {
        float currentHeight = controller.height;
        if (currentHeight >= targetHeightToCheck) return false;

        float radius = controller.radius * 0.9f;
        Vector3 bottomCenter = transform.position + Vector3.up * currentHeight;
        Vector3 topCenter = transform.position + Vector3.up * (targetHeightToCheck - radius);

        if (topCenter.y < bottomCenter.y) topCenter = bottomCenter + Vector3.up * 0.05f;
        return Physics.CheckCapsule(bottomCenter, topCenter, radius, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void UpdateHeightsAndTransform(float dt)
    {
        float targetColliderHeight;
        float targetOffset;

        switch (currentState)
        {
            case MovementState.Supine:
            case MovementState.SupineSliding:
            case MovementState.SupineDiving:
                targetColliderHeight = proneColliderHeight;
                targetOffset = supineOffset;
                break;
            case MovementState.Proning:
            case MovementState.DiveSliding:
                targetColliderHeight = proneColliderHeight;
                targetOffset = proneOffset;
                break;
            case MovementState.Diving:
                targetColliderHeight = proneColliderHeight;
                targetOffset = crouchOffset;
                break;
            case MovementState.Sliding:
                targetColliderHeight = slideColliderHeight;
                targetOffset = slideOffset;
                break;
            case MovementState.Crouching:
                targetColliderHeight = crouchColliderHeight;
                targetOffset = crouchOffset;
                break;
            default:
                targetColliderHeight = standingColliderHeight;
                targetOffset = 0f;
                break;
        }

        if (!Mathf.Approximately(controller.height, targetColliderHeight))
        {
            controller.height = targetColliderHeight;
            controller.center = new Vector3(0f, targetColliderHeight * 0.5f, 0f);

            bool shouldHaveStep = (currentState == MovementState.Standing || currentState == MovementState.Crouching);
            controller.stepOffset = shouldHaveStep ? defaultStepOffset : 0f;
        }

        if (crouchPivot != null)
        {
            float currentSharpness = IsSliding || IsDiving ? slideTransitionSharpness : transitionSharpness;
            float t = 1f - Mathf.Exp(-currentSharpness * dt);
            currentOffset = Mathf.Lerp(currentOffset, targetOffset, t);

            Vector3 pos = crouchPivot.localPosition;
            crouchPivot.localPosition = new Vector3(pos.x, baseCameraY + currentOffset, pos.z);
        }
    }

    public void ResetVelocity()
    {
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        currentSlideSpeed = 0f;
        sprintProgress = 0f;
        jumpUngroundTimer = 0f;
        actualVelocity = Vector3.zero;
        previousPosition = transform.position;
        currentState = MovementState.Standing;
    }

    private void OnDisable() => ResetVelocity();
}