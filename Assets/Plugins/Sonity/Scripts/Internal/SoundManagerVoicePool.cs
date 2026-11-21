// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using System;

namespace Sonity.Internal {

    [Serializable]
    public class SoundManagerVoicePool {

        // Voices could be a List, but becuse the length is basically never changed with preload, use an array
        [NonSerialized]
        public Voice[] voices = new Voice[0];

        // Stops voices after a certain time, start looking at the first items in the list, they are the oldest ones
        [NonSerialized]
        public List<int> voiceStopIndexes = new List<int>();

        // Dictionaries are not serializable and is filled at start
        [NonSerialized]
        private Dictionary<SonityGuid, List<Voice>> soundPolyGroupDictionary = new Dictionary<SonityGuid, List<Voice>>();

#if UNITY_EDITOR
        [NonSerialized]
        public int statisticsVoicesStolen;
#endif

        public void SoundPolyGroupLimitPolyphony(SoundPolyGroupBase soundPolyGroup, Voice voice) {
            if (soundPolyGroup == null) {
                return;
            } else {

                int polyLimit = soundPolyGroup.internals.polyphonyLimit;

                // Add if not in dictionary
                if (!soundPolyGroupDictionary.ContainsKey(soundPolyGroup.internals.sonityGuid)) {
                    List<Voice> newVoices = new List<Voice>();
                    for (int i = 0; i < polyLimit; i++) {
                        newVoices.Add(null);
                    }
                    soundPolyGroupDictionary.Add(soundPolyGroup.internals.sonityGuid, newVoices);
                }

                // Dictionary get value
                soundPolyGroupDictionary.TryGetValue(soundPolyGroup.internals.sonityGuid, out List<Voice> voices);

                int voicesCount = voices.Count;

                // Remove or add voices
                if (voicesCount < polyLimit) {
                    // Add Voices
                    int voicesToAdd = polyLimit - voicesCount;
                    for (int i = 0; i < voicesToAdd; i++) {
                        voices.Add(null);
                    }
                } else if (voicesCount > polyLimit) {
                    // Remove Voices
                    int voicesToRemove = voicesCount - polyLimit;
                    for (int i = 0; i < voicesToRemove; i++) {
                        // Get new Count
                        int toRemoveIndex = voices.Count - 1;
                        Voice voiceToRemove = voices[toRemoveIndex];
                        if (voiceToRemove != null) {
                            PoolVoice(voiceToRemove, false);
                        }
                        // Remove topmost voice in list
                        voices.RemoveAt(toRemoveIndex);
                    }
                }
                // Need to update voice count
                voicesCount = voices.Count;

                // If already contains itself
                for (int i = 0; i < voicesCount; i++) {
                    if (voices[i] == voice) {
                        return;
                    }
                }
                // If voice is null
                for (int i = 0; i < voicesCount; i++) {
                    if (voices[i] == null) {
                        voices[i] = voice;
                        return;
                    }
                }
                // If voice is not assigned
                for (int i = 0; i < voicesCount; i++) {
                    if (!voices[i].isAssigned) {
                        voices[i] = voice;
                        return;
                    }
                }
                // If voice has another SoundPolyGroup
                for (int i = 0; i < voicesCount; i++) {
                    if (voices[i].soundEvent.internals.data.soundPolyGroup != soundPolyGroup) {
                        voices[i] = voice;
                        return;
                    }
                }

                float higestPriority = float.MaxValue;
                int lowestPrioIndex = -1;

                // Finds the Voice with the lowest priority
                for (int i = 0; i < voicesCount; i++) {
                    Voice tempVoice = voices[i];
                    // Volume Without Fade
                    float priority = tempVoice.GetVolumeRatioWithoutFade() * tempVoice.soundEvent.internals.data.soundPolyGroupPriority;
                    if (priority < higestPriority) {
                        higestPriority = priority;
                        lowestPrioIndex = i;
                    }
                }

                // If index is lower, no voice was found, should't happen but hey
                if (lowestPrioIndex >= 0) {
                    // Pool Voice with lower priority
                    voices[lowestPrioIndex].PoolVoice(false, false);
                    // Sets Voice to the new Voice, need to set value in array
                    voices[lowestPrioIndex] = voice;
                }
                return;
            }
        }

        public void CreateVoice(int numberOf, bool disableVoices) {
            // Voices could be a List, but becuse the length is basically never changed with preload, use an array
            Array.Resize(ref voices, voices.Length + numberOf);
            for (int i = 0; i < numberOf; i++) {
                int newIndex = voices.Length - numberOf + i;
                Voice newVoice = new Voice($"Voice " + (voices.Length - numberOf + i + 1).ToString(), SoundManagerBase.Instance.Internals.cachedVoicePoolTransform);
                voices[newIndex] = newVoice;
                newVoice.voiceIndex = voices.Length - numberOf + i;
                if (disableVoices) {
                    newVoice.cachedGameObject.SetActive(false);
                }
            }
        }

        public void PoolVoice(Voice voice, bool isCalledByOnDestroy) {
            if (isCalledByOnDestroy) {
                // If its called by OnDestroy then force stop instead of pause
                voice.ResetVoice();
#if !SONITY_DISABLE_VOICE_EFFECTS
                voice.cachedVoiceEffect.SetEnabled(false);
#endif
                voice.StopOnDestroy();
            } else {
                voice.ResetVoice();
#if !SONITY_DISABLE_VOICE_EFFECTS
                voice.cachedVoiceEffect.SetEnabled(false);
#endif
                voice.SetBypassVoiceEffects(true);
                voice.SetState(VoiceState.Pause, false);
                // Adds the voice to the stoplist
                voice.stopTime = SoundTimeScale.GetTimeRuntime() + SoundManagerBase.Instance.Internals.settings.voiceStopTime;
                voiceStopIndexes.Add(voice.voiceIndex);
            }
        }

        public Voice GetVoice(float priority, AudioMixerGroup mixerGroup, bool isRestartingLoop) {

            int maxIndex = Mathf.Clamp(voices.Length, 0, SoundManagerBase.Instance.Internals.settings.voiceLimit);

            // Return an available Voice with the same audioMixerGroup
            if (mixerGroup != null) {
                for (int i = 0; i < maxIndex; i++) {
                    Voice voice = voices[i];
                    if (!voice.isAssigned && voice.cachedAudioMixerGroup == mixerGroup) {
                        voice.AssignVoice();
                        return voice;
                    }
                }
            }

            // Return an available Voice
            for (int i = 0; i < maxIndex; i++) {
                Voice voice = voices[i];
                if (!voice.isAssigned) {
                    voice.AssignVoice();
                    return voice;
                }
            }

            // Check if Voice max polyphony is reached
            // Never removes voice indexes in the pool, just clamp to voice limit instead
            if (Mathf.Clamp(voices.Length, 0, SoundManagerBase.Instance.Internals.settings.voiceLimit) >= SoundManagerBase.Instance.Internals.settings.voiceLimit) {
                // Restarting loops shouldnt steal polyphony
                if (isRestartingLoop) {
                    return null;
                }
                float higestPriority = 1f;
                int voiceIndex = 0;

                // Find the Voice with the lowest priority
                for (int i = 0; i < voices.Length; i++) {
                    Voice voiceAtIndex = voices[i];
                    if (!voiceAtIndex.soundContainer.internals.data.neverStealVoice) {
                        float savedPriority = voiceAtIndex.GetVolumeRatioWithoutFadeWithPriority();
                        if (savedPriority < higestPriority) {
                            higestPriority = savedPriority;
                            voiceIndex = i;
                        }
                    }
                }

                Voice voiceLowestPrio = voices[voiceIndex];
                voiceLowestPrio.PoolVoice(true, false);
#if UNITY_EDITOR
                statisticsVoicesStolen++;
#endif
                voiceLowestPrio.AssignVoice();
                return voiceLowestPrio;
            } else {
                // Create a new Voice
                CreateVoice(1, false);
                return GetVoice(priority, mixerGroup, isRestartingLoop);
            }
        }
    }
}