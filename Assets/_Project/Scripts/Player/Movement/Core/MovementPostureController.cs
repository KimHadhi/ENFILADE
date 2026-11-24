using UnityEngine;
using MovementState = PlayerMovement.MovementState;

public enum MovementPosture { Standing, Crouching, Sliding, Prone, Supine }

// Owns collider dimensions, clearance checks and the crouch camera pivot.
public sealed class MovementPostureController
{
    private readonly PlayerMovement ctx;
    internal bool airCrouching;
    internal readonly float standingColliderHeight, standingColliderRadius, defaultStepOffset;
    private readonly float baseCameraY;
    private float currentOffset;
    private float traversalCameraRecovery;
    private float diveEntryOffset, diveEntryHeight;

    internal void BeginDive()
    {
        // Preserve the actual entry pose, including an interrupted crouch.
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

    // An air crouch is the body's tuck alone: the legs come up under a head that stays where a jump's does (PlayerBody.AirPose,
    // HoldEyesInAir), and the landing goes on crouched while the key is held (MovementMotor). The capsule stays as it is.
    // Until 0.9.403 it drew the capsule's bottom 0.75 m up under the same top and the body rose with it to keep its feet on
    // that bottom: the view went up in the air and tops that high were cleared (the user, 2026-11-24: "덕 점프 했을 때 원래
    // 높이보다 살짝 뜨는데", "공중에 더 추가로 올라가는 것만 막으시면 돼요"), and what looked for a way onto something
    // looked from the feet below it (0.9.401).
    internal void UpdateAirCrouch()
    {
        bool canDuck = (ctx.CurrentState == MovementState.Standing || ctx.CurrentState == MovementState.Crouching)
            && ctx.StateMachine.Current.AllowsAutomaticPosture && !ctx.IsDiving;
        if (ctx.Motor.isGrounded || !canDuck)
        {
            airCrouching = false;
            return;
        }
        // In a long fall Ctrl only readies the landing roll (MovementLanding); the legs come out of a tuck and flail.
        airCrouching = !ctx.IsBigFall && (airCrouching || ctx.Input.CrouchHeld || ctx.Input.CrouchPressed);
    }
    public bool HasCeilingAbove(float targetHeightToCheck)
    {
        if (Mathf.Approximately(targetHeightToCheck, ctx.crouchColliderHeight))
            return CrouchBlockedAt(ctx.transform.position);
        if (ctx.Motor.controller.height >= targetHeightToCheck - 0.01f) return false;

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
        if (moveDir.sqrMagnitude < 0.001f || ctx.CurrentState == MovementState.DashPush) return false;
        Vector3 direction = moveDir.normalized;
        float distance = Mathf.Max(0f, forwardDistance);
        Vector3 feet = ctx.transform.position;
        // Use the destination posture's actual width, never the current (smaller) radius.
        // Require a little clearance beyond the real prone capsule, not a narrower probe.
        PostureCapsule(feet, ctx.proneColliderHeight + 0.04f, 0f, out Vector3 bottom, out Vector3 top, out float radius);
        if (Physics.CheckCapsule(bottom, top, radius, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || Physics.CheckCapsule(bottom + direction * distance, top + direction * distance, radius,
                ctx.groundLayers, QueryTriggerInteraction.Ignore)
            || Physics.CapsuleCast(bottom, top, radius, direction, distance,
                ctx.groundLayers, QueryTriggerInteraction.Ignore)) return false;

        // Side walls also overlap a crouch capsule. Only a downward-facing overhead
        // surface can justify automatic prone; a flying prop is never a tunnel.
        float roofDistance = distance > 0f ? distance + Mathf.Max(0f, standingColliderRadius - 0.08f) : 0f;
        int samples = Mathf.Max(1, Mathf.CeilToInt(roofDistance / 0.1f));
        for (int i = 0; i <= samples; i++)
            if (HasStaticLowRoof(feet + direction * (roofDistance * i / samples))) return true;
        return false;
    }

    private bool HasStaticLowRoof(Vector3 feet)
    {
        const float probeRadius = 0.08f;
        float startHeight = ctx.proneColliderHeight + probeRadius;
        float reach = ctx.crouchColliderHeight + ctx.crouchClearanceMargin - startHeight - probeRadius;
        return reach > 0f && Physics.SphereCast(feet + Vector3.up * startHeight, probeRadius,
            Vector3.up, out RaycastHit hit, reach, ctx.groundLayers, QueryTriggerInteraction.Ignore)
            && hit.normal.y < -0.65f && hit.rigidbody == null
            && hit.collider.GetComponentInParent<IShoveReceiver>() == null;
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

        // A copy on another PC runs no dive, so it has no flight to take this pose from.
        if (ctx.IsDiving && !ctx.IsNetworkDriven)
        {
            float progress = ctx.Actions.Dive.PoseProgress;
            targetColliderHeight = Mathf.Lerp(diveEntryHeight, targetColliderHeight, progress);
            targetOffset = Mathf.Lerp(diveEntryOffset, targetOffset, progress);
            // The camera stays inside the changing body under low roofs, including supine dives.
            targetOffset = Mathf.Min(targetOffset, targetColliderHeight - 0.03f - baseCameraY - ctx.EyeRise);
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

        Vector3 targetCenter = new Vector3(0f, newHeight * 0.5f, 0f);
        bool isCrawling = ctx.CurrentState == MovementState.Proning || ctx.CurrentState == MovementState.Supine;
        bool canStep = ctx.Motor.isGrounded && (ctx.CurrentState == MovementState.Standing ||
                             ctx.CurrentState == MovementState.Crouching ||
                             ctx.CurrentState == MovementState.StepDodge || ctx.CurrentState == MovementState.LandingRoll ||
                             ctx.IsSliding || isCrawling);

        float stepHeight = isCrawling
            ? Mathf.Clamp(ctx.AbilitySettings.proneStepHeight, 0f, ctx.AbilitySettings.stepHeight)
            : ctx.AbilitySettings.stepHeight;
        float targetStep = canStep ? Mathf.Min(stepHeight, newHeight) : 0f;
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
            // The shoes' rise moves the whole camera place, standing or crouched, as it moves the body's eyes.
            ctx.crouchPivot.localPosition = new Vector3(pos.x, baseCameraY + ctx.EyeRise + currentOffset, pos.z);
        }
    }

}
