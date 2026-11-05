using UnityEngine;

namespace AdvancedZombieAIPro
{
    internal sealed class ZombieWanderState : ZombieState
    {
        private bool hasDestination;
        private bool hasFoodDestination;
        private ZombieWaypoint currentWaypoint;
        private bool hasInvokedCurrentWaypointReached;
        private float foodRepathTimer;

        public ZombieWanderState(ZombieAI zombie) : base(zombie)
        {
        }

        public override ZombieStateType StateType => ZombieStateType.Wander;

        public override void Enter()
        {
            Zombie.ConfigureAgent(Zombie.Profile.wander.speed, Zombie.Profile.wander.arrivalDistance);
            hasDestination = false;
            hasFoodDestination = false;
            currentWaypoint = null;
            hasInvokedCurrentWaypointReached = false;
            foodRepathTimer = 0f;
            SelectNextDestination();
        }

        public override void Tick(float deltaTime)
        {
            if (ReactToTarget())
            {
                return;
            }

            if (Zombie.HasCurrentFood())
            {
                TickFoodDestination(deltaTime);
                return;
            }

            if (hasFoodDestination)
            {
                hasDestination = false;
                hasFoodDestination = false;
                currentWaypoint = null;
                hasInvokedCurrentWaypointReached = false;
            }

            if (!hasDestination)
            {
                SelectNextDestination();
                return;
            }

            if (!Zombie.HasReachedDestination(Zombie.Profile.wander.arrivalDistance))
            {
                return;
            }

            HandleDestinationReached();
        }

        private void SelectNextDestination()
        {
            currentWaypoint = null;
            hasInvokedCurrentWaypointReached = false;

            if (Zombie.HasCurrentFood())
            {
                SelectFoodDestination();
                return;
            }

            Zombie.ConfigureAgent(Zombie.Profile.wander.speed, Zombie.Profile.wander.arrivalDistance);
            hasFoodDestination = false;

            Vector3 destination;
            bool foundDestination = false;
            ZombieWaypoint selectedWaypoint = null;

            if (Zombie.Profile.wander.wanderMode == ZombieWanderMode.Waypoint)
            {
                foundDestination = Zombie.TryGetNextWaypointDestination(out destination, out selectedWaypoint);
                if (!foundDestination)
                {
                    selectedWaypoint = null;
                    foundDestination = Zombie.TryGetRandomWanderDestination(out destination);
                }
            }
            else
            {
                foundDestination = Zombie.TryGetRandomWanderDestination(out destination);
            }

            if (!foundDestination)
            {
                Zombie.StopAgent(false);
                Zombie.Log("Could not find a wander destination.");
                return;
            }

            hasDestination = Zombie.TrySetDestination(destination);
            currentWaypoint = hasDestination ? selectedWaypoint : null;
            Zombie.Log($"Wandering to {destination}.");
        }

        private void HandleDestinationReached()
        {
            hasDestination = false;
            InvokeCurrentWaypointReached();

            if (Zombie.CurrentStateType != ZombieStateType.Wander)
            {
                return;
            }

            float stopChance = Zombie.Profile.wander.idleChanceAfterDestination;
            float minimumWaitTime = Zombie.Profile.idle.minimumIdleTime;
            float maximumWaitTime = Zombie.Profile.idle.maximumIdleTime;
            bool useWaypointSettings = currentWaypoint != null && currentWaypoint.OverrideProfileSettings;

            if (useWaypointSettings)
            {
                stopChance = currentWaypoint.ChanceToStopAtWaypoint;
                minimumWaitTime = currentWaypoint.MinimumWaitTime;
                maximumWaitTime = currentWaypoint.MaximumWaitTime;
            }

            if (Random.value <= stopChance && Zombie.IsStateEnabled(ZombieStateType.Idle))
            {
                if (useWaypointSettings)
                {
                    Zombie.QueueNextIdleDuration(Random.Range(minimumWaitTime, maximumWaitTime));
                }

                if (Zombie.ChangeState(ZombieStateType.Idle))
                {
                    return;
                }

                Zombie.ClearQueuedIdleDuration();
            }

            SelectNextDestination();
        }

        private void InvokeCurrentWaypointReached()
        {
            if (currentWaypoint == null || hasInvokedCurrentWaypointReached)
            {
                return;
            }

            hasInvokedCurrentWaypointReached = true;
            currentWaypoint.InvokeReached();
        }

        private void TickFoodDestination(float deltaTime)
        {
            Zombie.ConfigureAgent(Zombie.Profile.hunger.moveSpeed, Zombie.Profile.hunger.eatingDistance);

            if (Zombie.IsCurrentFoodWithinEatingDistance())
            {
                hasDestination = false;
                hasFoodDestination = false;
                currentWaypoint = null;
                hasInvokedCurrentWaypointReached = false;
                Zombie.ChangeState(ZombieStateType.Eat);
                return;
            }

            foodRepathTimer -= deltaTime;
            if (!hasDestination || !hasFoodDestination || foodRepathTimer <= 0f)
            {
                SelectFoodDestination();
            }
        }

        private void SelectFoodDestination()
        {
            if (!Zombie.HasCurrentFood())
            {
                hasDestination = false;
                hasFoodDestination = false;
                currentWaypoint = null;
                hasInvokedCurrentWaypointReached = false;
                return;
            }

            Zombie.ConfigureAgent(Zombie.Profile.hunger.moveSpeed, Zombie.Profile.hunger.eatingDistance);
            hasDestination = Zombie.TrySetDestination(Zombie.CurrentFoodPosition);
            hasFoodDestination = hasDestination;
            currentWaypoint = null;
            hasInvokedCurrentWaypointReached = false;
            foodRepathTimer = Zombie.Profile.hunger.repathInterval;

            if (hasDestination)
            {
                Zombie.Log($"Wandering to food {Zombie.CurrentFood.name}.");
            }
            else
            {
                Zombie.StopAgent(false);
                Zombie.Log("Could not path to food.");
            }
        }
    }
}
