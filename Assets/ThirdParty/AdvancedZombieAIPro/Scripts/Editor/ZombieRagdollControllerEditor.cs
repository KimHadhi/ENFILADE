using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieRagdollController))]
    [CanEditMultipleObjects]
    internal sealed class ZombieRagdollControllerEditor : Editor
    {
        private SerializedProperty ragdollBodies;
        private SerializedProperty ragdollColliders;
        private SerializedProperty disableRagdollOnAwake;

        private void OnEnable()
        {
            ragdollBodies = serializedObject.FindProperty("ragdollBodies");
            ragdollColliders = serializedObject.FindProperty("ragdollColliders");
            disableRagdollOnAwake = serializedObject.FindProperty("disableRagdollOnAwake");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader("Zombie Ragdoll", "Ragdoll rigidbodies, colliders, and setup tools");
            DrawStatusSection();
            DrawSetupSection();
            DrawPartsSection();
            DrawActionsSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStatusSection()
        {
            ZombieInspectorGUI.DrawSection("Status", () =>
            {
                int bodyCount = 0;
                int colliderCount = 0;
                int nullBodyCount = 0;
                int nullColliderCount = 0;

                for (int i = 0; i < targets.Length; i++)
                {
                    ZombieRagdollController controller = targets[i] as ZombieRagdollController;
                    if (controller == null)
                    {
                        continue;
                    }

                    CountValid(controller.RagdollBodies, ref bodyCount, ref nullBodyCount);
                    CountValid(controller.RagdollColliders, ref colliderCount, ref nullColliderCount);
                }

                ZombieInspectorGUI.DrawStatusLine("Bodies", bodyCount.ToString());
                ZombieInspectorGUI.DrawStatusLine("Colliders", colliderCount.ToString());

                if (bodyCount == 0)
                {
                    EditorGUILayout.HelpBox("No ragdoll rigidbodies are assigned.", MessageType.Warning);
                }

                if (colliderCount == 0)
                {
                    EditorGUILayout.HelpBox("No ragdoll colliders are assigned.", MessageType.Warning);
                }

                if (nullBodyCount > 0)
                {
                    EditorGUILayout.HelpBox("The ragdoll body list contains null entries.", MessageType.Warning);
                }

                if (nullColliderCount > 0)
                {
                    EditorGUILayout.HelpBox("The ragdoll collider list contains null entries.", MessageType.Warning);
                }
            });
        }

        private void DrawSetupSection()
        {
            ZombieInspectorGUI.DrawSection("Setup", () =>
            {
                ZombieInspectorGUI.DrawProperty(disableRagdollOnAwake);
            });
        }

        private void DrawPartsSection()
        {
            ZombieInspectorGUI.DrawSection("Ragdoll Parts", () =>
            {
                ZombieInspectorGUI.DrawProperty(ragdollBodies);
                ZombieInspectorGUI.DrawProperty(ragdollColliders);
            });
        }

        private void DrawActionsSection()
        {
            ZombieInspectorGUI.DrawSection("Actions", () =>
            {
                if (GUILayout.Button("Refresh Ragdoll Parts"))
                {
                    serializedObject.ApplyModifiedProperties();
                    RefreshSelectedRagdolls();
                    serializedObject.Update();
                }
            });
        }

        private void RefreshSelectedRagdolls()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                ZombieRagdollController controller = targets[i] as ZombieRagdollController;
                if (controller == null)
                {
                    continue;
                }

                Undo.RecordObject(controller, "Refresh Ragdoll Parts");
                controller.RefreshRagdollParts();
                EditorUtility.SetDirty(controller);
            }
        }

        private static void CountValid(Object[] objects, ref int validCount, ref int nullCount)
        {
            if (objects == null)
            {
                return;
            }

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null)
                {
                    nullCount++;
                }
                else
                {
                    validCount++;
                }
            }
        }
    }
}
