using System;
using System.Collections.Generic;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Transitions exit/enter only branches below the lowest common ancestor.
/// Sibling transitions retain the active parent and its shared state.
/// </summary>
public sealed class MovementStateMachine
{
    private readonly IReadOnlyDictionary<MovementState, MovementBaseState> states;
    private readonly List<MovementBaseState> enterPath = new List<MovementBaseState>();
    private bool transitioning;

    public MovementBaseState Current { get; private set; }
    public MovementState CurrentState { get; private set; }
    public MovementState PreviousState { get; private set; }
    public float Duration { get; set; }
    /// <summary>(from, to), raised once the new state has entered.</summary>
    public event Action<MovementState, MovementState> Changed;

    public MovementStateMachine(IReadOnlyDictionary<MovementState, MovementBaseState> states, MovementState initial)
    {
        this.states = states ?? throw new ArgumentNullException(nameof(states));
        PreviousState = CurrentState = initial;
        ChangeState(initial);
    }

    internal static MovementStateMachine Create(PlayerMovement ctx)
    {
        var root = new MovementRootState(ctx);
        var locomotion = new LocomotionState(ctx, root);
        var prone = new ProneState(ctx, locomotion);
        var sliding = new SlidingGroupState(ctx, root);
        var diving = new DivingGroupState(ctx, root);
        var evasion = new AbilityGroupState(ctx, root, "Evasion");
        var recovery = new AbilityGroupState(ctx, root, "Recovery");
        var assault = new AbilityGroupState(ctx, root, "Assault");
        var parkour = new AbilityGroupState(ctx, root, "Parkour", false);
        var obstacle = new AbilityGroupState(ctx, parkour, "Obstacle", false);
        var wall = new AbilityGroupState(ctx, parkour, "Wall", false);
        var swing = new AbilityGroupState(ctx, parkour, "Swing", false);
        return new MovementStateMachine(new Dictionary<MovementState, MovementBaseState>
        {
            { MovementState.Standing, new StandingState(ctx, locomotion) },
            { MovementState.Crouching, new CrouchingState(ctx, locomotion) },
            { MovementState.Proning, new ProningState(ctx, prone) },
            { MovementState.Supine, new SupineState(ctx, prone) },
            { MovementState.Sliding, new SlidingState(ctx, sliding) },
            { MovementState.HeadFirstSliding, new HeadFirstSlidingState(ctx, sliding) },
            { MovementState.SupineSliding, new SupineSlidingState(ctx, sliding) },
            { MovementState.Diving, new DivingState(ctx, diving) },
            { MovementState.SupineDiving, new SupineDivingState(ctx, diving) },
            { MovementState.StepDodge, new GroundBurstState(ctx, evasion, MovementState.StepDodge) },
            { MovementState.LandingRoll, new GroundBurstState(ctx, recovery, MovementState.LandingRoll) },
            { MovementState.DashPush, new GroundBurstState(ctx, assault, MovementState.DashPush) },
            { MovementState.Vault, new ObstacleTraversalState(ctx, obstacle, true) },
            { MovementState.Mantle, new ObstacleTraversalState(ctx, obstacle, false) },
            { MovementState.LedgeHang, new LedgeHangState(ctx, obstacle) },
            { MovementState.LedgeJump, new LedgeJumpState(ctx, obstacle) },
            { MovementState.WallClimb, new WallMovementState(ctx, wall) },
            { MovementState.WallKick, new WallKickState(ctx, wall) },
            { MovementState.BarGrab, new BarGrabState(ctx, swing) },
            { MovementState.BarSwing, new BarSwingState(ctx, swing) },
            { MovementState.LacheRelease, new LacheReleaseState(ctx, swing) }
        }, MovementState.Standing);
    }

    public bool IsIn<T>() where T : MovementBaseState
    {
        for (var state = Current; state != null; state = state.Parent)
            if (state is T) return true;
        return false;
    }

    public void ChangeState(MovementState next)
    {
        if (!states.TryGetValue(next, out MovementBaseState target))
            throw new ArgumentOutOfRangeException(nameof(next), next, "Unregistered movement state.");
        if (ReferenceEquals(Current, target)) return;
        if (transitioning) throw new InvalidOperationException("State lifecycle callbacks must not start another transition.");
        MovementBaseState common = FindCommonAncestor(Current, target);
        transitioning = true;
        try
        {
            for (var state = Current; state != common; state = state.Parent) state.Exit();
            PreviousState = CurrentState;
            CurrentState = next;
            Current = target;
            Duration = 0f;
            enterPath.Clear();
            for (var state = target; state != common; state = state.Parent) enterPath.Add(state);
            for (int i = enterPath.Count - 1; i >= 0; i--) enterPath[i].Enter();
        }
        finally
        {
            enterPath.Clear();
            transitioning = false;
        }
        Changed?.Invoke(PreviousState, CurrentState);
    }

    private static MovementBaseState FindCommonAncestor(MovementBaseState from, MovementBaseState to)
    {
        for (var a = from; a != null; a = a.Parent)
            for (var b = to; b != null; b = b.Parent)
                if (ReferenceEquals(a, b)) return a;
        return null;
    }
}
