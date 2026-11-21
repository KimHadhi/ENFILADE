// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System;

namespace Sonity.Internal {

    public abstract class SoundTagEditorBase : Editor {

        public SoundTagBase mTarget;
        public SoundTagBase[] mTargets;

        [NonSerialized] public SerializedProperty internals;
        [NonSerialized] public SerializedProperty notes;

        [NonSerialized] public SerializedProperty sonityGuidEditor;

        public void OnEnable() {
            FindProperties();
        }

        public void FindProperties() {
            internals = serializedObject.FindProperty(nameof(SoundTagBase.internals));
            sonityGuidEditor = internals.FindPropertyRelative(EditorTextAssetGuid.sonityGuidEditorPropertyName);
            notes = internals.FindPropertyRelative(nameof(SoundTagInternals.notes));
        }

        public void BeginChange() {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
        }

        public void EndChange() {
            if (EditorGUI.EndChangeCheck()) {
                serializedObject.ApplyModifiedProperties();
            }
        }

        public Color defaultGuiColor;
        public GUIStyle guiStyleBoldCenter = new GUIStyle();

        public void StartBackgroundColor(Color color) {
            GUI.color = color;
            EditorGUILayout.BeginVertical("Button");
            GUI.color = defaultGuiColor;
        }

        public void StopBackgroundColor() {
            EditorGUILayout.EndVertical();
        }

        public override void OnInspectorGUI() {

            mTarget = (SoundTagBase)target;

            mTargets = new SoundTagBase[targets.Length];
            for (int i = 0; i < targets.Length; i++) {
                mTargets[i] = (SoundTagBase)targets[i];
            }

            defaultGuiColor = GUI.color;

            guiStyleBoldCenter.fontSize = 16;
            guiStyleBoldCenter.fontStyle = FontStyle.Bold;
            guiStyleBoldCenter.alignment = TextAnchor.MiddleCenter;
            if (EditorGUIUtility.isProSkin) {
                guiStyleBoldCenter.normal.textColor = EditorColorProSkin.GetDarkSkinTextColor();
            }

            EditorGUI.indentLevel = 0;

            EditorGuiFunction.DrawObjectNameBox((UnityEngine.Object)mTarget, NameOf.SoundTag, EditorTextSoundTag.soundTagTooltip, true);
            EditorGuiAssetTopToolbarButtons.DrawTopButton(serializedObject);
            EditorTrial.InfoText();
            EditorGUILayout.Separator();

            // Notes
            EditorGUI.indentLevel = 0;
            Color previousColor = GUI.color;
            if (string.IsNullOrEmpty(notes.stringValue) || notes.stringValue == "Notes") {
                // Make less transparent if empty or default text
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
            }
            BeginChange();
            notes.stringValue = EditorGUILayout.TextArea(notes.stringValue);
            EndChange();
            GUI.color = previousColor;
            EditorGUILayout.Separator();

            // Sonity GUID
            // Transparent background so the offset will be right
            StartBackgroundColor(new Color(0f, 0f, 0f, 0f));
            BeginChange();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(sonityGuidEditor, new GUIContent(EditorTextAssetGuid.sonityGuidLabel, EditorTextAssetGuid.sonityGuidTooltip));
            EditorGUI.EndDisabledGroup();
            EndChange();
            StopBackgroundColor();
        }
    }
}
#endif