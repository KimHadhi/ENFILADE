using UnityEngine;

public sealed class SupineDivingState : MovementBaseState
{
    public override MovementPosture Posture => MovementPosture.Supine;

    public SupineDivingState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

}
