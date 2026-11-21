// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

namespace Sonity.Internal {

    public static class EditorToolsLegacy {

        [MenuItem("Tools/Sonity 🔊/Legacy/Sonity 1.2.0 Assets Upgrade: Reserialize All Sonity assets for new GUIDs", false, 1100)] // Sonity
        public static void Sonity_1200_ReserializeAllSonityAssetsForNewGuids() {
            EditorToolsBase.ReserializeAllSonityAssets();
        }


        [MenuItem("Tools/Sonity 🔊/Legacy/Sonity 1.2.0 Legacy Support: Add Scripting Define Symbol for Legacy Functions Global SoundTag", false, 1110)] // Sonity
        public static void Sonity_1200_AddScriptingDefineSymbolForLegacyFunctionsGlobalSoundTag() {
            EditorScriptingDefineSymbols.LegacyFunctionsGlobalSoundTagShouldExist(true);
        }

        [MenuItem("Tools/Sonity 🔊/Legacy/Sonity 1.1.1 Legacy Support: Add Scripting Define Symbol for Legacy Functions Music and 2D", false, 1120)] // Sonity
        public static void Sonity_1110_AddScriptingDefineSymbolForLegacyFunctionsMusicAnd2D() {
            EditorScriptingDefineSymbols.LegacyFunctionsMusicAnd2DShouldExist(true);
        }

        [MenuItem("Tools/Sonity 🔊/Legacy/Sonity 1.0.6 SoundEvent Upgrade: Convert SoundEvent Modifier Volume and Polyphony to Base", false, 1130)] // Sonity
        public static void Sonity_1060_Upgrade_ConvertSoundEventVolumePolyphony() {
            foreach (UnityEngine.Object objectInstance in Selection.objects) {
                SoundEventBase soundEvent = null;
                try {
                    soundEvent = (SoundEventBase)objectInstance;
                } catch {

                }
                if (soundEvent != null) {
                    bool changeAnything = false;
                    if (soundEvent.internals.data.soundEventModifier.volumeUse) {
                        changeAnything = true;
                        soundEvent.internals.data.soundEventModifier.volumeUse = false;
                        soundEvent.internals.data.volumeRatio *= soundEvent.internals.data.soundEventModifier.volumeRatio;
                        soundEvent.internals.data.volumeDecibel = VolumeScale.ConvertRatioToDecibel(soundEvent.internals.data.volumeRatio);
                        soundEvent.internals.data.soundEventModifier.volumeRatio = 1f;
                        soundEvent.internals.data.soundEventModifier.volumeDecibel = 0f;
                    }
                    if (soundEvent.internals.data.soundEventModifier.polyphonyUse) {
                        changeAnything = true;
                        soundEvent.internals.data.soundEventModifier.polyphonyUse = false;
                        soundEvent.internals.data.polyphony *= soundEvent.internals.data.soundEventModifier.polyphony;
                        soundEvent.internals.data.soundEventModifier.polyphony = 1;
                    }
                    if (changeAnything) {
                        Debug.Log($"Sonity.SoundEvent: \"{soundEvent.internals.cachedName}\" converted SoundEvent modifier volume and polyphony to base");
                    }
                    EditorUtility.SetDirty(objectInstance);
                }
            }
        }

        [MenuItem("Tools/Sonity 🔊/Legacy/Sonity 1.0.6 SoundEvent Upgrade: Apply SoundContainer AudioMixerGroup to SoundEvent if None", false, 1140)] // Sonity
        public static void Sonity_1060_Upgrade_ApplySoundContainerAudioMixerGroupToSoundEvent() {
            foreach (UnityEngine.Object objectInstance in Selection.objects) {
                SoundEventBase soundEvent = null;
                try {
                    soundEvent = (SoundEventBase)objectInstance;
                } catch {

                }
                if (soundEvent != null) {
                    // Only take if SoundEvent AudioMixerGroup is null
                    if (soundEvent.internals.data.audioMixerGroup == null) {
                        if (soundEvent.internals.soundContainers.Length > 0) {
                            if (soundEvent.internals.soundContainers[0] != null) {
                                if (soundEvent.internals.soundContainers[0].internals.data.audioMixerGroup != null) {
                                    soundEvent.internals.data.audioMixerGroup = soundEvent.internals.soundContainers[0].internals.data.audioMixerGroup;
                                    Debug.Log($"Sonity.SoundEvent: \"{soundEvent.internals.cachedName}\" took AudioMixerGroup " +
                                        $"\"{soundEvent.internals.soundContainers[0].internals.data.audioMixerGroup.name}\" from \"{soundEvent.internals.soundContainers[0].internals.cachedName}\"");
                                }
                            }
                        }
                    }
                    EditorUtility.SetDirty(objectInstance);
                }
            }
        }
    }
}
#endif