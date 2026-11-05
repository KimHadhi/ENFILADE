namespace AdvancedZombieAIPro
{
    internal sealed class ZombieStunState : ZombieState
    {
        private float stunTimeRemaining;

        public ZombieStunState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Stun;

        public override void Enter()
        {
            Restart();
        }

        public override void Tick(float deltaTime)
        {
            Zombie.StopAgent(true);
            Zombie.ResetDetection();

            stunTimeRemaining -= deltaTime;
            if (stunTimeRemaining > 0f)
            {
                return;
            }

            Zombie.ResumeAfterStun();
        }

        internal void Restart()
        {
            stunTimeRemaining = Zombie.ConsumeStunDuration();
            Zombie.StopAgent(true);
            Zombie.ResetDetection();
            Zombie.EndSearchCycle();
            Zombie.Log($"Stunned for {stunTimeRemaining:0.##} seconds.");
        }
    }
}
