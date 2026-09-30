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
    public MovementLanding Landing { get; }
    public PlayerCombatActions Combat { get; }
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
        Landing = new MovementLanding(ctx);
        Combat = new PlayerCombatActions(ctx);
        Dodge.Succeeded += Lean.OnDodgeSucceeded;
    }
    internal void BeginFrame(float dt)
    {
        InputConsumed = false;
        Dodge.Tick(dt);
        StepTrail.Tick();
        Traversal.Tick(dt);
        Combat.Tick(dt);
        Landing.Tick(dt);
    }
    internal bool ProcessInput()
    {
        if (!ctx.InputAvailable || InputConsumed) return InputConsumed;
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
        if (IsOrdinaryAirborne && Traversal.TryStartAirAction())
            return InputConsumed = true;
        if (CanUseGroundActions && ctx.Input.ShovePressed && Combat.TryShove())
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
        ctx.ChangeState(ctx.HasCeilingAbove(ctx.StandingColliderHeight)
            ? (ctx.HasCeilingAbove(ctx.CrouchColliderHeight) ? State.Proning : State.Crouching) : State.Standing);
    }
    internal void Reset()
    {
        Dodge.Reset(); StepTrail.Clear(); Lean.Reset(); Traversal.Reset(); Landing.Reset(); Combat.Reset();
        InputConsumed = false;
    }
}
