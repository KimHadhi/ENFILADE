using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdvancedZombieAIPro
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Advanced Zombie AI Pro/Zombie Waypoint Path")]
    public sealed class ZombieWaypointPath : MonoBehaviour
    {
        public const int MinimumUsableWaypointCount = 1;
        private const float WaypointMarkerRadius = 0.25f;
        private static readonly Color WaypointMarkerColor = new Color(0.1f, 0.85f, 1f, 0.9f);
        private static readonly Color WaypointLineColor = new Color(1f, 0.82f, 0.1f, 0.9f);
        private static readonly ZombieWaypoint[] EmptyWaypoints = Array.Empty<ZombieWaypoint>();

        [SerializeField] private List<ZombieWaypoint> waypoints = new List<ZombieWaypoint>();

        public IReadOnlyList<ZombieWaypoint> Waypoints => waypoints != null ? waypoints : EmptyWaypoints;
        public int WaypointCount => waypoints != null ? waypoints.Count : 0;

        public int ValidWaypointCount
        {
            get
            {
                if (waypoints == null)
                {
                    return 0;
                }

                int validCount = 0;
                for (int i = 0; i < waypoints.Count; i++)
                {
                    if (waypoints[i] != null)
                    {
                        validCount++;
                    }
                }

                return validCount;
            }
        }

        public bool HasUsableWaypoints => ValidWaypointCount >= MinimumUsableWaypointCount;

        public bool TryGetWaypoint(int index, out ZombieWaypoint waypoint)
        {
            waypoint = null;
            if (waypoints == null || index < 0 || index >= waypoints.Count)
            {
                return false;
            }

            waypoint = waypoints[index];
            return waypoint != null;
        }

        public void AddWaypoint(ZombieWaypoint waypoint)
        {
            if (waypoint == null)
            {
                return;
            }

            if (waypoints == null)
            {
                waypoints = new List<ZombieWaypoint>();
            }

            waypoints.Add(waypoint);
        }

        private void OnValidate()
        {
            if (waypoints == null)
            {
                waypoints = new List<ZombieWaypoint>();
            }
        }

        private void OnDrawGizmos()
        {
            if (waypoints == null)
            {
                return;
            }

            bool hasFirstWaypoint = false;
            bool hasPreviousWaypoint = false;
            int validWaypointCount = 0;
            Vector3 firstPosition = Vector3.zero;
            Vector3 previousPosition = Vector3.zero;
            for (int i = 0; i < waypoints.Count; i++)
            {
                ZombieWaypoint waypoint = waypoints[i];
                if (waypoint == null)
                {
                    continue;
                }

                Vector3 waypointPosition = waypoint.transform.position;
                Gizmos.color = WaypointMarkerColor;
                Gizmos.DrawSphere(waypointPosition, WaypointMarkerRadius);

                if (!hasFirstWaypoint)
                {
                    firstPosition = waypointPosition;
                    hasFirstWaypoint = true;
                }

                if (hasPreviousWaypoint)
                {
                    Gizmos.color = WaypointLineColor;
                    Gizmos.DrawLine(previousPosition, waypointPosition);
                }

                previousPosition = waypointPosition;
                hasPreviousWaypoint = true;
                validWaypointCount++;
            }

            if (validWaypointCount > 1)
            {
                Gizmos.color = WaypointLineColor;
                Gizmos.DrawLine(previousPosition, firstPosition);
            }
        }
    }
}
