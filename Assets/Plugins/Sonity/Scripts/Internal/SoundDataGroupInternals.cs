// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;
using System.Collections.Generic;
using System;

namespace Sonity.Internal {

    [Serializable]
    public class SoundDataGroupInternals {

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
        // Put audioclips first in case addessables loads in order
        public List<AudioClip> audioClips = new List<AudioClip>(1);
        public List<SoundContainerBase> soundContainers = new List<SoundContainerBase>(1);
        public List<SoundEventBase> soundEvents = new List<SoundEventBase>(1);

        public bool soundEventsExpanded = true;
        public bool soundContainersExpanded = true;
        public bool audioClipsExpanded = true;

        // Puts child groups last in case addressables loads in order
        public SoundDataGroupBase[] soundDataGroupChildren = new SoundDataGroupBase[0];

        public void LoadUnloadAudioData(bool load, bool includeChildren, SoundDataGroupBase parent) {
            if (SoundManagerBase.Instance == null) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager} is null. Add one to the scene.");
                return;
            } else {
                if (!SoundManagerBase.Instance.Internals.InternalCheckSoundDataGroupIsInfiniteLoop(parent)) {
                    for (int i = 0; i < soundEvents.Count; i++) {
                        SoundEventBase soundEvent = soundEvents[i];
                        if (soundEvent != null) {
                            if (load) {
                                soundEvent.LoadAudioData();
                            } else {
                                soundEvent.UnloadAudioData();
                            }
                        }
                    }
                    if (includeChildren) {
                        for (int i = 0; i < soundDataGroupChildren.Length; i++) {
                            if (soundDataGroupChildren[i] != null) {
                                soundDataGroupChildren[i].internals.LoadUnloadAudioData(load, includeChildren, soundDataGroupChildren[i]);
                            }
                        }
                    }
                }
            }
        }

        public bool GetIfInfiniteLoop(SoundDataGroupBase soundDataGroup, out SoundDataGroupBase infiniteInstigator, out SoundDataGroupBase infinitePrevious) {

            infiniteInstigator = null;
            infinitePrevious = null;

            if (soundDataGroup == null) {
                return false;
            }

            List<SoundDataGroupBase> toCheck = new List<SoundDataGroupBase>();
            List<SoundDataGroupBase> isChecked = new List<SoundDataGroupBase>();

            toCheck.Add(soundDataGroup);

            while (toCheck.Count > 0) {
                SoundDataGroupBase soundDataGroupChild = toCheck[0];
                toCheck.RemoveAt(0);
                if (soundDataGroupChild != null) {
                    for (int i = 0; i < isChecked.Count; i++) {
                        if (isChecked[i] == soundDataGroupChild) {
                            infiniteInstigator = isChecked[i];
                            return true;
                        }
                    }

                    if (soundDataGroupChild.internals.soundDataGroupChildren != null && soundDataGroupChild.internals.soundDataGroupChildren.Length > 0) {
                        toCheck.AddRange(soundDataGroupChild.internals.soundDataGroupChildren);
                        infinitePrevious = soundDataGroupChild;
                    }
                    isChecked.Add(soundDataGroupChild);
                }
            }
            return false;
        }
    }
}