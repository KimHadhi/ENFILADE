using UnityEngine;

namespace AdvancedZombieAIPro
{
    internal sealed class ZombieSearchState : ZombieState
    {
        private float lookTimeRemaining;

        public ZombieSearchState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Search;

        public override void Enter()
        {
            if (!Zombie.IsSearchCycleActive)
            {
                Zombie.BeginSearchCycle(Zombie.GetSearchStartPosition());
            }

            lookTimeRemaining = Random.Range(Zombie.Profile.search.minimumSearchTime, Zombie.Profile.search.maximumSearchTime);
            Zombie.StopAgent(true);
            Zombie.Log($"Searching in place for {lookTimeRemaining:0.##} seconds.");
        }

        public override void Tick(float deltaTime)
        {
            if (ReactToTarget())
            {
                return;
            }

            lookTimeRemaining -= deltaTime;

            if (lookTimeRemaining > 0f)
            {
                return;
            }

            Zombie.CompleteCurrentSearchLocation();
            if (Zombie.IsSearchCycleComplete)
            {
                Zombie.Log("Search ended. Returning to passive behavior.");
                Zombie.EndSearchCycle();
                Zombie.ChangeState(Zombie.ChoosePassiveState());
                return;
            }

            if (Zombie.TrySelectNextSearchStep())
            {
                Zombie.ChangeState(ZombieStateType.Inspect);
                return;
            }

            Zombie.Log("Could not find a next search step. Returning to passive behavior.");
            Zombie.EndSearchCycle();
            Zombie.ChangeState(Zombie.ChoosePassiveState());
        }
    }
}
