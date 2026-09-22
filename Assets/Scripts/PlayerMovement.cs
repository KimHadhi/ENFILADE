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
        Sliding,        // 일반 지상 엉덩이 슬라이딩
        Diving,         // 슬라이딩 중 점프하여 공중으로 솟구친 다이빙
        DiveSliding,    // 배 깔고 미끄러지는 헤드퍼스트 슬라이딩
        Proning,        // 엎드리기 (전방향 기어가기)
        SupineSliding,  // 등 깔고 뒤로 미끄러지는 백 슬라이딩
        Supine          // 백 크롤 (누운 자세, S계열 후진만 가능)
    }

    [Header("Locomotion")]
    [Tooltip("기본 걷기 속도")]
    [SerializeField, Min(0f)] private float walkSpeed = 5.2f;
    [Tooltip("전력 질주 최고 속도")]
    [SerializeField, Min(0f)] private float sprintSpeed = 8.4f;
    [SerializeField, Min(0f)] private float crouchSpeed = 3.0f;
    [Tooltip("엎드려 기어가기 속도")]
    [SerializeField, Min(0f)] private float proneSpeed = 1.6f;
    [Tooltip("백 크롤(누운 자세) 기어가기 속도")]
    [SerializeField, Min(0f)] private float supineSpeed = 1.6f;
    [Tooltip("걷기에서 전력 질주 최고 속도까지 걸리는 시간")]
    [SerializeField, Min(0.01f)] private float sprintRampUpDuration = 0.1f;

    [Header("FOV Visual Compensation")]
    [SerializeField, Range(1f, 1.3f)] private float strafeCompensation = 1.15f;

    [Header("Heights & Eye Levels")]
    [SerializeField] private Transform crouchPivot;
    [SerializeField, Min(0.3f)] private float crouchColliderHeight = 0.9f;
    [SerializeField] private float crouchCameraY = 0.85f;
    [SerializeField, Min(0.3f)] private float slideColliderHeight = 0.6f;
    [SerializeField] private float slideCameraY = 0.5f;
    [Tooltip("엎드리기/다이빙 시 콜라이더 높이")]
    [SerializeField, Min(0.2f)] private float proneColliderHeight = 0.4f;
    [Tooltip("엎드리기 카메라 눈높이")]
    [SerializeField] private float proneCameraY = 0.25f;
    [Tooltip("백 크롤(누운 자세) 시 고개를 살짝 든 카메라 눈높이")]
    [SerializeField] private float supineCameraY = 0.35f;
    [SerializeField, Min(0f)] private float transitionSharpness = 14f;
    [SerializeField, Min(0f)] private float slideTransitionSharpness = 8.5f;

    [Header("Sliding Settings (일반 슬라이딩)")]
    [SerializeField, Min(0f)] private float slideKickBoost = 2.0f;
    [SerializeField, Min(0f)] private float slideFriction = 6.5f;
    [SerializeField, Min(0f)] private float slopeSlideGravity = 28f;
    [SerializeField, Min(0f)] private float slideEndSpeed = 2.0f;
    [SerializeField, Min(0f)] private float slideCooldown = 0.3f;
    [SerializeField, Min(0f)] private float slideSteerSharpness = 4.0f;
    [SerializeField, Min(0.1f)] private float groundSnapDistance = 0.4f;

    [Header("Dolphin Dive & Supine Slide Settings")]
    [Tooltip("슬라이딩 중 점프 시 전방으로 몸을 던지는 추진력")]
    [SerializeField, Min(0f)] private float diveForwardBoost = 3.5f;
    [Tooltip("다이브 수직 도약 높이")]
    [SerializeField, Min(0.3f)] private float diveJumpHeight = 0.9f;
    [Tooltip("다이브 및 백 슬라이딩 마찰력 (몸이 닿아 마찰이 심함)")]
    [SerializeField, Min(0f)] private float diveSlideFriction = 7.5f;
    [Tooltip("다이브 착지 시 최소 보장 속도")]
    [SerializeField, Min(0f)] private float minDiveSlideSpeed = 7.0f;

    [Header("Responsive Air Movement")]
    [SerializeField, Min(0f)] private float groundSharpness = 14f;
    [SerializeField, Min(0f)] private float airControlSharpness = 5.0f;

    [Header("Jump & Gravity")]
    [SerializeField, Min(0f)] private float jumpHeight = 1.3f;
    [SerializeField] private float gravity = -24f;
    [SerializeField, Range(0f, 0.2f)] private float coyoteTime = 0.1f;
    [SerializeField, Range(0f, 0.2f)] private float jumpBufferTime = 0.1f;

    [Header("Collision & Layers")]
    [SerializeField] private LayerMask groundLayers = ~0;

    private CharacterController controller;
    private InputAction moveAction;
    private InputAction sprintAction;
    private InputAction jumpAction;
    private InputAction crouchAction;
    private InputAction proneAction;

    private MovementState currentState = MovementState.Standing;
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
    private float standingCameraY;
    private float currentCameraY;

    private Vector3 previousPosition;
    private Vector3 actualVelocity;

    public MovementState CurrentState => currentState;
    public bool IsGrounded => isGrounded;
    public bool IsSliding => currentState == MovementState.Sliding || currentState == MovementState.DiveSliding || currentState == MovementState.SupineSliding;
    public bool IsCrouching => currentState == MovementState.Crouching;
    public bool IsProning => currentState == MovementState.Proning || currentState == MovementState.Supine;
    public bool IsDiving => currentState == MovementState.Diving;
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
            standingCameraY = crouchPivot.localPosition.y;
            currentCameraY = standingCameraY;
        }

        Transform lookPivot = transform.Find("CrouchPivot/BobPivot/LookPivot");
        if (lookPivot != null && lookPivot.localPosition.y != 0f)
        {
            if (Mathf.Approximately(standingCameraY, 0f))
            {
                standingCameraY = lookPivot.localPosition.y;
                currentCameraY = standingCameraY;
                Vector3 cpPos = crouchPivot.localPosition;
                crouchPivot.localPosition = new Vector3(cpPos.x, standingCameraY, cpPos.z);
            }
            Vector3 lpPos = lookPivot.localPosition;
            lookPivot.localPosition = new Vector3(lpPos.x, 0f, lpPos.z);
        }

        previousPosition = transform.position;
    }

    private void Update()
    {
        coyoteTimer -= Time.deltaTime;
        jumpBufferTimer -= Time.deltaTime;
        slideCooldownTimer -= Time.deltaTime;
        jumpUngroundTimer -= Time.deltaTime;

        CheckGroundedAndSlope();
        ProcessSprintRampUp();
        ProcessStateTransitions();
        ProcessJump();

        if (IsSliding)
        {
            UpdateSlidePhysics();
        }
        else
        {
            CalculateStandardMovement();
        }

        ApplyGravity();

        Vector3 move = (horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime;
        CollisionFlags flags = controller.Move(move);

        if (IsSliding)
        {
            CheckMultiRayWallCollision();
        }

        if (Time.deltaTime > 0f)
        {
            actualVelocity = (transform.position - previousPosition) / Time.deltaTime;
            previousPosition = transform.position;
        }

        UpdateHeightsAndTransform();
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

        if (Physics.SphereCast(sphereCenter, controller.radius * 0.9f, Vector3.down, out RaycastHit hit, 0.25f, groundLayers, QueryTriggerInteraction.Ignore))
        {
            isGrounded = true;
            groundNormal = hit.normal;
            coyoteTimer = coyoteTime;
        }
        else
        {
            isGrounded = controller.isGrounded;
            groundNormal = Vector3.up;
            if (isGrounded) coyoteTimer = coyoteTime;
        }

        // [핵심] 다이빙 중 착지 판정 (180도 스핀 감지 -> 백 슬라이딩)
        if (!wasGroundedBefore && isGrounded)
        {
            if (currentState == MovementState.Diving)
            {
                float landingSpeed = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).magnitude;

                // 현재 이동하는 물리 방향과 카메라가 바라보는 방향의 내적 계산 (앞:-1, 뒤:-1)
                float velocityDot = Vector3.Dot(horizontalVelocity.normalized, transform.forward);

                // 시점을 90도 이상 뒤로 돌렸거나(velocityDot < -0.2f), 명시적으로 S키를 누르고 있다면 등(Back)으로 미끄러짐
                if (velocityDot < -0.2f || CurrentInput.y < -0.1f)
                {
                    StartSupineSlide(landingSpeed);
                }
                else
                {
                    StartDiveSlide(landingSpeed);
                }
            }
        }
    }

    private void ProcessSprintRampUp()
    {
        Vector2 input = CurrentInput;
        bool sprintHeld = sprintAction != null && sprintAction.IsPressed();

        bool canSprintState = currentState == MovementState.Standing || currentState == MovementState.Crouching;
        bool wantsToSprint = InputAvailable && isGrounded && sprintHeld && input.y > 0.1f && canSprintState;

        if (wantsToSprint)
        {
            sprintProgress = Mathf.MoveTowards(sprintProgress, 1f, Time.deltaTime / sprintRampUpDuration);
        }
        else if (!IsSliding && currentState != MovementState.Diving)
        {
            sprintProgress = Mathf.MoveTowards(sprintProgress, 0f, Time.deltaTime / (sprintRampUpDuration * 1.5f));
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

        bool sprintJustPressed = InputAvailable && sprintAction != null && sprintAction.WasPressedThisFrame();
        float horizontalSpeed = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).magnitude;

        // 공중 일반/백 슬라이딩 선입력 얼리 스냅
        if (!isGrounded && !IsSliding && currentState != MovementState.Diving)
        {
            bool isBackward = CurrentInput.y < -0.1f;
            if (crouchPressed && horizontalSpeed > (walkSpeed * 1.05f) && slideCooldownTimer <= 0f)
            {
                if (TrySnapToGround(groundSnapDistance, out RaycastHit snapHit))
                {
                    groundNormal = snapHit.normal;
                    isGrounded = true;
                    verticalVelocity = -2f;

                    if (isBackward) StartSupineSlide(horizontalSpeed);
                    else StartSlide(horizontalSpeed);
                    return;
                }
            }
            return;
        }

        switch (currentState)
        {
            case MovementState.Standing:
                bool isFullSprint = (sprintProgress >= 0.95f) || (horizontalSpeed > sprintSpeed * 0.85f);

                if (proneJustPressed && isGrounded)
                {
                    if (isFullSprint && slideCooldownTimer <= 0f)
                    {
                        StartDiveSlide(horizontalSpeed);
                    }
                    else
                    {
                        // S키 누르고 엎드리기 입력 시 -> 누운 자세(백 크롤)
                        currentState = (CurrentInput.y < -0.1f) ? MovementState.Supine : MovementState.Proning;
                    }
                }
                else if (crouchJustPressed && isGrounded && isFullSprint && slideCooldownTimer <= 0f)
                {
                    StartSlide(horizontalSpeed);
                }
                else if (crouchPressed && isGrounded)
                {
                    currentState = MovementState.Crouching;
                }
                break;

            case MovementState.Crouching:
                if (proneJustPressed && isGrounded)
                {
                    currentState = (CurrentInput.y < -0.1f) ? MovementState.Supine : MovementState.Proning;
                }
                else if (!crouchPressed && !HasCeilingAbove(standingColliderHeight))
                {
                    currentState = MovementState.Standing;
                }
                break;

            case MovementState.Proning:
            case MovementState.Supine:
                // 엎드리기나 누운 자세에서 탈출(기상) 로직 공유
                if (crouchJustPressed && !HasCeilingAbove(crouchColliderHeight))
                {
                    currentState = MovementState.Crouching;
                }
                else if (proneJustPressed)
                {
                    if (!HasCeilingAbove(crouchColliderHeight))
                    {
                        currentState = crouchPressed || HasCeilingAbove(standingColliderHeight)
                            ? MovementState.Crouching
                            : MovementState.Standing;
                    }
                }
                else if (sprintJustPressed && CurrentInput.y > 0.1f)
                {
                    if (!HasCeilingAbove(standingColliderHeight))
                    {
                        currentState = MovementState.Standing;
                    }
                }
                break;

            case MovementState.Sliding:
                if (currentSlideSpeed <= slideEndSpeed) StopSlide();
                break;

            case MovementState.DiveSliding:
                if (currentSlideSpeed <= slideEndSpeed) currentState = MovementState.Proning;
                break;

            case MovementState.SupineSliding:
                if (currentSlideSpeed <= slideEndSpeed) currentState = MovementState.Supine;
                break;

            case MovementState.Diving:
                break;
        }
    }

    private bool TrySnapToGround(float maxDistance, out RaycastHit hit)
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        if (Physics.Raycast(origin, Vector3.down, out hit, maxDistance + 0.1f, groundLayers, QueryTriggerInteraction.Ignore))
        {
            float distanceToGround = hit.distance - 0.1f;
            if (distanceToGround > 0.001f)
            {
                controller.Move(Vector3.down * distanceToGround);
                currentCameraY += distanceToGround;
                if (crouchPivot != null)
                {
                    Vector3 pos = crouchPivot.localPosition;
                    crouchPivot.localPosition = new Vector3(pos.x, currentCameraY, pos.z);
                }
            }
            return true;
        }
        hit = default;
        return false;
    }

    private void StartSlide(float currentSpeed)
    {
        currentState = MovementState.Sliding;
        sprintProgress = 0f;
        Vector2 input = CurrentInput;
        Vector3 inputDir = (transform.right * input.x + transform.forward * input.y).normalized;
        Vector3 momentumDir = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).normalized;

        slideDirection = momentumDir.sqrMagnitude > 0.01f ? momentumDir : (inputDir.sqrMagnitude > 0.01f ? inputDir : transform.forward);
        currentSlideSpeed = Mathf.Max(currentSpeed, sprintSpeed) + slideKickBoost;
        slideCooldownTimer = slideCooldown;
    }

    private void StartDiveSlide(float currentSpeed)
    {
        currentState = MovementState.DiveSliding;
        sprintProgress = 0f;
        Vector3 momentumDir = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).normalized;
        slideDirection = momentumDir.sqrMagnitude > 0.01f ? momentumDir : transform.forward;
        currentSlideSpeed = Mathf.Max(currentSpeed, minDiveSlideSpeed);
        slideCooldownTimer = slideCooldown;
    }

    private void StartSupineSlide(float currentSpeed)
    {
        currentState = MovementState.SupineSliding;
        sprintProgress = 0f;
        Vector3 momentumDir = new Vector3(horizontalVelocity.x, 0f, horizontalVelocity.z).normalized;
        slideDirection = momentumDir.sqrMagnitude > 0.01f ? momentumDir : -transform.forward; // 뒤로 미끄러짐
        currentSlideSpeed = Mathf.Max(currentSpeed, minDiveSlideSpeed);
        slideCooldownTimer = slideCooldown;
    }

    private void UpdateSlidePhysics()
    {
        Vector2 input = CurrentInput;
        if (Mathf.Abs(input.x) > 0.1f)
        {
            Vector3 steerDir = transform.right * input.x;
            slideDirection = Vector3.Slerp(slideDirection, (slideDirection + steerDir * 0.5f).normalized, slideSteerSharpness * Time.deltaTime);
        }

        Vector3 slopeDirection = Vector3.ProjectOnPlane(slideDirection, groundNormal).normalized;
        float slopeDot = Vector3.Dot(slopeDirection, Vector3.down);

        float activeFriction = (currentState == MovementState.DiveSliding || currentState == MovementState.SupineSliding)
            ? diveSlideFriction
            : slideFriction;

        if (slopeDot > 0.05f)
        {
            currentSlideSpeed += slopeDot * slopeSlideGravity * Time.deltaTime;
        }
        else
        {
            float uphillResistance = slopeDot < -0.05f ? Mathf.Abs(slopeDot) * 15f : 0f;
            currentSlideSpeed -= (activeFriction + uphillResistance) * Time.deltaTime;
        }

        currentSlideSpeed = Mathf.Max(currentSlideSpeed, 0f);
        horizontalVelocity = slopeDirection * currentSlideSpeed;

        if (isGrounded) verticalVelocity = -5f;
    }

    private void StopSlide()
    {
        bool crouchPressed = InputAvailable && crouchAction != null && crouchAction.IsPressed();
        if (HasCeilingAbove(crouchColliderHeight)) currentState = MovementState.Proning;
        else if (crouchPressed || HasCeilingAbove(standingColliderHeight)) currentState = MovementState.Crouching;
        else currentState = MovementState.Standing;
    }

    private void CheckMultiRayWallCollision()
    {
        if (slideCooldown - slideCooldownTimer < 0.05f) return;

        float topRayY = (currentState == MovementState.DiveSliding || currentState == MovementState.SupineSliding) ? 0.35f : 0.55f;
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
                    if (currentState == MovementState.DiveSliding) currentState = MovementState.Proning;
                    else if (currentState == MovementState.SupineSliding) currentState = MovementState.Supine;
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

        if (jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            if (currentState == MovementState.Proning || currentState == MovementState.Supine)
            {
                if (!HasCeilingAbove(crouchColliderHeight))
                {
                    currentState = HasCeilingAbove(standingColliderHeight) ? MovementState.Crouching : MovementState.Standing;
                    jumpBufferTimer = 0f;
                }
                return;
            }

            if (currentState == MovementState.DiveSliding || currentState == MovementState.SupineSliding || currentState == MovementState.Diving)
                return;

            if (HasCeilingAbove(standingColliderHeight)) return;

            if (currentState == MovementState.Sliding)
            {
                currentState = MovementState.Diving;
                Vector3 launchDirection = slideDirection.normalized;

                horizontalVelocity = launchDirection * (currentSlideSpeed + diveForwardBoost);
                verticalVelocity = Mathf.Sqrt(2f * diveJumpHeight * Mathf.Abs(gravity));

                jumpUngroundTimer = 0.15f;
                isGrounded = false;
                coyoteTimer = 0f;
                jumpBufferTimer = 0f;
                return;
            }
            else if (currentState == MovementState.Crouching)
            {
                currentState = MovementState.Standing;
            }

            verticalVelocity = Mathf.Sqrt(2f * jumpHeight * Mathf.Abs(gravity));
            jumpUngroundTimer = 0.15f;
            isGrounded = false;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
        }
    }

    private void CalculateStandardMovement()
    {
        Vector2 input = CurrentInput;
        float inputMagnitude = Mathf.Clamp01(input.magnitude);

        float runningSpeed = Mathf.Lerp(walkSpeed, sprintSpeed, sprintProgress);
        float targetBaseSpeed;

        switch (currentState)
        {
            case MovementState.Proning:
                targetBaseSpeed = proneSpeed;
                break;
            case MovementState.Supine:
                targetBaseSpeed = supineSpeed;
                // [핵심] 백 크롤 강제 조작: S, SA, SD 등 후진 계열 입력이 아니면 아예 이동을 차단 (input.y가 양수거나 0인 순수 A,D 불가)
                if (input.y > -0.01f)
                {
                    input = Vector2.zero;
                    inputMagnitude = 0f;
                }
                break;
            case MovementState.Crouching:
                targetBaseSpeed = crouchSpeed;
                break;
            default:
                targetBaseSpeed = runningSpeed;
                break;
        }

        Vector3 inputDirection = (transform.right * input.x + transform.forward * input.y).normalized;
        float lateralWeight = Mathf.Abs(input.x);
        float visualBoost = Mathf.Lerp(1f, strafeCompensation, lateralWeight);

        float baseSpeed = targetBaseSpeed * visualBoost;
        Vector3 targetVelocity = inputDirection * (baseSpeed * inputMagnitude);

        if (isGrounded)
        {
            float t = 1f - Mathf.Exp(-groundSharpness * Time.deltaTime);
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
                    Vector3 newDir = Vector3.Slerp(horizontalVelocity.normalized, inputDirection, airControlSharpness * Time.deltaTime);
                    horizontalVelocity = newDir * currentAirSpeed;
                }
            }
            else
            {
                float t = 1f - Mathf.Exp(-airControlSharpness * Time.deltaTime);
                horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVelocity, t);
            }
        }
    }

    private void ApplyGravity()
    {
        if (isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        else verticalVelocity += gravity * Time.deltaTime;

        verticalVelocity = Mathf.Max(verticalVelocity, -50f);
    }

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

    private void UpdateHeightsAndTransform()
    {
        float targetColliderHeight;
        float targetCamY;

        switch (currentState)
        {
            case MovementState.Supine:
            case MovementState.SupineSliding:
                targetColliderHeight = proneColliderHeight;
                targetCamY = supineCameraY;  // 일반 엎드리기보다 살짝 높은 0.35
                break;
            case MovementState.Proning:
            case MovementState.Diving:
            case MovementState.DiveSliding:
                targetColliderHeight = proneColliderHeight;
                targetCamY = proneCameraY;
                break;
            case MovementState.Sliding:
                targetColliderHeight = slideColliderHeight;
                targetCamY = slideCameraY;
                break;
            case MovementState.Crouching:
                targetColliderHeight = crouchColliderHeight;
                targetCamY = crouchCameraY;
                break;
            default:
                targetColliderHeight = standingColliderHeight;
                targetCamY = standingCameraY;
                break;
        }

        if (!Mathf.Approximately(controller.height, targetColliderHeight))
        {
            controller.height = targetColliderHeight;
            controller.center = new Vector3(0f, targetColliderHeight * 0.5f, 0f);
            controller.stepOffset = (currentState == MovementState.Standing) ? defaultStepOffset : 0.05f;
        }

        if (crouchPivot != null)
        {
            float currentSharpness = IsSliding || currentState == MovementState.Diving ? slideTransitionSharpness : transitionSharpness;
            float t = 1f - Mathf.Exp(-currentSharpness * Time.deltaTime);
            currentCameraY = Mathf.Lerp(currentCameraY, targetCamY, t);

            Vector3 pos = crouchPivot.localPosition;
            crouchPivot.localPosition = new Vector3(pos.x, currentCameraY, pos.z);
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

    private void OnDisable()
    {
        ResetVelocity();
    }
}