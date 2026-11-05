using System;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    internal static class ZombieInspectorGUI
    {
        private static GUIStyle sectionHeaderStyle;
        private static GUIStyle titleStyle;
        private static GUIStyle subtitleStyle;
        private static GUIStyle centeredLabelStyle;
        private static Color headerColor;
        private static Color sectionColor;
        private static Color accentColor;
        private static Color progressBackgroundColor;
        private static Color progressFillColor;

        public static void DrawHeader(string title, string subtitle)
        {
            EnsureStyles();
            DrawHeader(title, subtitle, accentColor);
        }

        public static void DrawHeader(string title, string subtitle, Color customAccentColor)
        {
            EnsureStyles();
            DrawHeader(title, subtitle, customAccentColor, headerColor);
        }

        public static void DrawHeader(string title, string subtitle, Color customAccentColor, Color customHeaderColor)
        {
            EnsureStyles();

            EditorGUILayout.Space(4f);
            Rect headerRect = EditorGUILayout.GetControlRect(false, 48f);
            EditorGUI.DrawRect(headerRect, customHeaderColor);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 5f, headerRect.height), customAccentColor);

            Rect titleRect = new Rect(headerRect.x + 14f, headerRect.y + 7f, headerRect.width - 20f, 20f);
            Rect subtitleRect = new Rect(headerRect.x + 14f, headerRect.y + 27f, headerRect.width - 20f, 17f);
            GUI.Label(titleRect, title, titleStyle);
            GUI.Label(subtitleRect, subtitle, subtitleStyle);
        }

        public static void DrawSection(string title, Action drawContent)
        {
            EnsureStyles();
            DrawSection(title, drawContent, accentColor);
        }

        public static void DrawSection(string title, Action drawContent, Color customAccentColor)
        {
            EnsureStyles();
            DrawSection(title, drawContent, customAccentColor, sectionColor);
        }

        public static void DrawSection(string title, Action drawContent, Color customAccentColor, Color customSectionColor)
        {
            EnsureStyles();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Rect headerRect = EditorGUILayout.GetControlRect(false, 24f);
            EditorGUI.DrawRect(headerRect, customSectionColor);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 4f, headerRect.height), customAccentColor);
            GUI.Label(new Rect(headerRect.x + 10f, headerRect.y, headerRect.width - 12f, headerRect.height), title, sectionHeaderStyle);

            EditorGUILayout.Space(2f);
            EditorGUI.indentLevel++;
            drawContent();
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        public static void DrawProperty(SerializedProperty property)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, true);
            }
        }

        public static void DrawDelayedProperty(SerializedProperty property)
        {
            if (property == null)
            {
                return;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Float:
                    DrawDelayedFloatProperty(property);
                    break;
                case SerializedPropertyType.Integer:
                    DrawDelayedIntProperty(property);
                    break;
                default:
                    DrawProperty(property);
                    break;
            }
        }

        public static void DrawProgressBar(float normalizedValue, string label)
        {
            EnsureStyles();

            float value = Mathf.Clamp01(normalizedValue);
            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            EditorGUI.DrawRect(rect, progressBackgroundColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * value, rect.height), progressFillColor);
            GUI.Label(rect, label, centeredLabelStyle);
        }

        public static void DrawStatusLine(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.LabelField(value);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawDelayedFloatProperty(SerializedProperty property)
        {
            GUIContent label = new GUIContent(property.displayName, property.tooltip);
            Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginProperty(rect, label, property);
            EditorGUI.BeginChangeCheck();
            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            float value = EditorGUI.DelayedFloatField(rect, label, property.floatValue);
            EditorGUI.showMixedValue = previousMixedValue;
            if (EditorGUI.EndChangeCheck())
            {
                property.floatValue = value;
            }
            EditorGUI.EndProperty();
        }

        private static void DrawDelayedIntProperty(SerializedProperty property)
        {
            GUIContent label = new GUIContent(property.displayName, property.tooltip);
            Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginProperty(rect, label, property);
            EditorGUI.BeginChangeCheck();
            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            int value = EditorGUI.DelayedIntField(rect, label, property.intValue);
            EditorGUI.showMixedValue = previousMixedValue;
            if (EditorGUI.EndChangeCheck())
            {
                property.intValue = value;
            }
            EditorGUI.EndProperty();
        }

        private static void EnsureStyles()
        {
            if (sectionHeaderStyle != null)
            {
                return;
            }

            bool isProSkin = EditorGUIUtility.isProSkin;
            headerColor = isProSkin ? new Color(0.16f, 0.18f, 0.20f) : new Color(0.82f, 0.86f, 0.90f);
            sectionColor = isProSkin ? new Color(0.20f, 0.22f, 0.24f) : new Color(0.89f, 0.91f, 0.94f);
            accentColor = new Color(0.20f, 0.67f, 0.78f);
            progressBackgroundColor = isProSkin ? new Color(0.11f, 0.12f, 0.13f) : new Color(0.70f, 0.74f, 0.78f);
            progressFillColor = new Color(0.20f, 0.67f, 0.78f);

            sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal =
                {
                    textColor = isProSkin ? new Color(0.90f, 0.95f, 0.96f) : new Color(0.12f, 0.18f, 0.20f)
                }
            };

            titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                normal =
                {
                    textColor = isProSkin ? Color.white : new Color(0.08f, 0.12f, 0.14f)
                }
            };

            subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                normal =
                {
                    textColor = isProSkin ? new Color(0.68f, 0.76f, 0.80f) : new Color(0.30f, 0.38f, 0.42f)
                }
            };

            centeredLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    textColor = isProSkin ? Color.white : new Color(0.08f, 0.12f, 0.14f)
                }
            };
        }
    }
}
