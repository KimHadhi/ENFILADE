// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sonity.Internal {

    public static class EditorToolsBase {

        // Shortcut key modifiers
        // Needs "_" eg: _S for single key shortcut
        // % -> Ctrl on Windows, Linux, CMD on MacOS
        // ^ -> Ctrl on Windows, Linux, MacOS
        // # -> Shift
        // & -> Alt

        // Big jump in index triggers a divider
        [MenuItem("Tools/Sonity 🔊/Select/Select - SoundManager #M", false, 500)]
        public static void SelectSoundManager() {
            if (SoundManagerBase.Instance != null) {
                UnityEngine.Object[] unityObjects = new UnityEngine.Object[1];
                unityObjects[0] = SoundManagerBase.Instance.gameObject;
                Selection.objects = unityObjects;
            }
        }

        // Big jump in index triggers a divider
        [MenuItem("Tools/Sonity 🔊/Select/Select - All SoundEvents", false, 600)]
        public static void SelectAllSoundEvents() {
            List<string> paths = new List<string>();
            FindAssetPathsOfType<SoundEventBase>(paths);
            // Select the found assets
            Selection.objects = paths.Select(path => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)).Where(asset => asset != null).ToArray();
            Debug.Log($"Sonity: Selected {paths.Count} SoundEvents");
        }

        [MenuItem("Tools/Sonity 🔊/Select/Select - All SoundContainers", false, 601)]
        public static void SelectAllSoundContainers() {
            List<string> paths = new List<string>();
            FindAssetPathsOfType<SoundContainerBase>(paths);
            // Select the found assets
            Selection.objects = paths
                .Select(path => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path))
                .Where(asset => asset != null)
                .ToArray();
            Debug.Log($"Sonity: Selected {paths.Count} SoundContainers");
        }

        [MenuItem("Tools/Sonity 🔊/Select/Select - All SoundPolyGroups", false, 602)]
        public static void SelectAllSoundPolyGroups() {
            List<string> paths = new List<string>();
            FindAssetPathsOfType<SoundPolyGroupBase>(paths);
            // Select the found assets
            Selection.objects = paths.Select(path => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)).Where(asset => asset != null).ToArray();
            Debug.Log($"Sonity: Selected {paths.Count} SoundPolyGroups");
        }

        [MenuItem("Tools/Sonity 🔊/Select/Select - All SoundVolumeGroups", false, 603)]
        public static void SelectAllSoundVolumeGroups() {
            List<string> paths = new List<string>();
            FindAssetPathsOfType<SoundVolumeGroupBase>(paths);
            // Select the found assets
            Selection.objects = paths.Select(path => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)).Where(asset => asset != null).ToArray();
            Debug.Log($"Sonity: Selected {paths.Count} SoundVolumeGroups");
        }

        [MenuItem("Tools/Sonity 🔊/Select/Select - All SoundPhysicsConditions", false, 604)]
        public static void SelectAllSoundPhysicsConditions() {
            List<string> paths = new List<string>();
            FindAssetPathsOfType<SoundPhysicsConditionBase>(paths);
            // Select the found assets
            Selection.objects = paths.Select(path => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)).Where(asset => asset != null).ToArray();
            Debug.Log($"Sonity: Selected {paths.Count} SoundPhysicsConditions");
        }

        [MenuItem("Tools/Sonity 🔊/Select/Select - All SoundDataGroups", false, 605)]
        public static void SelectAllSoundDataGroups() {
            List<string> paths = new List<string>();
            FindAssetPathsOfType<SoundDataGroupBase>(paths);
            // Select the found assets
            Selection.objects = paths.Select(path => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)).Where(asset => asset != null).ToArray();
            Debug.Log($"Sonity: Selected {paths.Count} SoundDataGroups");
        }

        [MenuItem("Tools/Sonity 🔊/Select/Select - All SoundTags", false, 606)]
        public static void SelectAllSoundTags() {
            List<string> paths = new List<string>();
            FindAssetPathsOfType<SoundTagBase>(paths);
            // Select the found assets
            Selection.objects = paths.Select(path => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)).Where(asset => asset != null).ToArray();
            Debug.Log($"Sonity: Selected {paths.Count} SoundTags");
        }

        [MenuItem("Tools/Sonity 🔊/Select/Select - All SoundMix", false, 607)]
        public static void SelectAllSoundMix() {
            List<string> paths = new List<string>();
            FindAssetPathsOfType<SoundMixBase>(paths);
            // Select the found assets
            Selection.objects = paths.Select(path => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)).Where(asset => asset != null).ToArray();
            Debug.Log($"Sonity: Selected {paths.Count} SoundMixes");
        }

        [MenuItem("Tools/Sonity 🔊/Select/Select - All Sonity Assets", false, 700)]
        public static void SelectAllSonityAssets() {

            List<string> paths = new List<string>();

            FindAllSonityAssetPaths(paths);

            // Select the found assets
            Selection.objects = paths.Select(path => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)).Where(asset => asset != null).ToArray();

            Debug.Log($"Sonity: Selected {paths.Count} Sonity Assets");
        }

        // Big jump in index triggers a divider
        [MenuItem("Tools/Sonity 🔊/Tools/Reserialize - All Sonity Assets", false, 800)]
        public static void ReserializeAllSonityAssets() {

            List<string> paths = new List<string>();

            FindAllSonityAssetPaths(paths);

            // To force initialization on reserialization
            foreach (string path in paths) {
                SonityAssetPostprocessorImporter.InitializeAsset(path);
            }

            AssetDatabase.ForceReserializeAssets(paths);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Sonity: Reserialized {paths.Count} Sonity Assets");
        }

        [MenuItem("Tools/Sonity 🔊/Tools/Reserialize - Selected Assets", false, 801)]
        public static void ReserializeSelectedAssets() {

            List<string> paths = new List<string>();

            // Convert selected objects to path
            foreach (UnityEngine.Object unityEngineObject in Selection.objects) {
                string path = AssetDatabase.GetAssetPath(unityEngineObject);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) {
                    paths.Add(path);
                }
            }

            // To force initialization on reserialization
            foreach (string path in paths) {
                SonityAssetPostprocessorImporter.InitializeAsset(path);
            }

            AssetDatabase.ForceReserializeAssets(paths);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Sonity: Reserialized Selected Assets");
        }

        private static void FindAllSonityAssetPaths(List<string> pathsAdd) {
            FindAssetPathsOfType<SoundContainerBase>(pathsAdd);
            FindAssetPathsOfType<SoundEventBase>(pathsAdd);
            FindAssetPathsOfType<SoundPolyGroupBase>(pathsAdd);
            FindAssetPathsOfType<SoundDataGroupBase>(pathsAdd);
            FindAssetPathsOfType<SoundMixBase>(pathsAdd);
            FindAssetPathsOfType<SoundVolumeGroupBase>(pathsAdd);
            FindAssetPathsOfType<SoundTagBase>(pathsAdd);
            FindAssetPathsOfType<SoundPhysicsConditionBase>(pathsAdd);
            FindAssetPathsOfType<SoundPresetBase>(pathsAdd);
            // So no refs are needed atm
            FindAssetPathsWithName("SoundAreaBase", pathsAdd);
            FindAssetPathsWithName("SoundAreaTagBase", pathsAdd);
            FindAssetPathsWithName("SoundReverbBase", pathsAdd);
        }

        // Returns the path matching the name into the string list
        private static void FindAssetPathsWithName(string scriptableObjectName, List<string> addPaths) {
            string[] guids = AssetDatabase.FindAssets($"t:{scriptableObjectName}");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) != null) {
                    addPaths.Add(path);
                }
            }
        }

        // Returns the path matching the type into the string list
        private static void FindAssetPathsOfType<T>(List<string> addPaths) where T : ScriptableObject {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath<T>(path) != null) {
                    addPaths.Add(path);
                }
            }
        }

        [MenuItem("Tools/Sonity 🔊/Tools/Fix Object Names - All Assets in Project", false, 802)]
        public static void FixObjectNamesAllAssetsInProject() {
            string dialogTitle = "Editor Tool - Fix Object Names";
            // Prompt the user to do a dry run or the real thing. A dry run
            // allows the user to verify nothing unwanted is affected.
            int dialogResult =
                EditorUtility.DisplayDialogComplex(
                    dialogTitle,
                    message: "Find and fix assets with incorrect main object names?\n\n" +
                    "This loads every asset in the project and can be very slow in large projects.",
                    ok: "Dry Run",
                    cancel: "Cancel",
                    alt: "Fix All"
                );

            const int DialogResultOK = 0;
            const int DialogResultCancel = 1;

            if (dialogResult == DialogResultCancel) {
                return;
            }

            bool isDryRun = dialogResult == DialogResultOK;
            if (isDryRun) {
                dialogTitle += " (Dry Run)";
            }

            // Find all assets
            string[] allGuidsInProject = AssetDatabase.FindAssets("");

            // List of types that don't complain in the inspector when their
            // names are mismatched.
            System.Type[] assetTypeBlacklist = new[]
            {
            typeof(Shader), // Shaders appear with mismatched names regularly. Even in official packages.
            typeof(DefaultAsset),   // Folders with periods in them will have different names. This is OK.
        };

            bool didCancel = false;

            try {
                int assetCount = allGuidsInProject.Length;
                Debug.Log($"Fix Object Names: Starting scan over {assetCount} assets...");
                for (int i = 0; i < assetCount; i++) {
                    string assetGuid = allGuidsInProject[i];
                    string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);

                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (asset == null) {
                        continue;
                    }
                    string mainObjectName = asset.name;
                    string expectedMainObjectName = Path.GetFileNameWithoutExtension(assetPath);

                    didCancel = EditorUtility.DisplayCancelableProgressBar(
                             $"{dialogTitle} - {i + 1} of {assetCount}",
                            info: assetPath,
                            progress: (float)(i + 1) / assetCount
                        );

                    if (didCancel) {
                        // The user requested an early exit
                        break;
                    }

                    int blacklistedTypeIndex = System.Array.IndexOf(assetTypeBlacklist, asset.GetType());

                    if (blacklistedTypeIndex >= 0) {
                        // Skip assets of blacklisted types. See array definition
                        // above.
                        continue;
                    }

                    if (mainObjectName == expectedMainObjectName) {
                        // Asset is already correct
                        continue;
                    }

                    UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                    if (packageInfo != null &&
                        packageInfo.source != PackageSource.Embedded &&
                        packageInfo.source != PackageSource.Local) {
                        // Asset is read-only
                        continue;
                    }

                    if (isDryRun) {
                        Debug.Log($"Sonity: Fix Object Names - Would Fix: {assetPath}", asset);
                    } else {
                        Undo.RecordObject(asset, dialogTitle);
                        using SerializedObject serializedAsset = new SerializedObject(asset);
                        asset.name = expectedMainObjectName;
                        Debug.Log($"Sonity: Fix Object Names - Fixed: {assetPath}", asset);
                    }
                }
            } finally {
                EditorUtility.ClearProgressBar();
                if (didCancel) {
                    Debug.Log("Sonity: Fix Object Names - Canceled");
                } else {
                    Debug.Log("Sonity: Fix Object Names - Finished");
                }
            }
        }

        /// <summary>
        /// Sometimes renaming assets outside of Unity makes the name not update properly.
        /// It will complain "The main object name X should match the asset filename X".
        /// Instead of having to press the "Fix object name" button manually per item,
        /// this allows you to find and fix all the object names for multiple objects instead.
        /// </summary>
        [MenuItem("Tools/Sonity 🔊/Tools/Fix Object Names - Selected Assets", false, 803)]
        public static void FixObjectNamesSelectedAssets() {

            AssetDatabase.SaveAssets();

            for (int i = 0; i < Selection.objects.Length; i++) {

                UnityEngine.Object asset = Selection.objects[i];

                string currentObjectName = asset.name;
                string assetPath = FileUtil.GetLogicalPath(AssetDatabase.GetAssetPath(asset));
                var expectedObjectName = Path.GetFileNameWithoutExtension(assetPath);

                if (currentObjectName != expectedObjectName) {
                    asset.name = expectedObjectName;
                    AssetDatabase.SaveAssetIfDirty(asset);
                    Debug.Log($"Sonity: Fix Object Names - Changed name from \"{currentObjectName}\" to \"{expectedObjectName}\"", asset);
                }
            }
        }
    }
}
#endif