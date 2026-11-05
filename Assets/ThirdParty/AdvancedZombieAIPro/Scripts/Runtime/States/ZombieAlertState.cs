using UnityEngine;

namespace AdvancedZombieAIPro
{
    internal sealed class ZombieAlertState : ZombieState
    {
        private float alertElapsedTime;
        private bool alertAnimationStarted;
        private bool nearbyAlertSent;

        public ZombieAlertState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Alert;

        public override void Enter()
        {
            Zombie.StopAgent(true);
            alertElapsedTime = 0f;
            alertAnimationStarted = false;
            nearbyAlertSent = Zombie.ConsumeNextAlertPropagationSuppression();

            if (Zombie.Target != null)
            {
                Zombie.SetLastKnownTargetPosition(Zombie.Target.transform.position);
                Zombie.SetSuspiciousPosition(Zombie.Target.transform.position);
            }

            TrySendNearbyAlert();
            Zombie.Log("Alerted target. Waiting for Alert animation to finish.");
        }

        public override void Tick(float deltaTime)
        {
            alertElapsedTime += deltaTime;
            FaceAlertPosition(deltaTime);
            TrySendNearbyAlert();

            if (Zombie.Target != null && Zombie.HasFullDetection)
            {
                Zombie.SetLastKnownTargetPosition(Zombie.Target.transform.position);
                Zombie.SetSuspiciousPosition(Zombie.Target.transform.position);
            }

            if (!HasAlertAnimationFinished() || ShouldWaitForNearbyAlertDelay())
            {
                return;
            }

            if (Zombie.CanAttackTarget() && Zombie.IsStateEnabled(ZombieStateType.Attack))
            {
                Zombie.ChangeState(ZombieStateType.Attack);
                return;
            }

            if (Zombie.HasLastKnownTargetPosition || Zombie.Target != null)
            {
                Zombie.Log("Alert finished. Chasing target.");
                Zombie.ChangeState(ZombieStateType.Chase);
                return;
            }

            if (Zombie.HasSuspiciousPosition)
            {
                Zombie.ChangeState(ZombieStateType.Search);
                return;
            }

            Zombie.ChangeState(Zombie.ChoosePassiveState());
        }

        private void TrySendNearbyAlert()
        {
            if (nearbyAlertSent)
            {
                return;
            }

            if (alertElapsedTime < Zombie.Profile.alert.nearbyAlertDelay)
            {
                return;
            }

            nearbyAlertSent = true;
            Zombie.TryAlertNearbyZombies();
        }

        private bool ShouldWaitForNearbyAlertDelay()
        {
            return !nearbyAlertSent &&
                Zombie.Profile.alert.canAlertNearbyZombies &&
                Zombie.Profile.alert.nearbyAlertDelay > 0f;
        }

        private void FaceAlertPosition(float deltaTime)
        {
            if (Zombie.Target != null)
            {
                Zombie.FacePosition(Zombie.Target.transform.position, deltaTime, Zombie.Profile.alert.faceTargetTurnSpeed);
                return;
            }

            if (Zombie.HasLastKnownTargetPosition)
            {
                Zombie.FacePosition(Zombie.LastKnownTargetPosition, deltaTime, Zombie.Profile.alert.faceTargetTurnSpeed);
            }
        }

        private bool HasAlertAnimationFinished()
        {
            Animator animator = Zombie.Animator;
            if (animator == null || animator.runtimeAnimatorController == null || animator.layerCount == 0)
            {
                return true;
            }

            const int layerIndex = 0;
            string alertStateTag = string.IsNullOrWhiteSpace(Zombie.Profile.alert.animatorStateTag)
                ? "Alert"
                : Zombie.Profile.alert.animatorStateTag;
            bool isInTransition = animator.IsInTransition(layerIndex);
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);

            if (stateInfo.IsTag(alertStateTag))
            {
                alertAnimationStarted = true;
                return !isInTransition && stateInfo.normalizedTime >= 1f;
            }

            if (alertAnimationStarted && !isInTransition)
            {
                return true;
            }

            if (alertElapsedTime >= Zombie.Profile.alert.animatorFallbackTime)
            {
                Zombie.Log($"Alert Animator state tag '{alertStateTag}' was not detected. Continuing after fallback time.");
                return true;
            }

            return false;
        }
    }
}
