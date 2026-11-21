// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

namespace Sonity.Internal {

    public class SonityAssetPostprocessorImporter : AssetPostprocessor {

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
            foreach (string path in importedAssets) {
                InitializeAsset(path);
            }
            foreach (string path in movedAssets) {
                InitializeAsset(path);
            }
        }

        public static void InitializeAsset(string assetPath) {

            // Ignore non-asset files
            if (!assetPath.EndsWith(".asset")) {
                return;
            }

            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);

            if (asset == null) {
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath, AssetPathToGUIDOptions.OnlyExistingAssets);

            if (string.IsNullOrEmpty(guid)) {
                return;
            }

            switch (asset) {
                case SoundContainerBase sonityObject:
                    sonityObject.internals.InitializeObject(sonityObject);
                    break;
                case SoundEventBase sonityObject:
                    sonityObject.internals.InitializeObject(sonityObject);
                    break;
                case SoundPolyGroupBase sonityObject:
                    sonityObject.internals.InitializeObject(sonityObject);
                    break;
                case SoundDataGroupBase sonityObject:
                    sonityObject.internals.InitializeObject(sonityObject);
                    break;
                case SoundMixBase sonityObject:
                    sonityObject.internals.InitializeObject(sonityObject);
                    break;
                case SoundVolumeGroupBase sonityObject:
                    sonityObject.internals.InitializeObject(sonityObject);
                    break;
                case SoundTagBase sonityObject:
                    sonityObject.internals.InitializeObject(sonityObject);
                    break;
                case SoundPhysicsConditionBase sonityObject:
                    sonityObject.internals.InitializeObject(sonityObject);
                    break;

                // SoundPreset is Editor only, GUID and cached name not needed

                // SoundAreaBase
                // SoundAreaTagBase
                // SoundReverbBase
            }
        }
    }
}
#endif