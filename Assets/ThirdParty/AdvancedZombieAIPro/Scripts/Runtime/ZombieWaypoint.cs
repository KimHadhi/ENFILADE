using UnityEngine;
using UnityEngine.Events;

namespace AdvancedZombieAIPro
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Advanced Zombie AI Pro/Zombie Waypoint")]
    public sealed class ZombieWaypoint : MonoBehaviour
    {
        [SerializeField] private bool overrideProfileSettings = false;
        [SerializeField, Range(0f, 1f)] private float chanceToStopAtWaypoint = 0.45f;
        [SerializeField, Min(0f)] private float minimumWaitTime = 1.5f;
        [SerializeField, Min(0f)] private float maximumWaitTime = 4f;
        [SerializeField] private UnityEvent onWaypointReached = new UnityEvent();

        public bool OverrideProfileSettings => overrideProfileSettings;
        public float ChanceToStopAtWaypoint => Mathf.Clamp01(chanceToStopAtWaypoint);
        public float MinimumWaitTime => Mathf.Max(0f, minimumWaitTime);
        public float MaximumWaitTime => Mathf.Max(MinimumWaitTime, maximumWaitTime);
        public UnityEvent OnWaypointReached => onWaypointReached;

        private void OnValidate()
        {
            chanceToStopAtWaypoint = Mathf.Clamp01(chanceToStopAtWaypoint);
            minimumWaitTime = Mathf.Max(0f, minimumWaitTime);
            maximumWaitTime = Mathf.Max(minimumWaitTime, maximumWaitTime);

            if (onWaypointReached == null)
            {
                onWaypointReached = new UnityEvent();
            }
        }

        internal void InvokeReached()
        {
            onWaypointReached?.Invoke();
        }
    }
}
