using UnityEngine;

namespace AdvancedZombieAIPro
{
    internal sealed class ZombieInspectState : ZombieState
    {
        private float repathTimer;
        private bool hasDestination;

        public ZombieInspectState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Inspect;

        public override void Enter()
        {
            Zombie.ConfigureAgent(Zombie.Profile.inspect.speed, Zombie.Profile.inspect.arrivalDistance);
            repathTimer = 0f;
            hasDestination = false;
            MoveToSuspiciousPosition();
            if (Zombie.IsSearchCycleActive)
            {
                Zombie.Log($"Moving to search step {Zombie.SuspiciousPosition}.");
            }
            else
            {
                Zombie.Log($"Inspecting suspicious position {Zombie.SuspiciousPosition}.");
            }
        }

        public override void Tick(float deltaTime)
        {
            if (Zombie.CanAttackTarget() && Zombie.IsStateEnabled(ZombieStateType.Attack))
            {
                Zombie.ChangeState(ZombieStateType.Attack);
                return;
            }

            if (Zombie.HasFullDetection)
            {
                Zombie.EndSearchCycle();
                Zombie.ChangeState(Zombie.ShouldUseAlertBeforeChase() ? ZombieStateType.Alert : ZombieStateType.Chase);
                return;
            }

            if (!Zombie.IsSearchCycleActive && Zombie.HasPartialDetection && Zombie.Target != null)
            {
                Zombie.SetSuspiciousPosition(Zombie.Target.transform.position);
                repathTimer -= deltaTime;
                if (repathTimer <= 0f)
                {
                    MoveToSuspiciousPosition();
                    repathTimer = Zombie.Profile.inspect.repathInterval;
                }
            }

            if (!hasDestination)
            {
                MoveToSuspiciousPosition();
                return;
            }

            if (!Zombie.HasReachedDestination(Zombie.Profile.inspect.arrivalDistance))
            {
                return;
            }

            Zombie.Log("Reached inspected position.");
            if (Zombie.IsSearchCycleActive)
            {
                OnReachedSearchStep();
                return;
            }

            if (Random.value <= Zombie.Profile.inspect.searchChanceAfterArrival)
            {
                Zombie.BeginSearchCycle(Zombie.SuspiciousPosition);
                Zombie.ChangeState(ZombieStateType.Search);
            }
            else
            {
                Zombie.ChangeState(Zombie.ChoosePassiveState());
            }
        }

        private void MoveToSuspiciousPosition()
        {
            Vector3 destination = Zombie.HasSuspiciousPosition ? Zombie.SuspiciousPosition : Zombie.transform.position;
            hasDestination = Zombie.TrySetDestination(destination);
        }

        private void OnReachedSearchStep()
        {
            if (Zombie.IsSearchCycleComplete)
            {
                Zombie.Log("Search ended. Returning to passive behavior.");
                Zombie.EndSearchCycle();
                Zombie.ChangeState(Zombie.ChoosePassiveState());
                return;
            }

            Zombie.ChangeState(ZombieStateType.Search);
        }
    }
}
