// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;
using UnityEngine.Audio;
using System;

namespace Sonity.Internal {

    [Serializable]
    public class Voice {

        public Voice(string name, Transform parentTransform) {
            GameObject created = new GameObject(name, typeof(AudioSource));
            cachedAudioSource = created.GetComponent<AudioSource>();
            cachedAudioSource.playOnAwake = false;
            cachedAudioSource.dopplerLevel = 0f;
            cachedAudioSource.rolloffMode = AudioRolloffMode.Custom;
            cachedAudioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0, 1, 1, 1));
#if !SONITY_DISABLE_VOICE_EFFECTS
            cachedVoiceEffect = created.AddComponent<VoiceEffect>();
            // VoiceEffect component disable breaks the unity audio system
            //// Disable component on creation
            //cachedVoiceEffect.enabled = false;
#endif
            cachedGameObject = created;
            cachedTransform = cachedGameObject.transform;
            cachedTransform.parent = parentTransform;
            // Sonity Steam Audio
#if SONITY_ENABLE_INTEGRATION_STEAM_AUDIO
            cachedSteamAudioSource = created.AddComponent<SteamAudio.SteamAudioSource>();
#endif
        }

        [NonSerialized]
        public GameObject cachedGameObject;
        [NonSerialized]
        public Transform cachedTransform;
        [NonSerialized]
        public UnityEngine.Vector3 position = new UnityEngine.Vector3(0f, 0f, 0f);
        [NonSerialized]
        public Quaternion rotation = Quaternion.identity;

        public void SetPostion(UnityEngine.Vector3 newPostion) {
            // Only change Transform position when its actually changed
            if (position != newPostion) {
                position = newPostion;
                cachedTransform.localPosition = newPostion;
            }
        }

        public void SetRotation(Quaternion newRotation) {
            // Only change Transform rotation when its actually changed
            if (rotation != newRotation) {
                rotation = newRotation;
                cachedTransform.localRotation = newRotation;
            }
        }

        [NonSerialized]
        public AudioSource cachedAudioSource;
#if !SONITY_DISABLE_VOICE_EFFECTS
        [NonSerialized]
        public VoiceEffect cachedVoiceEffect;
#endif

#if SONITY_ENABLE_INTEGRATION_STEAM_AUDIO
        [NonSerialized]
        public SteamAudio.SteamAudioSource cachedSteamAudioSource;
#endif

        [NonSerialized]
        public int voiceIndex;

        [NonSerialized]
        public bool isAssigned;
        [NonSerialized]
        public float stopTime;
        [NonSerialized]
        public VoiceHolder instanceVoiceHolder;
        [NonSerialized]
        public SoundEventBase soundEvent;
#if UNITY_EDITOR
        // Is cached for the Draw SoundEvents
        [NonSerialized]
        public string soundEventName = "";
#endif
        [NonSerialized]
        public SoundContainerBase soundContainer;

        [NonSerialized]
        public bool clickFadeActive;
        [NonSerialized]
        public float clickFadeStartTime;

        // Used to calculate the time played
        [NonSerialized]
        public float lastStartTime;

        public float GetTimePlayed() {
            return SoundTimeScale.GetTimeRuntime() - lastStartTime;
        }

        public void PoolVoice(bool shouldRestartIfLoop, bool isCalledByOnDestroy) {
            if (instanceVoiceHolder != null) {
                instanceVoiceHolder.PoolSingleVoice(shouldRestartIfLoop, isCalledByOnDestroy);
            }
        }

        // Priority is later multiplied with volume when evaluated
        [NonSerialized]
        public float priority;

        public void SetAudioSourcePriority(float priority) {
            // For Unity AudioSources 0 is highest priority and 255 is lowest priority
            // Here 1 is high priority and 0 is low priority
            cachedAudioSource.priority = Mathf.RoundToInt((1f - priority) * 255f);
        }

        [NonSerialized]
        public AudioMixerGroup cachedAudioMixerGroup;

        private bool previousIsPlaying = false;

        public bool GetVoiceIsPlaying() {
            // If application is paused, then AudioSource.isPlaying will return false if not running in background.
            if (SoundManagerBase.Instance.Internals.onApplicationIsPaused) {
                return previousIsPlaying;
            } else {
                previousIsPlaying = cachedAudioSource.isPlaying;
                return cachedAudioSource.isPlaying;
            }
        }

        public void AssignVoice() {
            isAssigned = true;
            if (!cachedGameObject.activeSelf) {
                cachedGameObject.SetActive(true);
            }
        }

        public void ResetVoice() {
            if (cachedAudioSource != null) {
                cachedAudioSource.volume = 0f;
            }
            isAssigned = false;
            instanceVoiceHolder = null;
            soundEvent = null;
            // Resetting the SoundEventName is not necessary
            soundContainer = null;
            priority = 0;
            previousIsPlaying = false;
        }

        public void StopOnDestroy() {
            // OnDestroy might destroy the AudioSources first
            if (cachedAudioSource != null) {
                cachedAudioSource.volume = 0f;
                cachedAudioSource.Stop();
            }
            state = VoiceState.Stop;
        }

        [NonSerialized]
        private VoiceState state;

        public VoiceState GetState() {
            return state;
        }

        [NonSerialized]
        public bool playOneShot = false;

        [NonSerialized]
        public AudioClip cachedAudioClip;

        public void SetState(VoiceState newState, bool force) {
            if (newState == VoiceState.Play) {
                if (force || state != VoiceState.Play) {
                    if (state == VoiceState.Pause) {
                        cachedAudioSource.UnPause();
                    }
                    if (SoundManagerBase.Instance.Internals.settings.playOneShotOptimization) {
                        if (playOneShot) {
                            // Stop before playing, otherwise old voice will overlap with new and maybe old Voices will move to new Instance with different sound
                            cachedAudioSource.Stop();
                            cachedAudioSource.PlayOneShot(cachedAudioClip);
                        } else {
                            cachedAudioSource.Play();
                        }
                    } else {
                        cachedAudioSource.Play();
                    }
                    state = newState;
#if UNITY_EDITOR
                    SoundManagerBase.Instance.Internals.statistics.statisticsVoicesPlayed++;
#endif
                }
            } else if (newState == VoiceState.Pause) {
                if (force || state != VoiceState.Pause) {
                    cachedAudioSource.Pause();
                    state = newState;
                }
            } else if (newState == VoiceState.Stop) {
                if (force || state != VoiceState.Stop) {
                    cachedAudioSource.Stop();
                    state = newState;
                }
            }
        }

        // Volume
        [NonSerialized]
        private float volumeRatioCurrent = 1f;
        [NonSerialized]
        private float volumeRatioFadeCurrent = 1f;

        [NonSerialized]
        private float volumeRandomRatio = 1f;

        public void SetVolumeRandomRatio(float volumeRandomRatio) {
            this.volumeRandomRatio = volumeRandomRatio;
        }

        public void SetVolumeRatioFirst(float volumeRatio, float volumeFade) {
            volumeRatioCurrent = volumeRatio * volumeRandomRatio;
            volumeRatioFadeCurrent = volumeFade;
#if SONITY_ENABLE_VOLUME_INCREASE
            // With AudioListener Volume Increase
            cachedAudioSource.volume = volumeRatioCurrent * volumeRatioFadeCurrent * VolumeScale.volumeIncrease60dbAudioListenerRatioLowerBack;
#else
            // Without AudioListener Volume Increase
            cachedAudioSource.volume = volumeRatioCurrent * volumeRatioFadeCurrent;
#endif
            clickFadeActive = false;
        }

        public void SetVolumeRatioUpdate(float volumeRatio, float volumeFade) {
            // PlayOneShot doesnt support getting AudioSource.time
            if (!playOneShot) {
                if (soundContainer.internals.data.preventEndClicks && !soundContainer.internals.data.loopEnabled && GetAudioSourceClipLengthSeconds(true) > 0.1f) {
                    if (GetAudioSourceTimeSeconds(true) >= GetAudioSourceClipLengthSeconds(true) - 0.1f) {
                        if (!clickFadeActive && GetAudioSourceClipLengthSeconds(true) - GetAudioSourceTimeSeconds(true) < 0.1f) {
                            clickFadeActive = true;
                            clickFadeStartTime = SoundTimeScale.GetTimeRuntime();
                        }
                        if (clickFadeActive) {
                            // AudioSource.Time changes only every 0.022 seconds when DSP Buffer Size is set to Best Performance
                            // * 10 to scale to 0.1s, * 2 - 1 for silence at 0.05s
                            volumeFade *= LogLinExp.Get(Mathf.Clamp((0.1f - (SoundTimeScale.GetTimeRuntime() - clickFadeStartTime)) * 10f * 2f - 1f, 0f, 1f), -2f);
                        }
                    } else {
                        clickFadeActive = false;
                    }
                }
            }
            // volumeRandomRatio never changes after start
            if (volumeRatioCurrent != volumeRatio || volumeRatioFadeCurrent != volumeFade) {
                volumeRatioCurrent = volumeRatio * volumeRandomRatio;
                volumeRatioFadeCurrent = volumeFade;
#if SONITY_ENABLE_VOLUME_INCREASE
                // With AudioListener Volume Increase
                cachedAudioSource.volume = volumeRatioCurrent * volumeRatioFadeCurrent * VolumeScale.volumeIncrease60dbAudioListenerRatioLowerBack;
#else
                // Without AudioListener Volume Increase
                cachedAudioSource.volume = volumeRatioCurrent * volumeRatioFadeCurrent;
#endif
            }
#if UNITY_EDITOR
            if (soundEvent.internals.data.muteEnable || (SoundManagerBase.Instance.Internals.GetSoloEnabled() && !soundEvent.internals.data.soloEnable)) {
                cachedAudioSource.volume = 0f;
            }
#endif
        }

        public float GetVolumeRatioWithoutFade() {
            return volumeRatioCurrent;
        }

        public float GetVolumeRatioWithoutFadeWithPriority() {
            return volumeRatioCurrent * priority;
        }

        public float GetVolumeRatioWithFade() {
            return volumeRatioCurrent * volumeRatioFadeCurrent;
        }

        // Pitch
        [NonSerialized]
        private float pitchRatioStarting = 1f;
        [NonSerialized]
        private float pitchRatioCurrent = 1f;
        [NonSerialized]
        private bool reverseCurrent = false;

        public void SetPitchRatioStarting(float pitchRatio) {
            pitchRatioStarting = pitchRatio;
        }

        public void SetPitchRatioFirst(float pitchRatio, bool reverse) {
            pitchRatioCurrent = pitchRatio;
            reverseCurrent = reverse;
            if (reverse) {
                cachedAudioSource.pitch = -pitchRatioStarting * pitchRatioCurrent;
            } else {
                cachedAudioSource.pitch = pitchRatioStarting * pitchRatioCurrent;
            }
        }

        public void SetPitchRatioUpdate(float pitchRatio, bool reverse) {
            if (pitchRatioCurrent != pitchRatio || reverseCurrent != reverse) {
                pitchRatioCurrent = pitchRatio;
                reverseCurrent = reverse;
                if (reverse) {
                    cachedAudioSource.pitch = -pitchRatioStarting * pitchRatioCurrent;
                } else {
                    cachedAudioSource.pitch = pitchRatioStarting * pitchRatioCurrent;
                }
            }
        }

        // Spatial Blend
        [NonSerialized]
        private float spatialBlendCurrent = 0f;

        public void SetSpatialBlend(float spatialBlend) {
            if (spatialBlendCurrent != spatialBlend) {
                spatialBlendCurrent = spatialBlend;
                cachedAudioSource.spatialBlend = spatialBlend;
            }
        }

        // Spatial Spread
        [NonSerialized]
        // AudioSource default value is 0f
        private float spatialSpreadRatioCurrent = 0f;

        public void SetSpatialSpreadRatio(float spatialSpreadRatio) {
            if (spatialSpreadRatioCurrent != spatialSpreadRatio) {
                spatialSpreadRatioCurrent = spatialSpreadRatio;
                cachedAudioSource.spread = spatialSpreadRatioCurrent * 360f;
            }
        }

        // Reverb Zone Mix
        [NonSerialized]
        // AudioSource default is 1f
        private float reverbZoneMixRatioCurrent = 1f;

        public void SetReverbZoneMixRatio(float reverbZoneMixRatio) {
            // Range 0 to 1 is linear, 1.1 is 10 db boost(* 3.1622776601683795)
            if (reverbZoneMixRatio > 1f) {
                reverbZoneMixRatio = reverbZoneMixRatio * 0.031622776601683795f + 1f;
            }
            if (reverbZoneMixRatioCurrent != reverbZoneMixRatio) {
                reverbZoneMixRatioCurrent = reverbZoneMixRatio;
                cachedAudioSource.reverbZoneMix = reverbZoneMixRatio;
            }
        }

        // Stereo Pan
        // Is range -1 (behind left) to 1 (behind right)
        [NonSerialized]
        private float stereoPanCurrent = 0f;

        public void SetStereoPan(float stereoPan) {
            if (stereoPanCurrent != stereoPan) {
                stereoPanCurrent = stereoPan;
                cachedAudioSource.panStereo = stereoPan;
            }
        }

        // Bypass Reverb Zones
        [NonSerialized]
        private bool bypassReverbZonesCurrent = false;

        public void SetBypassReverbZones(bool bypassReverbZones) {
            if (bypassReverbZonesCurrent != bypassReverbZones) {
                bypassReverbZonesCurrent = bypassReverbZones;
                cachedAudioSource.bypassReverbZones = bypassReverbZones;
            }
        }

        public bool GetBypassReverbZones() {
            return bypassReverbZonesCurrent;
        }

        // Bypass Voice Effects
        [NonSerialized]
        private bool bypassVoiceEffectsCurrent = false;

        public void SetBypassVoiceEffects(bool bypassVoiceEffects) {
            if (bypassVoiceEffectsCurrent != bypassVoiceEffects) {
                bypassVoiceEffectsCurrent = bypassVoiceEffects;
                cachedAudioSource.bypassEffects = bypassVoiceEffects;
            }
        }

        public bool GetBypassVoiceEffects() {
            return bypassVoiceEffectsCurrent;
        }

        // Bypass Listerner Effects
        [NonSerialized]
        private bool bypassListenerEffectsCurrent = false;

        public void SetBypassListenerEffects(bool bypassListenerEffects) {
            if (bypassListenerEffectsCurrent != bypassListenerEffects) {
                bypassListenerEffectsCurrent = bypassListenerEffects;
                cachedAudioSource.bypassListenerEffects = bypassListenerEffects;
            }
        }

        // HRTF Plugin Spatialize
        [NonSerialized]
        private bool hrtfPluginSpatializeCurrent = false;

        public void SetHrtfPluginSpatialize(bool hrtfSpatialize) {
            if (hrtfPluginSpatializeCurrent != hrtfSpatialize) {
                hrtfPluginSpatializeCurrent = hrtfSpatialize;
                cachedAudioSource.spatialize = hrtfSpatialize;
            }
        }

        // HRTF Plugin Spatialize Post Effects
        [NonSerialized]
        private bool hrtfPluginSpatializePostEffectsCurrent = false;

        public void SetHrtfSpatializePostEffects(bool hrtfSpatializePostEffects) {
            if (hrtfPluginSpatializePostEffectsCurrent != hrtfSpatializePostEffects) {
                hrtfPluginSpatializePostEffectsCurrent = hrtfSpatializePostEffects;
                cachedAudioSource.spatializePostEffects = hrtfSpatializePostEffects;
            }
        }

        public float GetAudioSourceClipLengthSeconds(bool pitchSpeed) {
            // PlayOneShot doesnt assign an AudioClip to the AudioSource
            if (playOneShot) {
                if (cachedAudioClip == null) {
                    return Mathf.Infinity;
                }
                if (pitchSpeed) {
                    // Avoid divide by zero
                    if (cachedAudioSource.pitch == 0f) {
                        return cachedAudioClip.length;
                    }
                    // Abs so that reversed audioSource with negative pitch does not return a negative number
                    // Though PlayOneShot can't play reversed, but hey
                    return cachedAudioClip.length / Mathf.Abs(cachedAudioSource.pitch);
                } else {
                    return cachedAudioClip.length;
                }
            } else {
                if (cachedAudioSource.clip == null) {
                    return Mathf.Infinity;
                }
                if (pitchSpeed) {
                    // Avoid divide by zero
                    if (cachedAudioSource.pitch == 0f) {
                        return cachedAudioSource.clip.length;
                    }
                    // Abs so that reversed audioSource with negative pitch does not return a negative number
                    return cachedAudioSource.clip.length / Mathf.Abs(cachedAudioSource.pitch);
                } else {
                    return cachedAudioSource.clip.length;
                }
            }
        }

        public float GetAudioSourceTimeSeconds(bool pitchSpeed) {
            if (playOneShot) {
                Debug.LogWarning(
                    $"Sonity.{NameOf.SoundEvent}.GetLastPlayedClipTimeSeconds() in {soundEvent.internals.cachedName} " +
                    $"cannot be used in combination with \"PlayOneShot Optimize\", " +
                    $"to fix it, enable \"No PlayOneShot Optimize\" in the first {NameOf.SoundContainer} of the {NameOf.SoundEvent}.", soundEvent);
                return 0f;
            } else {
                if (pitchSpeed) {
                    // Avoid divide by zero
                    if (cachedAudioSource.pitch == 0f) {
                        return cachedAudioSource.time;
                    }
                    // Abs so that reversed audioSource with negative pitch does not return a negative number
                    return cachedAudioSource.time / Mathf.Abs(cachedAudioSource.pitch);
                } else {
                    return cachedAudioSource.time;
                }
            }
        }

        public float GetAudioSourceTimeRatio() {
            if (playOneShot) {
                Debug.LogWarning(
                    $"Sonity.{NameOf.SoundEvent}.GetLastPlayedClipTimeRatio() in {soundEvent.internals.cachedName} " +
                    $"cannot be used in combination with \"PlayOneShot Optimize\", " +
                    $"to fix it, enable \"No PlayOneShot Optimize\" in the first {NameOf.SoundContainer} of the {NameOf.SoundEvent}.", soundEvent);
                return 0f;
            } else {
                if (cachedAudioSource.clip == null) {
                    return 0f;
                }
                float clipLength = cachedAudioSource.clip.length;
                if (clipLength > 0f) {
                    return cachedAudioSource.time / clipLength;
                }
                return 0f;
            }
        }

        public void GetAudioSourceSpectrumData(ref float[] samples, int channel, FFTWindow window) {
            if (playOneShot) {
                Debug.LogWarning(
                    $"Sonity.{NameOf.SoundEvent}.GetSpectrumData() in {soundEvent.internals.cachedName} " +
                    $"cannot be used in combination with \"PlayOneShot Optimize\", " +
                    $"to fix it, enable \"No PlayOneShot Optimize\" in the first {NameOf.SoundContainer} of the {NameOf.SoundEvent}.", soundEvent);
                return;
            } else {
                if (cachedAudioSource.clip == null) {
                    return;
                } else {
                    cachedAudioSource.GetSpectrumData(samples, channel, window);
                }
            }
        }

        public AudioSource GetAudioSource() {
            return cachedAudioSource;
        }

        // Not used atm
        public float GetAudioSourceClipLengthSamples(bool pitchSpeed) {
            // PlayOneShot doesnt assign an AudioClip to the AudioSource
            if (playOneShot) {
                if (cachedAudioClip == null) {
                    return 0f;
                }
                if (pitchSpeed) {
                    // Avoid divide by zero
                    if (cachedAudioSource.pitch == 0f) {
                        return cachedAudioClip.samples;
                    }
                    // Abs so that reversed audioSource with negative pitch does not return a negative number
                    // Though PlayOneShot can't play reversed, but hey
                    return cachedAudioClip.samples / Mathf.Abs(cachedAudioSource.pitch);
                } else {
                    return cachedAudioClip.samples;
                }
            } else {
                if (cachedAudioSource.clip == null) {
                    return 0f;
                }
                if (pitchSpeed) {
                    // Avoid divide by zero
                    if (cachedAudioSource.pitch == 0f) {
                        return cachedAudioSource.clip.samples;
                    }
                    // Abs so that reversed audioSource with negative pitch does not return a negative number
                    return cachedAudioSource.clip.samples / Mathf.Abs(cachedAudioSource.pitch);
                } else {
                    return cachedAudioSource.clip.samples;
                }
            }
        }

        // Not used atm
        public float GetAudioSourceTimeSamples(bool pitchSpeed) {
            if (playOneShot) {
                Debug.LogWarning(
                    $"Sonity.{NameOf.SoundEvent}.GetLastPlayedClipTimeSamples() in {soundEvent.internals.cachedName} " +
                    $"cannot be used in combination with \"Optimize Optimization\", " +
                    $"to fix it, enable \"No PlayOneShot Optimize\" in the first {NameOf.SoundContainer} of the {NameOf.SoundEvent}.", soundEvent);
                return 0f;
            } else {
                if (pitchSpeed) {
                    // Avoid divide by zero
                    if (cachedAudioSource.pitch == 0f) {
                        return cachedAudioSource.timeSamples;
                    }
                    // Abs so that reversed audioSource with negative pitch does not return a negative number
                    return cachedAudioSource.timeSamples / Mathf.Abs(cachedAudioSource.pitch);
                } else {
                    return cachedAudioSource.timeSamples;
                }
            }
        }
    }
}