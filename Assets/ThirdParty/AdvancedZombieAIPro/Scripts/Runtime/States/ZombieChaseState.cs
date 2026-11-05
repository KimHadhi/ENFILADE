using UnityEngine;

namespace AdvancedZombieAIPro
{
    internal sealed class ZombieChaseState : ZombieState
    {
        private float repathTimer;
        private float playerLoseTimer;
        private bool isLosingTarget;

        public ZombieChaseState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Chase;

        public override void Enter()
        {
            Zombie.ConfigureAgent(Zombie.Profile.chase.speed, Zombie.Profile.chase.stoppingDistance);
            isLosingTarget = false;
            playerLoseTimer = 0f;
            repathTimer = 0f;

            if (Zombie.Target != null)
            {
                Zombie.SetLastKnownTargetPosition(Zombie.Target.transform.position);
            }

            Zombie.Log("Chasing target.");
        }

        public override void Tick(float deltaTime)
        {
            if (Zombie.CanAttackTarget() && Zombie.IsStateEnabled(ZombieStateType.Attack))
            {
                Zombie.ChangeState(ZombieStateType.Attack);
                return;
            }

            if (Zombie.Target == null)
            {
                GoToSearchOrPassive();
                return;
            }

            if (Zombie.HasFullDetection || Zombie.HasChaseVisibility)
            {
                isLosingTarget = false;
                playerLoseTimer = 0f;
                Zombie.ConfigureAgent(Zombie.Profile.chase.speed, Zombie.Profile.chase.stoppingDistance);
                Zombie.SetLastKnownTargetPosition(Zombie.Target.transform.position);
                repathTimer -= deltaTime;

                if (repathTimer <= 0f)
                {
                    Zombie.TrySetDestination(Zombie.Target.transform.position);
                    repathTimer = Zombie.Profile.chase.repathInterval;
                }

                return;
            }

            if (!Zombie.HasLastKnownTargetPosition)
            {
                GoToSearchOrPassive();
                return;
            }

            playerLoseTimer += deltaTime;
            if (!isLosingTarget)
            {
                isLosingTarget = true;
                Zombie.TrySetDestination(Zombie.Target.transform.position);
                repathTimer = Zombie.Profile.chase.repathInterval;
                Zombie.Log($"Target out of sight. Holding chase for {Zombie.Profile.chase.playerLoseTime:0.##} seconds before losing target.");
            }

            if (playerLoseTimer < Zombie.Profile.chase.playerLoseTime)
            {
                Zombie.ConfigureAgent(Zombie.Profile.chase.speed, Zombie.Profile.chase.stoppingDistance);
                repathTimer -= deltaTime;
                if (repathTimer <= 0f)
                {
                    Zombie.TrySetDestination(Zombie.Target.transform.position);
                    repathTimer = Zombie.Profile.chase.repathInterval;
                }

                return;
            }

            Zombie.StopAgent(true);
            Vector3 searchPosition = Zombie.transform.position;
            Zombie.SetLastKnownTargetPosition(searchPosition);
            Zombie.SetSuspiciousPosition(searchPosition);
            Zombie.BeginSearchCycle(searchPosition);
            Zombie.Log("Lost target. Searching from current chase position.");
            Zombie.ChangeState(ZombieStateType.Search);
        }

        private void GoToSearchOrPassive()
        {
            if (Zombie.HasLastKnownTargetPosition)
            {
                Zombie.ChangeState(ZombieStateType.Search);
            }
            else
            {
                Zombie.ChangeState(Zombie.ChoosePassiveState());
            }
        }
    }
}
