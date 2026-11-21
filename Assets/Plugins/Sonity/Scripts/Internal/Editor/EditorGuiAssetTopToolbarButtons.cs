// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace Sonity.Internal {

    public static class EditorGuiAssetTopToolbarButtons {

        public static void BeginChange(SerializedObject serializedObject) {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
        }

        public static void EndChange(SerializedObject serializedObject) {
            if (EditorGUI.EndChangeCheck()) {
                serializedObject.ApplyModifiedProperties();
            }
        }

        public static void DrawTopButton(SerializedObject serializedObject) {
            EditorGUILayout.BeginHorizontal();
            BeginChange(serializedObject);
            if (GUILayout.Button(new GUIContent("Editor Tools", ""), GUILayout.Width(EditorGUIUtility.labelWidth))) {
                EditorToolsMenuDraw();
            }
            EndChange(serializedObject);
            BeginChange(serializedObject);
            if (GUILayout.Button(new GUIContent("Select Assets", ""))) {
                SelectAssetsMenuDraw();
            }
            EndChange(serializedObject);
            EditorGUILayout.EndHorizontal();
        }

        private enum EditorToolsMenuType {
            ReferenceFinder_FindReferencesForSelectedAssets,
            ReferenceFinder_OpenReferenceFinderWindow,
            SelectSameType_InSameFolder,
            SelectSameType_InSubfolders,
            SelectionHistory_Back,
            SelectionHistory_Forward,
            // Tools
            Tools_ReserializeAllSonityAssets,
            Tools_SelectAllSonityAssets,
            Tools_ReserializeSelectedAssets,
            Tools_FixObjectNamesSelectedAssets,
            Tools_FixObjectNamesAllAssetsInProject,
            // Legacy
            Legacy_ReserializeAllSonityAssetsForNewGuids,
            Legacy_AddScriptingDefineSymbolForLegacyFunctionsGlobalSoundTag,
            Legacy_AddScriptingDefineSymbolForLegacyFunctionsMusicAnd2D,
            Legacy_ConvertSoundEventModifierVolumeAndPolyphonyToBase,
            Legacy_ApplySoundContainerAudioMixerGroupToSoundEventIfNone,
        }

        private class EditorToolsMenuObject {
            public EditorToolsMenuType type;
            public EditorToolsMenuObject(EditorToolsMenuType type) {
                this.type = type;
            }
        }

        private static void EditorToolsMenuDraw() {
            GenericMenu menu = new GenericMenu();
            // Tooltips dont work for menu

            // Reference Finder
#if SONITY_ENABLE_EDITOR_TOOL_REFERENCE_FINDER
            menu.AddItem(new GUIContent("Reference Finder 🔍 - Find References for Selected Assets"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.ReferenceFinder_FindReferencesForSelectedAssets));
            menu.AddItem(new GUIContent("Reference Finder 🔍 - Open Reference Finder Window"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.ReferenceFinder_OpenReferenceFinderWindow));
#else
            menu.AddDisabledItem(new GUIContent("Reference Finder 🔍 (activate in SoundManager)"));
#endif
            menu.AddSeparator("");

            // Select Same Type
#if SONITY_ENABLE_EDITOR_TOOL_SELECT_SAME_TYPE
            menu.AddItem(new GUIContent("Select Same Type 🤏 - In Same Folder"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.SelectSameType_InSameFolder));
            menu.AddItem(new GUIContent("Select Same Type 🤏 - In Subfolders"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.SelectSameType_InSubfolders));
#else
            menu.AddDisabledItem(new GUIContent("Select Same Type 🤏 (activate in SoundManager)"));
#endif
            menu.AddSeparator("");

            // Selection History
#if SONITY_ENABLE_EDITOR_TOOL_SELECTION_HISTORY
            menu.AddItem(new GUIContent("Selection History 📜 - Back"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.SelectionHistory_Back));
            menu.AddItem(new GUIContent("Selection History 📜 - Forward"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.SelectionHistory_Forward));
#else
            menu.AddDisabledItem(new GUIContent("Selection History 📜 (activate in SoundManager)"));
#endif
            menu.AddSeparator("");

            // Tools
            menu.AddItem(new GUIContent("Tools/Reserialize - All Sonity Assets"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.Tools_ReserializeAllSonityAssets));
            menu.AddItem(new GUIContent("Tools/Reserialize - Selected Assets"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.Tools_ReserializeSelectedAssets));
            menu.AddSeparator("Tools/");
            menu.AddItem(new GUIContent("Tools/Fix Object Names - All Assets in Project"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.Tools_FixObjectNamesAllAssetsInProject));
            menu.AddItem(new GUIContent("Tools/Fix Object Names - Selected Assets"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.Tools_FixObjectNamesSelectedAssets));
            menu.AddSeparator("Tools/");
            menu.AddItem(new GUIContent("Tools/Select - All Sonity Assets"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.Tools_SelectAllSonityAssets));
            menu.AddSeparator("");

            // Legacy
            menu.AddItem(new GUIContent("Legacy/Sonity 1.2.0 Assets Upgrade: Reserialize All Sonity assets for new GUIDs"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.Legacy_ReserializeAllSonityAssetsForNewGuids));
            menu.AddItem(new GUIContent("Legacy/Sonity 1.2.0 Legacy Support: Add Scripting Define Symbol for Legacy Functions Global SoundTag"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.Legacy_AddScriptingDefineSymbolForLegacyFunctionsGlobalSoundTag));
            menu.AddSeparator("Legacy/");
            menu.AddItem(new GUIContent("Legacy/Sonity 1.1.1 Legacy Support: Add Scripting Define Symbol for Legacy Functions Music and 2D"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.Legacy_AddScriptingDefineSymbolForLegacyFunctionsMusicAnd2D));
            menu.AddSeparator("Legacy/");
            menu.AddItem(new GUIContent("Legacy/Sonity 1.0.6 SoundEvent Upgrade: Convert SoundEvent Modifier Volume and Polyphony to Base"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.Legacy_ConvertSoundEventModifierVolumeAndPolyphonyToBase));
            menu.AddItem(new GUIContent("Legacy/Sonity 1.0.6 SoundEvent Upgrade: Apply SoundContainer AudioMixerGroup to SoundEvent if None"), false, EditorToolsMenuCallback, new EditorToolsMenuObject(EditorToolsMenuType.Legacy_ApplySoundContainerAudioMixerGroupToSoundEventIfNone));
            menu.ShowAsContext();
        }

        private static void EditorToolsMenuCallback(object obj) {
            try {
                EditorToolsMenuObject menuObject = (EditorToolsMenuObject)obj;
                // If updating this, update the tooltip and documentation also
                if (menuObject == null) {
                    return;
                }
                switch (menuObject.type) {
#if SONITY_ENABLE_EDITOR_TOOL_REFERENCE_FINDER
                    case EditorToolsMenuType.ReferenceFinder_FindReferencesForSelectedAssets:
                        EditorTools.ReferenceFinderSelection.FindReferencesTools();
                        break;
                    case EditorToolsMenuType.ReferenceFinder_OpenReferenceFinderWindow:
                        EditorTools.ReferenceFinderUnusedAssets.LaunchUnusedAssetsWindowTools();
                        break;
#endif
#if SONITY_ENABLE_EDITOR_TOOL_SELECT_SAME_TYPE
                    case EditorToolsMenuType.SelectSameType_InSameFolder:
                        EditorTools.EditorToolSelectSameType.ToolsSelectObjectsInSameFolder();
                        break;
                    case EditorToolsMenuType.SelectSameType_InSubfolders:
                        EditorTools.EditorToolSelectSameType.ToolsSelectObjectsInSubFolders();
                        break;
#endif
#if SONITY_ENABLE_EDITOR_TOOL_SELECTION_HISTORY
                    case EditorToolsMenuType.SelectionHistory_Back:
                        EditorTools.EditorToolSelectionHistory.ToolsBack(); ;
                        break;
                    case EditorToolsMenuType.SelectionHistory_Forward:
                        EditorTools.EditorToolSelectionHistory.ToolsForward(); ;
                        break;
#endif
                    case EditorToolsMenuType.Tools_ReserializeAllSonityAssets:
                        EditorToolsBase.ReserializeAllSonityAssets();
                        break;
                    case EditorToolsMenuType.Tools_SelectAllSonityAssets:
                        EditorToolsBase.SelectAllSonityAssets();
                        break;
                    case EditorToolsMenuType.Tools_ReserializeSelectedAssets:
                        EditorToolsBase.ReserializeSelectedAssets();
                        break;
                    case EditorToolsMenuType.Tools_FixObjectNamesSelectedAssets:
                        EditorToolsBase.FixObjectNamesSelectedAssets();
                        break;
                    case EditorToolsMenuType.Tools_FixObjectNamesAllAssetsInProject:
                        EditorToolsBase.FixObjectNamesAllAssetsInProject();
                        break;
                    case EditorToolsMenuType.Legacy_ReserializeAllSonityAssetsForNewGuids:
                        EditorToolsLegacy.Sonity_1200_ReserializeAllSonityAssetsForNewGuids();
                        break;
                    case EditorToolsMenuType.Legacy_AddScriptingDefineSymbolForLegacyFunctionsGlobalSoundTag:
                        EditorToolsLegacy.Sonity_1200_AddScriptingDefineSymbolForLegacyFunctionsGlobalSoundTag();
                        break;
                    case EditorToolsMenuType.Legacy_AddScriptingDefineSymbolForLegacyFunctionsMusicAnd2D:
                        EditorToolsLegacy.Sonity_1110_AddScriptingDefineSymbolForLegacyFunctionsMusicAnd2D();
                        break;
                    case EditorToolsMenuType.Legacy_ConvertSoundEventModifierVolumeAndPolyphonyToBase:
                        EditorToolsLegacy.Sonity_1060_Upgrade_ConvertSoundEventVolumePolyphony();
                        break;
                    case EditorToolsMenuType.Legacy_ApplySoundContainerAudioMixerGroupToSoundEventIfNone:
                        EditorToolsLegacy.Sonity_1060_Upgrade_ApplySoundContainerAudioMixerGroupToSoundEvent();
                        break;
                }
            } catch {
                return;
            }
        }


        private enum SelectAssetsMenuType {
            SelectSoundManager,
            SelectAllSoundEvents,
            SelectAllSoundContainers,
            SelectAllSoundPolyGroups,
            SelectAllSoundVolumeGroups,
            SelectAllSoundPhysicsConditions,
            SelectAllSoundDataGroups,
            SelectAllSoundTags,
            SelectAllSoundMix,
            SelectAllSonityAssets,
        }

        private class SelectAssetsMenuObject {
            public SelectAssetsMenuType type;
            public SelectAssetsMenuObject(SelectAssetsMenuType type) {
                this.type = type;
            }
        }

        private static void SelectAssetsMenuDraw() {
            GenericMenu menu = new GenericMenu();
            // Tooltips dont work for menu
            if (SoundManagerBase.Instance == null) {
                menu.AddDisabledItem(new GUIContent("No SoundManager"), false);
            } else {
                menu.AddItem(new GUIContent("Select SoundManager (Default Shift+M)"), false, SelectAssetsMenuCallback, new SelectAssetsMenuObject(SelectAssetsMenuType.SelectSoundManager));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Select All SoundEvents"), false, SelectAssetsMenuCallback, new SelectAssetsMenuObject(SelectAssetsMenuType.SelectAllSoundEvents));
            menu.AddItem(new GUIContent("Select All SoundContainers"), false, SelectAssetsMenuCallback, new SelectAssetsMenuObject(SelectAssetsMenuType.SelectAllSoundContainers));
            menu.AddItem(new GUIContent("Select All SoundPolyGroups"), false, SelectAssetsMenuCallback, new SelectAssetsMenuObject(SelectAssetsMenuType.SelectAllSoundPolyGroups));
            menu.AddItem(new GUIContent("Select All SoundVolumeGroups"), false, SelectAssetsMenuCallback, new SelectAssetsMenuObject(SelectAssetsMenuType.SelectAllSoundVolumeGroups));
            menu.AddItem(new GUIContent("Select All SoundPhysicsConditions"), false, SelectAssetsMenuCallback, new SelectAssetsMenuObject(SelectAssetsMenuType.SelectAllSoundPhysicsConditions));
            menu.AddItem(new GUIContent("Select All SoundDataGroups"), false, SelectAssetsMenuCallback, new SelectAssetsMenuObject(SelectAssetsMenuType.SelectAllSoundDataGroups));
            menu.AddItem(new GUIContent("Select All SoundTags"), false, SelectAssetsMenuCallback, new SelectAssetsMenuObject(SelectAssetsMenuType.SelectAllSoundTags));
            menu.AddItem(new GUIContent("Select All SoundMix"), false, SelectAssetsMenuCallback, new SelectAssetsMenuObject(SelectAssetsMenuType.SelectAllSoundMix));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Select All Sonity Assets"), false, SelectAssetsMenuCallback, new SelectAssetsMenuObject(SelectAssetsMenuType.SelectAllSonityAssets));
            menu.ShowAsContext();
        }

        private static void SelectAssetsMenuCallback(object obj) {
            try {
                SelectAssetsMenuObject menuObject = (SelectAssetsMenuObject)obj;
                // If updating this, update the tooltip and documentation also
                if (menuObject == null) {
                    return;
                }
                switch (menuObject.type) {
                    case SelectAssetsMenuType.SelectSoundManager:
                        EditorToolsBase.SelectSoundManager();
                        break;
                    case SelectAssetsMenuType.SelectAllSoundEvents:
                        EditorToolsBase.SelectAllSoundEvents();
                        break;
                    case SelectAssetsMenuType.SelectAllSoundContainers:
                        EditorToolsBase.SelectAllSoundContainers();
                        break;
                    case SelectAssetsMenuType.SelectAllSoundPolyGroups:
                        EditorToolsBase.SelectAllSoundPolyGroups();
                        break;
                    case SelectAssetsMenuType.SelectAllSoundVolumeGroups:
                        EditorToolsBase.SelectAllSoundVolumeGroups();
                        break;
                    case SelectAssetsMenuType.SelectAllSoundPhysicsConditions:
                        EditorToolsBase.SelectAllSoundPhysicsConditions();
                        break;
                    case SelectAssetsMenuType.SelectAllSoundDataGroups:
                        EditorToolsBase.SelectAllSoundDataGroups();
                        break;
                    case SelectAssetsMenuType.SelectAllSoundTags:
                        EditorToolsBase.SelectAllSoundTags();
                        break;
                    case SelectAssetsMenuType.SelectAllSoundMix:
                        EditorToolsBase.SelectAllSoundMix();
                        break;
                    case SelectAssetsMenuType.SelectAllSonityAssets:
                        EditorToolsBase.SelectAllSonityAssets();
                        break;
                }
            } catch {
                return;
            }
        }
    }
}
#endif