// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Sonity.Internal {

    public class SoundContainerInstance {

        public List<NextVoice> nextVoices = new List<NextVoice>();

        // Virtualize Todo
        //public List<SoundEventInstanceVirtualVoice> virtualVoices = new List<SoundEventInstanceVirtualVoice>();

        public float GetVolume(int v) {
            return soundContainer.internals.data.GetVolume(
                voiceHolder[v].voiceParameter.currentModifier,
                voiceHolder[v].playTypeInstance.GetScaledDistance(),
                voiceHolder[v].playTypeInstance.GetScaledIntensity()
                )
                * timelineSoundContainerSetting.GetVolumeRatio()
                * soundEvent.internals.data.GetVolumeRatioClamped()
                * soundEvent.internals.data.GetSoundVolumeGroupRatio();
        }

        public bool ShouldBePlaying(int v, bool useCachedVolume) {
            // Stopping sounds under threshold
            if (soundEvent.internals.data.intensityThresholdEnable && voiceHolder[v].voiceParameter.SoundParametersHasContinuousIntensity()) {
                if (voiceHolder[v].playTypeInstance.GetScaledIntensity() < soundEvent.internals.data.intensityThreshold) {
                    // If intensity is under threshold, then intensity is not updated anymore bcs the sound is not playing
                    if (voiceHolder[v].voiceParameter.currentModifier.intensityUse) {
                        // Update instensity
                        voiceHolder[v].playTypeInstance.SetIntensity(voiceHolder[v].voiceParameter.currentModifier.intensity, false);
                        if (voiceHolder[v].playTypeInstance.GetScaledIntensity() < soundEvent.internals.data.intensityThreshold) {
                            // Stop if still under threshold
                            return false;
                        }
                    }
                }
            }
            // If SoundContainer not distance based or is within range
            if (!soundContainer.internals.data.distanceEnabled || voiceHolder[v].playTypeInstance.GetScaledDistance() < 1f) {
                if (useCachedVolume) {
                    if (voiceHolder[v].voice.GetVolumeRatioWithoutFade() > 0f) {
                        return true;
                    }
                } else {
                    if (GetVolume(v) > 0f) {
                        return true;
                    }
                }
            }
            // If SoundParameter has any volume or intensity parameters with update continuous
            if (voiceHolder[v].voiceParameter.SoundParametersCanChangeVolume()) {
                return true;
            }
            return false;
        }

        public float NextVoiceGetVolume(int n) {
            return soundContainer.internals.data.GetVolume(
                nextVoices[n].voiceParameter.currentModifier,
                nextVoices[n].playTypeInstance.GetScaledDistance(),
                // Scaled Intensity if intensityUse
                nextVoices[n].voiceParameter.currentModifier.intensityUse ?
                soundEvent.internals.data.GetScaledIntensity(nextVoices[n].voiceParameter.currentModifier.intensity) : 1f
                )
                * timelineSoundContainerSetting.GetVolumeRatio()
                * soundEvent.internals.data.GetVolumeRatioClamped()
                * soundEvent.internals.data.GetSoundVolumeGroupRatio();
        }

        public bool NextVoiceShouldPlay(int n) {
            if (NextVoiceGetVolume(n) > 0f) {
                // If SoundContainer not distance based
                if (!soundContainer.internals.data.distanceEnabled) {
                    return true;
                }
                // If SoundContainer is within range
                if (nextVoices[n].playTypeInstance.GetScaledDistance() < 1f) {
                    return true;
                }
            }
            // Loops should play until stopped or transform null
            if (soundContainer.internals.data.loopEnabled) {
                return true;
            }
            // If SoundParameter has any volume or intensity parameters with update continuous
            if (nextVoices[n].voiceParameter.SoundParametersCanChangeVolume()) {
                return true;
            }
            return false;
        }

        public int nextVoiceIndex;
        public void NextVoice(int polyphony) {
            nextVoiceIndex++;
            if (nextVoiceIndex >= polyphony) {
                nextVoiceIndex = 0;
            }
        }

        // Random Clip
        public int[] randomClipLast;
        public int randomClipListPosition;
        public int randomClip;

        public bool RandomClipLastContains() {
            for (int i = 0; i < randomClipLast.Length; i++) {
                if (randomClipLast[i] == randomClip) {
                    return true;
                }
            }
            return false;
        }

        // If any voices are waiting to play (eg delayed)
        public bool GetNextVoiceListAnyAssigned() {
            for (int n = 0; n < nextVoices.Count; n++) {
                if (nextVoices[n].assinged) {
                    return true;
                }
            }
            return false;
        }

        // Returns if any of the voices are playing
        public bool GetIsPlaying() {
            for (int i = 0; i < voiceHolder.Length; i++) {
                if (voiceHolder[i].voice != null) {
                    if (voiceHolder[i].voice.GetVoiceIsPlaying()) {
                        return true;
                    }
                }
            }
            return false;
        }

        // Last played voice index to keep track of which was the last voice to play
        public int lastPlayedVoiceIndex;

        public float GetLastPlayedClipLength(bool pitchSpeed) {
            if (voiceHolder[lastPlayedVoiceIndex].voice == null) {
                // Need to return value higher than 0, otherwise TriggerOnTail feedback triggers itself when the sound has delay.
                return Mathf.Infinity;
            } else {
                return voiceHolder[lastPlayedVoiceIndex].voice.GetAudioSourceClipLengthSeconds(pitchSpeed);
            }
        }

        public float GetLastPlayedClipTimeSeconds(bool pitchSpeed) {
            if (voiceHolder[lastPlayedVoiceIndex].voice == null) {
                return 0f;
            } else {
                return voiceHolder[lastPlayedVoiceIndex].voice.GetAudioSourceTimeSeconds(pitchSpeed);
            }
        }

        public float GetLastPlayedClipTimeRatio() {
            if (voiceHolder[lastPlayedVoiceIndex].voice == null) {
                return 0f;
            } else {
                return voiceHolder[lastPlayedVoiceIndex].voice.GetAudioSourceTimeRatio();
            }
        }

        public void GetLastPlayedAudioSourceSpectrumData(ref float[] samples, int channel, FFTWindow window) {
            if (voiceHolder[lastPlayedVoiceIndex].voice == null) {
                return;
            } else {
                voiceHolder[lastPlayedVoiceIndex].voice.GetAudioSourceSpectrumData(ref samples, channel, window);
            }
        }

        public AudioSource GetLastPlayedAudioSource() {
            if (voiceHolder[lastPlayedVoiceIndex].voice == null) {
                return null;
            } else {
                return voiceHolder[lastPlayedVoiceIndex].voice.GetAudioSource();
            }
        }

        public VoiceHolder[] voiceHolder = new VoiceHolder[0];

        public SoundEventBase soundEvent;
        public SoundContainerBase soundContainer;

        public SoundEventTimelineData timelineSoundContainerSetting;

        // Sets the ranges of the latest created next voice
        public void NextVoiceSetMaxRange(int index) {
            if (soundContainer.internals.data.distanceEnabled) {
                nextVoices[index].maxRange = SoundManagerBase.Instance.Internals.settings.distanceScale
                    * soundContainer.internals.data.distanceScale
                    * nextVoices[index].voiceParameter.currentModifier.distanceScale;
            } else {
                nextVoices[index].maxRange = 0f;
            }
        }

        // Sets the ranges of the latest created next voice
        public void SetMaxRange(int v) {
            if (soundContainer.internals.data.distanceEnabled) {
                voiceHolder[v].maxRange =
                    SoundManagerBase.Instance.Internals.settings.distanceScale
                    * soundContainer.internals.data.distanceScale
                    * voiceHolder[v].voiceParameter.currentModifier.distanceScale;
            } else {
                voiceHolder[v].maxRange = 0f;
            }
        }

#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
        private bool addressableAudioMixerGroupCachedLoaded;
        private AudioMixerGroup addressableAudioMixerGroupCached;
#endif

        private AudioMixerGroup GetAudioMixerGroup() {
#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
#if UNITY_EDITOR
            // Cache disabled for editor so you can change the AudioMixerGroup during play
            addressableAudioMixerGroupCachedLoaded = false;
#endif
            if (addressableAudioMixerGroupCachedLoaded) {
                return addressableAudioMixerGroupCached;
            } else {
                if (SoundManagerBase.Instance != null && SoundManagerBase.Instance.Internals.settings.addressableAudioMixerUse) {
                    if (soundEvent.internals.data.audioMixerGroup == null) {
                        // Null AudioMixerGroups will be cached to null
                        addressableAudioMixerGroupCached = SoundManagerBase.Instance.Internals.settings.AddressableAudioMixerGroupConvert(soundContainer.internals.data.audioMixerGroup);
                        addressableAudioMixerGroupCachedLoaded = true;
                        return addressableAudioMixerGroupCached;
                    } else {
                        // Null AudioMixerGroups will be cached to null
                        addressableAudioMixerGroupCached = SoundManagerBase.Instance.Internals.settings.AddressableAudioMixerGroupConvert(soundEvent.internals.data.audioMixerGroup);
                        addressableAudioMixerGroupCachedLoaded = true;
                        return addressableAudioMixerGroupCached;
                    }
                }
            }
#endif
            return soundEvent.internals.data.audioMixerGroup;
        }

        // Prepares the voice for playback
        public void VoicePrepare(
            int s, 
            int v, 
            bool replaceVoiceParameter, 
            VoiceParameterInstance voiceParameter, 
            SoundEventPlayTypeInstance playTypeInstance,
            bool isRestartingLoop
            ) {

            VoiceHolder holder = voiceHolder[v];

            // Repace VoiceParameter
            if (replaceVoiceParameter) {
                holder.voiceParameter.CopyTo(voiceParameter);
            }

            // Set Voice Range (Needs to update voiceParameter first)
            SetMaxRange(v);
            // Doesnt need to copy from if playing again
            if (playTypeInstance != null) {
                holder.playTypeInstance.CopyTo(playTypeInstance);
            }
            holder.playTypeInstance.SetCachedDistancesAndAngle(holder.maxRange, holder.voiceParameter, true);

            // Fade in Start
            holder.voiceFade.SetToFadeIn(holder.voiceParameter.currentModifier);

            // Update intensity
            if (holder.voiceParameter.currentModifier.intensityUse) {
                holder.playTypeInstance.SetIntensity(holder.voiceParameter.currentModifier.intensity, true);
            } else {
                holder.playTypeInstance.ResetScaledIntensity();
            }

            Voice voice = holder.voice;

            // Gets a voice from the voice pool
            if (voice == null) {
                if (ShouldBePlaying(v, false)) {
                    voice = SoundManagerBase.Instance.Internals.voicePool.GetVoice(GetVolume(v) * soundContainer.internals.data.priority, GetAudioMixerGroup(), isRestartingLoop);
                    // If the voice shouldn't play
                    if (voice == null) {
                        holder.voiceIsToPlay = false;
                        return;
                    }
                    // Update holder with new Voice
                    holder.voice = voice;

                    SoundManagerBase.Instance.Internals.voicePool.SoundPolyGroupLimitPolyphony(soundEvent.internals.data.soundPolyGroup, voice);

                    // Sets variables so that the instance can be found from the Voice and SoundManager
                    voice.instanceVoiceHolder = holder;
                    voice.soundEvent = soundEvent;
#if UNITY_EDITOR
                    voice.soundEventName = soundEvent.internals.cachedName;
#endif
                    voice.soundContainer = soundContainer;

                    if (voice.soundContainer.internals.data.distanceEnabled) {
                        voice.cachedAudioSource.maxDistance = holder.maxRange;
                    } else {
                        voice.cachedAudioSource.maxDistance = Mathf.Infinity;
                    }

                    // AudioMixerGroups are checked if Addressable is enabled
                    AudioMixerGroup audioMixerGroup = GetAudioMixerGroup();
                    if (voice.cachedAudioMixerGroup != audioMixerGroup) {
                        voice.cachedAudioMixerGroup = audioMixerGroup;
                        voice.cachedAudioSource.outputAudioMixerGroup = audioMixerGroup;
                    }

                    voice.cachedAudioSource.ignoreListenerPause = soundEvent.internals.data.ignoreGlobalPause;

                } else {
                    // Loops should not play, but rather start when in range
                    if (soundContainer.internals.data.loopEnabled) {
                        holder.shouldRestartIfLoop = true;
                    } else {
                        return;
                    }
                }
            }

            // Set Position
            if (voice != null) {
                // Even if Force2D is enabled, set position on first prepare, helps debugging in world also!
                if (soundContainer.internals.data.lockAxisEnable) {
                    voice.SetPostion(AxisLock.Lock(holder.playTypeInstance.GetPosition(), soundContainer.internals.data.lockAxis, soundContainer.internals.data.lockAxisPosition));
                } else {
                    voice.SetPostion(holder.playTypeInstance.GetPosition());
                }

                // Set Rotation
                voice.SetRotation(holder.playTypeInstance.GetRotation());

                // Volume (also volume for priority)
                // Gets and saves the random volume
                voice.SetVolumeRandomRatio(soundContainer.internals.data.GetVolumeRatioRandom());
                // Needs to be updated so that voice stealing based on volume will work
                voice.SetVolumeRatioFirst(GetVolume(v), holder.voiceFade.GetVolume(holder.voiceParameter.currentModifier));

                // Priority is later multiplied with volume when evaluated
                voice.priority = soundContainer.internals.data.priority;

                // Gets and saves the random pitch
                voice.SetPitchRatioStarting(soundContainer.internals.data.GetPitchBaseAndRandom());

                // Random Clip
                if (soundContainer.internals.data.playOrder == PlayOrder.GlobalRandom) {
                    // Global randomclip
                    randomClip = soundContainer.internals.GetRandomClip();
                } else {
                    int audioClipsLength = soundContainer.internals.audioClips.Length;
                    // So that it wont break when changing the length in the editor at runtime
                    if (randomClipLast.Length != audioClipsLength / 2) {
                        randomClipLast = new int[audioClipsLength / 2];
                        randomClipListPosition = 0;
                        randomClip = 0;
                    }
                    if (randomClipLast.Length == 0) {
                        randomClip = 0;
                    } else {
                        // Pseudo random function remembering which clips it last played to avoid repetition
                        // Max 40 retries giving you a 0,0000000000009 probabiltiy of repetition
                        for (int i = 0; i < 40; i++) {
                            randomClip = UnityEngine.Random.Range(0, audioClipsLength);
                            if (!RandomClipLastContains()) {
                                break;
                            }
                        }
                        randomClipLast[randomClipListPosition] = randomClip;

                        // Wrap index
                        randomClipListPosition++;
                        if (randomClipListPosition >= randomClipLast.Length) {
                            randomClipListPosition = 0;
                        }
                    }
                }

                // Setting loop
                voice.cachedAudioSource.loop = soundContainer.internals.data.loopEnabled;

                // Doppler Amount
                voice.cachedAudioSource.dopplerLevel = soundContainer.internals.data.dopplerAmount;

                // Prepares the voice
                if (SoundManagerBase.Instance.Internals.settings.playOneShotOptimization && ShouldPlayOneShot(s, v)) {
                    voice.playOneShot = true;
                    voice.cachedAudioClip = soundContainer.internals.audioClips[randomClip];
                } else {
                    voice.playOneShot = false;
                    voice.cachedAudioSource.clip = soundContainer.internals.audioClips[randomClip];
                }
            }

            // Updates the curves of the voice
            VoiceUpdate(v, true);
        }

        public bool ShouldPlayOneShot(int s, int v) {

            VoiceHolder holder = voiceHolder[v];

            if (soundContainer.internals.data.noPlayOneShotOptimization) {
                return false;
            }
            if (soundContainer.internals.data.loopEnabled) {
                return false;
            }
            if (soundEvent.internals.data.triggerOnTailEnable) {
                return false;
            }
            if (soundContainer.internals.data.randomStartPosition) {
                return false;
            }
            // Reverse forces you to have a start offset > 0, so it works
            if (soundContainer.internals.data.GetStartPosition(holder.voiceParameter.currentModifier) > 0f) {
                return false;
            }
            // Force 2D overrides distance/spatial blend and follow position
            if (holder.voiceParameter.currentModifier.force2DUse && holder.voiceParameter.currentModifier.force2D) {
                return true;
            }
            if (!soundContainer.internals.data.distanceEnabled && soundContainer.internals.data.spatialBlend == 0) {
                // If its a 2D sound without distance, then it can still use PlayOneShot even if it has followPosition
                return true;
            }
            if (soundContainer.internals.data.followPosition || holder.voiceParameter.CanHaveFollowPosition()) {
                return false;
            }
            return true;
        }

        // Plays the voice
        public void VoicePlay(int v, int polyphony, float lastStartTime) {

            if (v >= voiceHolder.Length) {
                return;
            }

            VoiceHolder holder = voiceHolder[v];
            Voice voice = holder.voice;

            if (holder != null && voice != null) {

                voice.lastStartTime = lastStartTime;

                // Setting the last played voice index to keep track of which was the last voice to play
                lastPlayedVoiceIndex = v;

                // Sets pitch before playing so its more responsive
                voice.SetPitchRatioFirst(
                    holder.voiceParameter.currentModifier.pitchRatio 
                    * soundContainer.internals.data.GetPitchRatioIntensity(holder.playTypeInstance.GetScaledIntensity())
                    , soundContainer.internals.data.GetReverse(holder.voiceParameter.currentModifier)
                    );

                // For sounds being stopped because being out of range, the sound needs to be played before setting the start position.
                // Otherwise the start positon will be 0.
                // Before I had noted troubles where time needed to be set before play, otherwise this error might manifest:
                // "Error executing result (An invalid seek position was passed to this function)" but this doesn't seem to happen anymore.

#if UNITY_2019_1_OR_NEWER && !UNITY_2022_1_OR_NEWER
                // Fix for Unity 2021.3.0f1 where I noticed that it would complain when playing the AudioSource.
                // "SoundChannel.cpp(343) : Error executing result (An invalid seek position was passed to this function.)"
                // It seems that either stopping or setting the timesamples to 0 before playing the AudioSource solves this problem.
                // This doesn't happen in 2022.3.37f1
                voice.cachedAudioSource.timeSamples = 0;
#endif

                // Plays the voice
                voice.SetState(VoiceState.Play, true);

#if UNITY_WEBGL
                // Need to set time before Play
                // Otherwise can get "Error executing result (An invalid seek position was passed to this function)"
                if (voice.cachedAudioSource.clip.samples == 0) {
                    voice.cachedAudioSource.timeSamples = 0;
                }
#endif
                // Random Start Position
                if (soundContainer.internals.data.randomStartPosition) {
                    // Random Min Max
                    int clipSamples = voice.cachedAudioSource.clip.samples;
                    voice.cachedAudioSource.timeSamples =
                        (int)Mathf.Clamp(
                            UnityEngine.Random.Range(
                                clipSamples * soundContainer.internals.data.randomStartPositionMin,
                                clipSamples * soundContainer.internals.data.randomStartPositionMax
                                ), 0f, clipSamples - 1
                        );
                } else {
                    // Start Offset
                    float startPosition = soundContainer.internals.data.GetStartPosition(holder.voiceParameter.currentModifier);
                    if (startPosition > 0) {
                        int clipSamples = voice.cachedAudioSource.clip.samples;
                        voice.cachedAudioSource.timeSamples = (int)Mathf.Clamp(clipSamples * startPosition, 0f, clipSamples - 1);
                    }
                    // Dont need to set timesamples when start position is 0 because it will restart when using Play()
                }

                // Increments the voice for polyphony
                NextVoice(polyphony);
            }
        }

        // Updates the voice
        public void VoiceUpdate(int v, bool isVoicePrepare) {

            VoiceHolder holder = voiceHolder[v];
            Voice voice = holder.voice;

            // Update intensity
            if (holder.voiceParameter.currentModifier.intensityUse) {
                holder.playTypeInstance.SetIntensity(holder.voiceParameter.currentModifier.intensity, false);
            }

            if (voice != null) {
                // MaxRange & Cached Distances are updated before VoiceUpdate
                // Volume (also volume for priority)
                voice.SetVolumeRatioUpdate(GetVolume(v), holder.voiceFade.GetVolume(holder.voiceParameter.currentModifier));

                // Sets the priority to the AudioSource
                voice.SetAudioSourcePriority(voice.GetVolumeRatioWithoutFadeWithPriority());

                // Force 2D
                if (holder.voiceParameter.currentModifier.force2DUse && holder.voiceParameter.currentModifier.force2D) {
                    voice.SetSpatialBlend(0f);
                    voice.SetSpatialSpreadRatio(0.5f);
                    voice.SetStereoPan(0f);
                } else {
                    // Not Force 2D

                    // Spatial Blend
                    voice.SetSpatialBlend(
                        soundContainer.internals.data.GetSpatialBlend(
                            holder.voiceParameter.currentModifier,
                            holder.playTypeInstance.GetScaledDistance(),
                            holder.playTypeInstance.GetScaledIntensity()
                            )
                        );

                    // Spatial Spread Ratio
                    voice.SetSpatialSpreadRatio(
                        soundContainer.internals.data.GetSpatialSpread(
                            holder.playTypeInstance.GetScaledDistance(),
                            holder.playTypeInstance.GetScaledIntensity()
                            )
                        );

                    // Stereo Pan
                    voice.SetStereoPan(
                        soundContainer.internals.data.GetStereoPan(
                            holder.voiceParameter.currentModifier,
                            holder.playTypeInstance.GetAngle()
                            )
                        );
                }

                // Sets pitch
                voice.SetPitchRatioUpdate(
                    holder.voiceParameter.currentModifier.pitchRatio
                    * soundContainer.internals.data.GetPitchRatioIntensity(
                        holder.playTypeInstance.GetScaledIntensity()
                        ),
                    soundContainer.internals.data.GetReverse(
                        holder.voiceParameter.currentModifier)
                    );

#if !SONITY_DISABLE_VOICE_EFFECTS
                // Bypass Voice Effects Setting
                if (soundContainer.internals.data.GetVoiceEffectsEnabled() && !soundContainer.internals.data.GetBypassVoiceEffects(holder.voiceParameter.currentModifier)) {

                    VoiceEffect voiceEffect = voice.cachedVoiceEffect;

                    // Distortion
                    voiceEffect.DistortionSetEnabled(soundContainer.internals.data.GetDistortionEnabled());
                    if (voiceEffect.DistortionGetEnabled()) {
                        // Apply distortion
                        // Distortion range 0 to 1
                        voiceEffect.DistortionSetValue(
                            soundContainer.internals.data.GetDistortion(
                                holder.voiceParameter.currentModifier,
                                holder.playTypeInstance.GetScaledDistance(),
                                holder.playTypeInstance.GetScaledIntensity()
                                )
                            );
                    }

                    // Lowpass
                    voiceEffect.LowpassSetEnabled(soundContainer.internals.data.GetLowpassEnabled());
                    if (voiceEffect.LowpassGetEnabled()) {
                        // Apply lowpass
                        voiceEffect.LowpassSetValue(
                            // Frequency range 0 to 1
                            soundContainer.internals.data.GetLowpassFrequency(
                                holder.playTypeInstance.GetScaledDistance(),
                                holder.playTypeInstance.GetScaledIntensity()
                                ),
                            // Slope range 0 to 1
                            soundContainer.internals.data.GetLowpassAmount(
                                holder.playTypeInstance.GetScaledDistance(),
                                holder.playTypeInstance.GetScaledIntensity()
                                )
                        );
                    }

                    // Highpass
                    voiceEffect.HighpassSetEnabled(soundContainer.internals.data.GetHighpassEnabled());
                    if (voiceEffect.HighpassGetEnabled()) {
                        // Apply highpass
                        voiceEffect.HighpassSetValue(
                            // Frequency range 0 to 1
                            soundContainer.internals.data.GetHighpassFrequency(
                                holder.playTypeInstance.GetScaledDistance(),
                                holder.playTypeInstance.GetScaledIntensity()
                                ),
                            // Slope range 0 to 1
                            soundContainer.internals.data.GetHighpassAmount(
                                holder.playTypeInstance.GetScaledDistance(),
                                holder.playTypeInstance.GetScaledIntensity()
                                )
                        );
                    }

                    // Checks if any effects is actually enabled internally
                    voiceEffect.SetEnabled(voiceEffect.GetAnyEnabledInternal());

                    // Checks if voice effect limit is reached
                    if (voiceEffect.GetEnabled()) {
                        if (voiceEffect.checkVoiceEffectLimit) {
                            voiceEffect.checkVoiceEffectLimit = false;
                            if (!SoundManagerBase.Instance.Internals.voiceEffectPool.VoiceEffectShouldPlay(holder.voice)) {
                                voiceEffect.SetEnabled(false);
                            }
                        }
                    }

                    // Bypasses voice effects if disabled
                    voice.SetBypassVoiceEffects(!voiceEffect.GetEnabled());

                } else {
                    voice.cachedVoiceEffect.SetEnabled(false);
                    voice.SetBypassVoiceEffects(true);
                }
#endif

                // Bypass Reverb Zones
                voice.SetBypassReverbZones(soundContainer.internals.data.GetBypassReverbZones(holder.voiceParameter.currentModifier));

                // Reverb Zone Mix if not bypassed
                if (!voice.GetBypassReverbZones()) {
                    voice.SetReverbZoneMixRatio(
                        soundContainer.internals.data.GetReverbZoneMixRatio(
                            holder.voiceParameter.currentModifier,
                            holder.playTypeInstance.GetScaledDistance(),
                            holder.voiceParameter.currentModifier.intensity)
                        );
                }

                // Bypass Listener Effects
                voice.SetBypassListenerEffects(soundContainer.internals.data.GetBypassListenerEffects(holder.voiceParameter.currentModifier));

                // HRTF Spatialize
                voice.SetHrtfPluginSpatialize(soundContainer.internals.data.hrtfPluginSpatialize);
                voice.SetHrtfSpatializePostEffects(soundContainer.internals.data.hrtfPluginSpatializePostEffects);

                // Sonity Steam Audio
#if SONITY_ENABLE_INTEGRATION_STEAM_AUDIO
#if !UNITY_EDITOR
                // Makes it only update the steam audio settings once in builds
                // But enables live update editing in the editor
                if (isVoicePrepare) {
#endif
                SteamAudio.SteamAudioSource steamAudioSource = voice.cachedSteamAudioSource;
                    // HRTF Settings                                                                                                                                                  
                    steamAudioSource.directBinaural = soundContainer.internals.data.steamAudio.directBinaural;
                    steamAudioSource.interpolation = (SteamAudio.HRTFInterpolation)soundContainer.internals.data.steamAudio.interpolation;
                    steamAudioSource.perspectiveCorrection = soundContainer.internals.data.steamAudio.perspectiveCorrection;
                    // Attenuation Settings                                                                                                                                           
                    steamAudioSource.distanceAttenuation = soundContainer.internals.data.steamAudio.distanceAttenuation;
                    steamAudioSource.distanceAttenuationInput = (SteamAudio.DistanceAttenuationInput)soundContainer.internals.data.steamAudio.distanceAttenuationInput;
                    steamAudioSource.distanceAttenuationValue = soundContainer.internals.data.steamAudio.distanceAttenuationValue;
                    steamAudioSource.airAbsorption = soundContainer.internals.data.steamAudio.airAbsorption;
                    steamAudioSource.airAbsorptionInput = (SteamAudio.AirAbsorptionInput)soundContainer.internals.data.steamAudio.airAbsorptionInput;
                    steamAudioSource.airAbsorptionLow = soundContainer.internals.data.steamAudio.airAbsorptionLow;
                    steamAudioSource.airAbsorptionMid = soundContainer.internals.data.steamAudio.airAbsorptionMid;
                    steamAudioSource.airAbsorptionHigh = soundContainer.internals.data.steamAudio.airAbsorptionHigh;
                    // Directivity Settings                                                                                                                                           
                    steamAudioSource.directivity = soundContainer.internals.data.steamAudio.directivity;
                    steamAudioSource.directivityInput = (SteamAudio.DirectivityInput)soundContainer.internals.data.steamAudio.directivityInput;
                    steamAudioSource.dipoleWeight = soundContainer.internals.data.steamAudio.dipoleWeight;
                    steamAudioSource.dipolePower = soundContainer.internals.data.steamAudio.dipolePower;
                    steamAudioSource.directivityValue = soundContainer.internals.data.steamAudio.directivityValue;
                    // Occlusion Settings                                                                                                                                             
                    steamAudioSource.occlusion = soundContainer.internals.data.steamAudio.occlusion;
                    steamAudioSource.occlusionInput = (SteamAudio.OcclusionInput)soundContainer.internals.data.steamAudio.occlusionInput;
                    steamAudioSource.occlusionType = (SteamAudio.OcclusionType)soundContainer.internals.data.steamAudio.occlusionType;
                    steamAudioSource.occlusionRadius = soundContainer.internals.data.steamAudio.occlusionRadius;
                    steamAudioSource.occlusionSamples = soundContainer.internals.data.steamAudio.occlusionSamples;
                    steamAudioSource.occlusionValue = soundContainer.internals.data.steamAudio.occlusionValue;
                    steamAudioSource.transmission = soundContainer.internals.data.steamAudio.transmission;
                    steamAudioSource.transmissionType = (SteamAudio.TransmissionType)soundContainer.internals.data.steamAudio.transmissionType;
                    steamAudioSource.transmissionInput = (SteamAudio.TransmissionInput)soundContainer.internals.data.steamAudio.transmissionInput;
                    steamAudioSource.transmissionLow = soundContainer.internals.data.steamAudio.transmissionLow;
                    steamAudioSource.transmissionMid = soundContainer.internals.data.steamAudio.transmissionMid;
                    steamAudioSource.transmissionHigh = soundContainer.internals.data.steamAudio.transmissionHigh;
                    steamAudioSource.maxTransmissionSurfaces = soundContainer.internals.data.steamAudio.maxTransmissionSurfaces;
                    // Direct Mix Settings                                                                                                                                            
                    steamAudioSource.directMixLevel = soundContainer.internals.data.steamAudio.directMixLevel;
                    // Reflections Settings                                                                                                                                           
                    steamAudioSource.reflections = soundContainer.internals.data.steamAudio.reflections;
                    steamAudioSource.reflectionsType = (SteamAudio.ReflectionsType)soundContainer.internals.data.steamAudio.reflectionsType;
                    steamAudioSource.useDistanceCurveForReflections = soundContainer.internals.data.steamAudio.useDistanceCurveForReflections;
                    steamAudioSource.currentBakedSource = soundContainer.internals.data.steamAudio.currentBakedSource;
                    steamAudioSource.reflectionsIR = soundContainer.internals.data.steamAudio.reflectionsIR;
                    steamAudioSource.reverbTimeLow = soundContainer.internals.data.steamAudio.reverbTimeLow;
                    steamAudioSource.reverbTimeMid = soundContainer.internals.data.steamAudio.reverbTimeMid;
                    steamAudioSource.reverbTimeHigh = soundContainer.internals.data.steamAudio.reverbTimeHigh;
                    steamAudioSource.hybridReverbEQLow = soundContainer.internals.data.steamAudio.hybridReverbEQLow;
                    steamAudioSource.hybridReverbEQMid = soundContainer.internals.data.steamAudio.hybridReverbEQMid;
                    steamAudioSource.hybridReverbEQHigh = soundContainer.internals.data.steamAudio.hybridReverbEQHigh;
                    steamAudioSource.hybridReverbDelay = soundContainer.internals.data.steamAudio.hybridReverbDelay;
                    steamAudioSource.applyHRTFToReflections = soundContainer.internals.data.steamAudio.applyHRTFToReflections;
                    steamAudioSource.reflectionsMixLevel = soundContainer.internals.data.steamAudio.reflectionsMixLevel;
                    // Pathing Settings                                                                                                                                               
                    steamAudioSource.pathing = soundContainer.internals.data.steamAudio.pathing;
                    steamAudioSource.pathingProbeBatch = soundContainer.internals.data.steamAudio.pathingProbeBatch;
                    steamAudioSource.pathValidation = soundContainer.internals.data.steamAudio.pathValidation;
                    steamAudioSource.findAlternatePaths = soundContainer.internals.data.steamAudio.findAlternatePaths;
                    steamAudioSource.pathingEQ = soundContainer.internals.data.steamAudio.pathingEQ;
                    steamAudioSource.pathingSH = soundContainer.internals.data.steamAudio.pathingSH;
                    steamAudioSource.applyHRTFToPathing = soundContainer.internals.data.steamAudio.applyHRTFToPathing;
                    steamAudioSource.pathingMixLevel = soundContainer.internals.data.steamAudio.pathingMixLevel;

                    // Force update, otherwise eg occlusion/pathing won't work
                    steamAudioSource.UpdateOutputs(SteamAudio.SimulationFlags.Direct | SteamAudio.SimulationFlags.Reflections | SteamAudio.SimulationFlags.Pathing);
#if !UNITY_EDITOR
                }
#endif
#endif
                // Pool when fade out is finished
                if (holder.voiceFade.state == VoiceFadeState.FadePool) {
                    holder.voiceFade.Reset();
                    holder.PoolSingleVoice(false, false);
                    return;
                }
            }
        }
    }
}