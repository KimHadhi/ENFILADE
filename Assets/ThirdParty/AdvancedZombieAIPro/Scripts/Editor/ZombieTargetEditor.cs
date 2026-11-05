using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieTarget))]
    [CanEditMultipleObjects]
    internal sealed class ZombieTargetEditor : Editor
    {
        private static readonly Color TargetAccentColor = new Color32(0xE8, 0x5D, 0x75, 0xFF);

        private SerializedProperty top;
        private SerializedProperty bottom;
        private SerializedProperty center;
        private SerializedProperty left;
        private SerializedProperty right;
        private SerializedProperty primaryCollider;

        private void OnEnable()
        {
            top = serializedObject.FindProperty("top");
            bottom = serializedObject.FindProperty("bottom");
            center = serializedObject.FindProperty("center");
            left = serializedObject.FindProperty("left");
            right = serializedObject.FindProperty("right");
            primaryCollider = serializedObject.FindProperty("primaryCollider");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader(
                "Zombie Target",
                "Detection region markers and fallback bounds",
                TargetAccentColor);
            DrawRegionSection();
            DrawBoundsSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRegionSection()
        {
            ZombieInspectorGUI.DrawSection("Region Markers", () =>
            {
                ZombieInspectorGUI.DrawProperty(top);
                ZombieInspectorGUI.DrawProperty(bottom);
                ZombieInspectorGUI.DrawProperty(center);
                ZombieInspectorGUI.DrawProperty(left);
                ZombieInspectorGUI.DrawProperty(right);

                if (!HasAnyRegionMarkerAssigned())
                {
                    EditorGUILayout.HelpBox("No region markers are assigned. The target will derive Top, Bottom, Center, Left, and Right positions from fallback bounds.", MessageType.Info);
                }
            }, TargetAccentColor);
        }

        private void DrawBoundsSection()
        {
            ZombieInspectorGUI.DrawSection("Fallback Bounds", () =>
            {
                ZombieInspectorGUI.DrawProperty(primaryCollider);
                DrawBoundsWarnings();
            }, TargetAccentColor);
        }

        private void DrawBoundsWarnings()
        {
            if (!primaryCollider.hasMultipleDifferentValues)
            {
                Collider collider = primaryCollider.objectReferenceValue as Collider;
                if (collider != null && !collider.enabled)
                {
                    EditorGUILayout.HelpBox("Primary Collider is disabled. Runtime fallback bounds will search enabled child colliders instead.", MessageType.Warning);
                }
            }

            if (HasAnyTargetWithoutCollider())
            {
                EditorGUILayout.HelpBox("At least one selected target has no child colliders. Runtime fallback regions will use default bounds around the transform.", MessageType.Warning);
            }
        }

        private bool HasAnyRegionMarkerAssigned()
        {
            return HasReference(top) ||
                HasReference(bottom) ||
                HasReference(center) ||
                HasReference(left) ||
                HasReference(right);
        }

        private static bool HasReference(SerializedProperty property)
        {
            return property != null &&
                !property.hasMultipleDifferentValues &&
                property.objectReferenceValue != null;
        }

        private bool HasAnyTargetWithoutCollider()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                ZombieTarget zombieTarget = targets[i] as ZombieTarget;
                if (zombieTarget == null)
                {
                    continue;
                }

                if (zombieTarget.GetComponentInChildren<Collider>() == null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
