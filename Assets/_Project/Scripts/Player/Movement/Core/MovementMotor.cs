using UnityEngine;
using System.Collections.Generic;
using MovementState = PlayerMovement.MovementState;

// Owns capsule movement and support detection; state rules live in the state tree.
public sealed class MovementMotor
{
    private readonly PlayerMovement ctx;
    internal readonly CharacterController controller;
    public Vector3 HorizontalVelocity;
    public float VerticalVelocity;
    internal bool isGrounded, landedThisFrame;
    internal Vector3 groundNormal = Vector3.up;
    internal float jumpUngroundTimer, airborneTimer;
    internal CollisionFlags lastMoveFlags;
    internal Vector3 lastMoveGroundNormal = Vector3.up;
    internal Vector3 previousPosition, actualVelocity;
    private bool hasRequestedMove;
    private bool hasTraversalPosition;
    private Vector3 traversalPosition;
    private Vector3 requestedMove, additionalMove;
    private readonly List<Vector3> requestedPath = new List<Vector3>();
    private bool slideSupported;
    internal float SupportAngle => ctx.IsSliding ? Mathf.Max(controller.slopeLimit, ctx.AbilitySettings.slideMaxSlopeAngle) : controller.slopeLimit;
    internal void RequestDisplacement(Vector3 displacement) { requestedPath.Clear(); hasRequestedMove = true; requestedMove = displacement; }
    internal void RequestPath(List<Vector3> positions)
    { hasRequestedMove = false; requestedPath.Clear(); requestedPath.AddRange(positions); }
    internal void AddDisplacement(Vector3 displacement) { additionalMove += displacement; }
    // Call only after a standing-capsule sweep, with a single traversal surface
    // excluded. The controller stays enabled; no global collision filters change.
    internal void RequestTraversalPosition(Vector3 position)
    { requestedPath.Clear(); hasRequestedMove = false; hasTraversalPosition = true; traversalPosition = position; }
    internal void RecoverTraversalPosition(Vector3 position)
    {
        hasTraversalPosition = hasRequestedMove = false;
        requestedPath.Clear(); requestedMove = additionalMove = Vector3.zero;
        previousPosition += position - ctx.transform.position;
        ctx.transform.position = position;
        Physics.SyncTransforms();
    }

    internal MovementMotor(PlayerMovement context, CharacterController characterController)
    {
        ctx = context;
        controller = characterController;
        previousPosition = ctx.transform.position;
    }

    internal void TickTimers(float dt)
    {
        jumpUngroundTimer -= dt;
        if (!isGrounded) airborneTimer += dt;
        else airborneTimer = 0f;
    }

    internal void Move(float dt)
    {
        ctx.DodgePassThrough?.BeforeMove();
        ctx.Abilities.Landing.BeforeMove();
        lastMoveGroundNormal = Vector3.zero;
        if (hasTraversalPosition)
        {
            ctx.transform.position = traversalPosition;
            // Keep the controller's native pose aligned even when no physics
            // tick occurs between this render frame and the next normal Move.
            Physics.SyncTransforms();
            lastMoveFlags = CollisionFlags.None;
        }
        else if (requestedPath.Count > 0)
        {
            lastMoveFlags = CollisionFlags.None;
            // Follow the curve within a long frame instead of cutting through its corners.
            foreach (Vector3 point in requestedPath)
                lastMoveFlags |= controller.Move(point - ctx.transform.position);
        }
        else lastMoveFlags = hasRequestedMove ? controller.Move(requestedMove) : MoveCharacter(dt);
        if (additionalMove.sqrMagnitude > 0f) lastMoveFlags |= controller.Move(additionalMove);
        hasTraversalPosition = hasRequestedMove = false; requestedMove = additionalMove = Vector3.zero;
        requestedPath.Clear();
        ctx.Abilities.StepTrail.RecordMovement();
        ctx.DodgePassThrough?.AfterMove();
        if ((lastMoveFlags & CollisionFlags.Above) != 0 && VerticalVelocity > 0f) VerticalVelocity = 0f;
        if (dt > 0f)
        {
            actualVelocity = (ctx.transform.position - previousPosition) / dt;
            previousPosition = ctx.transform.position;
        }
    }

    internal void RecordContact(ControllerColliderHit hit)
    {
        if (hit.normal.y > lastMoveGroundNormal.y) lastMoveGroundNormal = hit.normal;
    }

    internal void Reset()
    {
        HorizontalVelocity = Vector3.zero;
        VerticalVelocity = 0f;
        jumpUngroundTimer = airborneTimer = 0f;
        actualVelocity = Vector3.zero;
        lastMoveFlags = CollisionFlags.None;
        lastMoveGroundNormal = groundNormal = Vector3.up;
        isGrounded = landedThisFrame = false;
        slideSupported = false;
        previousPosition = ctx.transform.position;
        hasTraversalPosition = hasRequestedMove = false; requestedMove = additionalMove = Vector3.zero;
        requestedPath.Clear();
    }

    internal void CheckGroundedAndSlope()
    {
        landedThisFrame = false;
        if (ctx.IsParkour)
        {
            isGrounded = false; slideSupported = false; groundNormal = Vector3.up;
            return;
        }
        if (jumpUngroundTimer > 0f || VerticalVelocity > 0f)
        {
            isGrounded = false;
            slideSupported = false;
            groundNormal = Vector3.up;
            return;
        }

        bool wasGroundedBefore = isGrounded;
        bool touchedGround = controller.isGrounded || (lastMoveFlags & CollisionFlags.Below) != 0 || (ctx.IsSliding && slideSupported);

        Vector3 bottomSphereLocal = controller.center - Vector3.up * (controller.height * 0.5f - controller.radius);
        Vector3 sphereCenter = ctx.transform.TransformPoint(bottomSphereLocal);
        Vector3 scale = ctx.transform.lossyScale;
        float worldRadius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        // Starting inside the support makes SphereCast miss, especially on sloped capsules.
        float probeLift = Mathf.Max(0.1f, worldRadius + WorldSkinWidth * 2f);
        bool sphereHit = Physics.SphereCast(sphereCenter + Vector3.up * probeLift,
            worldRadius * 0.9f, Vector3.down, out RaycastHit hit, 0.25f + probeLift,
            ctx.groundLayers, QueryTriggerInteraction.Ignore);

        Vector3 supportNormal = Vector3.up;
        bool walkableSupport = sphereHit && TryGetSupportNormal(hit, out supportNormal);
        isGrounded = touchedGround || (wasGroundedBefore && walkableSupport);
        float walkableY = Mathf.Cos(SupportAngle * Mathf.Deg2Rad);
        groundNormal = isGrounded
            ? (walkableSupport ? supportNormal : (lastMoveGroundNormal.y >= walkableY ? lastMoveGroundNormal : Vector3.up))
            : Vector3.up;
        if (isGrounded) ctx.Actions.coyoteTimer = ctx.coyoteTime;

        if (isGrounded && (ctx.Posture.airBodyTucked || ctx.Posture.airCrouching))
        {
            bool wasAirCrouching = ctx.Posture.airCrouching;
            ctx.Posture.RebaseAirCrouchFeet();
            if (wasAirCrouching && ctx.StateMachine.Current.AllowsAutomaticPosture && !ctx.IsDiving && !ctx.IsSliding)
            {
                bool crouchHeld = ctx.InputAvailable && ctx.Input.crouchAction != null && ctx.Input.crouchAction.IsPressed();
                ctx.ChangeState(crouchHeld || ctx.HasCeilingAbove(ctx.Posture.standingColliderHeight)
                    ? MovementState.Crouching : MovementState.Standing);
            }
        }

        if (!wasGroundedBefore && isGrounded)
        {
            landedThisFrame = true;
            if (!ctx.Abilities.Landing.OnLanding()) ctx.StateMachine.Current.OnLanding();
        }
    }

    // Feet are derived from the capsule, including its tucked-airborne center.
    private Vector3 ControllerFeet => ctx.transform.TransformPoint(
        controller.center - Vector3.up * (controller.height * 0.5f));

    private float WorldControllerRadius => controller.radius * Mathf.Max(
        Mathf.Abs(ctx.transform.lossyScale.x), Mathf.Abs(ctx.transform.lossyScale.z));

    private float WorldSkinWidth => controller.skinWidth * Mathf.Max(
        Mathf.Abs(ctx.transform.lossyScale.x), Mathf.Abs(ctx.transform.lossyScale.z));

    private bool isSlideGroundSnap;
    public bool IsSlideGroundSnap => isSlideGroundSnap;

    private bool TryWalkableRay(Vector3 origin, float distance, out RaycastHit hit)
    {
        return Physics.Raycast(origin, Vector3.down, out hit, distance,
            ctx.groundLayers, QueryTriggerInteraction.Ignore)
            && hit.normal.y >= Mathf.Cos(SupportAngle * Mathf.Deg2Rad);
    }

    // Sphere contacts at stair edges are radial normals, not tread normals.
    private bool TryGetSupportNormal(RaycastHit sphereHit, out Vector3 normal)
    {
        float lift = Mathf.Max(0.1f, ctx.Posture.defaultStepOffset * Mathf.Abs(ctx.transform.lossyScale.y) + WorldSkinWidth * 2f);
        Vector3 lateralNormal = Vector3.ProjectOnPlane(sphereHit.normal, Vector3.up);
        Vector3 inset = lateralNormal.sqrMagnitude > 0.000001f ? lateralNormal.normalized * 0.02f : Vector3.zero;
        if (TryWalkableRay(sphereHit.point - inset + Vector3.up * lift,
            lift + 0.05f, out RaycastHit surface)
            || TryWalkableRay(sphereHit.point + Vector3.up * lift, lift + 0.05f, out surface))
        {
            normal = surface.normal;
            return true;
        }
        // Triangle seams may put the reported contact just outside either tread.
        // Sample both sides of that seam; do not use its radial collision normal.
        for (int i = 0; i < 4; i++)
        {
            Vector3 offset = i < 2 ? Vector3.right * (i == 0 ? 0.02f : -0.02f)
                : Vector3.forward * (i == 2 ? 0.02f : -0.02f);
            if (TryWalkableRay(sphereHit.point + offset + Vector3.up * lift,
                lift + 0.05f, out surface))
            {
                normal = surface.normal;
                return true;
            }
        }
        normal = Vector3.up;
        return false;
    }

    // Estimate height change ALONG travel, without rotating the horizontal heading.
    // Three lanes make diagonal travel less sensitive to individual tread corners.
    public float GetSlideSlopeDot(Vector3 direction)
    {
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (!isGrounded || direction.sqrMagnitude < 0.001f) return 0f;

        Vector3 feet = ControllerFeet;
        Vector3 right = Vector3.Cross(Vector3.up, direction);
        float reach = Mathf.Max(0.4f, WorldControllerRadius * 2f);
        float width = WorldControllerRadius * 0.5f;
        float lift = Mathf.Max(ctx.groundSnapDistance, ctx.Posture.defaultStepOffset * Mathf.Abs(ctx.transform.lossyScale.y)) + 0.1f;
        float gradeSum = 0f;
        int validLanes = 0;

        for (int lane = -1; lane <= 1; lane++)
        {
            Vector3 origin = feet + right * (lane * width) + Vector3.up * lift;
            float depth = lift + ctx.groundSnapDistance + ctx.Posture.defaultStepOffset * Mathf.Abs(ctx.transform.lossyScale.y);
            if (!TryWalkableRay(origin, depth, out RaycastHit center)
                || !TryWalkableRay(origin - direction * reach, depth, out RaycastHit back)
                || !TryWalkableRay(origin + direction * reach, depth, out RaycastHit front)) continue;

            // Do not infer a ramp across a missing support or a large drop.
            if (Mathf.Abs(front.point.y - center.point.y) > ctx.groundSnapDistance
                || Mathf.Abs(back.point.y - center.point.y) > ctx.groundSnapDistance) continue;
            float grade = (front.point.y - back.point.y) / (2f * reach);
            if (Mathf.Abs(grade) > Mathf.Tan(SupportAngle * Mathf.Deg2Rad)) continue;
            gradeSum += grade;
            validLanes++;
        }

        float slopeGrade = validLanes > 0 ? gradeSum / validLanes
            : -Vector3.Dot(direction, groundNormal) / Mathf.Max(groundNormal.y, 0.01f);
        return -slopeGrade / Mathf.Sqrt(1f + slopeGrade * slopeGrade);
    }

    public bool IsSlideStepContact(ControllerColliderHit hit)
    {
        if (!isGrounded || controller.stepOffset <= 0f) return false;
        Vector3 feet = ControllerFeet;
        float stepHeight = controller.stepOffset * Mathf.Abs(ctx.transform.lossyScale.y);
        float tolerance = Mathf.Max(0.02f, WorldSkinWidth);
        if (hit.point.y > feet.y + stepHeight + tolerance) return false;

        Vector3 intoStep = -Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
        Vector3 origin = hit.point + intoStep * Mathf.Max(0.03f, WorldSkinWidth);
        origin.y = feet.y + stepHeight + tolerance;
        return TryWalkableRay(origin, stepHeight + tolerance * 2f, out RaycastHit top)
            && top.collider == hit.collider
            && top.point.y >= feet.y - tolerance
            && top.point.y <= feet.y + stepHeight + tolerance;
    }

    private CollisionFlags MoveCharacter(float dt)
    {
        if (dt <= 0f) return CollisionFlags.None;
        if (!ctx.IsSliding || !isGrounded || VerticalVelocity > 0f || jumpUngroundTimer > 0f)
        {
            slideSupported = false;
            ctx.Abilities.Slide.BeforeMove(HorizontalVelocity * dt);
            return controller.Move((HorizontalVelocity + Vector3.up * VerticalVelocity) * dt);
        }

        // Advance horizontally first, then settle the whole capsule onto support.
        // Small slices prevent a fast diagonal slide spanning several stair edges.
        float maxAdvance = Mathf.Max(0.05f, WorldControllerRadius * 0.5f);
        int slices = Mathf.Clamp(Mathf.CeilToInt(HorizontalVelocity.magnitude * dt / maxAdvance), 1, 32);
        float sliceDt = dt / slices;
        CollisionFlags flags = CollisionFlags.None;
        for (int i = 0; i < slices; i++)
        {
            flags &= ~CollisionFlags.Below;
            if (!ctx.IsSliding || !isGrounded || VerticalVelocity > 0f)
            {
                flags |= controller.Move((HorizontalVelocity + Vector3.up * VerticalVelocity) * (sliceDt * (slices - i)));
                break;
            }

            ctx.Abilities.Slide.BeforeMove(HorizontalVelocity * sliceDt);
            flags |= controller.Move(HorizontalVelocity * sliceDt);
            // Collision callbacks may change movement state or launch a bounce.
            if (!ctx.IsSliding || VerticalVelocity > 0f) continue;

            if (TrySnapSlidingToGround(out CollisionFlags snapFlags))
            {
                flags |= snapFlags;
                VerticalVelocity = -2f;
            }
            else
            {
                isGrounded = false;
                slideSupported = false;
                flags &= ~CollisionFlags.Below;
                groundNormal = Vector3.up;
            }
        }
        return flags;
    }

    private bool TrySnapSlidingToGround(out CollisionFlags flags)
    {
        flags = CollisionFlags.None;
        Vector3 bottomSphere = ctx.transform.TransformPoint(controller.center
            - Vector3.up * (controller.height * 0.5f - controller.radius));
        float radius = WorldControllerRadius;
        float lift = Mathf.Max(0.15f, radius + WorldSkinWidth * 2f);
        if (!Physics.SphereCast(bottomSphere + Vector3.up * lift, radius,
            Vector3.down, out RaycastHit hit, lift + ctx.groundSnapDistance,
            ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || !TryGetSupportNormal(hit, out Vector3 supportNormal)) return false;

        // Use the volume sweep's distance, not a center ray's tread height:
        // a ray can see the lower tread while the capsule still overlaps the upper one.
        float drop = Mathf.Clamp(hit.distance - lift, 0f, ctx.groundSnapDistance);
        // Always settle, even if the sweep reports zero gap: horizontal Move alone
        // does not reliably report Below. Keep verified support across frames.
        isSlideGroundSnap = true;
        try
        {
            flags = controller.Move(Vector3.down * (drop + Mathf.Max(0.015f, WorldSkinWidth)));
        }
        finally
        {
            isSlideGroundSnap = false;
        }
        groundNormal = supportNormal;
        slideSupported = true;
        flags |= CollisionFlags.Below;
        return true;
    }


    internal void ApplyGravity(float dt)
    {
        // 접지 슬라이딩의 바닥 밀착은 MoveCharacter에서 실제 이동 후 처리합니다.
        if (isGrounded && VerticalVelocity < 0f)
        {
            if (!ctx.IsSliding)
            {
                VerticalVelocity = -2f;
            }
        }
        else
        {
            bool isDiving = ctx.IsDiving;
            float currentGravity = isDiving ? (ctx.gravity * ctx.diveGravityScale) : ctx.gravity;
            VerticalVelocity += currentGravity * dt;
        }
        VerticalVelocity = Mathf.Max(VerticalVelocity, -50f);
    }

    public bool HasGroundAhead(Vector3 direction, float distanceAhead, float checkDepth)
    {
        Vector3 checkOrigin = ctx.transform.position + (direction * distanceAhead) + Vector3.up * 0.1f;
        return Physics.Raycast(checkOrigin, Vector3.down, checkDepth + 0.1f, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    }

}
