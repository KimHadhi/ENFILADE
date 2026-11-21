// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

namespace Sonity.Internal {

    public static class EditorColorProSkin {

        private static readonly float customEditorBackgroundAlphaDarkSkin = 0.8f;
        private static readonly float customEditorBackgroundAlphaLightSkin = 0.2f;

        public static float GetCustomEditorBackgroundAlpha() {
            if (EditorGUIUtility.isProSkin) {
                return customEditorBackgroundAlphaDarkSkin;
            } else {
                return customEditorBackgroundAlphaLightSkin;
            }
        }

        private static readonly float customPropertyDrawerBackgroundAlphaDarkSkin = 1f;
        private static readonly float customPropertyDrawerBackgroundAlphaLightSkin = 0.3f;

        public static float GetCustomPropertyDrawerBackgroundAlpha() {
            if (EditorGUIUtility.isProSkin) {
                return customPropertyDrawerBackgroundAlphaDarkSkin;
            } else {
                return customPropertyDrawerBackgroundAlphaLightSkin;
            }
        }

        // Black
        private static readonly Color lightSkinTextColor = new Color(0f, 0f, 0f);
        public static Color GetLightSkinTextColor() {
            return lightSkinTextColor;
        }

        // Light grey
        private static readonly Color darkSkinTextColor = new Color(0.706f, 0.706f, 0.706f);
        public static Color GetDarkSkinTextColor() {
            return darkSkinTextColor;
        }

        public static Color GetTextDefaultDarkOrLight() {
            if (EditorGUIUtility.isProSkin) {
                return darkSkinTextColor;
            } else {
                return lightSkinTextColor;
            }
        }

        // Green
        public static Color GetTextGreen() {
            if (EditorGUIUtility.isProSkin) {
                return new Color(0f, 1f, 0f);
            } else {
                return EditorColor.ChangeValue(new Color(0f, 1f, 0f), -0.4f);
            }
        }

        // Red
        public static Color GetTextRed() {
            if (EditorGUIUtility.isProSkin) {
                return new Color(1f, 0f, 0f);
            } else {
                return EditorColor.ChangeValue(new Color(1f, 0f, 0f), -0.2f);
            }
        }

        // Orange
        public static Color GetTextOrange() {
            if (EditorGUIUtility.isProSkin) {
                return new Color(1f, 0.5f, 0f);
            } else {
                return EditorColor.ChangeValue(new Color(1f, 0.5f, 0f), -0.1f);
            }
        }
    }
}
#endif