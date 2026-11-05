using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieWaypoint))]
    [CanEditMultipleObjects]
    internal sealed class ZombieWaypointEditor : Editor
    {
        private static readonly Color WaypointAccentColor = new Color32(0xF2, 0xC9, 0x4C, 0xFF);

        private SerializedProperty overrideProfileSettings;
        private SerializedProperty chanceToStopAtWaypoint;
        private SerializedProperty minimumWaitTime;
        private SerializedProperty maximumWaitTime;
        private SerializedProperty onWaypointReached;

        private void OnEnable()
        {
            overrideProfileSettings = serializedObject.FindProperty("overrideProfileSettings");
            chanceToStopAtWaypoint = serializedObject.FindProperty("chanceToStopAtWaypoint");
            minimumWaitTime = serializedObject.FindProperty("minimumWaitTime");
            maximumWaitTime = serializedObject.FindProperty("maximumWaitTime");
            onWaypointReached = serializedObject.FindProperty("onWaypointReached");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader(
                "Zombie Waypoint",
                "Per-waypoint wait override and reached event",
                WaypointAccentColor);
            ZombieInspectorGUI.DrawSection("Waypoint Settings", () =>
            {
                ZombieInspectorGUI.DrawProperty(overrideProfileSettings);

                if (overrideProfileSettings.boolValue || overrideProfileSettings.hasMultipleDifferentValues)
                {
                    ZombieInspectorGUI.DrawProperty(chanceToStopAtWaypoint);
                    ZombieInspectorGUI.DrawDelayedProperty(minimumWaitTime);
                    ZombieInspectorGUI.DrawDelayedProperty(maximumWaitTime);
                }
            }, WaypointAccentColor);

            ZombieInspectorGUI.DrawSection("Event", () =>
            {
                ZombieInspectorGUI.DrawProperty(onWaypointReached);
            }, WaypointAccentColor);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
