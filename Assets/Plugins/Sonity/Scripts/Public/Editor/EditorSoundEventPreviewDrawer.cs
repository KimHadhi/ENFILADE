// SoundEvent Preview Drawer by Dangledorf
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

namespace Sonity.Internal {

    [CustomPropertyDrawer(typeof(SoundEvent))]
    internal class EditorSoundEventPreviewDrawer : PropertyDrawer {

        private const float kButtonWidth = 24f;
        private const float kSpacing = 2f;

        private static readonly GUIContent guiContentPlay = new GUIContent("▶", "Preview this SoundEvent");
        private static readonly GUIContent guiContentStop = new GUIContent("■", "Stop the preview");

        private static SoundEventBase soundEventCurrentlyPlaying;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {

            label = EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = position;
            fieldRect.width -= kButtonWidth + kSpacing;

            Rect buttonRect = new Rect(fieldRect.xMax + kSpacing, position.y, kButtonWidth, position.height);

            // ObjectField (not PropertyField) so we don't recurse back into this drawer.
            EditorGUI.ObjectField(fieldRect, property, label);

            SoundEventBase soundEvent = property.objectReferenceValue as SoundEventBase;

            if (!EditorPreviewSound.IsUpdating()) {
                soundEventCurrentlyPlaying = null;
            }

            bool isThisPlaying = soundEvent != null && soundEventCurrentlyPlaying == soundEvent;

            using (new EditorGUI.DisabledScope(soundEvent == null)) {
                if (GUI.Button(buttonRect, isThisPlaying ? guiContentStop : guiContentPlay)) {
                    if (isThisPlaying) {
                        // Stop the voice and allow Trigger On Stop and FadeOuts to play
                        EditorPreviewSound.Stop(true, true);
                        soundEventCurrentlyPlaying = null;
                    } else {
                        PlayPreview(soundEvent);
                        soundEventCurrentlyPlaying = soundEvent;
                    }
                }
            }

            // Keep the play/stop icon in sync once the preview finishes on its own.
            if (soundEventCurrentlyPlaying != null && Event.current.type == EventType.Repaint) {
                EditorWindow.focusedWindow?.Repaint();
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return EditorGUIUtility.singleLineHeight;
        }

        // Build the voices with AddEditorPreviewSoundData and then plays them
        private static void PlayPreview(SoundEventBase soundEvent) {
            EditorPreviewSound.Stop(false, false);
            soundEvent.internals.InitializeTimelineSoundContainerSetting();
            for (int i = 0; i < soundEvent.internals.soundContainers.Length; i++) {
                SoundContainerBase soundContainer = soundEvent.internals.soundContainers[i];
                if (soundContainer == null) {
                    continue;
                }
                EditorPreviewSoundData setting = new EditorPreviewSoundData();
                setting.soundEvent = soundEvent;
                setting.soundContainer = soundContainer;
                setting.soundEventModifierList.Add(soundEvent.internals.data.soundEventModifier);
                SoundMixBase soundMix = soundEvent.internals.data.soundMix;
                while (soundMix != null && !soundMix.internals.CheckIsInfiniteLoop(soundMix, true)) {
                    setting.soundEventModifierList.Add(soundMix.internals.soundEventModifier);
                    soundMix = soundMix.internals.soundMixParent;
                }
                setting.timelineSoundContainerData = soundEvent.internals.data.timelineSoundContainerData[i];
                EditorPreviewSound.AddEditorPreviewSoundData(setting);
            }
            EditorPreviewSound.SetEditorSetting(new EditorPreviewControls());
            EditorPreviewSound.PlaySoundEvent(soundEvent);
        }
    }
}
#endif