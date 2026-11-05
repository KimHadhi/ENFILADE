using UnityEngine;

namespace AdvancedZombieAIPro
{
    internal sealed class ZombieIdleState : ZombieState
    {
        private float idleTimeRemaining;

        public ZombieIdleState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Idle;

        public override void Enter()
        {
            Zombie.StopAgent(true);
            if (!Zombie.TryConsumeQueuedIdleDuration(out idleTimeRemaining))
            {
                idleTimeRemaining = Random.Range(Zombie.Profile.idle.minimumIdleTime, Zombie.Profile.idle.maximumIdleTime);
            }

            Zombie.Log($"Idle for {idleTimeRemaining:0.##} seconds.");
        }

        public override void Tick(float deltaTime)
        {
            if (ReactToTarget())
            {
                return;
            }

            idleTimeRemaining -= deltaTime;
            if (idleTimeRemaining > 0f)
            {
                return;
            }

            if (!Zombie.ChangeState(ZombieStateType.Wander))
            {
                idleTimeRemaining = Random.Range(Zombie.Profile.idle.minimumIdleTime, Zombie.Profile.idle.maximumIdleTime);
            }
        }
    }
}
