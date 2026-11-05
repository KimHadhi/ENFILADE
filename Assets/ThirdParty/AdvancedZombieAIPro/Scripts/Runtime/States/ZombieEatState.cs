namespace AdvancedZombieAIPro
{
    internal sealed class ZombieEatState : ZombieState
    {
        private bool isEating;

        public ZombieEatState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Eat;

        public override void Enter()
        {
            isEating = false;
            Zombie.SetEatingAnimationActive(false);

            if (!Zombie.HasCurrentFood())
            {
                Zombie.ChangeState(Zombie.ChoosePassiveState());
                return;
            }

            if (!Zombie.IsCurrentFoodWithinEatingDistance())
            {
                Zombie.Log("Food is not close enough to eat. Returning to Wander.");
                Zombie.ChangeState(ZombieStateType.Wander);
                return;
            }

            Zombie.StopAgent(true);
            Zombie.Log($"Started eating {Zombie.CurrentFood.name}.");
        }

        public override void Exit()
        {
            Zombie.SetEatingAnimationActive(false);
        }

        public override void Tick(float deltaTime)
        {
            if (Zombie.ShouldDetectTargetWhileEating() && ReactToTarget())
            {
                Zombie.ClearCurrentFood();
                return;
            }

            if (!Zombie.HasCurrentFood())
            {
                FinishEating("Food is no longer available.");
                return;
            }

            if (!Zombie.IsCurrentFoodWithinEatingDistance())
            {
                isEating = false;
                Zombie.SetEatingAnimationActive(false);
                Zombie.Log("Food moved out of eating distance. Returning to Wander.");
                Zombie.ChangeState(ZombieStateType.Wander);
                return;
            }

            Zombie.StopAgent(true);
            isEating = true;
            Zombie.SetEatingAnimationActive(true);

            string foodName = Zombie.CurrentFood != null ? Zombie.CurrentFood.name : "food";
            float consumedEnergy = Zombie.EatCurrentFood(deltaTime);
            if (consumedEnergy > 0f)
            {
                Zombie.Log($"Eating {foodName}. Hunger is now {Zombie.CurrentHunger:0.##}.");
            }

            if (Zombie.CurrentHunger >= Zombie.Profile.hunger.maximumHunger || !Zombie.HasCurrentFood())
            {
                FinishEating("Finished eating.");
            }
        }

        private void FinishEating(string reason)
        {
            if (isEating)
            {
                Zombie.BeginHungerDepletionPause();
            }

            Zombie.Log(reason);
            Zombie.ClearCurrentFood();
            Zombie.SetEatingAnimationActive(false);
            Zombie.ChangeState(Zombie.ChoosePassiveState());
        }
    }
}
