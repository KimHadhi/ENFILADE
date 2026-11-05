namespace AdvancedZombieAIPro
{
    internal abstract class ZombieState
    {
        protected ZombieState(ZombieAI zombie)
        {
            Zombie = zombie;
        }

        protected ZombieAI Zombie { get; }

        public abstract ZombieStateType StateType { get; }

        public virtual void Enter()
        {
        }

        public virtual void Exit()
        {
        }

        public abstract void Tick(float deltaTime);

        protected bool ReactToTarget(bool allowInspect = true)
        {
            return Zombie.TryHandleTargetReaction(allowInspect);
        }
    }
}
