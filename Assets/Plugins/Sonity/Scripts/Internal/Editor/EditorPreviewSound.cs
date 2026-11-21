// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sonity.Internal {

    public static class EditorPreviewSound {

        public class EditorPreviewSoundCurveDotValues {

            public bool use = false;
            public float distance = 0f;
            public float intensity = 1f;
            public float angleToCenter = 0f;

            public void Reset() {
                use = false;
                distance = 0f;
                intensity = 1f;
                angleToCenter = 0f;
            }
        }

        // Should be read and writeable
        private static bool update = false;
        private static bool firstUpdate = true;
        private static List<EditorPreviewVoice> voice = new List<EditorPreviewVoice>();
        private static bool stopButtonPressed = false;
        private static bool triggerOnTailPlayed = false;

        private static EditorPreviewControls editorSetting = new EditorPreviewControls();
        private static double previousTimeSinceStart = 0d;
        private static float timeSinceLastUpdate = 0f;
        private static double timeSinceStart = 0d;
        private static EditorPreviewSoundCurveDotValues soundCurvePreviewDotValues = new EditorPreviewSoundCurveDotValues();

        private static SoundEventBase soundEventCurrent = null;
        private static GameObject previewAudioListenerGameObject = null;

        // Needed for disabling domain reloading
#if UNITY_2019_2_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#else
        // Older versions than 2019.2 doesn't have the SubsystemRegistration load type
        [RuntimeInitializeOnLoadMethod]
#endif
        static void ResetStatics() {
            StopAndClearAll();

            update = false;
            firstUpdate = true;
            voice = new List<EditorPreviewVoice>();
            stopButtonPressed = false;
            triggerOnTailPlayed = false;

            editorSetting = new EditorPreviewControls();
            previousTimeSinceStart = 0d;
            timeSinceLastUpdate = 0f;
            timeSinceStart = 0d;
            soundCurvePreviewDotValues = new EditorPreviewSoundCurveDotValues();

            soundEventCurrent = null;
            previewAudioListenerGameObject = null;
        }

        public static bool IsUpdating() {
            return update;
        }

        public static float GetTimeSinceStart() {
            return (float)timeSinceStart;
        }

        public static void SetEditorSetting(EditorPreviewControls editorSetting) {
            EditorPreviewSound.editorSetting = editorSetting;
        }

        public static EditorPreviewControls GetEditorSetting() {
            return editorSetting;
        }

        public static void UseCurvePreviewValues() {
            soundCurvePreviewDotValues.Reset();
            soundCurvePreviewDotValues.use = true;
        }

        public static EditorPreviewSoundCurveDotValues GetCurvePreviewValues() {
            return soundCurvePreviewDotValues;
        }

        public static void AddEditorPreviewSoundData(EditorPreviewSoundData previewSoundData) {
            voice.Add(new EditorPreviewVoice());
            voice[voice.Count - 1].previewSoundContainerSetting = previewSoundData;
            voice[voice.Count - 1].gameObject = EditorUtility.CreateGameObjectWithHideFlags(
                "PreviewVoice", HideFlags.HideAndDontSave, new System.Type[] { typeof(AudioSource)});
            voice[voice.Count - 1].audioSource = voice[voice.Count - 1].gameObject.GetComponent<AudioSource>();
#if !SONITY_DISABLE_VOICE_EFFECTS
            voice[voice.Count - 1].voiceEffect = voice[voice.Count - 1].gameObject.AddComponent<VoiceEffect>();
            // VoiceEffect component disable breaks the unity audio system
            //// Disable component on creation
            //voice[voice.Count - 1].voiceEffect.enabled = false;
#endif
        }

        public static void PlayLastEditorPreviewSoundData(bool isTriggerOn) {
            voice[voice.Count - 1].Play(isTriggerOn);
        }

        public static void Stop(bool allowFadeOut, bool isStopButton) {
            if (!stopButtonPressed) {
                stopButtonPressed = true;
                if (allowFadeOut) {
                    StopAllowFadeOut();
                } else {
                    StopAndClearAll();
                }
                // Trigger On Stop
                if (isStopButton && update) {
                    if (soundEventCurrent != null && soundEventCurrent.internals.data.triggerOnStopEnable) {
                        TriggerOtherSoundEvent(soundEventCurrent, soundEventCurrent.internals.data.triggerOnStopSoundEvents, soundEventCurrent.internals.data.triggerOnStopWhichToPlay, SoundEventTriggerOnType.TriggerOnStop);
                    }
                }
            } else {
                StopAndClearAll();
            }
        }

        private static void StopAllowFadeOut() {
            for (int i = 0; i < voice.Count; i++) {
                if (voice[i] != null && !voice[i].isTriggerOn) {
                    voice[i].voiceFade.SetToFadeOut(voice[i].soundEventModifier);
                }
            }
        }

        private static void StopAndClearAll() {
            EditorApplication.playModeStateChanged -= PlayModeStateChanged;
            timeSinceStart = 0d;
            StopUpdateAndAudioSource();
            DestroyObjects();
            voice.Clear();
            soundCurvePreviewDotValues.Reset();
            soundEventCurrent = null;
            SetEditorSetting(new EditorPreviewControls());

            if (!Application.isPlaying) {
                // Reset AudioListener volume
                AudioListener.volume = 1f;
            }

            // Destroying the temporary audiolistener
            if (previewAudioListenerGameObject != null) {
                UnityEngine.Object.DestroyImmediate(previewAudioListenerGameObject);
            }
        }

        private static bool SoundContainerNullFound(SoundContainerBase soundContainer) {
            // SoundContainer can be null if SoundTag has SE with null SC
            if (soundContainer == null && voice[0] != null) {
                return false;
            }
            if (soundContainer.internals.audioClips.Length == 0) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity: \"{soundContainer.internals.cachedName}\" ({NameOf.SoundContainer}) has no {nameof(AudioClip)}s.", soundContainer);
                }
                return true;
            }
            for (int i = 0; i < soundContainer.internals.audioClips.Length; i++) {
                if (soundContainer.internals.audioClips[i] == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity: \"{soundContainer.internals.cachedName}\" ({NameOf.SoundContainer}) has null {nameof(AudioClip)}s.", soundContainer);
                    }
                    return true;
                }
            }
            return false;
        }

        public static void PlaySoundEvent(SoundEventBase soundEvent = null) {

            // Creating a temporary audiolistener, so that the rotation and position of the preview will be correct
            if (!Application.isPlaying) {
                if (previewAudioListenerGameObject == null) {
                    previewAudioListenerGameObject = new GameObject("SonityEditorPreviewAudioListener", typeof(AudioListener));
                    previewAudioListenerGameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                }
            }

            stopButtonPressed = false;
            triggerOnTailPlayed = false;

            if (EditorUtility.audioMasterMute) {
                if (soundEvent == null) {
                    if (voice[0] != null && voice[0].previewSoundContainerSetting.soundContainer != null) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundContainer} \"{voice[0].previewSoundContainerSetting.soundContainer.internals.cachedName}\" preview will not be heard because \"Mute Audio\" is enabled in the Unity editor");
                    }
                } else {
                    Debug.LogWarning($"Sonity.{NameOf.SoundEvent} \"{soundEvent.internals.cachedName}\" preview will not be heard because \"Mute Audio\" is enabled in the Unity editor");
                }
            }

            if (!update) {
                update = true;
                EditorApplication.update += EditorUpdate;
            }
            for (int i = 0; i < voice.Count; i++) {
                if (SoundContainerNullFound(voice[i].previewSoundContainerSetting.soundContainer)) {
                    StopAndClearAll();
                    return;
                } else {
                    voice[i].Play();
                }
            }

            // Set even if null
            soundEventCurrent = soundEvent;

            // Trigger On Play
            if (soundEventCurrent != null && soundEventCurrent.internals.data.triggerOnPlayEnable) {
                TriggerOtherSoundEvent(soundEventCurrent, soundEventCurrent.internals.data.triggerOnPlaySoundEvents, soundEventCurrent.internals.data.triggerOnPlayWhichToPlay, SoundEventTriggerOnType.TriggerOnPlay);
            }
        }

        private static void TriggerOtherSoundEvent(SoundEventBase soundEvent, SoundEventBase[] soundEvents, WhichToPlay whichToPlay, SoundEventTriggerOnType triggerType) {
            if (triggerType == SoundEventTriggerOnType.TriggerOnPlay) {
                if (soundEvent.internals.data.CheckTriggerOnPlayIsInfiniteLoop(soundEvent, true)) {
                    return;
                }
            } else if (triggerType == SoundEventTriggerOnType.TriggerOnStop) {
                if (soundEvent.internals.data.CheckTriggerOnStopIsInfiniteLoop(soundEvent, true)) {
                    return;
                }
            } else if (triggerType == SoundEventTriggerOnType.TriggerOnTail) {
                if (soundEvent.internals.data.CheckTriggerOnTailLengthTooShort(soundEvent, true)) {
                    return;
                }
            }
            if (soundEvents.Length == 0) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity: \"{soundEvent.internals.cachedName}\": The {triggerType.ToString()} has no {NameOf.SoundEvent}s.", soundEvent);
                }
            } else {
                if (whichToPlay == WhichToPlay.PlayAll) {
                    for (int i = 0; i < soundEvents.Length; i++) {
                        if (soundEvents[i] == null) {
                            if (ShouldDebug.Warnings()) {
                                Debug.LogWarning($"Sonity: \"{soundEvent.internals.cachedName}\": The {triggerType.ToString()} has null {NameOf.SoundEvent}s.", soundEvent);
                            }
                        } else {
                            AddAndPlaySoundEvent(soundEvents[i], true);
                        }
                    }
                } else if (whichToPlay == WhichToPlay.PlayOneRandom) {
                    int randomInt = UnityEngine.Random.Range(0, soundEvents.Length);
                    if (soundEvents[randomInt] == null) {
                        if (ShouldDebug.Warnings()) {
                            Debug.LogWarning($"Sonity: \"{soundEvent.internals.cachedName}\": The {triggerType.ToString()} has null {NameOf.SoundEvent}s.", soundEvent);
                        }
                    } else {
                        AddAndPlaySoundEvent(soundEvents[randomInt], true);
                    }
                }
            }
        }

        public static void AddAndPlaySoundEvent(SoundEventBase soundEvent, bool isTriggerOn) {
            soundEvent.internals.InitializeTimelineSoundContainerSetting();
            for (int i = 0; i < soundEvent.internals.soundContainers.Length; i++) {
                if (soundEvent.internals.soundContainers[i] == null) {
                    Debug.LogWarning($"Sonity: \"{soundEvent.internals.cachedName}\": ({NameOf.SoundContainer}) has null {NameOf.SoundContainer}s.", soundEvent);
                } else {
                    if (!SoundContainerNullFound(soundEvent.internals.soundContainers[i])) {
                        EditorPreviewSoundData newPreviewSoundContainerSetting = new EditorPreviewSoundData();
                        newPreviewSoundContainerSetting.soundEvent = soundEvent;
                        newPreviewSoundContainerSetting.soundContainer = soundEvent.internals.soundContainers[i];
                        newPreviewSoundContainerSetting.soundEventModifierList.Add(soundEvent.internals.data.soundEventModifier);
                        // Adding SoundMix and their parents
                        SoundMixBase tempSoundMix = soundEvent.internals.data.soundMix;
                        while (tempSoundMix != null && !tempSoundMix.internals.CheckIsInfiniteLoop(tempSoundMix, true)) {
                            newPreviewSoundContainerSetting.soundEventModifierList.Add(tempSoundMix.internals.soundEventModifier);
                            tempSoundMix = tempSoundMix.internals.soundMixParent;
                        }
                        newPreviewSoundContainerSetting.timelineSoundContainerData = soundEvent.internals.data.timelineSoundContainerData[i];
                        // Add
                        AddEditorPreviewSoundData(newPreviewSoundContainerSetting);
                        // Play
                        PlayLastEditorPreviewSoundData(isTriggerOn);
                    }
                }
            }
        }

        private static void StopUpdateAndAudioSource() {
            if (update) {
                update = false;
                firstUpdate = true;
                EditorApplication.update -= EditorUpdate;
            }
            for (int i = 0; i < voice.Count; i++) {
                if (voice[i] != null) {
                    voice[i].audioSource.Stop();
                }
            }
        }

        private static void PlayModeStateChanged(PlayModeStateChange state) {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) {
                StopAndClearAll();
            }
        }

        public static void EditorUpdate() {
            float audioListenerVolume = 1f;
            if (SoundManagerBase.Instance != null) {
                audioListenerVolume *= SoundManagerBase.Instance.Internals.settings.globalVolumeRatio;
            }
#if SONITY_ENABLE_VOLUME_INCREASE
            audioListenerVolume *= VolumeScale.volumeIncrease60dbAudioListenerRatioMax;
#endif
            AudioListener.volume = audioListenerVolume;

            // Timer for Fade and Timeline
            if (firstUpdate) {
                firstUpdate = false;
                timeSinceLastUpdate = 0f;
                timeSinceStart = 0f;
                EditorApplication.playModeStateChanged += PlayModeStateChanged;
                // Intensity smoothing
                for (int i = 0; i < voice.Count; i++) {
                    if (voice[i] != null) {
                        voice[i].intensityCurrent = editorSetting.intensityTarget;
                    }
                }
            } else {
                timeSinceLastUpdate = (Mathf.Clamp((float)EditorApplication.timeSinceStartup - (float)previousTimeSinceStart, 0f, Mathf.Infinity));
                timeSinceStart += EditorApplication.timeSinceStartup - previousTimeSinceStart;
            }
            previousTimeSinceStart = EditorApplication.timeSinceStartup;

            // Intensity smoothing
            for (int i = 0; i < voice.Count; i++) {
                if (voice[i] != null) {
                    // If previewing SC then SE is null
                    if (voice[i].previewSoundContainerSetting.soundEvent != null && voice[i].previewSoundContainerSetting.soundEvent.internals.data.intensitySeekTime > 0f) {
                        // Smoothing
                        if (voice[i].intensityCurrent > editorSetting.intensityTarget) {
                            voice[i].intensityCurrent = Mathf.Clamp(voice[i].intensityCurrent - timeSinceLastUpdate / voice[i].previewSoundContainerSetting.soundEvent.internals.data.intensitySeekTime, editorSetting.intensityTarget, 1f);
                        } else {
                            voice[i].intensityCurrent = Mathf.Clamp(voice[i].intensityCurrent + timeSinceLastUpdate / voice[i].previewSoundContainerSetting.soundEvent.internals.data.intensitySeekTime, 0f, editorSetting.intensityTarget);
                        }
                    } else {
                        // No smoothing
                        voice[i].intensityCurrent = editorSetting.intensityTarget;
                    }
                }
            }

            if (soundCurvePreviewDotValues.use) {
                soundCurvePreviewDotValues.distance = Vector3.Distance(Vector3.zero - Vector3.zero, GetEditorSetting().position);
                // Intensity Only for SoundContainer
                if (voice.Count > 0 && voice[0] != null) {
                    soundCurvePreviewDotValues.intensity = voice[0].intensityCurrent;
                }
                soundCurvePreviewDotValues.angleToCenter = AngleAroundAxis.Get(Vector3.zero - GetEditorSetting().position, Vector3.forward, Vector3.up);
            }

            // Trigger On Tail
            if (!triggerOnTailPlayed && soundEventCurrent != null && soundEventCurrent.internals.data.triggerOnTailEnable) {
                // Only first SC in SE triggers tail
                if (voice.Count > 0 && voice[0] != null) {
                    // Removed "voice[0].audioSource.isPlaying && " so that TriggerOnTail tail length 0 works in preview
                    if (voice[0].audioSource.time >= voice[0].audioSource.clip.length - soundEventCurrent.internals.data.triggerOnTailLength) {
                        triggerOnTailPlayed = true;
                        TriggerOtherSoundEvent(soundEventCurrent, soundEventCurrent.internals.data.triggerOnTailSoundEvents, soundEventCurrent.internals.data.triggerOnTailWhichToPlay, SoundEventTriggerOnType.TriggerOnTail);
                    }
                }
            }

            for (int i = 0; i < voice.Count; i++) {
                if (voice[i] != null) {
                    if (voice[i].played) {
                        voice[i].UpdateSoundContainerModifier();
                        voice[i].UpdateVoice();
                    } else {
                        if (!stopButtonPressed || voice[i].isTriggerOn) {
                            voice[i].InternalPlayDelay((float)timeSinceStart);
                        }
                    }
                }
            }

            for (int i = 0; i < voice.Count; i++) {
                if (voice[i] != null) {
                    // So that e.g. loops will be stopped properly
                    if (voice[i].voiceFade.state == VoiceFadeState.FadePool) {
                        voice[i].audioSource.Stop();
                    }
                }
            }

            int voiceTotalCounter = 0;
            int voicePoolCounter = 0;

            for (int i = 0; i < voice.Count; i++) {
                if (voice[i] != null) {
                    if (voice[i].played && !voice[i].audioSource.isPlaying && voice[i].voiceFade.state != VoiceFadeState.FadeIn) {
                        voicePoolCounter++;
                    }
                    voiceTotalCounter++;
                }
            }
            if (voicePoolCounter >= voiceTotalCounter) {
                Stop(false, false);
            }
        }

        public static void DestroyObjects() {
            for (int i = 0; i < voice.Count; i++) {
                if (voice[i] != null) {
#if !SONITY_DISABLE_VOICE_EFFECTS
                    try {
                        if (voice[i].voiceEffect != null) {
                            UnityEngine.Object.DestroyImmediate(voice[i].voiceEffect);
                        }
                    } catch {
                        // Nothing
                    }
#endif
                    try {
                        if (voice[i].audioSource != null) {
                            UnityEngine.Object.DestroyImmediate(voice[i].audioSource);
                        }
                    } catch {
                        // Nothing
                    }
                    try {
                        if (voice[i].gameObject != null) {
                            UnityEngine.Object.DestroyImmediate(voice[i].gameObject);
                        }
                    } catch {
                        // Nothing
                    }
                }
                
            }
        }
    }
}
#endif