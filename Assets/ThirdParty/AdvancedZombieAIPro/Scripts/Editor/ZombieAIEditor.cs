using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieAI))]
    [CanEditMultipleObjects]
    internal sealed class ZombieAIEditor : Editor
    {
        private SerializedProperty profile;
        private SerializedProperty targetReference;
        private SerializedProperty agent;
        private SerializedProperty animator;
        private SerializedProperty health;
        private SerializedProperty ragdollController;
        private SerializedProperty debugMode;
        private SerializedProperty waypointPath;
        private SerializedProperty onStandAttack;
        private SerializedProperty onChaseAttack;

        private void OnEnable()
        {
            profile = serializedObject.FindProperty("profile");
            targetReference = serializedObject.FindProperty("target");
            agent = serializedObject.FindProperty("agent");
            animator = serializedObject.FindProperty("animator");
            health = serializedObject.FindProperty("health");
            ragdollController = serializedObject.FindProperty("ragdollController");
            debugMode = serializedObject.FindProperty("debugMode");
            waypointPath = serializedObject.FindProperty("waypointPath");
            onStandAttack = serializedObject.FindProperty("onStandAttack");
            onChaseAttack = serializedObject.FindProperty("onChaseAttack");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader("Zombie AI", "Runtime brain, references, waypoint path, and attack events");
            DrawRuntimeSection();
            DrawCoreSection();
            DrawWaypointSection();
            DrawAttackEventsSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntimeSection()
        {
            ZombieInspectorGUI.DrawSection("Runtime", () =>
            {
                if (targets.Length != 1)
                {
                    EditorGUILayout.HelpBox("Select one Zombie AI component to inspect live runtime status.", MessageType.Info);
                    return;
                }

                ZombieAI zombie = target as ZombieAI;
                if (zombie == null)
                {
                    return;
                }

                ZombieInspectorGUI.DrawStatusLine("Current State", zombie.CurrentStateName);
                ZombieInspectorGUI.DrawStatusLine("Current Mode", zombie.CurrentMode.ToString());
                ZombieInspectorGUI.DrawStatusLine("Visible Regions", zombie.VisibleRegionCount.ToString());
                ZombieInspectorGUI.DrawStatusLine("Detection", GetDetectionStatus(zombie));

                ZombieProfile zombieProfile = zombie.Profile;
                if (zombieProfile != null && zombie.IsHungerEnabled)
                {
                    float maximumHunger = Mathf.Max(0.01f, zombieProfile.hunger.maximumHunger);
                    ZombieInspectorGUI.DrawStatusLine("Current Hunger", $"{zombie.CurrentHunger:0.##} / {maximumHunger:0.##}");
                    ZombieInspectorGUI.DrawProgressBar(
                        zombie.CurrentHunger / maximumHunger,
                        $"Hunger {zombie.CurrentHunger:0.##} / {maximumHunger:0.##}");
                }
            });
        }

        private void DrawCoreSection()
        {
            ZombieInspectorGUI.DrawSection("Core", () =>
            {
                ZombieInspectorGUI.DrawProperty(profile);
                ZombieInspectorGUI.DrawProperty(targetReference);
                ZombieInspectorGUI.DrawProperty(agent);
                ZombieInspectorGUI.DrawProperty(animator);
                ZombieInspectorGUI.DrawProperty(health);
                ZombieInspectorGUI.DrawProperty(ragdollController);
                ZombieInspectorGUI.DrawProperty(debugMode);
                DrawRefreshReferencesButton();
                DrawCoreWarnings();
            });
        }

        private void DrawWaypointSection()
        {
            ZombieInspectorGUI.DrawSection("Waypoint Wander", () =>
            {
                ZombieInspectorGUI.DrawProperty(waypointPath);
                DrawWaypointWarnings();
            });
        }

        private void DrawAttackEventsSection()
        {
            ZombieInspectorGUI.DrawSection("Attack Events", () =>
            {
                ZombieInspectorGUI.DrawProperty(onStandAttack);
                ZombieInspectorGUI.DrawProperty(onChaseAttack);
            });
        }

        private void DrawCoreWarnings()
        {
            if (!profile.hasMultipleDifferentValues && profile.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No Zombie Profile is assigned. The zombie cannot run configured AI behavior without a profile.", MessageType.Warning);
            }

            if (!agent.hasMultipleDifferentValues && agent.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No NavMeshAgent reference is assigned. The component will try to find one at runtime, but movement needs a valid agent.", MessageType.Warning);
            }

            if (!animator.hasMultipleDifferentValues && animator.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No Animator is assigned. AI can still run, but animation parameters and triggers will not be applied.", MessageType.Info);
            }

            if (!health.hasMultipleDifferentValues && health.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No Zombie Health is assigned. Damage and death handling will not be driven by a health component.", MessageType.Warning);
            }

            if (ShouldWarnMissingRagdollController())
            {
                EditorGUILayout.HelpBox("This profile uses ragdoll death, but no Zombie Ragdoll Controller is assigned.", MessageType.Warning);
            }
        }

        private void DrawWaypointWarnings()
        {
            if (profile.hasMultipleDifferentValues || waypointPath.hasMultipleDifferentValues)
            {
                return;
            }

            ZombieProfile zombieProfile = profile.objectReferenceValue as ZombieProfile;
            if (zombieProfile == null)
            {
                return;
            }

            ZombieWaypointPath path = waypointPath.objectReferenceValue as ZombieWaypointPath;
            if (zombieProfile.wander.wanderMode != ZombieWanderMode.Waypoint)
            {
                if (path != null)
                {
                    EditorGUILayout.HelpBox("A waypoint path is assigned, but this profile is not using Waypoint Wander mode.", MessageType.Info);
                }

                return;
            }

            if (path == null)
            {
                EditorGUILayout.HelpBox("This zombie uses Waypoint Wandering, but no waypoint path is assigned.", MessageType.Warning);
                return;
            }

            if (!path.HasUsableWaypoints)
            {
                EditorGUILayout.HelpBox("This zombie uses Waypoint Wandering, but its waypoint path has no valid waypoints.", MessageType.Warning);
            }
        }

        private bool ShouldWarnMissingRagdollController()
        {
            if (profile.hasMultipleDifferentValues ||
                ragdollController.hasMultipleDifferentValues ||
                ragdollController.objectReferenceValue != null)
            {
                return false;
            }

            ZombieProfile zombieProfile = profile.objectReferenceValue as ZombieProfile;
            return zombieProfile != null && zombieProfile.death.deathMode == ZombieDeathMode.Ragdoll;
        }

        private void DrawRefreshReferencesButton()
        {
            if (!GUILayout.Button("Refresh References"))
            {
                return;
            }

            serializedObject.ApplyModifiedProperties();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is ZombieAI zombie)
                {
                    RefreshReferences(zombie);
                }
            }
            serializedObject.Update();
        }

        internal static void RefreshReferences(ZombieAI zombie)
        {
            SerializedObject zombieObject = new SerializedObject(zombie);
            bool undoRecorded = false;
            bool changed = false;

            changed |= AssignMissingReference(zombieObject.FindProperty("agent"), zombie.GetComponent<NavMeshAgent>(), zombie, ref undoRecorded);
            changed |= AssignMissingReference(zombieObject.FindProperty("animator"), zombie.GetComponent<Animator>(), zombie, ref undoRecorded);
            changed |= AssignMissingReference(zombieObject.FindProperty("health"), zombie.GetComponent<ZombieHealth>(), zombie, ref undoRecorded);
            changed |= AssignMissingReference(zombieObject.FindProperty("ragdollController"), zombie.GetComponent<ZombieRagdollController>(), zombie, ref undoRecorded);

            if (!changed)
            {
                return;
            }

            zombieObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(zombie);
            PrefabUtility.RecordPrefabInstancePropertyModifications(zombie);
        }

        private static bool AssignMissingReference(SerializedProperty property, Object reference, Object undoTarget, ref bool undoRecorded)
        {
            if (property == null || property.objectReferenceValue != null || reference == null)
            {
                return false;
            }

            if (!undoRecorded)
            {
                Undo.RecordObject(undoTarget, "Refresh References");
                undoRecorded = true;
            }

            property.objectReferenceValue = reference;
            return true;
        }

        private static string GetDetectionStatus(ZombieAI zombie)
        {
            if (zombie.HasFullDetection)
            {
                return "Full";
            }

            if (zombie.HasChaseVisibility)
            {
                return "Chase Visibility";
            }

            if (zombie.HasPartialDetection)
            {
                return "Partial";
            }

            if (zombie.HasAnyDetection)
            {
                return "Any Region";
            }

            return "None";
        }
    }
}
