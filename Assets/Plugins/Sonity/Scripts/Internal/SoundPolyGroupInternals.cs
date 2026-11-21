// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System;
using UnityEngine;

namespace Sonity.Internal {

    [Serializable]
    public class SoundPolyGroupInternals {

        public string cachedName;
        public SonityGuid sonityGuid;

#if UNITY_EDITOR
        public string sonityVersion = new Version(0, 0, 0, 0).ToString(); // Major Minor Patch Hotfix
        [SerializeField] private string sonityGuidEditor = ""; // Used for drawing the field in the editor

        // Triggered by the AssetPostprocessor
        public void InitializeObject(UnityEngine.Object asset) {
            bool anyChanged = false;
            // Serialized Version
            if (new Version(sonityVersion) < SonityVersion.version) {
                // Any already initialized version will have a value above 0
                if (new Version(sonityVersion) > new Version(0, 0, 0, 0)) {
                    // Any upgrades here
                }
                sonityVersion = SonityVersion.version.ToString();
                anyChanged = true;
            }
            // Cached Name
            if (cachedName != asset.name) {
                cachedName = asset.name;
                anyChanged = true;
            }
            // Sonity GUID
            if (sonityGuid != new SonityGuid(asset)) {
                sonityGuid = new SonityGuid(asset);
                sonityGuidEditor = sonityGuid.ToString();
                anyChanged = true;
            }
            // Save any new values
            if (anyChanged) {
                UnityEditor.EditorUtility.SetDirty(asset);
            }
        }
#endif

#if UNITY_EDITOR
        public string notes = "Notes";
#endif

        public int polyphonyLimit = 1;
    }
}