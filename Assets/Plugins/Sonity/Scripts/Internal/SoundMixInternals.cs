// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;
using System.Collections.Generic;
using System;

namespace Sonity.Internal {

    [Serializable]
    public class SoundMixInternals {

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
        public SoundEventModifier soundEventModifier = new SoundEventModifier();
        public SoundMixBase soundMixParent;

        /// <summary>
        /// Sets the volume based on decibel.
        /// The volume will be saved in the scriptable object.
        /// </summary>
        /// <param name="volumeDecibel"> Range NegativeInfinity to 0 </param>
        public void SetVolumeDecibel(float volumeDecibel) {
            Mathf.Clamp(volumeDecibel, Mathf.NegativeInfinity, 0);
            soundEventModifier.volumeDecibel = volumeDecibel;
            soundEventModifier.volumeRatio = VolumeScale.ConvertDecibelToRatio(volumeDecibel);
        }

        /// <summary>
        /// Sets the pitch based on semitones.
        /// The pitch will be saved in the scriptable object.
        /// </summary>
        /// <param name="pitchSemitone"> No range limit </param>
        public void SetPitchSemitone(float pitchSemitone) {
            soundEventModifier.pitchSemitone = pitchSemitone;
            soundEventModifier.pitchRatio = PitchScale.SemitonesToRatio(pitchSemitone);
        }

        public bool CheckIsInfiniteLoop(SoundMixBase soundMix, bool isEditor) {
            if (isEditor) {
                bool isInfiniteLoop = GetIfInfiniteLoop(soundMix, out SoundMixBase infiniteInstigator, out SoundMixBase infinitePrevious);
                if (isInfiniteLoop) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundMix}: \"{infiniteInstigator.internals.cachedName}\" in \"{infinitePrevious.internals.cachedName}\" creates an infinite loop", infiniteInstigator);
                    }
                }
                return isInfiniteLoop;
            } else {
                return SoundManagerBase.Instance.Internals.InternalCheckSoundMixIsInfiniteLoop(soundMix);
            }
        }

        public bool GetIfInfiniteLoop(SoundMixBase soundMix, out SoundMixBase infiniteInstigator, out SoundMixBase infinitePrevious) {

            infiniteInstigator = null;
            infinitePrevious = null;

            if (soundMix == null) {
                return false;
            }

            List<SoundMixBase> toCheck = new List<SoundMixBase>();
            List<SoundMixBase> isChecked = new List<SoundMixBase>();

            toCheck.Add(soundMix);

            while (toCheck.Count > 0) {
                SoundMixBase soundMixChild = toCheck[0];
                toCheck.RemoveAt(0);
                if (soundMixChild != null) {
                    for (int i = 0; i < isChecked.Count; i++) {
                        if (isChecked[i] == soundMixChild) {
                            infiniteInstigator = isChecked[i];
                            return true;
                        }
                    }

                    if (soundMixChild.internals.soundMixParent != null) {
                        toCheck.Add(soundMixChild.internals.soundMixParent);
                        infinitePrevious = soundMixChild;
                    }

                    isChecked.Add(soundMixChild);
                }
            }
            return false;
        }
    }
}