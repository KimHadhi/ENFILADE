using UnityEngine;

namespace AdvancedZombieAIPro
{
    internal sealed class ZombieHitState : ZombieState
    {
        private float hitElapsedTime;
        private bool hitAnimationStarted;

        public ZombieHitState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Hit;

        public override void Enter()
        {
            Zombie.StopAgent(true);
            hitElapsedTime = 0f;
            hitAnimationStarted = false;
            Zombie.TriggerHitReaction(ZombieHitReactionKind.StandHit);
            Zombie.Log("Hit reaction started.");
        }

        public override void Tick(float deltaTime)
        {
            hitElapsedTime += deltaTime;

            if (Zombie.Profile.hit.faceHitSource && Zombie.HasHitSourcePosition)
            {
                Zombie.FacePosition(Zombie.HitSourcePosition, deltaTime, Zombie.Profile.hit.faceHitSourceTurnSpeed);
            }

            if (!HasHitAnimationFinished())
            {
                return;
            }

            ResumeAfterHit();
        }

        private void ResumeAfterHit()
        {
            if (Zombie.CanAttackTarget() && Zombie.IsStateEnabled(ZombieStateType.Attack))
            {
                Zombie.ChangeState(ZombieStateType.Attack);
                return;
            }

            if (Zombie.ShouldChaseKnownTargetAfterHit())
            {
                Zombie.SetLastKnownTargetPosition(Zombie.Target.transform.position);
                Zombie.SetSuspiciousPosition(Zombie.Target.transform.position);
                bool useAlert = Zombie.ShouldUseAlertBeforeChase();
                Zombie.Log(useAlert ? "Hit reaction finished. Alerting target." : "Hit reaction finished. Chasing target.");
                Zombie.ChangeState(useAlert ? ZombieStateType.Alert : ZombieStateType.Chase);
                return;
            }

            if (Zombie.TryStartLocalDamageSearch())
            {
                return;
            }

            if (Zombie.HasSuspiciousPosition || Zombie.HasLastKnownTargetPosition)
            {
                Zombie.Log("Hit reaction finished. Inspecting hit source.");
                Zombie.ChangeState(ZombieStateType.Inspect);
                return;
            }

            Zombie.Log("Hit reaction finished. Returning to passive behavior.");
            Zombie.ChangeState(Zombie.ChoosePassiveState());
        }

        private bool HasHitAnimationFinished()
        {
            Animator animator = Zombie.Animator;
            if (animator == null || animator.runtimeAnimatorController == null || animator.layerCount == 0)
            {
                return true;
            }

            const int layerIndex = 0;
            string hitStateTag = string.IsNullOrWhiteSpace(Zombie.Profile.hit.animatorStateTag)
                ? "Hit"
                : Zombie.Profile.hit.animatorStateTag;
            bool isInTransition = animator.IsInTransition(layerIndex);
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);

            if (stateInfo.IsTag(hitStateTag))
            {
                hitAnimationStarted = true;
                return !isInTransition && stateInfo.normalizedTime >= 1f;
            }

            if (hitAnimationStarted && !isInTransition)
            {
                return true;
            }

            if (hitElapsedTime >= Zombie.Profile.hit.animatorFallbackTime)
            {
                Zombie.Log($"Hit Animator state tag '{hitStateTag}' was not detected. Continuing after fallback time.");
                return true;
            }

            return false;
        }
    }
}
