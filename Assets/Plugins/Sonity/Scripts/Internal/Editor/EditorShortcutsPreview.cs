// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace Sonity.Internal {

    public static class EditorShortcutsPreview {

        // Should be read and writeable
        private static EditorShortcutsPreviewRepaint previewRepaint = null;

        private static float playTime = 0f;
        private static bool playPressed = false;

        private static float stopTime = 0f;
        private static bool stopPressed = false;

        // There was about 0.004 difference in test
        private static readonly float timeAllowedDifference = 0.1f;

        // Needed for disabling domain reloading
#if UNITY_2019_2_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#else
        // Older versions than 2019.2 doesn't have the SubsystemRegistration load type
        [RuntimeInitializeOnLoadMethod]
#endif
        static void ResetStatics() {
            previewRepaint = null;
            playTime = 0f;
            playPressed = false;
            stopTime = 0f;
            stopPressed = false;
        }

        // Shortcut key modifiers
        // Needs "_" eg: _S for single key shortcut
        // % -> Ctrl on Windows, Linux, CMD on MacOS
        // ^ -> Ctrl on Windows, Linux, MacOS
        // # -> Shift
        // & -> Alt

        /// <summary>
        /// Previews the selected <see cref="SoundEventBase">SoundEvent</see> or <see cref="SoundContainerBase">SoundContainer</see>.
        /// </summary>
        [MenuItem("Tools/Sonity 🔊/Sound Preview - Play ^Q", false, 110)] // Sonity
        private static void SoundPreviewPlay() {
            playTime = SoundTimeScale.GetTimeEditor();
            playPressed = true;
            previewRepaint = ScriptableObject.CreateInstance<EditorShortcutsPreviewRepaint>();
            previewRepaint.DoRepaint();
        }

        /// <summary>
        /// Stops any playing preview of <see cref="SoundEventBase">SoundEvents</see> and <see cref="SoundContainerBase">SoundContainers</see>, press two times to skip fade out.
        /// </summary>
        [MenuItem("Tools/Sonity 🔊/Sound Preview - Stop ^W", false, 110)] // Sonity
        private static void SoundPreviewStop() {
            stopTime = SoundTimeScale.GetTimeEditor();
            stopPressed = true;
            previewRepaint = ScriptableObject.CreateInstance<EditorShortcutsPreviewRepaint>();
            previewRepaint.DoRepaint();
        }

        private class EditorShortcutsPreviewRepaint : Editor {
            // So that the inspectors of the objects will refresh
            public void DoRepaint() {
                Repaint();
            }
        }

        public static bool GetPlayIsPressed() {
            if (playPressed && playTime > 0f) {
                playPressed = false;
                if (SoundTimeScale.GetTimeEditor() + timeAllowedDifference >= playTime && SoundTimeScale.GetTimeEditor() - timeAllowedDifference <= playTime) {
                    playTime = 0f;
                    return true;
                }
            }
            return false;
        }

        public static bool GetStopIsPressed() {
            if (stopPressed && stopTime > 0f) {
                stopPressed = false;
                if (SoundTimeScale.GetTimeEditor() + timeAllowedDifference >= stopTime && SoundTimeScale.GetTimeEditor() - timeAllowedDifference <= stopTime) {
                    stopTime = 0f;
                    return true;
                }
            }
            return false;
        }
    }
}
#endif