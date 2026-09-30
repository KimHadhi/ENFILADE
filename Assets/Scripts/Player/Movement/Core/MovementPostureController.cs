using UnityEngine;
using MovementState = PlayerMovement.MovementState;

public enum MovementPosture { Standing, Crouching, Sliding, Prone, Supine }

// Owns collider dimensions, clearance checks and the crouch camera pivot.
public sealed class MovementPostureController
{
    private readonly PlayerMovement ctx;
    internal bool airCrouching, airBodyTucked;
    internal float airCrouchTop, airCameraOffset;
    internal readonly float standingColliderHeight, standingColliderRadius, defaultStepOffset;
    private readonly float baseCameraY;
    private float currentOffset;
    private float traversalCameraRecovery;
    private float diveEntryOffset, diveEntryHeight;

    internal void BeginDive()
    {
        // Preserve the actual entry pose, including an interrupted crouch or tucked air jump.
        if (airCrouching || airBodyTucked) RebaseAirCrouchFeet();
        diveEntryOffset = currentOffset;
        diveEntryHeight = ctx.Motor.controller.height;
    }

    internal MovementPostureController(PlayerMovement context)
    {
        ctx = context;
        var controller = ctx.Motor.controller;
        standingColliderHeight = ctx.runtimeBodyDefaults.x;
        standingColliderRadius = ctx.runtimeBodyDefaults.y;
        defaultStepOffset = ctx.runtimeBodyDefaults.z;
        controller.center = new Vector3(0f, controller.height * 0.5f, 0f);
        if (ctx.crouchPivot == null) ctx.crouchPivot = ctx.transform.Find("CrouchPivot");
        baseCameraY = ctx.runtimeBodyDefaults.w;
        if (ctx.crouchPivot != null) currentOffset = ctx.crouchPivot.localPosition.y - baseCameraY;
    }

    internal void UpdateAirCrouch()
    {
        // Dive owns a timed takeoff-to-prone pose. Air crouch must not override it.
        if (ctx.IsDiving)
        {
            if (airCrouching || airBodyTucked) RebaseAirCrouchFeet();
            return;
        }
        if (!ctx.StateMachine.Current.AllowsAutomaticPosture)
        {
            if (airCrouching || airBodyTucked) RebaseAirCrouchFeet();
            return;
        }
        bool canDuck = ctx.CurrentState == MovementState.Standing || ctx.CurrentState == MovementState.Crouching;
        bool compactMovement = ctx.IsDiving || ctx.IsSliding;
        if (ctx.Motor.isGrounded || (!canDuck && !compactMovement))
        {
            if (airBodyTucked || airCrouching) RebaseAirCrouchFeet();
            return;
        }

        bool crouchHeld = ctx.InputAvailable && ctx.Input.crouchAction != null && ctx.Input.crouchAction.IsPressed();
        bool crouchJustPressed = ctx.InputAvailable && ctx.Input.crouchAction != null && ctx.Input.crouchAction.WasPressedThisFrame();
        airCrouching = canDuck && (airCrouching || crouchHeld || crouchJustPressed);

        if ((airCrouching || compactMovement) && !airBodyTucked)
        {
            airCrouchTop = ctx.Motor.controller.center.y + ctx.Motor.controller.height * 0.5f;
            airCameraOffset = currentOffset;
            airBodyTucked = true;
        }
    }

    internal void RebaseAirCrouchFeet()
    {
        float feetOffset = ctx.Motor.controller.center.y - ctx.Motor.controller.height * 0.5f;
        if (Mathf.Abs(feetOffset) > 0.0001f)
        {
            Vector3 shift = ctx.transform.TransformVector(Vector3.up * feetOffset);
            ctx.transform.position += shift;
            ctx.Motor.controller.center -= Vector3.up * feetOffset;
            ctx.Motor.previousPosition += shift;
            currentOffset -= feetOffset;
            if (ctx.crouchPivot != null)
            {
                Vector3 position = ctx.crouchPivot.localPosition;
                position.y -= feetOffset;
                ctx.crouchPivot.localPosition = position;
            }
        }
        airCrouching = false;
        airBodyTucked = false;
    }

    public bool HasCeilingAbove(float targetHeightToCheck)
    {
        if (Mathf.Approximately(targetHeightToCheck, ctx.crouchColliderHeight))
            return CrouchBlockedAt(ctx.transform.position);
        if (!airCrouching && ctx.Motor.controller.height >= targetHeightToCheck - 0.01f) return false;

        float radius = Mathf.Min(standingColliderRadius, ctx.Motor.controller.radius) * 0.9f;
        Vector3 feetPos = ctx.transform.position;

        float currentTopY = ctx.Motor.controller.height;
        float startSphereCenterY = currentTopY + radius * 0.2f;
        float endSphereCenterY = targetHeightToCheck - radius;

        if (endSphereCenterY <= startSphereCenterY)
        {
            return Physics.CheckSphere(feetPos + Vector3.up * (targetHeightToCheck - radius), radius, ctx.groundLayers, QueryTriggerInteraction.Ignore);
        }

        Vector3 bottomCenter = feetPos + Vector3.up * startSphereCenterY;
        Vector3 topCenter = feetPos + Vector3.up * endSphereCenterY;

        return Physics.CheckCapsule(bottomCenter, topCenter, radius, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    }

    public bool HasCeilingAhead(Vector3 direction, float distance, float targetHeightToCheck)
    {
        if (direction.sqrMagnitude < 0.001f) return false;
        if (Mathf.Approximately(targetHeightToCheck, ctx.crouchColliderHeight))
            return CrouchBlockedAt(ctx.transform.position + direction.normalized * distance);

        float radius = Mathf.Min(standingColliderRadius, ctx.Motor.controller.radius) * 0.9f;
        Vector3 aheadFeet = ctx.transform.position + direction.normalized * distance;

        float currentTopY = ctx.Motor.controller.height;
        float startSphereCenterY = currentTopY + radius * 0.2f;
        float endSphereCenterY = targetHeightToCheck - radius;

        if (endSphereCenterY <= startSphereCenterY)
        {
            return Physics.CheckSphere(aheadFeet + Vector3.up * (targetHeightToCheck - radius), radius, ctx.groundLayers, QueryTriggerInteraction.Ignore);
        }

        Vector3 bottomCenter = aheadFeet + Vector3.up * startSphereCenterY;
        Vector3 topCenter = aheadFeet + Vector3.up * endSphereCenterY;

        return Physics.CheckCapsule(bottomCenter, topCenter, radius, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    }

    public bool HasVentCeilingAhead(Vector3 moveDir, float forwardDistance = 0.5f)
    {
        if (moveDir.sqrMagnitude < 0.001f) return false;
        Vector3 direction = moveDir.normalized;
        float distance = Mathf.Max(0f, forwardDistance);
        Vector3 feet = ctx.transform.position, ahead = feet + direction * distance;
        // Use the destination posture's actual width, never the current (smaller) radius.
        PostureCapsule(feet, ctx.proneColliderHeight, 0f, out Vector3 bottom, out Vector3 top, out float radius);
        if (Physics.CheckCapsule(bottom, top, radius, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || Physics.CheckCapsule(bottom + direction * distance, top + direction * distance, radius,
                ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || Physics.CapsuleCast(bottom, top, radius, direction, distance,
                ctx.groundLayers, QueryTriggerInteraction.Ignore)) return false;

        if (CrouchBlockedAt(feet) || CrouchBlockedAt(ahead)) return true;
        // Include thin lintels between the two samples so early detection cannot skip them.
        PostureCapsule(feet, ctx.crouchColliderHeight, ctx.crouchClearanceMargin, out bottom, out top, out radius);
        return Physics.CapsuleCast(bottom, top, radius, direction, distance,
            ctx.groundLayers, QueryTriggerInteraction.Ignore);
    }

    private bool CrouchBlockedAt(Vector3 feet)
    {
        PostureCapsule(feet, ctx.crouchColliderHeight, ctx.crouchClearanceMargin,
            out Vector3 bottom, out Vector3 top, out float radius);
        return Physics.CheckCapsule(bottom, top, radius, ctx.groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void PostureCapsule(Vector3 feet, float height, float headroom,
        out Vector3 bottom, out Vector3 top, out float radius)
    {
        radius = Mathf.Max(0.01f, Mathf.Min(standingColliderRadius, height * 0.5f) - 0.01f);
        float bottomY = radius + 0.02f;
        bottom = feet + Vector3.up * bottomY;
        top = feet + Vector3.up * Mathf.Max(bottomY, height + headroom - radius);
    }

    public void UpdateHeightsAndTransform(float dt)
    {
        traversalCameraRecovery = ctx.StateMachine.Current is ObstacleTraversalState
            ? 0.2f : Mathf.Max(0f, traversalCameraRecovery - dt);
        float targetColliderHeight = standingColliderHeight;
        float targetOffset = 0f;
        switch (ctx.StateMachine.Current.Posture)
        {
            case MovementPosture.Crouching: targetColliderHeight = ctx.crouchColliderHeight; targetOffset = ctx.crouchOffset; break;
            case MovementPosture.Sliding: targetColliderHeight = ctx.slideColliderHeight; targetOffset = ctx.slideOffset; break;
            case MovementPosture.Prone: targetColliderHeight = ctx.proneColliderHeight; targetOffset = ctx.proneOffset; break;
            case MovementPosture.Supine: targetColliderHeight = ctx.proneColliderHeight; targetOffset = ctx.supineOffset; break;
        }

        if (airCrouching) targetColliderHeight = ctx.crouchColliderHeight;
        if (airBodyTucked)
        {
            targetColliderHeight = Mathf.Min(targetColliderHeight, airCrouchTop);
            targetOffset = airCameraOffset;
        }

        if (ctx.IsDiving)
        {
            float progress = ctx.Actions.Dive.PoseProgress;
            targetColliderHeight = Mathf.Lerp(diveEntryHeight, targetColliderHeight, progress);
            targetOffset = Mathf.Lerp(diveEntryOffset, targetOffset, progress);
            // The camera stays inside the changing body under low roofs, including supine dives.
            targetOffset = Mathf.Min(targetOffset, targetColliderHeight - 0.03f - baseCameraY);
        }

        float newHeight;
        if (ctx.Motor.controller.height < targetColliderHeight && dt > 0f)
        {
            newHeight = Mathf.MoveTowards(ctx.Motor.controller.height, targetColliderHeight, dt * 8.0f);
        }
        else
        {
            newHeight = targetColliderHeight;
        }

        float targetRadius = Mathf.Min(standingColliderRadius, newHeight * 0.5f);

        float centerY;
        if (airBodyTucked)
        {
            centerY = airCrouchTop - newHeight * 0.5f;
        }
        else
        {
            centerY = newHeight * 0.5f;
        }

        Vector3 targetCenter = new Vector3(0f, centerY, 0f);
        bool canStep = ctx.Motor.isGrounded && (ctx.CurrentState == MovementState.Standing ||
                             ctx.CurrentState == MovementState.Crouching ||
                             ctx.IsSliding);

        float targetStep = canStep ? Mathf.Min(ctx.AbilitySettings.stepHeight, newHeight) : 0f;
        if (!Mathf.Approximately(ctx.Motor.controller.height, newHeight) || !Mathf.Approximately(ctx.Motor.controller.radius, targetRadius) || ctx.Motor.controller.center != targetCenter)
        {
            ctx.Motor.controller.stepOffset = 0f;
            if (targetRadius < ctx.Motor.controller.radius) ctx.Motor.controller.radius = targetRadius;
            ctx.Motor.controller.height = newHeight;
            ctx.Motor.controller.radius = targetRadius;
            ctx.Motor.controller.center = targetCenter;
        }
        if (!Mathf.Approximately(ctx.Motor.controller.stepOffset, targetStep)) ctx.Motor.controller.stepOffset = targetStep;

        if (ctx.crouchPivot != null)
        {
            bool isSlidingOrDiving = ctx.IsSliding || ctx.IsDiving;
            float currentSharpness = isSlidingOrDiving ? ctx.slideTransitionSharpness : ctx.transitionSharpness;
            if (traversalCameraRecovery > 0f) currentSharpness = Mathf.Min(currentSharpness, 6f);
            // Dive already has a smooth time curve; a second filter delays the landing pose.
            float t = ctx.IsDiving || dt <= 0f ? 1f : 1f - Mathf.Exp(-currentSharpness * dt);
            currentOffset = Mathf.Lerp(currentOffset, targetOffset, t);
            Vector3 pos = ctx.crouchPivot.localPosition;
            ctx.crouchPivot.localPosition = new Vector3(pos.x, baseCameraY + currentOffset, pos.z);
        }
    }

}
