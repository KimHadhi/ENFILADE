// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace Sonity.Internal {

    public static class EditorScriptingDefineSymbols {

        // Built In Functions ///////////////////////////////////////////////////////////////
        private static readonly string audioListenerVolumeIncreaseDefineSymbol = "SONITY_ENABLE_VOLUME_INCREASE";
        private static readonly string addressableAudioMixerDefineSymbol = "SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER";
        private static readonly string soundEventLibraryDefineSymbol = "SONITY_ENABLE_SOUNDEVENT_LIBRARY";
        private static readonly string entitySoundBaseDefineSymbol = "SONITY_ENABLE_ENTITY_SOUND_BASE";
        private static readonly string entitySoundLibraryDefineSymbol = "SONITY_ENABLE_ENTITY_SOUND_LIBRARY";
        private static readonly string disableVoiceEffectsDefineSymbol = "SONITY_DISABLE_VOICE_EFFECTS";
        private static readonly string soundManagerManualUpdateDefineSymbol = "SONITY_ENABLE_SOUNDMANAGER_MANUAL_UPDATE";

        // Integrations ///////////////////////////////////////////////////////////////
        private static readonly string integrationSteamAudioDefineSymbol = "SONITY_ENABLE_INTEGRATION_STEAM_AUDIO";
        private static readonly string integrationPlayMakerDefineSymbol = "SONITY_ENABLE_INTEGRATION_PLAYMAKER";
        //private static readonly string integrationMultiplayerSoundDefineSymbol = "SONITY_ENABLE_INTEGRATION_MULTIPLAYER";

        // Editor Tools ///////////////////////////////////////////////////////////////
        private static readonly string editorToolSelectionHistoryDefineSymbol = "SONITY_ENABLE_EDITOR_TOOL_SELECTION_HISTORY";
        private static readonly string editorToolReferenceFinderDefineSymbol = "SONITY_ENABLE_EDITOR_TOOL_REFERENCE_FINDER";
        private static readonly string editorToolSelectSameTypeDefineSymbol = "SONITY_ENABLE_EDITOR_TOOL_SELECT_SAME_TYPE";

        // Legacy ///////////////////////////////////////////////////////////////
        private static readonly string legacyFunctionsMusicAnd2DDefineSymbol = "SONITY_ENABLE_LEGACY_FUNCTIONS_MUSIC_AND_2D";
        private static readonly string legacyFunctionsGlobalSoundTagDefineSymbol = "SONITY_ENABLE_LEGACY_FUNCTIONS_GLOBAL_SOUNDTAG";

        // Built In Functions ///////////////////////////////////////////////////////////////
        // Volume Increase
        public static bool AudioListenerVolumeIncreaseExists() {
            return DefineSymbolExists(audioListenerVolumeIncreaseDefineSymbol);
        }
        public static void AudioListenerVolumeIncreaseAddRemove(bool shouldExist) {
            DefineSymbolAddRemove(audioListenerVolumeIncreaseDefineSymbol, shouldExist);
        }
        // Addressable AudioMixer
        public static bool AddressableAudioMixerExists() {
            return DefineSymbolExists(addressableAudioMixerDefineSymbol);
        }
        public static void AddressableAudioMixerAddRemove(bool shouldExist) {
            DefineSymbolAddRemove(addressableAudioMixerDefineSymbol, shouldExist);
        }
        // SoundEvent Library
        public static bool SoundEventLibraryExists() {
            return DefineSymbolExists(soundEventLibraryDefineSymbol);
        }
        public static void SoundEventLibraryShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(soundEventLibraryDefineSymbol, shouldExist);
        }
        // EntitySound Base
        public static bool EntitySoundBaseExists() {
            return DefineSymbolExists(entitySoundBaseDefineSymbol);
        }
        public static void EntitySoundBaseShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(entitySoundBaseDefineSymbol, shouldExist);
        }
        // EntitySound Library
        public static bool EntitySoundLibraryExists() {
            return DefineSymbolExists(entitySoundLibraryDefineSymbol);
        }
        public static void EntitySoundLibraryShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(entitySoundLibraryDefineSymbol, shouldExist);
        }
        // Disable Voice Effects
        public static bool DisableVoiceEffectsExists() {
            return DefineSymbolExists(disableVoiceEffectsDefineSymbol);
        }
        public static void DisableVoiceEffectsShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(disableVoiceEffectsDefineSymbol, shouldExist);
        }
        // SoundManager Manual Update
        public static bool SoundManagerManualUpdateExists() {
            return DefineSymbolExists(soundManagerManualUpdateDefineSymbol);
        }
        public static void SoundManagerManualUpdateShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(soundManagerManualUpdateDefineSymbol, shouldExist);
        }
        // Integrations ///////////////////////////////////////////////////////////////

        // Steam Audio
        public static bool IntegrationSteamAudioExists() {
            return DefineSymbolExists(integrationSteamAudioDefineSymbol);
        }
        public static void IntegrationSteamAudioShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(integrationSteamAudioDefineSymbol, shouldExist);
        }
        // PlayMaker
        public static bool IntegrationPlayMakerExists() {
            return DefineSymbolExists(integrationPlayMakerDefineSymbol);
        }
        public static void IntegrationPlayMakerShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(integrationPlayMakerDefineSymbol, shouldExist);
        }

        // Editor Tools ///////////////////////////////////////////////////////////////
        // Editor Tool Selection History
        public static bool EditorToolSelectionHistoryExists() {
            return DefineSymbolExists(editorToolSelectionHistoryDefineSymbol);
        }
        public static void EditorToolSelectionHistoryShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(editorToolSelectionHistoryDefineSymbol, shouldExist);
        }
        // Editor Tool Reference Finder
        public static bool EditorToolReferenceFinderExists() {
            return DefineSymbolExists(editorToolReferenceFinderDefineSymbol);
        }
        public static void EditorToolReferenceFinderShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(editorToolReferenceFinderDefineSymbol, shouldExist);
        }
        // Editor Tool Select Same Type
        public static bool EditorToolSelectSameTypeExists() {
            return DefineSymbolExists(editorToolSelectSameTypeDefineSymbol);
        }
        public static void EditorToolSelectSameTypeShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(editorToolSelectSameTypeDefineSymbol, shouldExist);
        }

        // Legacy ///////////////////////////////////////////////////////////////
        public static void LegacyFunctionsMusicAnd2DShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(legacyFunctionsMusicAnd2DDefineSymbol, shouldExist);
        }

        public static void LegacyFunctionsGlobalSoundTagShouldExist(bool shouldExist) {
            DefineSymbolAddRemove(legacyFunctionsGlobalSoundTagDefineSymbol, shouldExist);
        }

        // Checks if the Define Symbol Exists
        private static bool DefineSymbolExists(string defineSymbol) {
#if UNITY_2021_2_OR_NEWER
            string definesString = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup));
#else
            string definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
#endif
            List<string> allDefines = definesString.Split(';').ToList();
            return allDefines.Contains(defineSymbol);
        }

        // Adds or removes the given define symbols to PlayerSettings define symbols
        private static void DefineSymbolAddRemove(string defineSymbol, bool shouldExist) {
#if UNITY_2021_2_OR_NEWER
            string definesString = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup));
#else
            string definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
#endif
            List<string> allDefines = definesString.Split(';').ToList();
            if (shouldExist) {
                // Adds a new define if it doesnt already exist
                if (!allDefines.Contains(defineSymbol)) {
                    allDefines.Add(defineSymbol);
                    Debug.Log($"Sonity.{NameOf.SoundManager}: Added scripting define symbol \"{defineSymbol}\"");
                }
            } else {
                // Remove the define if it exists
                for (int i = allDefines.Count - 1; i >= 0; i--) {
                    if (allDefines[i] == defineSymbol) {
                        allDefines.RemoveAt(i);
                        Debug.Log($"Sonity.{NameOf.SoundManager}: Removed scripting define symbol \"{defineSymbol}\"");
                    }
                }
            }

            // Merges and adds the defines
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), string.Join(";", allDefines.ToArray()));
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, string.Join(";", allDefines.ToArray()));
#endif
        }
    }
}
#endif