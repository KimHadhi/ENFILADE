using UnityEditor;
using UnityEngine;

namespace ImpactCFX.EditorScripts
{
    [CustomPropertyDrawer(typeof(Range))]
    public class RangeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty minProperty = property.FindPropertyRelative("Min");
            SerializedProperty maxProperty = property.FindPropertyRelative("Max");

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            EditorGUI.BeginProperty(position, label, property);

            ImpactEditorUtilities.DrawRangeProperty(position, label, minProperty, maxProperty);

            EditorGUI.EndProperty();

            EditorGUI.indentLevel = indent;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
