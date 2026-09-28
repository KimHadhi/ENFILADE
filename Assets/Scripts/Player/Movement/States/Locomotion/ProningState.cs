using UnityEngine;

public sealed class ProningState : MovementBaseState
{
    public ProningState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    public override float LocomotionSpeed => ctx.ProneSpeed;

    // 기립/캔슬 공통 처리는 ProneState 부모가 전부 담당함
}