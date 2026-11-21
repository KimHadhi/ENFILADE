// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR
#if SONITY_ENABLE_EDITOR_TOOL_SELECT_SAME_TYPE

using System.Collections.Generic;
using UnityEditor;
using System.Linq;
using System.IO;

namespace Sonity.Internal.EditorTools {

    // Toolbar Actions
    // Tools/Sonity Tools 🛠/Select Same Type 🤏/In Same Folder
    // Tools/Sonity Tools 🛠/Select Same Type 🤏/In Subfolders
    // Assets/Sonity Tools 🛠/Select Same Type 🤏/In Same Folder
    // Assets/Sonity Tools 🛠/Select Same Type 🤏/In Subfolders

    // Default Shortcuts
    // Ctrl+Alt+A - Select Same Type 🤏/In Same Folder
    // Ctrl+Alt+Shift+A - Select Same Type 🤏/In Subfolders

    // Version 1.1
    // Added so that if multiple types are selected, then it will select all those multiple types

    // Version 1.0
    // First Release

    public static class EditorToolSelectSameType {

        // Shortcut key modifiers
		// Needs "_" eg: _S for single key shortcut
        // % -> Ctrl on Windows, Linux, CMD on MacOS
        // ^ -> Ctrl on Windows, Linux, MacOS
        // # -> Shift
        // & -> Alt

        const string toolsMenuPathInSameFolder = "Tools/Sonity Tools 🛠/Select Same Type 🤏/In Same Folder";
        const string toolsMenuPathInSubfolders = "Tools/Sonity Tools 🛠/Select Same Type 🤏/In Subfolders";
        const string assetsMenuPathInSameFolder = "Assets/Sonity Tools 🛠/Select Same Type 🤏/In Same Folder ^&A";
        const string assetsMenuPathInSubfolders = "Assets/Sonity Tools 🛠/Select Same Type 🤏/In Subfolders ^&#A";

        const int toolsMenuPriority = 101;
        const int assetsMenuPriority = 101;

        [MenuItem(assetsMenuPathInSameFolder, false, assetsMenuPriority)]
        public static void AssetsSelectObjectsInSameFolder() {
            SelectObjectsOfSameType(false);
        }

        [MenuItem(assetsMenuPathInSubfolders, false, assetsMenuPriority)]
        public static void AssetsSelectObjectsInSubFolders() {
            SelectObjectsOfSameType(true);
        }

        [MenuItem(toolsMenuPathInSameFolder, false, toolsMenuPriority)]
        public static void ToolsSelectObjectsInSameFolder() {
            SelectObjectsOfSameType(false);
        }

        [MenuItem(toolsMenuPathInSubfolders, false, toolsMenuPriority)]
        public static void ToolsSelectObjectsInSubFolders() {
            SelectObjectsOfSameType(true);
        }

        private static void SelectObjectsOfSameType(bool subFolders) {

            AssetDatabase.SaveAssets();

            if (Selection.objects.Length > 0) {

                // Select multiple types of objects
                List<UnityEngine.Object> selectedObjects = Selection.objects.ToList();

                // Finding all guids of right type
                List<string> typeNames = new List<string>();

                for (int i = 0; i < selectedObjects.Count; i++) {
                    string name = selectedObjects[i].GetType().Name;
                    // Only have 1 instance of each type
                    if (!typeNames.Contains(name)) {
                        typeNames.Add(name);
                    }
                }

                // Select assets in the folder of the first selected asset
                string selectedPath = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);

                // Remove Filename from Path
                selectedPath = selectedPath.Replace(Path.GetFileName(selectedPath), "");

                List<string> foundGuids = new List<string>();

                // Add all matching types
                for (int i = 0; i < typeNames.Count; i++) {
                    foundGuids.AddRange(AssetDatabase.FindAssets($"t:{typeNames[i]}", new[] { selectedPath }).ToList<string>());
                }

                // Removing files in subfolders
                if (!subFolders) {
                    for (int i = foundGuids.Count - 1; i >= 0; i--) {
                        // Getting path
                        string tempString = AssetDatabase.GUIDToAssetPath(foundGuids[i]);
                        // Removing parent path
                        tempString = tempString.Replace(selectedPath, "");
                        // If contains subpath
                        if (tempString.Contains("/")){
                            // Remove index with subpath
                            foundGuids.RemoveAt(i);
                        }
                    }
                }

                List<UnityEngine.Object> foundObjects = new List<UnityEngine.Object>();

                // Load found objects
                for (int i = 0; i < foundGuids.Count; i++) {
                    UnityEngine.Object tempObject = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(foundGuids[i]), typeof(UnityEngine.Object));
                    if (tempObject != null) {
                        if (!foundObjects.Contains(tempObject)) {
                            foundObjects.Add(tempObject);
                        }
                    }
                }

                // Set selection to found objects
                Selection.objects = foundObjects.ToArray();

                AssetDatabase.SaveAssets();
            }
        }
    }
}
#endif
#endif