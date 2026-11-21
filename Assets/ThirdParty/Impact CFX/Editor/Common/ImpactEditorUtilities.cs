using UnityEditor;
using UnityEngine;

namespace ImpactCFX.EditorScripts
{
    public static class ImpactEditorUtilities
    {
        public static void Separator()
        {
            EditorGUILayout.Space();
            GUILayout.Box("", GUILayout.MaxWidth(Screen.width - 25f), GUILayout.Height(2));
        }

        public static void DrawRangeProperty(Rect position, GUIContent label, SerializedProperty min, SerializedProperty max, bool enableMax = true)
        {
            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            float width = position.width;

            position.width = 28;
            EditorGUI.LabelField(position, new GUIContent("Min", min.tooltip));

            position.x = position.max.x;
            position.width = (width - 70) / 2;
            EditorGUI.PropertyField(position, min, new GUIContent("", min.tooltip));

            bool wasGUIEnabled = GUI.enabled;
            GUI.enabled = wasGUIEnabled && enableMax;

            position.x = position.max.x + 10;
            position.width = 30;
            EditorGUI.LabelField(position, new GUIContent("Max", max.tooltip));

            position.x = position.max.x;
            position.width = (width - 70) / 2;

            EditorGUI.PropertyField(position, max, new GUIContent("", max.tooltip));
            GUI.enabled = wasGUIEnabled;
        }
    }
}
