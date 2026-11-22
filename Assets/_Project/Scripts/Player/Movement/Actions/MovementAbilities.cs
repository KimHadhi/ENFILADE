using UnityEngine;
using State = PlayerMovement.MovementState;

/// <summary>Arbitrates one exclusive action per frame; each feature owns its own state/data.</summary>
public sealed class MovementAbilities
{
    private readonly PlayerMovement ctx;
    public DodgeController Dodge { get; } = new DodgeController();
    /// <summary>The step dodge under way: its clip and how that carries the body.</summary>
    public DodgeMove DodgeMove { get; }
    public MovementLean Lean { get; }
    public StepDodgeTrail StepTrail { get; }
    public ParkourTraversal Traversal { get; }
    /// <summary>The body's one surge, a short run along the ground to a spot: the vault's early jump and a Détente's dose ask for it (GroundSurge).</summary>
    public GroundSurge Surge { get; }
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
    /// <summary>
    /// A knife swing has the body (PlayerMeleeCombat.HoldsBody: winding up, charging, or its clip not home yet): no parkour
    /// move begins until it is over. Asked where parkour begins: the moves in the air here (TryStartAirAction) and the
    /// jump's surge to a vault (ParkourTraversal.SurgesToVault); the press then goes as the jump it is. Ladders and cables
    /// (Fixtures) don't ask: walking off a roof onto its ladder is what keeps the player from falling off it.
    /// </summary>
    internal bool SwingHoldsBody => ctx.MeleeCombat != null && ctx.MeleeCombat.HoldsBody;

    internal MovementAbilities(PlayerMovement context)
    {
        ctx = context;
        DodgeMove = new DodgeMove(ctx);
        Lean = new MovementLean(ctx);
        StepTrail = new StepDodgeTrail(ctx);
        Surge = new GroundSurge(ctx);
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
        Surge.Tick(dt);
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
            DodgeMove.Begin(ActionDirection, ActionPosture == MovementPosture.Crouching, continuingStep);
            if (continuingStep) ((StepDodgeState)ctx.StateMachine.Current).Restart();
            else ctx.ChangeState(State.StepDodge);
            StepTrail.Begin();
            return InputConsumed = true;
        }
        if (Fixtures.TryStart())
            return InputConsumed = true;
        if (IsOrdinaryAirborne && !SwingHoldsBody && Traversal.TryStartAirAction())
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
        if (ctx.CurrentState == State.StepDodge) ctx.DodgePassThrough.OrNull()?.Finish();
        // A blocked crouch capsule may be a side wall or a shoved body, not a low tunnel.
        bool lowPassage = ctx.CurrentState != State.DashPush && ctx.HasVentCeilingAhead(ctx.transform.forward, 0f);
        bool ceiling = ctx.HasCeilingAbove(ctx.StandingColliderHeight);
        // A dodge with the crouch still wanted (held, or latched on toggle: MovementInput) comes straight back down to
        // it, rather than standing for a frame and dropping again. Off a ledge the air's own tuck takes it (MovementPostureController).
        // CrouchHeld already says whose the keys are (a held key reads as let go with a screen up, a latched crouch stays).
        bool crouch = ctx.CurrentState == State.StepDodge && ctx.IsGrounded && ctx.Input.CrouchHeld;
        ctx.ChangeState(ceiling ? (lowPassage ? State.Proning : State.Crouching) : crouch ? State.Crouching : State.Standing);
    }
    internal void Reset()
    {
        Dodge.Reset(); DodgeMove.Reset(); StepTrail.Clear(); Lean.Reset(); Surge.Stop(); Traversal.Reset(); Fixtures.Reset(); Landing.Reset(); Combat.Reset(); Slide.Reset();
        InputConsumed = false;
    }
}
