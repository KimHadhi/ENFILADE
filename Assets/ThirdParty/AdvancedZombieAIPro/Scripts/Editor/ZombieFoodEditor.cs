using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieFood))]
    [CanEditMultipleObjects]
    internal sealed class ZombieFoodEditor : Editor
    {
        private SerializedProperty energy;
        private SerializedProperty eatPoint;
        private SerializedProperty destroyWhenDepleted;
        private SerializedProperty sourceZombieHealth;

        private void OnEnable()
        {
            energy = serializedObject.FindProperty("energy");
            eatPoint = serializedObject.FindProperty("eatPoint");
            destroyWhenDepleted = serializedObject.FindProperty("destroyWhenDepleted");
            sourceZombieHealth = serializedObject.FindProperty("sourceZombieHealth");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader("Zombie Food", "Food source setup, availability, and remaining energy");
            DrawStatusSection();
            DrawFoodSection();
            DrawDeadZombieFoodSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStatusSection()
        {
            ZombieInspectorGUI.DrawSection("Status", () =>
            {
                if (targets.Length != 1)
                {
                    EditorGUILayout.HelpBox("Select one Zombie Food component to inspect live availability.", MessageType.Info);
                    return;
                }

                ZombieFood food = target as ZombieFood;
                if (food == null)
                {
                    return;
                }

                float maximumEnergy = Mathf.Max(0.01f, food.Energy);
                ZombieInspectorGUI.DrawStatusLine("Remaining Energy", $"{food.RemainingEnergy:0.##} / {maximumEnergy:0.##}");
                ZombieInspectorGUI.DrawStatusLine("Available", food.IsAvailable ? "Yes" : "No");
                ZombieInspectorGUI.DrawStatusLine("Eat Position", food.EatPosition.ToString("F2"));
                ZombieInspectorGUI.DrawProgressBar(
                    food.RemainingEnergy / maximumEnergy,
                    $"{food.RemainingEnergy:0.##} / {maximumEnergy:0.##}");
            });
        }

        private void DrawFoodSection()
        {
            ZombieInspectorGUI.DrawSection("Food", () =>
            {
                ZombieInspectorGUI.DrawDelayedProperty(energy);
                ZombieInspectorGUI.DrawProperty(eatPoint);
                ZombieInspectorGUI.DrawProperty(destroyWhenDepleted);

                if (!eatPoint.hasMultipleDifferentValues && eatPoint.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("Eat Point is not assigned. The food object's transform position will be used.", MessageType.Info);
                }
            });
        }

        private void DrawDeadZombieFoodSection()
        {
            ZombieInspectorGUI.DrawSection("Dead Zombie Food", () =>
            {
                ZombieInspectorGUI.DrawProperty(sourceZombieHealth);
                DrawRefreshReferencesButton();

                if (!sourceZombieHealth.hasMultipleDifferentValues &&
                    sourceZombieHealth.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("No Source Zombie Health is assigned. This food is treated as a normal food item. Assign a source zombie health to make it available only after that zombie dies.", MessageType.Info);
                }
                else if (!sourceZombieHealth.hasMultipleDifferentValues)
                {
                    EditorGUILayout.HelpBox("This food becomes available only after the assigned source zombie is dead.", MessageType.Info);
                }
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
                if (targets[i] is ZombieFood food)
                {
                    RefreshReferences(food);
                }
            }
            serializedObject.Update();
        }

        internal static void RefreshReferences(ZombieFood food)
        {
            SerializedObject foodObject = new SerializedObject(food);
            SerializedProperty sourceZombieHealthProperty = foodObject.FindProperty("sourceZombieHealth");
            ZombieHealth health = food.GetComponent<ZombieHealth>();

            if (sourceZombieHealthProperty == null ||
                sourceZombieHealthProperty.objectReferenceValue != null ||
                health == null)
            {
                return;
            }

            Undo.RecordObject(food, "Refresh References");
            sourceZombieHealthProperty.objectReferenceValue = health;
            foodObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(food);
            PrefabUtility.RecordPrefabInstancePropertyModifications(food);
        }
    }
}
