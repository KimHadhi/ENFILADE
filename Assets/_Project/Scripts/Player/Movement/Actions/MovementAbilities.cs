using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>Arbitrates one exclusive action per frame; each feature owns its own state/data.</summary>
public sealed class MovementAbilities
{
    private readonly PlayerMovement ctx;
    public DodgeController Dodge { get; } = new DodgeController();
    public MovementLean Lean { get; }
    public StepDodgeTrail StepTrail { get; }
    public ParkourTraversal Traversal { get; }
    public MovementFixtures Fixtures { get; }
    public MovementLanding Landing { get; }
    public PlayerCombatActions Combat { get; }
    public SlideCombat Slide { get; }
    internal bool InputConsumed;
    internal Vector3 ActionDirection;
    internal MovementPosture ActionPosture;
    public bool CanUseGroundActions => ctx.IsGrounded &&
        (ctx.CurrentState == State.Standing || ctx.CurrentState == State.Crouching);
    public bool IsOrdinaryAirborne => !ctx.IsGrounded &&
        (ctx.CurrentState == State.Standing || ctx.CurrentState == State.Crouching);

    internal MovementAbilities(PlayerMovement context)
    {
        ctx = context;
        Lean = new MovementLean(ctx);
        StepTrail = new StepDodgeTrail(ctx);
        Traversal = new ParkourTraversal(ctx);
        Fixtures = new MovementFixtures(ctx);
        Landing = new MovementLanding(ctx);
        Combat = new PlayerCombatActions(ctx);
        Slide = new SlideCombat(ctx);
        Dodge.Succeeded += Lean.OnDodgeSucceeded;
    }
    internal void BeginFrame(float dt)
    {
        InputConsumed = false;
        Dodge.Tick(dt);
        StepTrail.Tick();
        Traversal.Tick(dt);
        Fixtures.Tick(dt);
        Combat.Tick(dt);
        Slide.Tick(dt);
        Landing.Tick(dt);
    }
    internal bool ProcessInput()
    {
        if (!ctx.InputAvailable || InputConsumed) return InputConsumed;
        if (ctx.IsSliding && ctx.Input.DodgePressed && Slide.TryBoost())
            return InputConsumed = true;
        bool continuingStep = ctx.IsGrounded && ctx.CurrentState == State.StepDodge && Dodge.CanStart;
        if (continuingStep && ctx.Input.LeanPressed)
        {
            ctx.Abilities.ReturnToLocomotion();
            continuingStep = false;
        }
        if ((CanUseGroundActions || continuingStep) && ctx.Input.DodgePressed &&
            Dodge.TryStart(DodgeKind.Step, 0, ctx.AbilitySettings.stepInvulnerability, ctx.AbilitySettings.stepDodgeCooldown))
        {
            ActionDirection = InputDirection(-ctx.transform.forward);
            if (!continuingStep) ActionPosture = ctx.IsCrouching ? MovementPosture.Crouching : MovementPosture.Standing;
            if (continuingStep) ((GroundBurstState)ctx.StateMachine.Current).RestartStep();
            else ctx.ChangeState(State.StepDodge);
            StepTrail.Begin();
            return InputConsumed = true;
        }
        if (Fixtures.TryStart())
            return InputConsumed = true;
        if (IsOrdinaryAirborne && Traversal.TryStartAirAction())
            return InputConsumed = true;
        // Running, V is the dash push with a gun or fists. Otherwise the fists own V (shove, guard break), and with a
        // gun it is the crowd shove. PlayerMeleeCombat runs first and drops its pending shove once the dash is under way.
        bool fists = ctx.MeleeCombat != null && ctx.MeleeCombat.IsActive;
        if (CanUseGroundActions && ctx.Input.ShovePressed && (Combat.TryDashPush() || (!fists && Combat.TryShove())))
            return InputConsumed = true;
        return false;
    }
    internal Vector3 InputDirection(Vector3 fallback)
    {
        Vector2 input = ctx.CurrentInput;
        Vector3 direction = ctx.transform.right * input.x + ctx.transform.forward * input.y;
        return direction.sqrMagnitude > 0.01f ? Vector3.ProjectOnPlane(direction, Vector3.up).normalized : fallback.normalized;
    }
    internal void ReturnToLocomotion()
    {
        // Thrown or dropped while downed, they land back on the ground.
        if (ctx.IsDowned) { ctx.ChangeState(State.Proning); return; }
        // Resolve an enemy overlap before choosing posture, so its body is not mistaken for a low ceiling.
        if (ctx.CurrentState == State.StepDodge) ctx.DodgePassThrough?.Finish();
        // A blocked crouch capsule may be a side wall or a shoved body, not a low tunnel.
        bool lowPassage = ctx.CurrentState != State.DashPush && ctx.HasVentCeilingAhead(ctx.transform.forward, 0f);
        bool ceiling = ctx.HasCeilingAbove(ctx.StandingColliderHeight);
        // Temporary probe: after climbing off a ladder the player came up crouched with nothing to crouch under (user,
        // 2026-11-08); name what the stand-up check met.
        if (ceiling && (ctx.CurrentState == State.Mantle || ctx.CurrentState == State.Vault)) LogCeiling();
        ctx.ChangeState(ceiling ? (lowPassage ? State.Proning : State.Crouching) : State.Standing);
    }
    private void LogCeiling()
    {
        Vector3 feet = ctx.transform.position;
        var hits = Physics.OverlapCapsule(feet + Vector3.up * .5f, feet + Vector3.up * (ctx.StandingColliderHeight - .3f), .27f,
            ctx.groundLayers, QueryTriggerInteraction.Ignore);
        var names = new System.Text.StringBuilder();
        foreach (var hit in hits) names.Append($" {hit.name} (layer {LayerMask.LayerToName(hit.gameObject.layer)}, {hit.GetType().Name});");
        ParkourProbeLog.Line($"{ctx.CurrentState} ended crouched at {feet.ToString("F2")}, controller height {ctx.Motor.controller.height:0.00}:"
            + (hits.Length > 0 ? names.ToString() : " nothing found in the standing capsule"));
    }
    internal void Reset()
    {
        Dodge.Reset(); StepTrail.Clear(); Lean.Reset(); Traversal.Reset(); Fixtures.Reset(); Landing.Reset(); Combat.Reset(); Slide.Reset();
        InputConsumed = false;
    }
}
