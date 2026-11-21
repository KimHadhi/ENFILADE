// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;
using System;

namespace Sonity.Internal {

    [Serializable]
    public abstract class SoundEventBase : ScriptableObject {

        public SoundEventInternals internals = new SoundEventInternals();

        /// <summary>
        /// Loads the audio data of any <see cref="AudioClip"/>s assigned to the <see cref="SoundContainerBase">SoundContainers</see> of this <see cref="SoundEventBase">SoundEvent</see>
        /// </summary>
        public void LoadAudioData() {
            LoadUnloadAudioData(true);
        }

        /// <summary>
        /// Unloads the audio data of any <see cref="AudioClip"/>s assigned to the <see cref="SoundContainerBase">SoundContainers</see> of this <see cref="SoundEventBase">SoundEvent</see>
        /// </summary>
        public void UnloadAudioData() {
            LoadUnloadAudioData(false);
        }

        private void LoadUnloadAudioData(bool load) {
            if (internals.soundContainers.Length == 0) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"{NameOf.SoundEvent} {internals.cachedName} has no {NameOf.SoundContainer}.", this);
                }
            } else {
                for (int i = 0; i < internals.soundContainers.Length; i++) {
                    if (internals.soundContainers[i] == null) {
                        if (ShouldDebug.Warnings()) {
                            // If you wrap the index in {i}, the unity auditor will warn "conversion from value int32 to ref type", so just wrap it in " + i + " instead
                            Debug.LogWarning($"{NameOf.SoundEvent} {internals.cachedName} {NameOf.SoundContainer} " + i + " is null.", this);
                        }
                    } else {
                        internals.soundContainers[i].internals.LoadUnloadAudioData(load, internals.soundContainers[i]);
                    }
                }
            }
        }

        public bool IsNull() {
            for (int i = 0; i < internals.soundContainers.Length; i++) {
                // Checks if the SoundEvent is null
                if (internals.soundContainers[i] == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity: \"{internals.cachedName}\" ({NameOf.SoundEvent}) has null {NameOf.SoundContainer}s.", this);
                    }
                    return true;
                } else {
                    // Checks if the AudioClips are not empty
                    if (internals.soundContainers[i].internals.audioClips.Length == 0) {
                        if (ShouldDebug.Warnings()) {
                            Debug.LogWarning($"Sonity: \"{internals.soundContainers[i].internals.cachedName}\" ({NameOf.SoundContainer}) has no {nameof(AudioClip)}s.", internals.soundContainers[i]);
                        }
                        return true;
                    } else {
                        for (int ii = 0; ii < internals.soundContainers[i].internals.audioClips.Length; ii++) {
                            // Checks if the AudioClips are null
                            if (internals.soundContainers[i].internals.audioClips[ii] == null) {
                                if (ShouldDebug.Warnings()) {
                                    Debug.LogWarning($"Sonity: \"{internals.soundContainers[i].internals.cachedName}\" ({NameOf.SoundContainer}) has null {nameof(AudioClip)}s.", internals.soundContainers[i]);
                                }
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}