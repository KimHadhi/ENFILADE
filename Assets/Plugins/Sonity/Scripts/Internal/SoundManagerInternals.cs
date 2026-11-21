// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;
using UnityEngine.Audio;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Sonity.Internal {

    public class SoundEventInstanceGroup {
        // Is used to check if the SoundEvent has been unloaded by eg addressables
        public SoundEventBase soundEvent;
        // Even if SoundEvent is deleted and null, the guid will be left
        public SonityGuid sonityGuid;
        public List<SoundEventInstance> activeInstances = new List<SoundEventInstance>();
    }

    public class CheckedDictionaryValueSoundEvent {

        public bool nullChecked = false;
        public bool isNull = false;

        public bool savedMaxLengthChecked = false;
        public float savedMaxLength = 0f;

        public bool savedMinLengthChecked = false;
        // Is MinLengthWithoutPitchOnlyFirstSoundContainer
        public float savedMinLength = 0f;

        public bool savedMaxDistanceChecked = false;
        public float savedMaxDistance = 0f;

        public bool triggerOnPlayIsInfiniteLoop = false;
        public bool triggerOnPlayIsInfiniteLoopChecked = false;
        public bool triggerOnStopIsInfiniteLoop = false;
        public bool triggerOnStopIsInfiniteLoopChecked = false;
        // TriggerOnTail should be allowed to loop for e.g. music

        public int statisticsNumberOfPlays = 0;
    }

    public class CheckedDictionaryValueSoundDataGroup {
        public bool isInfiniteLoopChecked = false;
        public bool isInfiniteLoop = false;
    }

    public class CheckedDictionaryValueSoundMix {
        public bool isInfiniteLoopChecked = false;
        public bool isInfiniteLoop = false;
    }

    public class CheckedDictionaryValueSoundPhysicsCondition {
        public bool isInfiniteLoopChecked = false;
        public bool isInfiniteLoop = false;
    }

    [Serializable]
    public class SoundManagerInternals {

        public SoundManagerInternalsSettings settings = new SoundManagerInternalsSettings();
#if UNITY_EDITOR
        public SoundManagerInternalsEditorTools editorTools = new SoundManagerInternalsEditorTools();
        public SoundManagerInternalsDebug debug = new SoundManagerInternalsDebug();
        public SoundManagerInternalsStatistics statistics = new SoundManagerInternalsStatistics();
#endif
        [NonSerialized]
        public bool onApplicationIsPaused = false;
        [NonSerialized]
        public bool isGoingToDelete = false;
        [NonSerialized]
        public bool applicationIsQuitting = false;

        [NonSerialized]
        public Dictionary<SonityGuid, SoundEventInstanceGroup> soundEventGroupDictionary = new Dictionary<SonityGuid, SoundEventInstanceGroup>();

        [NonSerialized]
        public ActiveList<SoundEventInstance> soundEventInstanceList = new ActiveList<SoundEventInstance>();

        [NonSerialized]
        public SoundManagerVoicePool voicePool = new SoundManagerVoicePool();
#if !SONITY_DISABLE_VOICE_EFFECTS
        [NonSerialized]
        public SoundManagerVoiceEffectPool voiceEffectPool = new SoundManagerVoiceEffectPool();
#endif
        [NonSerialized]
        public Dictionary<SonityGuid, CheckedDictionaryValueSoundEvent> checkedDictionarySoundEvent = new Dictionary<SonityGuid, CheckedDictionaryValueSoundEvent>();
        [NonSerialized]
        public Dictionary<SonityGuid, CheckedDictionaryValueSoundDataGroup> checkedDictionarySoundDataGroup = new Dictionary<SonityGuid, CheckedDictionaryValueSoundDataGroup>();
        [NonSerialized]
        public Dictionary<SonityGuid, CheckedDictionaryValueSoundMix> checkedDictionarySoundMix = new Dictionary<SonityGuid, CheckedDictionaryValueSoundMix>();
        [NonSerialized]
        public Dictionary<SonityGuid, CheckedDictionaryValueSoundPhysicsCondition> checkedDictionarySoundPhysicsCondition = new Dictionary<SonityGuid, CheckedDictionaryValueSoundPhysicsCondition>();

        // Chached Objects
        [NonSerialized]
        public Transform cachedSoundManagerTransform;
        [NonSerialized]
        public GameObject cachedVoicePoolGameObject;
        [NonSerialized]
        public Transform cachedVoicePoolTransform;
        [NonSerialized]
        public GameObject cachedMusicGameObject;
        [NonSerialized]
        public Transform cachedMusicTransform;
        [NonSerialized]
        public GameObject cachedUIGameObject;
        [NonSerialized]
        public Transform cachedUITransform;
        [NonSerialized]
        public AudioListener cachedAudioListener;
        [NonSerialized]
        public Transform cachedAudioListenerTransform;
        [NonSerialized]
        public AudioListenerDistanceBase cachedAudioListenerDistance;
        [NonSerialized]
        public Transform cachedAudioListenerDistanceTransform;

        public void AwakeCheck() {
            AwakeSetup();
            if (!settings.asyncStartup) {
                AwakeSetupPreloadVoices();
            }
#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
            // Addressables preload AudioMixer
            if (settings.addressableAudioMixerUse && !settings.GetAddressableAudioMixerAsyncLoad()) {
                settings.LoadAddressableAudioMixer();
            }
#endif
        }

        public IEnumerator AwakeCheckAsync() {
            if (settings.asyncStartup) {
                // AwakeSetup is run before async to set correct time for fades etc
                AwakeSetupPreloadVoices();
#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
                if (settings.addressableAudioMixerUse && settings.GetAddressableAudioMixerAsyncLoad()) {
                    yield return settings.LoadAddressableAudioMixerAsync();
                }
#endif
            }
            yield return null; // Is needed if addressable audiomixer is disabled
        }

        private void AwakeSetupPreloadVoices() {
#if SONITY_ENABLE_INTEGRATION_STEAM_AUDIO
                if (settings.voicePreload > 0 && !settings.asyncStartup) {
                    Debug.LogError($"Sonity.{NameOf.SoundManager}: Steam Audio in combination with Voice Preload requires Async Startup.");
                }
#endif
            // Preload Voices on Awake
            voicePool.CreateVoice(settings.voicePreload, true);
        }

        private void AwakeSetup() {
            FindAudioListener(true);
            FindAudioListenerDistance(true);
            if (settings.dontDestroyOnLoad) {
                // DontDestroyOnLoad only works for root GameObjects
                SoundManagerBase.Instance.gameObject.transform.parent = null;
                UnityEngine.Object.DontDestroyOnLoad(SoundManagerBase.Instance.gameObject);
            }
            if (cachedVoicePoolGameObject == null) {
                cachedVoicePoolGameObject = new GameObject("SonityVoicePool");
                cachedVoicePoolTransform = cachedVoicePoolGameObject.transform;
                cachedVoicePoolTransform.parent = cachedSoundManagerTransform;
            }
            if (cachedMusicGameObject == null) {
                cachedMusicGameObject = new GameObject("SonityMusic");
                cachedMusicTransform = cachedMusicGameObject.transform;
                cachedMusicTransform.parent = cachedSoundManagerTransform;
            }
            if (cachedUIGameObject == null) {
                cachedUIGameObject = new GameObject("SonityUI");
                cachedUITransform = cachedUIGameObject.transform;
                cachedUITransform.parent = cachedSoundManagerTransform;
            }
            cachedSoundManagerTransform.position = Vector3.zero;
            cachedVoicePoolTransform.position = Vector3.zero;
            cachedMusicTransform.position = Vector3.zero;
            cachedUITransform.position = Vector3.zero;
#if SONITY_ENABLE_VOLUME_INCREASE
            AudioListener.volume = settings.globalVolumeRatio * VolumeScale.volumeIncrease60dbAudioListenerRatioMax;
#else
            AudioListener.volume = settings.globalVolumeRatio;
#endif
            // Update time value based on the selected time scale
            SoundTimeScale.UpdateTime(settings.soundTimeScale);
        }

        public void Destroy() {
            isGoingToDelete = true;
            InternalOnDestroyForceStopEverything();
            if (cachedMusicGameObject != null) {
                UnityEngine.Object.Destroy(cachedMusicGameObject);
            }
            if (cachedUIGameObject != null) {
                UnityEngine.Object.Destroy(cachedUIGameObject);
            }
        }

        public void FindAudioListener(bool isAwake = false) {
            if (cachedAudioListener == null || !cachedAudioListener.isActiveAndEnabled) {
#if UNITY_6000_4_OR_NEWER || SONITY_DLL_RUNTIME
                // Unity 6000.4 removes FindObjectsSortMode
                AudioListener[] audioListenerList = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
#elif UNITY_2022_3_OR_NEWER
                // FindObjectsSortMode.InstanceID is slower but is more consistent
                AudioListener[] audioListenerList = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);
#else
                AudioListener[] audioListenerList = UnityEngine.Object.FindObjectsOfType<AudioListener>();
#endif
                foreach (AudioListener audioListener in audioListenerList) {
                    if (audioListener != null && audioListener.isActiveAndEnabled) {
                        // Finds the first enabled AudioListener
                        cachedAudioListener = audioListener;
                        break;
                    }
                }
                if (cachedAudioListener == null || !cachedAudioListener.isActiveAndEnabled) {
                    // On awake with domain reload disabled, the AudioListener is not activeAndEnabled
                    if (!isAwake) {
                        if (ShouldDebug.Warnings()) {
                            Debug.LogWarning(
                                $"Sonity.{NameOf.SoundManager} could find no active {nameof(AudioListener)} in the scene." +
                                $"If you are creating one in runtime, make sure it is created in Awake()."
                                );
                        }
                    }
                } else {
                    cachedAudioListenerTransform = cachedAudioListener.transform;
                    AudioListener.pause = settings.globalPause;
#if SONITY_ENABLE_VOLUME_INCREASE
                    AudioListener.volume = settings.globalVolumeRatio * VolumeScale.volumeIncrease60dbAudioListenerRatioMax;
#else
                    AudioListener.volume = settings.globalVolumeRatio;
#endif
                }
            }
        }

        private void FindAudioListenerDistance(bool isAwake = false) {
            if (settings.overrideListenerDistance) {
                if (cachedAudioListenerDistance == null || !cachedAudioListenerDistance.isActiveAndEnabled) {
#if UNITY_6000_4_OR_NEWER || SONITY_DLL_RUNTIME
                    // Unity 6000.4 removes FindObjectsSortMode
                    AudioListenerDistanceBase[] audioListenerDistanceList = UnityEngine.Object.FindObjectsByType<AudioListenerDistanceBase>(FindObjectsInactive.Exclude);
#elif UNITY_2022_3_OR_NEWER
                    // FindObjectsSortMode.InstanceID is slower but is more consistent
                    AudioListenerDistanceBase[] audioListenerDistanceList = UnityEngine.Object.FindObjectsByType<AudioListenerDistanceBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID);
#else
                    AudioListenerDistanceBase[] audioListenerDistanceList = UnityEngine.Object.FindObjectsOfType<AudioListenerDistanceBase>();
#endif
                    foreach (AudioListenerDistanceBase audioListenerDistance in audioListenerDistanceList) {
                        if (audioListenerDistance != null && audioListenerDistance.isActiveAndEnabled) {
                            // Finds the first enabled AudioListenerDistance
                            cachedAudioListenerDistance = audioListenerDistance;
                            break;
                        }
                    }
                    if (cachedAudioListenerDistance == null || !cachedAudioListenerDistance.isActiveAndEnabled) {
                        // On awake with domain reload disabled, the AudioListenerDistance is not activeAndEnabled
                        if (!isAwake) {
                            if (ShouldDebug.Warnings()) {
                                Debug.LogWarning(
                                    $"Sonity.{NameOf.SoundManager} could find no active {NameOf.AudioListenerDistance} in the scene." +
                                    $"If you are creating one in runtime, make sure it is created in Awake()."
                                    );
                            }
                        }
                    } else {
                        cachedAudioListenerDistanceTransform = cachedAudioListenerDistance.transform;
                    }
                }
            }
        }

        public float GetDistanceToAudioListener(Vector3 position) {
            if (settings.overrideListenerDistance && settings.overrideListenerDistanceAmount > 0f) {
                if (settings.overrideListenerDistanceAmount < 100f) {
                    if (cachedAudioListener == null || !cachedAudioListener.isActiveAndEnabled) {
                        FindAudioListener();
                        if (cachedAudioListener == null) {
                            return float.MaxValue;
                        }
                    }
                    if (cachedAudioListenerDistance == null || !cachedAudioListenerDistance.isActiveAndEnabled) {
                        FindAudioListenerDistance();
                        if (cachedAudioListenerDistance == null) {
                            return float.MaxValue;
                        }
                    }
                    // Position between the AudioListener and Audio Listener Distance
                    return Vector3.Distance(position, Vector3.Lerp(cachedAudioListenerTransform.position, cachedAudioListenerDistanceTransform.position, settings.overrideListenerDistanceAmount * 0.01f));
                } else {
                    if (cachedAudioListenerDistance == null) {
                        FindAudioListenerDistance();
                        if (cachedAudioListenerDistance == null) {
                            return float.MaxValue;
                        }
                    }
                    // Position at the AudioListenerDistance
                    return Vector3.Distance(position, cachedAudioListenerDistanceTransform.position);
                }
            } else {
                if (cachedAudioListener == null || !cachedAudioListener.isActiveAndEnabled) {
                    FindAudioListener();
                    if (cachedAudioListener == null) {
                        return float.MaxValue;
                    }
                }
                // Position at the AudioListener
                return Vector3.Distance(position, cachedAudioListenerTransform.position);
            }
        }

        public float GetAngleToAudioListener(Vector3 position) {
            if (cachedAudioListener == null || !cachedAudioListener.isActiveAndEnabled) {
                FindAudioListener();
                if (cachedAudioListener == null) {
                    return 0f;
                }
            }
            return AngleAroundAxis.Get(position - cachedAudioListenerTransform.position, cachedAudioListenerTransform.forward, Vector3.up);
        }

        [NonSerialized]
        private CheckedDictionaryValueSoundEvent checkedDictionaryValueSoundEvent;
        [NonSerialized]
        private CheckedDictionaryValueSoundDataGroup checkedDictionaryValueSoundDataGroup;
        [NonSerialized]
        private CheckedDictionaryValueSoundMix checkedDictionaryValueSoundMix;
        [NonSerialized]
        private CheckedDictionaryValueSoundPhysicsCondition checkedDictionaryValueSoundPhysicsCondition;

        public class InternalPlayBufferInstance {
            public SoundEventBase soundEvent;
            public SoundEventPlayType playType;
            public Transform owner;
            public Vector3? positionVector;
            public Transform positionTransform;
            public SoundEventModifier soundEventModifierSoundPicker;
            public SoundEventModifier soundEventModifierSoundTag;
            public SoundParameterInternals[] soundParameters;
            public SoundParameterInternals soundParameterDistanceScale;
            public SoundTagBase localSoundTag;

            public InternalPlayBufferInstance(
                SoundEventBase soundEvent, SoundEventPlayType playType, Transform owner, Vector3? positionVector, Transform positionTransform,
                SoundEventModifier soundEventModifierSoundPicker, SoundEventModifier soundEventModifierSoundTag,
                SoundParameterInternals[] soundParameters, SoundParameterInternals soundParameterDistanceScale, SoundTagBase localSoundTag) {
                this.soundEvent = soundEvent;
                this.playType = playType;
                this.owner = owner;
                this.positionVector = positionVector;
                this.positionTransform = positionTransform;
                this.soundEventModifierSoundPicker = soundEventModifierSoundPicker;
                this.soundEventModifierSoundTag = soundEventModifierSoundTag;
                this.soundParameters = soundParameters;
                this.soundParameterDistanceScale = soundParameterDistanceScale;
                this.localSoundTag = localSoundTag;
            }
        }

        private List<InternalPlayBufferInstance> internalPlayBufferInstances = new List<InternalPlayBufferInstance>();

        // Used for some things like Trigger On Stop play because play needs to be delayed for the next frame
        // Otherwise it will play when its stopping and cause an error when enumerating over the dictionaries
        public void InternalPlayBuffered(
            SoundEventBase soundEvent, SoundEventPlayType playType, Transform owner, Vector3? positionVector, Transform positionTransform,
            SoundEventModifier soundEventModifierSoundPicker, SoundEventModifier soundEventModifierSoundTag,
            SoundParameterInternals[] soundParameters, SoundParameterInternals soundParameterDistanceScale, SoundTagBase localSoundTag) {

            internalPlayBufferInstances.Add(new InternalPlayBufferInstance(
                soundEvent, playType, owner, positionVector, positionTransform, soundEventModifierSoundPicker,
                soundEventModifierSoundTag, soundParameters, soundParameterDistanceScale, localSoundTag
                ));
        }

        public void ManagedUpdate() {

            // Update time value based on the selected time scale
            SoundTimeScale.UpdateTime(settings.soundTimeScale);

            // Used to play Trigger On Stop sounds
            for (int i = internalPlayBufferInstances.Count - 1; i >= 0; i--) {
                InternalPlay(
                    internalPlayBufferInstances[i].soundEvent,
                    internalPlayBufferInstances[i].playType,
                    internalPlayBufferInstances[i].owner,
                    internalPlayBufferInstances[i].positionVector,
                    internalPlayBufferInstances[i].positionTransform,
                    internalPlayBufferInstances[i].soundEventModifierSoundPicker,
                    internalPlayBufferInstances[i].soundEventModifierSoundTag,
                    internalPlayBufferInstances[i].soundParameters,
                    internalPlayBufferInstances[i].soundParameterDistanceScale,
                    internalPlayBufferInstances[i].localSoundTag
                    );
                internalPlayBufferInstances.RemoveAt(i);
            }

            for (int i = soundEventInstanceList.CountActive - 1; i >= 0; i--) {
                SoundEventInstance instance = soundEventInstanceList[i];

                // Null SoundEvent Instances (unloaded by eg addressables)
                if (instance.soundEvent == null) {
                    // Stop voices and deactivate instance (keep in Dictionary if SoundEvent is added again
                    instance.PoolAllVoices(false, false, false);
                    soundEventInstanceList.DeactivateAtIndex(i);
                    // Don't update the current voice if SoundEvent is null
                    continue;
                }

                // Managed update
                instance.ManagedUpdate();

                // Waiting for pooling
                if (instance.waitingForPooling) {
                    instance.waitingForPooling = false;
                    soundEventInstanceList.DeactivateAtIndex(i);

                    // Remove from Group
                    if (soundEventGroupDictionary.TryGetValue(instance.soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceGroup)) {
                        for (int ii = instanceGroup.activeInstances.Count - 1; ii >= 0; ii--) {
                            SoundEventInstance activeInstance = instanceGroup.activeInstances[ii];
                            if (activeInstance == instance) {
                                instanceGroup.activeInstances.RemoveAtSwapBack(ii);
                                break;
                            }
                        }
                    }
                }
            }

            // Stops voices after a certain time, starts looking at the first items in the list, which are the oldest ones
            for (int i = 0; i < voicePool.voiceStopIndexes.Count; i++) {
                Voice voice = voicePool.voices[voicePool.voiceStopIndexes[i]];
                if (voice.GetState() == VoiceState.Pause) {
                    if (voice.stopTime < SoundTimeScale.GetTimeRuntime()) {
                        voice.SetState(VoiceState.Stop, true);
                        voice.cachedGameObject.SetActive(false);
                        // Removes the first items which are oldest
                        voicePool.voiceStopIndexes.RemoveAtSwapBack(i);
                    } else {
                        // Any later items in the list is newer than the first items, so don't need to iterate more
                        break;
                    }
                } else {
                    // Removes the first items which are oldest
                    voicePool.voiceStopIndexes.RemoveAtSwapBack(i);
                }
            }
        }

        // For accessing currently playing Instances
        private SoundEventInstance GetSoundEventInstanceActive(SoundEventInstanceGroup instanceGroup, TransformID transformID) {
            for (int i = instanceGroup.activeInstances.Count - 1; i >= 0; i--) {
                SoundEventInstance instance = instanceGroup.activeInstances[i];
                if (SonityGuidCompare.IsSameNotNull(instance.soundEvent, instanceGroup.soundEvent) && instance.ownerTransformID == transformID) {
                    return instance;
                }
            }
            return null;
        }

        public void InternalPlay(
            SoundEventBase soundEvent, SoundEventPlayType playType, Transform owner, Vector3? positionVector, Transform positionTransform,
            SoundEventModifier soundEventModifierSoundPicker, SoundEventModifier soundEventModifierSoundTag, 
            SoundParameterInternals[] soundParameters, SoundParameterInternals soundParameterDistanceScale, SoundTagBase localSoundTag) {
#if UNITY_EDITOR
            // Legacy Sonity 1.2.0 Upgrade check
            if (soundEvent != null && soundEvent.internals.sonityGuid == SonityGuid.Empty) {
                SonityEditorPrefs.SonityLegacyMissingSonityGuids = true;
                // Dont use cached name here, becuse its probably not set
                Debug.LogError($"Sonity: SoundEvent {soundEvent.name} has no SonityGuid, run \"Tools/Sonity 🔊/Legacy/Sonity 1.2.0 Assets Upgrade: Reserialize All Sonity assets for new GUIDs\"", SoundManagerBase.Instance);
                return;
            }
#endif
            if (!settings.disablePlayingSounds && !applicationIsQuitting) {
                // If polyphony should be limited globally
                if (soundEvent.internals.data.polyphonyMode == PolyphonyMode.LimitedGlobally) {
                    // Play only has the owner as position
                    if (playType == SoundEventPlayType.Play) {
                        positionTransform = owner;
                        playType = SoundEventPlayType.PlayAtTransform;
                    }
                    // If its played by music or 2D don't override owner, because then StopAllMusic won't work.
                    if (owner != cachedMusicTransform && owner != cachedUITransform) {
                        owner = SoundManagerBase.Instance.Internals.cachedSoundManagerTransform;
                    }
                }
                // If SoundEvent Group exists
                if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceGroup)) {

                    // Checks if the SoundEvent should retrigger itself
                    SoundEventInstance instance = GetSoundEventInstanceActive(instanceGroup, new TransformID(owner));

                    // Found previously playing instance with the same owner
                    if (instance != null) {
                        // Only assigned values in the list
                        instance.Play(playType, owner, positionVector, positionTransform, soundEventModifierSoundPicker, soundEventModifierSoundTag, soundParameters, soundParameterDistanceScale, localSoundTag);
                        return;
                    } else {
                        if (soundEventInstanceList.TryGetActiveItem(out SoundEventInstance getInstance)) {
                            instanceGroup.activeInstances.Add(getInstance);
                            // Need to initialize when re-using old Instances
                            getInstance.Initialize(soundEvent);
                            getInstance.Play(playType, owner, positionVector, positionTransform, soundEventModifierSoundPicker, soundEventModifierSoundTag, soundParameters, soundParameterDistanceScale, localSoundTag);
                            return;
                        }
                        // Create a new instance
                        SoundEventInstance newInstance = new SoundEventInstance();
                        newInstance.Initialize(soundEvent, true);
                        instanceGroup.activeInstances.Add(newInstance);
                        soundEventInstanceList.AddActiveItem(newInstance);
                        newInstance.Play(playType, owner, positionVector, positionTransform, soundEventModifierSoundPicker, soundEventModifierSoundTag, soundParameters, soundParameterDistanceScale, localSoundTag);
                        return;
                    }
                } else {
                    // Nullcheck SoundEvent
                    if (!checkedDictionarySoundEvent.TryGetValue(soundEvent.internals.sonityGuid, out checkedDictionaryValueSoundEvent)) {
                        checkedDictionaryValueSoundEvent = new CheckedDictionaryValueSoundEvent();
                        checkedDictionarySoundEvent.Add(soundEvent.internals.sonityGuid, checkedDictionaryValueSoundEvent);
                    }
                    if (!checkedDictionaryValueSoundEvent.nullChecked) {
                        checkedDictionaryValueSoundEvent.nullChecked = true;
                        checkedDictionaryValueSoundEvent.isNull = soundEvent.IsNull();
#if UNITY_EDITOR
                        // So the problem can be fixed in runtime
                        checkedDictionaryValueSoundEvent.nullChecked = false;
#endif
                    }
                    if (checkedDictionaryValueSoundEvent.isNull) {
                        return;
                    }

                    // New Instance Group
                    instanceGroup = new SoundEventInstanceGroup();
                    instanceGroup.soundEvent = soundEvent;
                    instanceGroup.sonityGuid = soundEvent.internals.sonityGuid;
                    soundEventGroupDictionary.Add(soundEvent.internals.sonityGuid, instanceGroup);

                    // Checks if there is a unused instance to use
                    if (soundEventInstanceList.TryGetActiveItem(out SoundEventInstance getInstance)) {

                        // Add Instance
                        instanceGroup.activeInstances.Add(getInstance);

                        // Need to initialize when re-using old Instances
                        getInstance.Initialize(soundEvent);
                        getInstance.Play(playType, owner, positionVector, positionTransform, soundEventModifierSoundPicker, soundEventModifierSoundTag, soundParameters, soundParameterDistanceScale, localSoundTag);
                        return;
                    }

                    // Create a new instance
                    SoundEventInstance newInstance = new SoundEventInstance();
                    newInstance.Initialize(soundEvent, true);
                    soundEventInstanceList.AddActiveItem(newInstance);
                    instanceGroup.activeInstances.Add(newInstance);
                    newInstance.Play(playType, owner, positionVector, positionTransform, soundEventModifierSoundPicker, soundEventModifierSoundTag, soundParameters, soundParameterDistanceScale, localSoundTag);
                    return;
                }
            }
        }

        public void InternalStop(SoundEventBase soundEvent, Transform owner, OwnerPlayType ownerPlayType, bool allowFadeOut) {
            SoundEventInstanceGroup instanceDictionaryValue;
            switch (ownerPlayType) {
                case OwnerPlayType.Normal:
                    if (soundEvent.internals.data.polyphonyMode == PolyphonyMode.LimitedPerOwner) {
                        if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out instanceDictionaryValue)) {
                            SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(owner));
                            if (instance != null) {
                                instance.PoolAllVoices(allowFadeOut, true, false);
                                return;
                            }
                        }
                    } else if (soundEvent.internals.data.polyphonyMode == PolyphonyMode.LimitedGlobally) {
                        // If limited globally then it uses the SoundManager transform
                        if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out instanceDictionaryValue)) {
                            SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(SoundManagerBase.Instance.Internals.cachedSoundManagerTransform));
                            if (instance != null) {
                                instance.PoolAllVoices(allowFadeOut, true, false);
                                return;
                            }
                        }
                    }
                    break;
                case OwnerPlayType.Music:
                    // If music then uses the music transform
                    if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out instanceDictionaryValue)) {
                        SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(SoundManagerBase.Instance.Internals.cachedMusicTransform));
                        if (instance != null) {
                            instance.PoolAllVoices(allowFadeOut, true, false);
                            return;
                        }
                    }
                    break;
                case OwnerPlayType.UI:
                    // If UI then uses the UI transform
                    if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out instanceDictionaryValue)) {
                        SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(SoundManagerBase.Instance.Internals.cachedUITransform));
                        if (instance != null) {
                            instance.PoolAllVoices(allowFadeOut, true, false);
                            return;
                        }
                    }
                    break;
            }
        }

        public void InternalStopAtPosition(SoundEventBase soundEvent, Transform position, bool allowFadeOut) {
            if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceDictionaryValue)) {
                for (int i = 0; i < instanceDictionaryValue.activeInstances.Count; i++) {
                    instanceDictionaryValue.activeInstances[i].PoolVoicesWithPositionTransform(position, allowFadeOut);
                }
            }
        }

        public void InternalStopAllAtOwner(Transform owner, bool allowFadeOut) {
            TransformID transformID = new TransformID(owner);
            for (int i = 0; i < soundEventInstanceList.CountActive; i++) {
                SoundEventInstance instance = soundEventInstanceList[i];
                if (instance.ownerTransformID == transformID) {
                    instance.PoolAllVoices(allowFadeOut, true, false);
                }
            }
        }

        public void InternalStopEverywhere(SoundEventBase soundEvent, bool allowFadeOut) {
            if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceGroup)) {
                for (int i = 0; i < instanceGroup.activeInstances.Count; i++) {
                    instanceGroup.activeInstances[i].PoolAllVoices(allowFadeOut, true, false);
                }
            }
        }

        public void InternalStopEverything(bool allowFadeOut) {
            for (int i = 0; i < soundEventInstanceList.CountActive; i++) {
                soundEventInstanceList[i].PoolAllVoices(allowFadeOut, true, false);
            }
        }

        public void InternalOnDestroyForceStopEverything() {
            // Probably nice with a try catch here
            try {
                for (int i = 0; i < soundEventInstanceList.CountActive; i++) {
                    soundEventInstanceList[i].PoolAllVoices(false, true, true);
                }
            } catch { }
        }

        public void InternalSetPauseUnpause(SoundEventBase soundEvent, Transform owner, OwnerPlayType ownerPlayType, bool pause, bool forcePause = false) {
            SoundEventInstanceGroup instanceGroup;
            switch (ownerPlayType) {
                case OwnerPlayType.Normal:
                    if (soundEvent.internals.data.polyphonyMode == PolyphonyMode.LimitedPerOwner) {
                        if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out instanceGroup)) {
                            SoundEventInstance instance = GetSoundEventInstanceActive(instanceGroup, new TransformID(owner));
                            if (instance != null) {
                                instance.PauseUnpauseNormal(pause, forcePause);
                                return;
                            }
                        }
                    }
                    else if (soundEvent.internals.data.polyphonyMode == PolyphonyMode.LimitedGlobally) {
                        // If limited globally then it uses the SoundManager transform
                        if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out instanceGroup)) {
                            SoundEventInstance instance = GetSoundEventInstanceActive(instanceGroup, new TransformID(SoundManagerBase.Instance.Internals.cachedSoundManagerTransform));
                            if (instance != null) {
                                instance.PauseUnpauseNormal(pause, forcePause);
                                return;
                            }
                        }
                    }
                    break;
                case OwnerPlayType.Music:
                    // If music then uses the music transform
                    if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out instanceGroup)) {
                        SoundEventInstance instance = GetSoundEventInstanceActive(instanceGroup, new TransformID(SoundManagerBase.Instance.Internals.cachedMusicTransform));
                        if (instance != null) {
                            instance.PauseUnpauseNormal(pause, forcePause);
                            return;
                        }
                    }
                    break;
                case OwnerPlayType.UI:
                    // If UI then uses the UI transform
                    if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out instanceGroup)) {
                        SoundEventInstance instance = GetSoundEventInstanceActive(instanceGroup, new TransformID(SoundManagerBase.Instance.Internals.cachedUITransform));
                        if (instance != null) {
                            instance.PauseUnpauseNormal(pause, forcePause);
                            return;
                        }
                    }
                    break;
            }
        }

        public void InternalSetPauseUnpauseAllAtOwner(Transform owner, bool pause, bool forcePause) {
            TransformID transformID = new TransformID(owner);
            for (int i = 0; i < soundEventInstanceList.CountActive; i++) {
                SoundEventInstance instance = soundEventInstanceList[i];
                if (instance.ownerTransformID == transformID) {
                    instance.PauseUnpauseNormal(pause, forcePause);
                }
            }
        }

        public void InternalSetPauseUnpauseEverywhere(SoundEventBase soundEvent, bool pause, bool forcePause) {
            if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceGroup)) {
                for (int i = 0; i < instanceGroup.activeInstances.Count; i++) {
                    instanceGroup.activeInstances[i].PauseUnpauseNormal(pause, forcePause);
                }
            }
        }

        public void InternalSetPauseUnpauseEverything(bool pause, bool forcePause) {
            for (int i = 0; i < soundEventInstanceList.CountActive; i++) {
                soundEventInstanceList[i].PauseUnpauseNormal(pause, forcePause);
            }
        }

        public void InternalSetGlobalPauseUnpause(bool pause) {
            if (AudioListener.pause != pause) {
                AudioListener.pause = pause;
                settings.globalPause = pause;

                for (int i = 0; i < soundEventInstanceList.CountActive; i++) {
                    soundEventInstanceList[i].PauseUnpauseGlobal(pause);
                }
            }
        }

        public bool InternalGetGlobalPaused() {
            return settings.globalPause;
        }

        public void InternalSetGlobalVolumeRatio(float volumeRatio) {
            volumeRatio = Mathf.Clamp(volumeRatio, 0f, 1f);
            if (settings.globalVolumeRatio != volumeRatio) {
                settings.globalVolumeRatio = volumeRatio;
                settings.globalVolumeDecibel = VolumeScale.ConvertRatioToDecibel(settings.globalVolumeRatio);
#if SONITY_ENABLE_VOLUME_INCREASE
                AudioListener.volume = settings.globalVolumeRatio * VolumeScale.volumeIncrease60dbAudioListenerRatioMax;
#else
                AudioListener.volume = settings.globalVolumeRatio;
#endif
            }
        }

        public void InternalSetGlobalVolumeDecibel(float volumeDecibel) {
            volumeDecibel = Mathf.Clamp(volumeDecibel, Mathf.NegativeInfinity, 0f);
            if (settings.globalVolumeDecibel != volumeDecibel) {
                settings.globalVolumeDecibel = volumeDecibel;
                settings.globalVolumeRatio = VolumeScale.ConvertDecibelToRatio(volumeDecibel);
#if SONITY_ENABLE_VOLUME_INCREASE
                AudioListener.volume = settings.globalVolumeRatio * VolumeScale.volumeIncrease60dbAudioListenerRatioMax;
#else
                AudioListener.volume = settings.globalVolumeRatio;
#endif
            }
        }

        public float InternalGetGlobalVolumeRatio() {
            return settings.globalVolumeRatio;
        }

        public float InternalGetGlobalVolumeDecibel() {
            return settings.globalVolumeDecibel;
        }

        public SoundEventState InternalGetSoundEventState(SoundEventBase soundEvent, Transform owner) {
            if (soundEvent == null || owner == null) {
                return SoundEventState.NotPlaying;
            }
            if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceDictionaryValue)) {
                SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(owner));
                if (instance != null) {
                    return instance.GetSoundEventState();
                }
            }
            return SoundEventState.NotPlaying;
        }

        public float InternalGetLastPlayedClipLength(SoundEventBase soundEvent, Transform owner, bool pitchSpeed) {
            if (soundEvent == null || owner == null) {
                return Mathf.Infinity;
            }
            if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceDictionaryValue)) {
                SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(owner));
                if (instance != null) {
                   return instance.GetLastPlayedAudioSourceClipLength(pitchSpeed);
                }
            }
            return Mathf.Infinity;
        }

        public float InternalGetLastPlayedClipTimeSeconds(SoundEventBase soundEvent, Transform owner, bool pitchSpeed) {
            if (soundEvent == null || owner == null) {
                return 0f;
            }
            if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceDictionaryValue)) {
                SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(owner));
                if (instance != null) {
                    return instance.GetLastPlayedAudioSourceTimeSeconds(pitchSpeed);
                }
            }
            return 0f;
        }

        public float InternalGetLastPlayedClipTimeRatio(SoundEventBase soundEvent, Transform owner) {
            if (soundEvent == null || owner == null) {
                return 0f;
            }
            if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceDictionaryValue)) {
                SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(owner));
                if (instance != null) {
                    return instance.GetLastPlayedAudioSourceTimeRatio();
                }
            }
            return 0f;
        }

        public void InternalGetSpectrumData(SoundEventBase soundEvent, Transform owner, ref float[] samples, int channel, FFTWindow window, SpectrumDataFrom spectrumDataFrom) {
            if (soundEvent == null || owner == null) {
                return;
            }
            if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceDictionaryValue)) {
                SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(owner));
                if (instance != null) {
                    instance.GetSpectrumData(ref samples, channel, window, spectrumDataFrom);
                }
            }
        }

        public AudioSource InternalGetLastPlayedAudioSource(SoundEventBase soundEvent, Transform owner) {
            if (soundEvent == null || owner == null) {
                return null;
            }
            if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceDictionaryValue)) {
                SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(owner));
                if (instance != null) {
                    return instance.GetLastPlayedAudioSource();
                }
            }
            return null;
        }

        public float InternalGetMaxLength(SoundEventBase soundEvent) {
            if (soundEvent == null) {
                return 0f;
            }
            if (!checkedDictionarySoundEvent.TryGetValue(soundEvent.internals.sonityGuid, out checkedDictionaryValueSoundEvent)) {
                checkedDictionaryValueSoundEvent = new CheckedDictionaryValueSoundEvent();
                checkedDictionarySoundEvent.Add(soundEvent.internals.sonityGuid, checkedDictionaryValueSoundEvent);
            }
            if (!checkedDictionaryValueSoundEvent.savedMaxLengthChecked) {
                checkedDictionaryValueSoundEvent.savedMaxLengthChecked = true;
                checkedDictionaryValueSoundEvent.savedMaxLength = soundEvent.internals.GetMaxLengthWithPitchAndTimeline();
            }
            return checkedDictionaryValueSoundEvent.savedMaxLength;
        }

        public bool InternalCheckTriggerOnTailLengthTooShort(SoundEventBase soundEvent) {
            if (soundEvent == null) {
                return true;
            }
            if (!checkedDictionarySoundEvent.TryGetValue(soundEvent.internals.sonityGuid, out checkedDictionaryValueSoundEvent)) {
                checkedDictionaryValueSoundEvent = new CheckedDictionaryValueSoundEvent();
                checkedDictionarySoundEvent.Add(soundEvent.internals.sonityGuid, checkedDictionaryValueSoundEvent);
            }
            if (!checkedDictionaryValueSoundEvent.savedMinLengthChecked) {
                checkedDictionaryValueSoundEvent.savedMinLengthChecked = true;
                checkedDictionaryValueSoundEvent.savedMinLength = soundEvent.internals.GetMinLengthFirstSoundContainerWithoutPitch();
                if (checkedDictionaryValueSoundEvent.savedMinLength < soundEvent.internals.data.triggerOnTailLength) {
#if UNITY_EDITOR
                    // So the problem can be fixed in runtime
                    checkedDictionaryValueSoundEvent.savedMinLengthChecked = false;
#endif
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundEvent} \"{soundEvent.internals.cachedName}\" Trigger On Tail: Tail Length is SonityGuider than the shortest AudioClip on the first SoundContainer.", soundEvent);
                    }
                    return true;
                }
            }
            return false;
        }

        public bool InternalCheckTriggerOnPlayIsInfiniteLoop(SoundEventBase soundEvent) {
            if (soundEvent == null) {
                return true;
            }
            if (!checkedDictionarySoundEvent.TryGetValue(soundEvent.internals.sonityGuid, out checkedDictionaryValueSoundEvent)) {
                checkedDictionaryValueSoundEvent = new CheckedDictionaryValueSoundEvent();
                checkedDictionarySoundEvent.Add(soundEvent.internals.sonityGuid, checkedDictionaryValueSoundEvent);
            }
            if (!checkedDictionaryValueSoundEvent.triggerOnPlayIsInfiniteLoopChecked) {
                checkedDictionaryValueSoundEvent.triggerOnPlayIsInfiniteLoopChecked = true;
                checkedDictionaryValueSoundEvent.triggerOnPlayIsInfiniteLoop =
                    soundEvent.internals.data.GetIfInfiniteLoop(soundEvent, out SoundEventBase infiniteInstigator, out SoundEventBase infinitePrevious, TriggerOnType.TriggerOnPlay);
                if (checkedDictionaryValueSoundEvent.triggerOnPlayIsInfiniteLoop) {
#if UNITY_EDITOR
                    // So the problem can be fixed in runtime
                    checkedDictionaryValueSoundEvent.triggerOnPlayIsInfiniteLoopChecked = false;
#endif
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundEvent} TriggerOnPlay: \"{infiniteInstigator.internals.cachedName}\" in \"{infinitePrevious.internals.cachedName}\" creates an infinite loop", infiniteInstigator);
                    }
                    return true;
                }
            }
            return checkedDictionaryValueSoundEvent.triggerOnPlayIsInfiniteLoop;
        }

        public bool InternalCheckTriggerOnStopIsInfiniteLoop(SoundEventBase soundEvent) {
            if (soundEvent == null) {
                return true;
            }
            if (!checkedDictionarySoundEvent.TryGetValue(soundEvent.internals.sonityGuid, out checkedDictionaryValueSoundEvent)) {
                checkedDictionaryValueSoundEvent = new CheckedDictionaryValueSoundEvent();
                checkedDictionarySoundEvent.Add(soundEvent.internals.sonityGuid, checkedDictionaryValueSoundEvent);
            }
            if (!checkedDictionaryValueSoundEvent.triggerOnStopIsInfiniteLoopChecked) {
                checkedDictionaryValueSoundEvent.triggerOnStopIsInfiniteLoopChecked = true;
                checkedDictionaryValueSoundEvent.triggerOnStopIsInfiniteLoop =
                    soundEvent.internals.data.GetIfInfiniteLoop(soundEvent, out SoundEventBase infiniteInstigator, out SoundEventBase infinitePrevious, TriggerOnType.TriggerOnStop);
                if (checkedDictionaryValueSoundEvent.triggerOnStopIsInfiniteLoop) {
#if UNITY_EDITOR
                    // So the problem can be fixed in runtime
                    checkedDictionaryValueSoundEvent.triggerOnStopIsInfiniteLoopChecked = false;
#endif
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundEvent} TriggerOnStop: \"{infiniteInstigator.internals.cachedName}\" in \"{infinitePrevious.internals.cachedName}\" creates an infinite loop", infiniteInstigator);
                    }
                    return true;
                }
            }
            return checkedDictionaryValueSoundEvent.triggerOnStopIsInfiniteLoop;
        }

        public bool InternalCheckSoundDataGroupIsInfiniteLoop(SoundDataGroupBase soundDataGroup) {
            if (soundDataGroup == null) {
                return true;
            }
            if (!checkedDictionarySoundDataGroup.TryGetValue(soundDataGroup.internals.sonityGuid, out checkedDictionaryValueSoundDataGroup)) {
                checkedDictionaryValueSoundDataGroup = new CheckedDictionaryValueSoundDataGroup();
                checkedDictionarySoundDataGroup.Add(soundDataGroup.internals.sonityGuid, checkedDictionaryValueSoundDataGroup);
            }
            if (!checkedDictionaryValueSoundDataGroup.isInfiniteLoopChecked) {
                checkedDictionaryValueSoundDataGroup.isInfiniteLoopChecked = true;
                checkedDictionaryValueSoundDataGroup.isInfiniteLoop = soundDataGroup.internals.GetIfInfiniteLoop(soundDataGroup, out SoundDataGroupBase infiniteInstigator, out SoundDataGroupBase infinitePrevious);
                if (checkedDictionaryValueSoundDataGroup.isInfiniteLoop) {
#if UNITY_EDITOR
                    // So the problem can be fixed in runtime
                    checkedDictionaryValueSoundDataGroup.isInfiniteLoopChecked = false;
#endif
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundDataGroup}: \"{infiniteInstigator.internals.cachedName}\" in \"{infinitePrevious.internals.cachedName}\" creates an infinite loop", infiniteInstigator);
                    }
                }
            }
            return checkedDictionaryValueSoundDataGroup.isInfiniteLoop;
        }

        public bool InternalCheckSoundMixIsInfiniteLoop(SoundMixBase soundMix) {
            if (soundMix == null) {
                return true;
            }
            if (!checkedDictionarySoundMix.TryGetValue(soundMix.internals.sonityGuid, out checkedDictionaryValueSoundMix)) {
                checkedDictionaryValueSoundMix = new CheckedDictionaryValueSoundMix();
                checkedDictionarySoundMix.Add(soundMix.internals.sonityGuid, checkedDictionaryValueSoundMix);
            }
            if (!checkedDictionaryValueSoundMix.isInfiniteLoopChecked) {
                checkedDictionaryValueSoundMix.isInfiniteLoopChecked = true;
                checkedDictionaryValueSoundMix.isInfiniteLoop = soundMix.internals.GetIfInfiniteLoop(soundMix, out SoundMixBase infiniteInstigator, out SoundMixBase infinitePrevious);
                if (checkedDictionaryValueSoundMix.isInfiniteLoop) {
#if UNITY_EDITOR
                    // So the problem can be fixed in runtime
                    checkedDictionaryValueSoundMix.isInfiniteLoopChecked = false;
#endif
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundDataGroup}:\"{infiniteInstigator.internals.cachedName}\" in \"{infinitePrevious.internals.cachedName}\" creates an infinite loop", infiniteInstigator);
                    }
                }
            }
            return checkedDictionaryValueSoundMix.isInfiniteLoop;
        }

        public bool InternalCheckSoundPhysicsConditionIsInfiniteLoop(SoundPhysicsConditionBase condition) {
            if (condition == null) {
                return true;
            }
            if (!checkedDictionarySoundPhysicsCondition.TryGetValue(condition.internals.sonityGuid, out checkedDictionaryValueSoundPhysicsCondition)) {
                checkedDictionaryValueSoundPhysicsCondition = new CheckedDictionaryValueSoundPhysicsCondition();
                checkedDictionarySoundPhysicsCondition.Add(condition.internals.sonityGuid, checkedDictionaryValueSoundPhysicsCondition);
            }
            if (!checkedDictionaryValueSoundPhysicsCondition.isInfiniteLoopChecked) {
                checkedDictionaryValueSoundPhysicsCondition.isInfiniteLoopChecked = true;
                checkedDictionaryValueSoundPhysicsCondition.isInfiniteLoop = condition.internals.GetIfInfiniteLoop(condition, out SoundPhysicsConditionBase infiniteInstigator, out SoundPhysicsConditionBase infinitePrevious);
                if (checkedDictionaryValueSoundPhysicsCondition.isInfiniteLoop) {
#if UNITY_EDITOR
                    // So the problem can be fixed in runtime
                    checkedDictionaryValueSoundPhysicsCondition.isInfiniteLoopChecked = false;
#endif
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundPhysicsCondition}: \"{infiniteInstigator.internals.cachedName}\" in \"{infinitePrevious.internals.cachedName}\" creates an infinite loop", infiniteInstigator);
                    }
                }
            }
            return checkedDictionaryValueSoundPhysicsCondition.isInfiniteLoop;
        }

#if UNITY_EDITOR
        public int InternalStatisticsNumberOfPlays(SoundEventBase soundEvent, bool increment) {
            if (soundEvent == null) {
                return 0;
            }
            if (!checkedDictionarySoundEvent.TryGetValue(soundEvent.internals.sonityGuid, out checkedDictionaryValueSoundEvent)) {
                checkedDictionaryValueSoundEvent = new CheckedDictionaryValueSoundEvent();
                checkedDictionarySoundEvent.Add(soundEvent.internals.sonityGuid, checkedDictionaryValueSoundEvent);
            }
            if (increment) {
                checkedDictionaryValueSoundEvent.statisticsNumberOfPlays++;
            }
            return checkedDictionaryValueSoundEvent.statisticsNumberOfPlays;
        }
#endif

        public float InternalGetTimePlayed(SoundEventBase soundEvent, Transform owner) {
            if (soundEvent == null || owner == null) {
                return 0f;
            }
            if (soundEventGroupDictionary.TryGetValue(soundEvent.internals.sonityGuid, out SoundEventInstanceGroup instanceDictionaryValue)) {
                SoundEventInstance instance = GetSoundEventInstanceActive(instanceDictionaryValue, new TransformID(owner));
                if (instance != null) {
                    return instance.GetTimePlayed();
                }
            }
            return 0f;
        }

        public bool InternalGetContainsLoop(SoundEventBase soundEvent) {
            if (soundEvent == null) {
                return false;
            }
            return soundEvent.internals.GetContainsLoop();
        }

        public float InternalGetMaxDistance(SoundEventBase soundEvent) {
            if (soundEvent == null) {
                return 0f;
            }
            if (!checkedDictionarySoundEvent.TryGetValue(soundEvent.internals.sonityGuid, out CheckedDictionaryValueSoundEvent value)) {
                value = new CheckedDictionaryValueSoundEvent();
                checkedDictionarySoundEvent.Add(soundEvent.internals.sonityGuid, value);
            }
            if (!value.savedMaxDistanceChecked) {
                value.savedMaxDistanceChecked = true;
                value.savedMaxDistance = soundEvent.internals.GetMaxDistance();
            }
            // Scale each time with global distance scale
            return value.savedMaxDistance * settings.distanceScale;
        }

        public void InternalLoadUnloadAudioData(SoundEventBase soundEvent, bool load) {
            if (soundEvent == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(null, null, null, null)}");
                }
            } else {
                if (soundEvent.internals.soundContainers.Length == 0) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} has no {NameOf.SoundContainer}s.{DebugInfoString(soundEvent, null, null, null)}", soundEvent);
                    }
                } else {
                    for (int i = 0; i < soundEvent.internals.soundContainers.Length; i++) {
                        if (soundEvent.internals.soundContainers[i] == null) {
                            if (ShouldDebug.Warnings()) {
                                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} has a null {NameOf.SoundContainer}.{DebugInfoString(soundEvent, null, null, null)}", soundEvent);
                            }
                        } else {
                            soundEvent.internals.soundContainers[i].internals.LoadUnloadAudioData(load, soundEvent.internals.soundContainers[i]);
                        }
                    }
                }
            }
        }

#if UNITY_EDITOR
        [NonSerialized]
        private List<SoundEventBase> soloSoundEvents = new List<SoundEventBase>();

        public bool GetSoloEnabled() {
            for (int i = soloSoundEvents.Count - 1; i >= 0; i--) {
                if (soloSoundEvents[i] != null && soloSoundEvents[i].internals.data.soloEnable) {
                    return true;
                } else {
                    soloSoundEvents.RemoveAt(i);
                }
            }
            return false;
        }

        public bool GetIsInSolo(SoundEventBase soundEvent) {
            if (soloSoundEvents.Contains(soundEvent)) {
                return true;
            }
            return false;
        }

        public void AddSolo(SoundEventBase soundEvent) {
            if (!soloSoundEvents.Contains(soundEvent)) {
                soloSoundEvents.Add(soundEvent);
            }
        }
#endif

        public string DebugInfoString(SoundEventBase soundEvent, Transform transform, Transform position, Transform owner) {
            string tempString = "";
            bool previousNull = true;

            if (soundEvent != null) {
                previousNull = false;
                tempString += $" \"{soundEvent.internals.cachedName}\" ({NameOf.SoundEvent})";
            } else {
                previousNull = true;
            }
            if (transform != null) {
                if (!previousNull) {
                    tempString += ",";
                }
                previousNull = false;
                // Unity Auditor Warning: The Object.name property allocates managed memory
                // But Sonity Debug Log SoundEvents shouldn't be enabled in the build unless you have a debug build anyway
                tempString += $" \"{transform.name}\" (Transform)";
            } else {
                previousNull = true;
            }
            if (position != null) {
                if (!previousNull) {
                    tempString += ",";
                }
                previousNull = false;
                // Unity Auditor Warning: The Object.name property allocates managed memory
                // But Sonity Debug Log SoundEvents shouldn't be enabled in the build unless you have a debug build anyway
                tempString += $" \"{position.name}\" (position Transform)";
            } else {
                previousNull = true;
            }
            if (owner != null) {
                if (!previousNull) {
                    tempString += ",";
                }
                previousNull = false;
                // Unity Auditor Warning: The Object.name property allocates managed memory
                // But Sonity Debug Log SoundEvents shouldn't be enabled in the build unless you have a debug build anyway
                tempString += $" \"{owner.name}\" (owner Transform)";
            } else {
                previousNull = true;
            }
            // Add first At
            if (tempString != "") {
                tempString = $" Using{tempString}.";
            }
            return tempString;
        }

        public void Play(SoundEventBase soundEvent, Transform owner) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null.{DebugInfoString(soundEvent, owner, null, null)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, owner, null, null)}" , owner);
                    }
                } else {
                    InternalPlay(soundEvent, SoundEventPlayType.Play, owner, null, null, null, null, null, null, null);
                }
            }
        }

        public void Play(SoundEventBase soundEvent, Transform owner, SoundTagBase localSoundTag) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null.{DebugInfoString(soundEvent, owner, null, null)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, owner, null, null)}", owner);
                    }
                } else {
                    InternalPlay(soundEvent, SoundEventPlayType.Play, owner, null, null, null, null, null, null, localSoundTag);
                }
            }
        }

        public void Play(SoundEventBase soundEvent, Transform owner, params SoundParameterInternals[] soundParameters) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null.{DebugInfoString(soundEvent, owner, null, null)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, owner, null, null)}", owner);
                    }
                } else {
                    InternalPlay(soundEvent, SoundEventPlayType.Play, owner, null, null, null, null, soundParameters, null, null);
                }
            }
        }

        public void Play(SoundEventBase soundEvent, Transform owner, SoundTagBase localSoundTag, params SoundParameterInternals[] soundParameters) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null.{DebugInfoString(soundEvent, owner, null, null)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, owner, null, null)}", owner);
                    }
                } else {
                    InternalPlay(soundEvent, SoundEventPlayType.Play, owner, null, null, null, null, soundParameters, null, localSoundTag);
                }
            }
        }

        public void PlayAtPosition(SoundEventBase soundEvent, Transform owner, Transform position) {
            if (position == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The position {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, position, owner)}", owner);
                }
            } else {
                if (owner == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The owner {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, position, owner)}", soundEvent);
                    }
                } else {
                    if (soundEvent == null) {
                        if (ShouldDebug.Warnings()) {
                            Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, null, position, owner)}", owner);
                        }
                    } else {
                        InternalPlay(soundEvent, SoundEventPlayType.PlayAtTransform, owner, null, position, null, null, null, null, null);
                    }
                }
            }
        }

        public void PlayAtPosition(SoundEventBase soundEvent, Transform owner, Transform position, SoundTagBase localSoundTag) {
            if (position == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The position {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, position, owner)}", owner);
                }
            } else {
                if (owner == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The owner {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, position, owner)}", soundEvent);
                    }
                } else {
                    if (soundEvent == null) {
                        if (ShouldDebug.Warnings()) {
                            Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, null, position, owner)}", owner);
                        }
                    } else {
                        InternalPlay(soundEvent, SoundEventPlayType.PlayAtTransform, owner, null, position, null, null, null, null, localSoundTag);
                    }
                }
            }
        }

        public void PlayAtPosition(SoundEventBase soundEvent, Transform owner, Transform position, params SoundParameterInternals[] soundParameters) {
            if (position == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The position {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, position, owner)}", owner);
                }
            } else {
                if (owner == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The owner {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, position, owner)}", soundEvent);
                    }
                } else {
                    if (soundEvent == null) {
                        if (ShouldDebug.Warnings()) {
                            Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, null, position, owner)}", owner);
                        }
                    } else {
                        InternalPlay(soundEvent, SoundEventPlayType.PlayAtTransform, owner, null, position, null, null, soundParameters, null, null);
                    }
                }
            }
        }

        public void PlayAtPosition(SoundEventBase soundEvent, Transform owner, Transform position, SoundTagBase localSoundTag, params SoundParameterInternals[] soundParameters) {
            if (position == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The position {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, position, owner)}", owner);
                }
            } else {
                if (owner == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The owner {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, position, owner)}", soundEvent);
                    }
                } else {
                    if (soundEvent == null) {
                        if (ShouldDebug.Warnings()) {
                            Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, null, position, owner)}", owner);
                        }
                    } else {
                        InternalPlay(soundEvent, SoundEventPlayType.PlayAtTransform, owner, null, position, null, null, soundParameters, null, localSoundTag);
                    }
                }
            }
        }

        public void PlayAtPosition(SoundEventBase soundEvent, Transform owner, Vector3 position) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, null, owner)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, null, null, owner)}", owner);
                    }
                } else {
                    InternalPlay(soundEvent, SoundEventPlayType.PlayAtVector, owner, position, null, null, null, null, null, null);
                }
            }
        }

        public void PlayAtPosition(SoundEventBase soundEvent, Transform owner, Vector3 position, SoundTagBase localSoundTag) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, null, owner)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, null, null, owner)}", owner);
                    }
                } else {
                    InternalPlay(soundEvent, SoundEventPlayType.PlayAtVector, owner, position, null, null, null, null, null, localSoundTag);
                }
            }
        }

        public void PlayAtPosition(SoundEventBase soundEvent, Transform owner, Vector3 position, params SoundParameterInternals[] soundParameters) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null.{DebugInfoString(soundEvent, null, null, owner)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, null, null, owner)}", owner);
                    }
                } else {
                    InternalPlay(soundEvent, SoundEventPlayType.PlayAtVector, owner, position, null, null, null, soundParameters, null, null);
                }
            }
        }

        public void PlayAtPosition(SoundEventBase soundEvent, Transform owner, Vector3 position, SoundTagBase localSoundTag, params SoundParameterInternals[] soundParameters) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null{DebugInfoString(soundEvent, null, null, owner)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null{DebugInfoString(soundEvent, null, null, owner)}", owner);
                    }
                } else {
                    InternalPlay(soundEvent, SoundEventPlayType.PlayAtVector, owner, position, null, null, null, soundParameters, null, localSoundTag);
                }
            }
        }

        public void Stop(SoundEventBase soundEvent, Transform owner, OwnerPlayType ownerPlayType, bool allowFadeOut = true) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null{DebugInfoString(soundEvent, owner, null, null)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null{DebugInfoString(soundEvent, owner, null, null)}", owner);
                    }
                } else {
                    InternalStop(soundEvent, owner, ownerPlayType, allowFadeOut);
                }
            }
        }

        public void StopAtPosition(SoundEventBase soundEvent, Transform position, bool allowFadeOut = true) {
            if (position == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null{DebugInfoString(soundEvent, position, null, null)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null{DebugInfoString(soundEvent, position, null, null)}", position);
                    }
                } else {
                    InternalStopAtPosition(soundEvent, position, allowFadeOut);
                }
            }
        }

        public void StopAllAtOwner(Transform owner, bool allowFadeOut = true) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null{DebugInfoString(null, owner, null, null)}");
                }
            } else {
                InternalStopAllAtOwner(owner, allowFadeOut);
            }
        }

        public void StopEverywhere(SoundEventBase soundEvent, bool allowFadeOut = true) {
            if (soundEvent == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null{DebugInfoString(soundEvent, null, null, null)}");
                }
            } else {
                InternalStopEverywhere(soundEvent, allowFadeOut);
            }
        }

        public void StopEverything(bool allowFadeOut = true) {
            InternalStopEverything(allowFadeOut);
        }

        public void Pause(SoundEventBase soundEvent, Transform owner, OwnerPlayType ownerPlayType, bool forcePause = false) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null.{DebugInfoString(soundEvent, owner, null, null)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, owner, null, null)}", owner);
                    }
                } else {
                    InternalSetPauseUnpause(soundEvent, owner, ownerPlayType, true, forcePause);
                }
            }
        }

        public void Unpause(SoundEventBase soundEvent, Transform owner, OwnerPlayType ownerPlayType) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null.{DebugInfoString(soundEvent, owner, null, null)}", soundEvent);
                }
            } else {
                if (soundEvent == null) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null.{DebugInfoString(soundEvent, owner, null, null)}", owner);
                    }
                } else {
                    InternalSetPauseUnpause(soundEvent, owner, ownerPlayType,false, false);
                }
            }
        }

        public void PauseAllAtOwner(Transform owner, bool forcePause) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null{DebugInfoString(null, owner, null, null)}");
                }
            } else {
                InternalSetPauseUnpauseAllAtOwner(owner, true, forcePause);
            }
        }

        public void UnpauseAllAtOwner(Transform owner) {
            if (owner == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {nameof(Transform)} is null{DebugInfoString(null, owner, null, null)}");
                }
            } else {
                InternalSetPauseUnpauseAllAtOwner(owner, false, false);
            }
        }

        public void PauseEverywhere(SoundEventBase soundEvent, bool forcePause) {
            if (soundEvent == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null{DebugInfoString(soundEvent, null, null, null)}");
                }
            } else {
                InternalSetPauseUnpauseEverywhere(soundEvent, true, forcePause);
            }
        }

        public void UnpauseEverywhere(SoundEventBase soundEvent) {
            if (soundEvent == null) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: The {NameOf.SoundEvent} is null{DebugInfoString(soundEvent, null, null, null)}");
                }
            } else {
                InternalSetPauseUnpauseEverywhere(soundEvent, false, false);
            }
        }

        public void PauseEverything(bool forcePause = false) {
            InternalSetPauseUnpauseEverything(true, forcePause);
        }

        public void UnpauseEverything() {
            InternalSetPauseUnpauseEverything(false, false);
        }

        public void SetGlobalPause() {
            InternalSetGlobalPauseUnpause(true);
        }

        public void SetGlobalUnpause() {
            InternalSetGlobalPauseUnpause(false);
        }

        public bool GetGlobalPaused() {
            return InternalGetGlobalPaused();
        }

        public void SetGlobalVolumeRatio(float volumeRatio) {
            InternalSetGlobalVolumeRatio(volumeRatio);
        }

        public void SetGlobalVolumeDecibel(float volumeDecibel) {
            InternalSetGlobalVolumeDecibel(volumeDecibel);
        }

        public float GetGlobalVolumeRatio() {
            return InternalGetGlobalVolumeRatio();
        }

        public float GetGlobalVolumeDecibel() {
            return InternalGetGlobalVolumeDecibel();
        }

        public void UIPlay(SoundEventBase soundEvent) {
            Play(soundEvent, cachedUITransform);
        }

        public void UIPlay(SoundEventBase soundEvent, SoundTagBase localSoundTag) {
            Play(soundEvent, cachedUITransform, localSoundTag);
        }

        public void UIPlay(SoundEventBase soundEvent, params SoundParameterInternals[] soundParameters) {
            Play(soundEvent, cachedUITransform, soundParameters);
        }

        public void UIPlay(SoundEventBase soundEvent, SoundTagBase localSoundTag, params SoundParameterInternals[] soundParameters) {
            Play(soundEvent, cachedUITransform, localSoundTag, soundParameters);
        }

        public void UIPlayAtPosition(SoundEventBase soundEvent, Vector3 position) {
            PlayAtPosition(soundEvent, cachedUITransform, position);
        }

        public void UIPlayAtPosition(SoundEventBase soundEvent, Transform position) {
            PlayAtPosition(soundEvent, cachedUITransform, position);
        }

        public void UIStop(SoundEventBase soundEvent, bool allowFadeOut = true) {
            Stop(soundEvent, cachedUITransform, OwnerPlayType.UI, allowFadeOut);
        }

        public void UIStopAll(bool allowFadeOut = true) {
            StopAllAtOwner(cachedUITransform, allowFadeOut);
        }

        public void UIPause(SoundEventBase soundEvent, bool forcePause = false) {
            Pause(soundEvent, cachedUITransform, OwnerPlayType.UI, forcePause);
        }

        public void UIUnpause(SoundEventBase soundEvent) {
            Unpause(soundEvent, cachedUITransform, OwnerPlayType.UI);
        }

        public void UIPauseAll(bool forcePause = false) {
            PauseAllAtOwner(cachedUITransform, forcePause);
        }

        public void UIUnpauseAll() {
            UnpauseAllAtOwner(cachedUITransform);
        }

        public SoundEventState UIGetSoundEventState(SoundEventBase soundEvent) {
            return InternalGetSoundEventState(soundEvent, cachedUITransform);
        }

        public float UIGetLastPlayedClipLength(SoundEventBase soundEvent, bool pitchSpeed) {
            return InternalGetLastPlayedClipLength(soundEvent, cachedUITransform, pitchSpeed);
        }

        public float UIGetLastPlayedClipTimeSeconds(SoundEventBase soundEvent, bool pitchSpeed) {
            return InternalGetLastPlayedClipTimeSeconds(soundEvent, cachedUITransform, pitchSpeed);
        }

        public float UIGetLastPlayedClipTimeRatio(SoundEventBase soundEvent) {
            return InternalGetLastPlayedClipTimeRatio(soundEvent, cachedUITransform);
        }

        public float UIGetTimePlayed(SoundEventBase soundEvent) {
            return InternalGetTimePlayed(soundEvent, cachedUITransform);
        }

        public Transform UIGetTransform() {
            return cachedUITransform;
        }

        public void MusicPlay(SoundEventBase soundEvent, bool stopAllOtherMusic = true, bool allowFadeOut = true) {
            if (stopAllOtherMusic) {
                MusicStopAll(allowFadeOut);
            }
            Play(soundEvent, cachedMusicTransform);
        }

        public void MusicPlay(SoundEventBase soundEvent, bool stopAllOtherMusic = true, bool allowFadeOut = true, params SoundParameterInternals[] soundParameters) {
            if (stopAllOtherMusic) {
                MusicStopAll(allowFadeOut);
            }
            Play(soundEvent, cachedMusicTransform, soundParameters);
        }

        public void MusicStop(SoundEventBase soundEvent, bool allowFadeOut = true) {
            Stop(soundEvent, cachedMusicTransform, OwnerPlayType.Music, allowFadeOut);
        }

        public void MusicStopAll(bool allowFadeOut = true) {
            StopAllAtOwner(cachedMusicTransform, allowFadeOut);
        }

        public void MusicPause(SoundEventBase soundEvent, bool forcePause = false) {
            Pause(soundEvent, cachedMusicTransform, OwnerPlayType.Music, forcePause);
        }

        public void MusicUnpause(SoundEventBase soundEvent) {
            Unpause(soundEvent, cachedMusicTransform, OwnerPlayType.Music);
        }

        public void MusicPauseAll(bool forcePause = false) {
            PauseAllAtOwner(cachedMusicTransform, forcePause);
        }

        public void MusicUnpauseAll() {
            UnpauseAllAtOwner(cachedMusicTransform);
        }

        public SoundEventState MusicGetSoundEventState(SoundEventBase soundEvent) {
            return InternalGetSoundEventState(soundEvent, cachedMusicTransform);
        }

        public float MusicGetLastPlayedClipLength(SoundEventBase soundEvent, bool pitchSpeed) {
            return InternalGetLastPlayedClipLength(soundEvent, cachedMusicTransform, pitchSpeed);
        }

        public float MusicGetLastPlayedClipTimeSeconds(SoundEventBase soundEvent, bool pitchSpeed) {
            return InternalGetLastPlayedClipTimeSeconds(soundEvent, cachedMusicTransform, pitchSpeed);
        }

        public float MusicGetLastPlayedClipTimeRatio(SoundEventBase soundEvent) {
            return InternalGetLastPlayedClipTimeRatio(soundEvent, cachedMusicTransform);
        }

        public float MusicGetTimePlayed(SoundEventBase soundEvent) {
            return InternalGetTimePlayed(soundEvent, cachedMusicTransform);
        }

        public Transform MusicGetTransform() {
            return cachedMusicTransform;
        }

        public SoundEventState GetSoundEventState(SoundEventBase soundEvent, Transform owner) {
            return InternalGetSoundEventState(soundEvent, owner);
        }

        public float GetLastPlayedClipLength(SoundEventBase soundEvent, Transform owner, bool pitchSpeed) {
            return InternalGetLastPlayedClipLength(soundEvent, owner, pitchSpeed);
        }

        public float GetLastPlayedClipTimeSeconds(SoundEventBase soundEvent, Transform owner, bool pitchSpeed) {
            return InternalGetLastPlayedClipTimeSeconds(soundEvent, owner, pitchSpeed);
        }

        public float GetLastPlayedClipTimeRatio(SoundEventBase soundEvent, Transform owner) {
            return InternalGetLastPlayedClipTimeRatio(soundEvent, owner);
        }

        public void GetSpectrumData(SoundEventBase soundEvent, Transform owner, ref float[] samples, int channel, FFTWindow window, SpectrumDataFrom spectrumDataFrom) {
            InternalGetSpectrumData(soundEvent, owner, ref samples, channel, window, spectrumDataFrom);
        }

        public AudioSource GetLastPlayedAudioSource(SoundEventBase soundEvent, Transform owner) {
            return InternalGetLastPlayedAudioSource(soundEvent, owner);
        }

        public float GetMaxLength(SoundEventBase soundEvent) {
            return InternalGetMaxLength(soundEvent);
        }

        public float GetMaxDistance(SoundEventBase soundEvent) {
            return InternalGetMaxDistance(soundEvent);
        }

        public float GetTimePlayed(SoundEventBase soundEvent, Transform owner) {
            return InternalGetTimePlayed(soundEvent, owner);
        }

        public bool GetContainsLoop(SoundEventBase soundEvent) {
            return InternalGetContainsLoop(soundEvent);
        }

        public Transform GetSoundManagerTransform() {
            return cachedSoundManagerTransform;
        }

        public void LoadAudioData(SoundEventBase soundEvent) {
            InternalLoadUnloadAudioData(soundEvent, true);
        }

        public void UnloadAudioData(SoundEventBase soundEvent) {
            InternalLoadUnloadAudioData(soundEvent, false);
        }

        public void SetGlobalSoundTagAtIndex(int index, SoundTagBase soundTag) {
            // Fill out all missing indexes
            if (settings.globalSoundTags.Count <= index) {
                for (int i = 0; i >= settings.globalSoundTags.Count - index; i--) {
                    settings.globalSoundTags.Add(null);
                }
            }
            settings.globalSoundTags[index] = soundTag;
        }

        public SoundTagBase GetGlobalSoundTagAtIndex(int index) {
            if (settings.globalSoundTags.Count <= index) {
                // Could also that its just not assigned yet, so not a big problem
                Debug.LogWarning($"Sonity: SoundManager - GetGlobalSoundTag index {index} doesn't exist, maybe assign default values?");
                return null;
            }
            return settings.globalSoundTags[index];
        }

        public bool GetGlobalSoundTagContains(SoundTagBase soundTag) {
            // If null dont trigger
            if (soundTag == null) {
                return false;
            }
            for (int i = 0; i < settings.globalSoundTags.Count; i++) {
                if (SonityGuidCompare.IsSameNotNull(settings.globalSoundTags[i], soundTag)) {
                    return true;
                }
            }
            return false;
        }
        
        public void SetGlobalDistanceScale(float distanceScale) {
            settings.distanceScale = Mathf.Clamp(distanceScale, 0f, Mathf.Infinity);
        }

        public float GetGlobalDistanceScale() {
            return settings.distanceScale;
        }

        public void SetSpeedOfSoundEnabled(bool speedOfSoundEnabled) {
            settings.speedOfSoundEnabled = speedOfSoundEnabled;
        }

        public void SetSpeedOfSoundScale(float speedOfSoundScale) {
            settings.speedOfSoundScale = Mathf.Clamp(speedOfSoundScale, 0f, Mathf.Infinity);
        }

        public float GetSpeedOfSoundScale() {
            return settings.speedOfSoundScale;
        }

        public void SetVoiceLimit(int voiceLimit) {
            settings.voiceLimit = Mathf.Clamp(voiceLimit, 0, int.MaxValue);
        }

        public int GetVoiceLimit() {
            return settings.voiceLimit;
        }

        public void SetVoiceEffectLimit(int voiceEffectLimit) {
            settings.voiceEffectLimit = Mathf.Clamp(voiceEffectLimit, 0, int.MaxValue);
        }

        public int GetVoiceEffectLimit() {
            return settings.voiceEffectLimit;
        }

        public void SetDisablePlayingSounds(bool disablePlayingSounds) {
            settings.disablePlayingSounds = disablePlayingSounds;
        }

        public bool GetDisablePlayingSounds() {
            return settings.disablePlayingSounds;
        }

        public AudioMixer GetAddressableAudioMixer() {
#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
            return settings.GetAddressableAudioMixer();
#else
            Debug.LogError($"Sonity.{NameOf.SoundManager}: The Script Define Symbol \"SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER\" is false but you're trying to access the Addressable AudioMixer");
            return null;
#endif
        }
    }
}