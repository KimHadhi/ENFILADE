// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Sonity.Internal {

    [Serializable]
    public class SoundPhysicsConditionInternals {

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
        public SoundPhysicsConditionBase[] childConditions = new SoundPhysicsConditionBase[0];

        public SoundTagBase soundTag;
        public PhysicsPlayOn physicsPlayOn = PhysicsPlayOn.OnCollisionAndTrigger;
        public bool playDisregardingConditions = false;

        public bool tagExpand = true;
        public bool layerExpand = true;
        public bool terrainIndexExpand = true;
        public bool terrainNameExpand = true;
        public bool componentExpand = true;

        public bool isTagAbortAllOnNoMatch = false;
        public bool isLayerAbortAllOnNoMatch = false;
        public bool isTerrainIndexAbortAllOnNoMatch = false;
        public bool isTerrainNameAbortAllOnNoMatch = false;
        public bool hasComponentAbortAllOnNoMatch = false;

        public bool isNotTagAbortAllOnMatch = false;
        public bool isNotLayerAbortAllOnMatch = false;
        public bool isNotTerrainIndexAbortAllOnMatch = false;
        public bool isNotTerrainNameAbortAllOnMatch = false;
        public bool hasNotComponentAbortAllOnMatch = false;

        public bool isTagUse = false;
        public bool isNotTagUse = false;
        public bool isLayerUse = false;
        public bool isNotLayerUse = false;
        public bool isTerrainIndexUse = false;
        public bool isNotTerrainIndexUse = false;
        public bool isTerrainNameUse = false;
        public bool isNotTerrainNameUse = false;
        public bool hasComponentUse = false;
        public bool hasNotComponentUse = false;

        public string[] isTagArray = new string[] { "Untagged" };
        public string[] isNotTagArray = new string[] { "Untagged" };
        public int[] isLayerArray = new int[1];
        public int[] isNotLayerArray = new int[1];
        public int[] isTerrainIndexArray = new int[1];
        public int[] isNotTerrainIndexArray = new int[1];
        public string[] isTerrainNameArray = new string[] { "Grass" };
        public string[] isNotTerrainNameArray = new string[] { "Grass" };
        public string[] hasComponentArray = new string[] { "Terrain" };
        public string[] hasNotComponentArray = new string[] { "Terrain" };

        public bool CheckIsInfiniteLoop(SoundPhysicsConditionBase condition, bool isEditor) {
            if (isEditor) {
                bool isInfiniteLoop = GetIfInfiniteLoop(condition, out SoundPhysicsConditionBase infiniteInstigator, out SoundPhysicsConditionBase infinitePrevious);
                if (isInfiniteLoop) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundPhysicsCondition}: \"{infiniteInstigator.internals.cachedName}\" in \"{infinitePrevious.internals.cachedName}\" creates an infinite loop", infiniteInstigator);
                    }
                }
                return isInfiniteLoop;
            } else {
                if (SoundManagerBase.Instance == null) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager} is null. Add one to the scene.");
                    return true;
                } else {
                    return SoundManagerBase.Instance.Internals.InternalCheckSoundPhysicsConditionIsInfiniteLoop(condition);
                }
            }
        }

        private bool isInfiniteLoop;
        private SoundPhysicsConditionBase tempInfiniteInstigator;
        private SoundPhysicsConditionBase tempInfinitePrevious;

        // Checks if any object in the hierarchy contains itself
        public bool GetIfInfiniteLoop(SoundPhysicsConditionBase condition, out SoundPhysicsConditionBase infiniteInstigator, out SoundPhysicsConditionBase infinitePrevious) {
            isInfiniteLoop = false;
            infiniteInstigator = null;
            infinitePrevious = null;
            if (condition != null) {
                GetIfInfiniteLoopSub(new IsInfiniteLoopClass(condition, null));
                if (isInfiniteLoop) {
                    infiniteInstigator = tempInfiniteInstigator;
                    infinitePrevious = tempInfinitePrevious;
                    return true;
                }
            }
            return false;
        }

        private void GetIfInfiniteLoopSub(IsInfiniteLoopClass currentObject) {
            for (int i = 0; i < currentObject.self.internals.childConditions.Length; i++) {
                SoundPhysicsConditionBase child = currentObject.self.internals.childConditions[i];
                if (child != null) {
                    if (!currentObject.allParentsList.Contains(child)) {
                        GetIfInfiniteLoopSub(new IsInfiniteLoopClass(child, currentObject.allParentsList));
                    } else {
                        tempInfiniteInstigator = child;
                        tempInfinitePrevious = currentObject.self;
                        isInfiniteLoop = true;
                        return;
                    }
                }
            }
            return;
        }

        private class IsInfiniteLoopClass {
            public SoundPhysicsConditionBase self;
            public List<SoundPhysicsConditionBase> allParentsList = new List<SoundPhysicsConditionBase>();
            public IsInfiniteLoopClass(SoundPhysicsConditionBase soundPhysicsConditionBase, List<SoundPhysicsConditionBase> parentsToAdd) {
                self = soundPhysicsConditionBase;
                allParentsList.Add(self);
                if (parentsToAdd != null) {
                    allParentsList.AddRange(parentsToAdd);
                }
            }
        }
    }
}