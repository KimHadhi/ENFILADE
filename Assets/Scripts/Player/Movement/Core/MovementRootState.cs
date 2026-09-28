public sealed class MovementRootState : MovementBaseState
{
    public MovementRootState(PlayerMovement context) : base(context) { }
    public override string Name => "Movement";
}
