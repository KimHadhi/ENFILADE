namespace AdvancedZombieAIPro
{
    internal sealed class ZombieAttackState : ZombieState
    {
        private float cooldownRemaining;
        private ZombieAttackKind currentAttackKind;

        public ZombieAttackState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Attack;

        public override void Enter()
        {
            BeginAttack();
        }

        public override void Tick(float deltaTime)
        {
            if (Zombie.Target != null)
            {
                Zombie.FacePosition(Zombie.Target.transform.position, deltaTime);

                if (currentAttackKind == ZombieAttackKind.ChaseAttack)
                {
                    Zombie.ConfigureAgent(Zombie.Profile.chase.speed, Zombie.Profile.chase.stoppingDistance);
                    Zombie.TrySetDestination(Zombie.Target.transform.position);
                }
            }

            cooldownRemaining -= deltaTime;
            if (cooldownRemaining > 0f)
            {
                return;
            }

            if (Zombie.CanAttackTarget())
            {
                BeginAttack();
                return;
            }

            if (Zombie.Target != null)
            {
                Zombie.SetLastKnownTargetPosition(Zombie.Target.transform.position);
                Zombie.SetSuspiciousPosition(Zombie.Target.transform.position);
                Zombie.ChangeState(ZombieStateType.Chase);
            }
            else if (Zombie.HasLastKnownTargetPosition || Zombie.HasSuspiciousPosition)
            {
                Zombie.ChangeState(ZombieStateType.Search);
            }
            else
            {
                Zombie.ChangeState(Zombie.ChoosePassiveState());
            }
        }

        private void BeginAttack()
        {
            currentAttackKind = Zombie.ShouldUseChaseAttack() ? ZombieAttackKind.ChaseAttack : ZombieAttackKind.StandAttack;
            cooldownRemaining = Zombie.Profile.attack.attackCooldown;

            if (currentAttackKind == ZombieAttackKind.StandAttack)
            {
                Zombie.StopAgent(false);
            }
            else if (Zombie.Target != null)
            {
                Zombie.ConfigureAgent(Zombie.Profile.chase.speed, Zombie.Profile.chase.stoppingDistance);
                Zombie.TrySetDestination(Zombie.Target.transform.position);
            }

            Zombie.TriggerAttack(currentAttackKind);
        }
    }
}
