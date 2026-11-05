using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieHealth))]
    [CanEditMultipleObjects]
    internal sealed class ZombieHealthEditor : Editor
    {
        private SerializedProperty zombieAI;
        private SerializedProperty onDamaged;
        private SerializedProperty onHealthChanged;
        private SerializedProperty onHealthDepleted;

        private void OnEnable()
        {
            zombieAI = serializedObject.FindProperty("zombieAI");
            onDamaged = serializedObject.FindProperty("onDamaged");
            onHealthChanged = serializedObject.FindProperty("onHealthChanged");
            onHealthDepleted = serializedObject.FindProperty("onHealthDepleted");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader("Zombie Health", "Runtime health, damage events, and death notification");
            DrawStatusSection();
            DrawCoreSection();
            DrawEventsSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStatusSection()
        {
            ZombieInspectorGUI.DrawSection("Status", () =>
            {
                if (targets.Length != 1)
                {
                    EditorGUILayout.HelpBox("Select one Zombie Health component to inspect live health status.", MessageType.Info);
                    return;
                }

                ZombieHealth health = target as ZombieHealth;
                if (health == null)
                {
                    return;
                }

                float maximumHealth = Mathf.Max(0.01f, health.MaximumHealth);
                ZombieInspectorGUI.DrawStatusLine("Current Health", $"{health.CurrentHealth:0.##} / {maximumHealth:0.##}");
                ZombieInspectorGUI.DrawStatusLine("Alive", health.IsAlive ? "Yes" : "No");
                ZombieInspectorGUI.DrawProgressBar(
                    health.CurrentHealth / maximumHealth,
                    $"{health.CurrentHealth:0.##} / {maximumHealth:0.##}");
            });
        }

        private void DrawCoreSection()
        {
            ZombieInspectorGUI.DrawSection("Core", () =>
            {
                ZombieInspectorGUI.DrawProperty(zombieAI);
                DrawRefreshReferencesButton();

                if (!zombieAI.hasMultipleDifferentValues && zombieAI.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("No Zombie AI is assigned. This health component will use fallback maximum health and will not notify Zombie AI damage/death logic.", MessageType.Warning);
                }
            });
        }

        private void DrawEventsSection()
        {
            ZombieInspectorGUI.DrawSection("Events", () =>
            {
                ZombieInspectorGUI.DrawProperty(onDamaged);
                ZombieInspectorGUI.DrawProperty(onHealthChanged);
                ZombieInspectorGUI.DrawProperty(onHealthDepleted);
            });
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
                if (targets[i] is ZombieHealth health)
                {
                    RefreshReferences(health);
                }
            }
            serializedObject.Update();
        }

        internal static void RefreshReferences(ZombieHealth health)
        {
            SerializedObject healthObject = new SerializedObject(health);
            SerializedProperty zombieAIProperty = healthObject.FindProperty("zombieAI");
            ZombieAI zombie = health.GetComponent<ZombieAI>();

            if (zombieAIProperty == null || zombieAIProperty.objectReferenceValue != null || zombie == null)
            {
                return;
            }

            Undo.RecordObject(health, "Refresh References");
            zombieAIProperty.objectReferenceValue = zombie;
            healthObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(health);
            PrefabUtility.RecordPrefabInstancePropertyModifications(health);
        }
    }
}
