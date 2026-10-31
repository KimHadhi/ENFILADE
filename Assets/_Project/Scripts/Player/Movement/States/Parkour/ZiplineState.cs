using UnityEngine;

/// <summary>
/// Riding a zipline, after Beatrun: the ride starts at the speed of the run-up and builds to its top speed whichever
/// way the cable slopes. Ctrl, the end of the cable or anything in the way lets go, keeping most of the speed.
/// </summary>
public sealed class ZiplineState : MovementBaseState
{
    private Vector3 catchStart;
    public ZiplineState(PlayerMovement ctx, MovementBaseState parent) : base(ctx, parent) { }
    public override void Enter()
    {
        catchStart = ctx.transform.position;
        ctx.HorizontalVelocity = Vector3.zero; ctx.VerticalVelocity = 0f;
        ctx.Abilities.Landing.SupportedByTraversal();
    }
    public override void UpdateLogic(float dt)
    {
        var fixtures = ctx.Abilities.Fixtures;
        if (fixtures.Zipline == null || !fixtures.Zipline.isActiveAndEnabled || fixtures.ZiplineFraction >= 1f
            || (ctx.InputAvailable && ctx.Input.CrouchPressed)) Release();
    }
    public override void UpdatePhysics(float dt)
    {
        var fixtures = ctx.Abilities.Fixtures;
        var settings = ctx.AbilitySettings;
        if (fixtures.Zipline == null) return;
        float length = Mathf.Max(.1f, Vector3.Distance(fixtures.ZiplineFrom, fixtures.ZiplineTo));
        fixtures.ZiplineFraction = Mathf.MoveTowards(fixtures.ZiplineFraction, 1f, dt * fixtures.ZiplineSpeed / length);
        Vector3 here = ctx.transform.position;
        Vector3 hang = Vector3.Lerp(fixtures.ZiplineFrom, fixtures.ZiplineTo, fixtures.ZiplineFraction) - Vector3.up * settings.ziplineHangLength;
        // Pulled in from wherever the hands caught the cable.
        Vector3 next = Vector3.Lerp(catchStart, hang, Mathf.SmoothStep(0f, 1f, (ctx.CurrentStateDuration + dt) / Mathf.Max(.01f, settings.ziplineCatchTime)));
        if (!ctx.Abilities.Traversal.Detector.ClearPath(here, next, ctx.StandingColliderHeight)) { Release(); return; }
        fixtures.ZiplineSpeed = Mathf.MoveTowards(fixtures.ZiplineSpeed, settings.ziplineMaxSpeed, settings.ziplineAcceleration * dt);
        ctx.Motor.RequestDisplacement(next - here);
    }
    private void Release()
    {
        var fixtures = ctx.Abilities.Fixtures;
        Vector3 velocity = fixtures.ZiplineDirection * (fixtures.ZiplineSpeed * ctx.AbilitySettings.ziplineReleaseShare);
        fixtures.LockZipline();
        ctx.Abilities.ReturnToLocomotion();
        ctx.HorizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        ctx.VerticalVelocity = velocity.y;
    }
}
