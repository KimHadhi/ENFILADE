namespace AdvancedZombieAIPro
{
    internal sealed class ZombieDeadState : ZombieState
    {
        public ZombieDeadState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Dead;

        public override void Enter()
        {
            Zombie.EnterDeadState();
        }

        public override void Tick(float deltaTime)
        {
            Zombie.MaintainDeadState();
        }
    }
}
