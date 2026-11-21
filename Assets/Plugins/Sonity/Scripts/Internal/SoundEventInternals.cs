// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Sonity.Internal {

    [Serializable]
    public class SoundEventInternals {

        public string cachedName;
        public SonityGuid sonityGuid;

#if UNITY_EDITOR
        public string sonityVersion = new Version(0, 0, 0, 0).ToString(); // Major Minor Patch Hotfix
        [SerializeField] private string sonityGuidEditor = ""; // Used for drawing the field in the editor

        // Triggered by the AssetPostprocessor
        public void InitializeObject(UnityEngine.Object asset) {
            bool anyChanged = false;
            // SoundEvent specific initialization
            // Initialize Timeline SoundContainer Data
            if (data.timelineSoundContainerData.Length != soundContainers.Length) {
                anyChanged = true;
                Array.Resize(ref data.timelineSoundContainerData, soundContainers.Length);
                for (int i = 0; i < data.timelineSoundContainerData.Length; i++) {
                    if (data.timelineSoundContainerData[i] == null) {
                        anyChanged = true;
                        data.timelineSoundContainerData[i] = new SoundEventTimelineData();
                    }
                }
            }
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

		public SoundEventInternals() {
            // Resizing timeline length on creation
            InitializeTimelineSoundContainerSetting();
        }

        public void InitializeTimelineSoundContainerSetting() {
            if (data.timelineSoundContainerData.Length != soundContainers.Length) {
                Array.Resize(ref data.timelineSoundContainerData, soundContainers.Length);
            }
            for (int i = 0; i < data.timelineSoundContainerData.Length; i++) {
                if (data.timelineSoundContainerData[i] == null) {
                    data.timelineSoundContainerData[i] = new SoundEventTimelineData();
                }
            }
        }

#if UNITY_EDITOR
        public string notes = "Notes";
#endif

        public SoundEventInternalsData data = new SoundEventInternalsData();

        public SoundContainerBase[] soundContainers = new SoundContainerBase[1];

        public bool GetContainsLoop() {
            for (int i = 0; i < soundContainers.Length; i++) {
                if (soundContainers[i] != null) {
                    if (soundContainers[i].internals.data.loopEnabled) {
                        return true;
                    }
                }
            }
            return false;
        }

        // Used to check the array length the timeline settings
        public SoundEventTimelineData GetTimelineSoundContainerSetting(int index) {
            if (data.timelineSoundContainerData.Length != soundContainers.Length) {
                Array.Resize(ref data.timelineSoundContainerData, soundContainers.Length);
                for (int i = 0; i < data.timelineSoundContainerData.Length; i++) {
                    if (data.timelineSoundContainerData[i] == null) {
                        data.timelineSoundContainerData[i] = new SoundEventTimelineData();
                    }
                }
            }
            return data.timelineSoundContainerData[index];
        }

        public float GetMaxLengthWithPitchAndTimeline() {
            float maxLength = 0f;
            // The lowest delay
            float timelineLowestDelay = Mathf.Infinity;
            for (int i = 0; i < soundContainers.Length; i++) {
                if (timelineLowestDelay > GetTimelineSoundContainerSetting(i).delay) {
                    timelineLowestDelay = GetTimelineSoundContainerSetting(i).delay;
                }
            }
            for (int i = 0; i < soundContainers.Length; i++) {
                if (soundContainers[i] != null) {
                    float length = 0f;
                    // Avoid divide by zero
                    if (soundContainers[i].internals.data.pitchRatio > 0f) {
                        // Get the longest AudioClip length and apply pitch ratio
                        length = soundContainers[i].internals.GetLongestAudioClipLength() / soundContainers[i].internals.data.pitchRatio;
                    }

                    // If pitch is used
                    if (data.soundEventModifier.pitchUse) {
                        // Avoid divide by zero
                        if (data.soundEventModifier.pitchRatio > 0f) {
                            // Scaling to the SoundEvent settings pitch ratio
                            length /= data.soundEventModifier.pitchRatio;
                        }
                    }

                    // Adding the timeline delay
                    length += GetTimelineSoundContainerSetting(i).delay - timelineLowestDelay;

                    // The longest length
                    if (maxLength < length) {
                        maxLength = length;
                    }
                }
            }
            return maxLength;
        }

        public float GetMaxDistance() {
            float maxDistance = 0f;
            for (int i = 0; i < soundContainers.Length; i++) {
                SoundContainerBase soundContainer = soundContainers[i];
                if (soundContainer != null) {
                    if (soundContainer.internals.data.distanceEnabled) {
                        if (maxDistance < soundContainer.internals.data.distanceScale) {
                            maxDistance = soundContainer.internals.data.distanceScale;
                        }
                    } else {
                        return Mathf.Infinity;
                    }
                }
            }
            // SoundEventModifier is scaled by SoundParameter also
            if (data.soundEventModifier.distanceScaleUse) {
                maxDistance *= data.soundEventModifier.distanceScale;
            }
            // Scale with SoundManager distance scale afterwards
            return maxDistance;
        }

        public float GetMinLengthFirstSoundContainerWithoutPitch() {
            if (soundContainers.Length > 0 && soundContainers[0] != null) {
                return soundContainers[0].internals.GetShortestAudioClipLength();
            }
            return 0f;
        }
    }

    [Serializable]
    public class SoundEventInternalsData {

        public SoundEventInternalsData() {
            // Here you can change any default values like eg:
            //soundEventModifier.fadeInLengthUse = true;
            //soundEventModifier.fadeOutLengthUse = true;
        }

        private bool SoundManagerIsNull() {
            if (SoundManagerBase.Instance == null) {
                if (!ApplicationQuitting.GetApplicationIsQuitting()) {
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager} is null. Add one to the scene.");
                }
                return true;
            }
            return false;
        }

        public bool CheckTriggerOnPlayIsInfiniteLoop(SoundEventBase soundEvent, bool isEditor) {
            if (isEditor) {
                if (GetIfInfiniteLoop(soundEvent, out SoundEventBase infiniteInstigator, out SoundEventBase infinitePrevious, TriggerOnType.TriggerOnPlay)) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundEvent} TriggerOnPlay: \"{infiniteInstigator.internals.cachedName}\" in \"{infinitePrevious.internals.cachedName}\" creates an infinite loop", infiniteInstigator);
                    }
                    return true;
                }
                return false;
            } else {
                if (!SoundManagerIsNull()) {
                    return SoundManagerBase.Instance.Internals.InternalCheckTriggerOnPlayIsInfiniteLoop(soundEvent);
                } else {
                    return true;
                }
            }
        }

        public bool CheckTriggerOnStopIsInfiniteLoop(SoundEventBase soundEvent, bool isEditor) {
            if (isEditor) {
                if (GetIfInfiniteLoop(soundEvent, out SoundEventBase infiniteInstigator, out SoundEventBase infinitePrevious, TriggerOnType.TriggerOnStop)) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundEvent} TriggerOnStop: \"{infiniteInstigator.internals.cachedName}\" in \"{infinitePrevious.internals.cachedName}\" creates an infinite loop", infiniteInstigator);
                    }
                    return true;
                }
                return false;
            } else {
                if (!SoundManagerIsNull()) {
                    return SoundManagerBase.Instance.Internals.InternalCheckTriggerOnStopIsInfiniteLoop(soundEvent);
                } else {
                    return true;
                }
            }
        }

        public bool CheckTriggerOnTailLengthTooShort(SoundEventBase soundEvent, bool isEditor) {
            if (isEditor) {
                if (soundEvent.internals.GetMinLengthFirstSoundContainerWithoutPitch() < triggerOnTailLength) {
                    if (ShouldDebug.Warnings()) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundEvent} \"{soundEvent.internals.cachedName}\" Trigger On Tail: Tail Length is longer than the shortest AudioClip on the first SoundContainer.", soundEvent);
                    }
                    return true;
                }
                return false;
            } else {
                if (!SoundManagerIsNull()) {
                    return SoundManagerBase.Instance.Internals.InternalCheckTriggerOnTailLengthTooShort(soundEvent);
                } else {
                    return true;
                }
            }
        }

        public bool GetIfInfiniteLoop(SoundEventBase soundEvent, out SoundEventBase infiniteInstigator, out SoundEventBase infinitePrevious, TriggerOnType triggerOnType) {
            infiniteInstigator = null;
            infinitePrevious = null;

            if (soundEvent == null) {
                return false;
            }

            List<SoundEventBase> toCheck = new List<SoundEventBase>();
            List<SoundEventBase> isChecked = new List<SoundEventBase>();

            toCheck.Add(soundEvent);

            while (toCheck.Count > 0) {
                SoundEventBase soundEventChild = toCheck[0];
                toCheck.RemoveAt(0);
                if (soundEventChild != null) {
                    for (int i = 0; i < isChecked.Count; i++) {
                        if (isChecked[i] == soundEventChild) {
                            infiniteInstigator = soundEventChild;
                            return true;
                        }
                    }
                    if (triggerOnType == TriggerOnType.TriggerOnPlay) {
                        if (soundEventChild.internals.data.triggerOnPlayEnable && soundEventChild.internals.data.triggerOnPlaySoundEvents != null) {
                            for (int i = 0; i < soundEventChild.internals.data.triggerOnPlaySoundEvents.Length; i++) {
                                toCheck.Add(soundEventChild.internals.data.triggerOnPlaySoundEvents[i]);
                                infinitePrevious = soundEventChild;
                            }
                        }
                    } else if (triggerOnType == TriggerOnType.TriggerOnStop) {
                        if (soundEventChild.internals.data.triggerOnStopEnable && soundEventChild.internals.data.triggerOnStopSoundEvents != null) {
                            for (int i = 0; i < soundEventChild.internals.data.triggerOnStopSoundEvents.Length; i++) {
                                toCheck.Add(soundEventChild.internals.data.triggerOnStopSoundEvents[i]);
                                infinitePrevious = soundEventChild;
                            }
                        }
                    } else if (triggerOnType == TriggerOnType.TriggerOnTail) {
                        if (soundEventChild.internals.data.triggerOnTailEnable && soundEventChild.internals.data.triggerOnTailSoundEvents != null) {
                            for (int i = 0; i < soundEventChild.internals.data.triggerOnTailSoundEvents.Length; i++) {
                                toCheck.Add(soundEventChild.internals.data.triggerOnTailSoundEvents[i]);
                                infinitePrevious = soundEventChild;
                            }
                        }
                    }
                    // TriggerOnTail should be enabled to loop
                    isChecked.Add(soundEventChild);
                }
            }
            return false;
        }

#if UNITY_EDITOR
        public AudioMixerGroup previewAudioMixerGroup;
#endif
        public bool expandSoundContainers = true;
        public bool expandTimeline = true;
        public bool expandPreview = false;

        public bool expandAllSoundTag = false;

        public SoundEventTimelineData[] timelineSoundContainerData = new SoundEventTimelineData[0];

        public bool disableEnable = false;
#if UNITY_EDITOR
        public bool muteEnable = false;
        public bool soloEnable = false;
#endif

#if UNITY_EDITOR
        public bool debugExpand = false;
        public DebugSoundEventLogShow debugLogShow = DebugSoundEventLogShow.Show;
        public DebugSoundEventDrawShow debugDrawShow = DebugSoundEventDrawShow.Show;
        public bool debugDrawSoundEventStyleOverride = false;
        public float debugDrawSoundEventFontSizeMultiplier = 1f;
        public float debugDrawSoundEventOpacityMultiplier = 1f;
        public Color debugDrawSoundEventColorStart = new Color(1f, 0f, 1f);
        public Color debugDrawSoundEventColorEnd = new Color(0.66f, 0f, 1f);
        public Color debugDrawSoundEventColorOutline = new Color(0f, 0f, 0f);
#endif

        // Base
        public bool baseExpand = true;
        // Decibel only used in the editor
        public float volumeDecibel = 0;
        public float volumeRatio = 1f;

        public float GetVolumeRatioClamped() {
            return Mathf.Clamp(volumeRatio, 0f, VolumeScale.volumeIncrease24dbMaxRatio);
        }

        public int polyphony = 1;

        public SoundEventModifier soundEventModifier = new SoundEventModifier();

        // Settings
        public bool settingsExpandBase = true;
        public bool settingsExpandAdvanced = false;
        public PolyphonyMode polyphonyMode;
        public AudioMixerGroup audioMixerGroup;
        public SoundVolumeGroupBase soundVolumeGroup;

        public float GetSoundVolumeGroupRatio() {
            if (soundVolumeGroup == null) {
                return 1f;
            }
            return soundVolumeGroup.internals.GetVolumeRatioClamped();
        }

        public SoundMixBase soundMix;
        public SoundPolyGroupBase soundPolyGroup;
        public float soundPolyGroupPriority = 1f;
        public float cooldownTime = 0f;
        public float probability = 100f;
        public bool passParameters = true;
        public bool ignoreLocalPause = false;
        public bool ignoreGlobalPause = false;

        // Intensity
        public bool expandIntensity = false;
        public float intensityAdd = 0f;
        public float intensityMultiplier = 1f;
        public float intensityRolloff = 0f;
        public float intensitySeekTime = 0f;
        public AnimationCurve intensityCurve = AnimationCurve.Linear(0, 0, 1, 1);
        public bool intensityThresholdEnable = false;
        public float intensityThreshold = 0f;

        public float intensityScaleAdd = 0f;
        public float intensityScaleMultiplier = 1f;

#if UNITY_EDITOR
        public bool intensityRecord = false;
        public bool GetIntensityRecord() {
            // Double check for DLL
            if (!Application.isEditor) {
                return false;
            } else {
                return intensityRecord;
            }
        }
        public int intensityDebugResolution = 100;
        public float intensityDebugZoom = 0.9f;
        public List<float> intensityDebugValueList;
#endif

        private float GetRolloff(float value, float rolloff) {
            return LogLinExp.Get(value, rolloff);
        }

        public float GetScaledIntensity(float unscaledIntensity) {
            return Mathf.Clamp(intensityCurve.Evaluate(GetRolloff(Mathf.Clamp((unscaledIntensity + intensityAdd + intensityScaleAdd) * intensityMultiplier * intensityScaleMultiplier, 0f, 1f), -intensityRolloff)), 0f, 1f);
        }

        public float EditorGetIntensityOnlyCurveAndRolloff(float unscaledIntensity) {
            return Mathf.Clamp(intensityCurve.Evaluate(GetRolloff(Mathf.Clamp(unscaledIntensity, 0f, 1f), -intensityRolloff)), 0f, 1f);
        }

        public float GetUnscaledIntensity(float intensity) {
            // Avoid divide by 0
            if (intensityMultiplier == 0f || intensityScaleMultiplier == 0f) {
                return 0f;
            } else {
                return intensity / intensityMultiplier / intensityScaleMultiplier - intensityAdd - intensityScaleAdd;
            }
        }

        // Trigger On
        public bool triggerOnExpand = false;

        // TriggerOnPlay
        public bool triggerOnPlayEnable = false;
        public SoundEventBase[] triggerOnPlaySoundEvents = new SoundEventBase[1];
        public WhichToPlay triggerOnPlayWhichToPlay = WhichToPlay.PlayAll;

        // TriggerOnStop
        public bool triggerOnStopEnable = false;
        public SoundEventBase[] triggerOnStopSoundEvents = new SoundEventBase[1];
        public WhichToPlay triggerOnStopWhichToPlay = WhichToPlay.PlayAll;

        // TriggerOnTail
        public bool triggerOnTailEnable = false;
        public SoundEventBase[] triggerOnTailSoundEvents = new SoundEventBase[1];
        public WhichToPlay triggerOnTailWhichToPlay = WhichToPlay.PlayAll;
        public float triggerOnTailLength = 0f;
        public float triggerOnTailBpm = 120f;
        public BeatLength triggerOnTailBeatLength = BeatLength.OneBar;

        // Random SoundEvent for Trigger On
        [NonSerialized]
        private int[] triggerOnPlayRandomLast = new int[0];
        [NonSerialized]
        private int triggerOnPlayRandomPosition;
        [NonSerialized]
        private int[] triggerOnStopRandomLast = new int[0];
        [NonSerialized]
        private int triggerOnStopRandomPosition;
        [NonSerialized]
        private int[] triggerOnTailRandomLast = new int[0];
        [NonSerialized]
        private int triggerOnTailRandomPosition;

        public int GetTriggerOnPlayRandomSoundEvent() {
            return GetTriggerOnRandomSoundEvent(ref triggerOnPlayRandomLast, ref triggerOnPlayRandomPosition, triggerOnPlaySoundEvents.Length);
        }

        public int GetTriggerOnStopRandomSoundEvent() {
            return GetTriggerOnRandomSoundEvent(ref triggerOnStopRandomLast, ref triggerOnStopRandomPosition, triggerOnStopSoundEvents.Length);
        }

        public int GetTriggerOnTailRandomSoundEvent() {
            return GetTriggerOnRandomSoundEvent(ref triggerOnTailRandomLast, ref triggerOnTailRandomPosition, triggerOnTailSoundEvents.Length);
        }

        private int GetTriggerOnRandomSoundEvent(ref int[] randomLast, ref int randomPosition, int soundEventLength) {
            // So that it wont break when changing the length in the editor at runtime
            if (randomLast.Length != soundEventLength / 2) {
                randomLast = new int[soundEventLength / 2];
                randomPosition = 0;
            }

            if (randomLast.Length == 0) {
                return 0;
            }

            int randomSoundEvent = 0;
            // Pseudo random function remembering which clips it last played to avoid repetition
            // Max 40 retries giving you a 0,0000000000009 probabiltiy of repetition
            for (int i = 0; i < 40; i++) {
                randomSoundEvent = UnityEngine.Random.Range(0, soundEventLength);
                if (!RandomLastContains(randomLast, randomSoundEvent)) {
                    break;
                }
            }
            randomLast[randomPosition] = randomSoundEvent;

            // Wrap index
            randomPosition++;
            if (randomPosition >= randomLast.Length) {
                randomPosition = 0;
            }

            return randomSoundEvent;
        }

        private bool RandomLastContains(int[] randomClipLast, int randomSoundEvent) {
            for (int i = 0; i < randomClipLast.Length; i++) {
                if (randomClipLast[i] == randomSoundEvent) {
                    return true;
                }
            }
            return false;
        }

        // SoundTag
        public bool soundTagEnable = false;
        public SoundTagMode soundTagMode = SoundTagMode.Local;
        public SoundTagGroup[] soundTagGroups = new SoundTagGroup[1];
    }
}