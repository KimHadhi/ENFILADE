using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieAudioController))]
    [CanEditMultipleObjects]
    internal sealed class ZombieAudioControllerEditor : Editor
    {
        private static readonly Color AudioAccentColor = new Color32(0xD9, 0x86, 0x34, 0xFF);
        private const string LoopSourceObjectName = "LoopSource";
        private const string OneShotSourceObjectName = "OneShotSource";

        private SerializedProperty audioProfile;
        private SerializedProperty zombieAI;
        private SerializedProperty health;
        private SerializedProperty loopSource;
        private SerializedProperty oneShotSource;
        private SerializedProperty respectGamePause;
        private SerializedProperty stopAudioOnDisable;

        private void OnEnable()
        {
            audioProfile = serializedObject.FindProperty("audioProfile");
            zombieAI = serializedObject.FindProperty("zombieAI");
            health = serializedObject.FindProperty("health");
            loopSource = serializedObject.FindProperty("loopSource");
            oneShotSource = serializedObject.FindProperty("oneShotSource");
            respectGamePause = serializedObject.FindProperty("respectGamePause");
            stopAudioOnDisable = serializedObject.FindProperty("stopAudioOnDisable");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader(
                "Zombie Audio Controller",
                "State-driven zombie audio playback",
                AudioAccentColor);
            DrawStatusSection();
            DrawReferencesSection();
            DrawAudioSourcesSection();
            DrawRuntimeBehaviourSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStatusSection()
        {
            ZombieInspectorGUI.DrawSection("Status", () =>
            {
                if (targets.Length != 1)
                {
                    EditorGUILayout.HelpBox("Select one Zombie Audio Controller to inspect live audio status.", MessageType.Info);
                    return;
                }

                ZombieAudioController controller = target as ZombieAudioController;
                if (controller == null)
                {
                    return;
                }

                ZombieInspectorGUI.DrawStatusLine("Loop Source Playing", GetClipName(controller.CurrentLoopClip));
                ZombieInspectorGUI.DrawStatusLine("One Shot Source Playing", GetClipName(controller.CurrentOneShotClip));
            }, AudioAccentColor);
        }

        private void DrawReferencesSection()
        {
            ZombieInspectorGUI.DrawSection("References", () =>
            {
                ZombieInspectorGUI.DrawProperty(audioProfile);
                ZombieInspectorGUI.DrawProperty(zombieAI);
                ZombieInspectorGUI.DrawProperty(health);
                DrawRefreshReferencesButton();
                DrawReferenceWarnings();
            }, AudioAccentColor);
        }

        private void DrawAudioSourcesSection()
        {
            ZombieInspectorGUI.DrawSection("Audio Sources", () =>
            {
                ZombieInspectorGUI.DrawProperty(loopSource);
                ZombieInspectorGUI.DrawProperty(oneShotSource);
                DrawAudioSourceWarnings();
            }, AudioAccentColor);
        }

        private void DrawRuntimeBehaviourSection()
        {
            ZombieInspectorGUI.DrawSection("Runtime Behaviour", () =>
            {
                ZombieInspectorGUI.DrawProperty(respectGamePause);
                ZombieInspectorGUI.DrawProperty(stopAudioOnDisable);
            }, AudioAccentColor);
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
                if (targets[i] is ZombieAudioController controller)
                {
                    RefreshReferences(controller);
                }
            }
            serializedObject.Update();
        }

        private void DrawReferenceWarnings()
        {
            if (!audioProfile.hasMultipleDifferentValues && audioProfile.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No Zombie Audio Profile is assigned. The controller will not play zombie sounds without a profile.", MessageType.Warning);
            }

            if (!zombieAI.hasMultipleDifferentValues && zombieAI.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No Zombie AI is assigned. The controller will try to find one on the same GameObject at runtime, but it needs a Zombie AI to follow behaviour states.", MessageType.Warning);
            }

            if (!health.hasMultipleDifferentValues && health.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No Zombie Health is assigned. Hit and death sounds use health events; the controller will try to find a health reference at runtime.", MessageType.Info);
            }
        }

        private void DrawAudioSourceWarnings()
        {
            bool missingLoopSource = !loopSource.hasMultipleDifferentValues && loopSource.objectReferenceValue == null;
            bool missingOneShotSource = !oneShotSource.hasMultipleDifferentValues && oneShotSource.objectReferenceValue == null;

            if (missingLoopSource || missingOneShotSource)
            {
                EditorGUILayout.HelpBox("Missing audio source references will be assigned or created automatically at runtime.", MessageType.Info);
            }

            if (!loopSource.hasMultipleDifferentValues &&
                !oneShotSource.hasMultipleDifferentValues &&
                loopSource.objectReferenceValue != null &&
                loopSource.objectReferenceValue == oneShotSource.objectReferenceValue)
            {
                EditorGUILayout.HelpBox("Loop Source and One Shot Source should usually be separate AudioSources so loops and one-shots can be controlled independently.", MessageType.Warning);
            }
        }

        internal static void RefreshReferences(ZombieAudioController controller)
        {
            SerializedObject controllerObject = new SerializedObject(controller);
            bool undoRecorded = false;
            bool changed = false;

            SerializedProperty zombieAIProperty = controllerObject.FindProperty("zombieAI");
            SerializedProperty healthProperty = controllerObject.FindProperty("health");
            SerializedProperty loopSourceProperty = controllerObject.FindProperty("loopSource");
            SerializedProperty oneShotSourceProperty = controllerObject.FindProperty("oneShotSource");

            ZombieAI zombie = controller.GetComponent<ZombieAI>();
            changed |= AssignMissingReference(zombieAIProperty, zombie, controller, ref undoRecorded);

            ZombieHealth healthReference = controller.GetComponent<ZombieHealth>();
            if (healthReference == null && zombie != null)
            {
                healthReference = zombie.Health;
            }

            changed |= AssignMissingReference(healthProperty, healthReference, controller, ref undoRecorded);

            AudioSource[] audioSources = controller.GetComponents<AudioSource>();
            AudioSource loopCandidate = FindNamedChildAudioSource(controller.transform, LoopSourceObjectName);
            if (loopCandidate == null)
            {
                loopCandidate = GetFirstAudioSource(audioSources);
            }

            changed |= AssignMissingReference(loopSourceProperty, loopCandidate, controller, ref undoRecorded);

            AudioSource loopReference = loopSourceProperty.objectReferenceValue as AudioSource;
            AudioSource oneShotCandidate = FindNamedChildAudioSource(controller.transform, OneShotSourceObjectName);
            if (oneShotCandidate == null)
            {
                oneShotCandidate = GetDifferentAudioSource(audioSources, loopReference);
            }

            changed |= AssignMissingReference(oneShotSourceProperty, oneShotCandidate, controller, ref undoRecorded);

            if (!changed)
            {
                return;
            }

            controllerObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        }

        private static bool AssignMissingReference(
            SerializedProperty property,
            UnityEngine.Object reference,
            UnityEngine.Object undoTarget,
            ref bool undoRecorded)
        {
            if (property == null || property.objectReferenceValue != null || reference == null)
            {
                return false;
            }

            if (!undoRecorded)
            {
                Undo.RecordObject(undoTarget, "Refresh References");
                undoRecorded = true;
            }

            property.objectReferenceValue = reference;
            return true;
        }

        private static AudioSource GetFirstAudioSource(AudioSource[] audioSources)
        {
            return audioSources != null && audioSources.Length > 0 ? audioSources[0] : null;
        }

        private static AudioSource GetDifferentAudioSource(AudioSource[] audioSources, AudioSource sourceToSkip)
        {
            if (audioSources == null)
            {
                return null;
            }

            for (int i = 0; i < audioSources.Length; i++)
            {
                if (audioSources[i] != null && audioSources[i] != sourceToSkip)
                {
                    return audioSources[i];
                }
            }

            return null;
        }

        private static AudioSource FindNamedChildAudioSource(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            Transform child = parent.Find(childName);
            return child != null ? child.GetComponent<AudioSource>() : null;
        }

        private static string GetClipName(AudioClip clip)
        {
            return clip != null ? clip.name : "None";
        }
    }
}
