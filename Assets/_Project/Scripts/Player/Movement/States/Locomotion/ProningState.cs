
public sealed class ProningState : MovementBaseState
{
    public ProningState(PlayerMovement context, MovementBaseState parent) : base(context, parent) { }

    // Gone down from on their feet, the body falls first and the crawl starts once it is down (PlayerMovement.DownedFalling).
    public override float LocomotionSpeed => ctx.DownedFalling ? 0f : ctx.ProneSpeed;

    // 기립/캔슬 공통 처리는 ProneState 부모가 전부 담당함
}