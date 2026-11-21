// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Sonity.Internal {

    public class SoundEventInstance {

        public string name = "";

        public SoundEventBase soundEvent;

        // Starting at a negative value so it wont be unable to play at start
        private float cooldownTimeCurrent = Mathf.NegativeInfinity;

        private int voicesNotPlaying;

        public bool waitingForPooling;

        public TransformID ownerTransformID;

        //private SoundContainerInstance[] soundContainerInstance = new SoundContainerInstance[0];
        private List<SoundContainerInstance> soundContainerInstance = new List<SoundContainerInstance>();

        private bool foundVoice;

        private VoiceParameterInstance latestVoiceParameterInstance = new VoiceParameterInstance();

        private SoundMixBase tempSoundMix;

        private SoundEventInstancePlayValues playValuesLast = new SoundEventInstancePlayValues();

        private int lastPlayedSoundContainerIndex = 0;

        private bool triggerOnTailClipFound = false;
        private float triggerOnTailClipLength = 0f;
        private bool triggerOnTailIsStopped = false;

        [NonSerialized]
        private bool triggerOnTailNullChecked = false;
        [NonSerialized]
        private bool triggerOnTailIsNull = false;

#if UNITY_EDITOR
        private bool mutedWarned = false;
        private bool disabledWarned = false;
        private bool intensityRecordWarned = false;
#endif

        // Need to set SoundEvent before calling this
        private void SoundContainerInstancesReset() {

            soundContainerInstance.Clear();
            int instancesToAdd = this.soundEvent.internals.soundContainers.Length;
            for (int i = 0; i < instancesToAdd; i++) {
                SoundContainerInstance container = new SoundContainerInstance();
                container.soundEvent = this.soundEvent;
                container.soundContainer = this.soundEvent.internals.soundContainers[i];
                container.randomClipLast = new int[this.soundEvent.internals.soundContainers[i].internals.audioClips.Length / 2];
                container.timelineSoundContainerSetting = soundEvent.internals.GetTimelineSoundContainerSetting(i);
                soundContainerInstance.Add(container);
            }

            // BETTER LATER RESET
            //int currentCount = soundContainerInstance.Count;
            //int newCount = this.soundEvent.internals.soundContainers.Length;

            //// Remove or add instances
            //if (currentCount < newCount) {
            //    // Add Instances
            //    int instancesToAdd = newCount - currentCount;
            //    for (int i = 0; i < instancesToAdd; i++) {
            //        soundContainerInstance.Add(new SoundContainerInstance());
            //    }
            //} else if (currentCount > newCount) {
            //    // Remove Instances
            //    int instancesToRemove = currentCount - newCount;
            //    for (int i = 0; i < instancesToRemove; i++) {
            //        // Remove topmost instance in list
            //        soundContainerInstance.RemoveAt(soundContainerInstance.Count - 1);
            //    }
            //}
            //// Need to update current count
            //currentCount = soundContainerInstance.Count;

            // OLD
            //soundContainerInstance = new SoundContainerInstance[this.soundEvent.internals.soundContainers.Length];
            //for (int i = 0; i < soundContainerInstance.Count; i++) {
            //    soundContainerInstance[i] = new SoundContainerInstance();
            //    SoundContainerInstance container = soundContainerInstance[i];
            //    container.soundEvent = this.soundEvent;
            //    container.soundContainer = this.soundEvent.internals.soundContainers[i];
            //    container.randomClipLast = new int[this.soundEvent.internals.soundContainers[i].internals.audioClips.Length / 2];
            //    container.timelineSoundContainerSetting = soundEvent.internals.GetTimelineSoundContainerSetting(i);
            //}
        }


        public void Initialize(SoundEventBase soundEvent, bool firstTime = false) {
            // Dont update SoundEvent if its the same
            if (firstTime || !SonityGuidCompare.IsSameNotNull(this.soundEvent, soundEvent)) {
                this.soundEvent = soundEvent;
                name = this.soundEvent.internals.cachedName;

                // Need to reset because can be used by different SoundEvents
                cooldownTimeCurrent = Mathf.NegativeInfinity;
                triggerOnTailNullChecked = false;
                triggerOnTailIsNull = false;
                lastPlayedSoundContainerIndex = 0;
#if UNITY_EDITOR
                mutedWarned = false;
                disabledWarned = false;
                intensityRecordWarned = false;
#endif

                SoundContainerInstancesReset();

                //soundContainerInstance = new SoundContainerInstance[this.soundEvent.internals.soundContainers.Length];
                //for (int i = 0; i < soundContainerInstance.Count; i++) {
                //    soundContainerInstance[i] = new SoundContainerInstance();
                //    SoundContainerInstance container = soundContainerInstance[i];
                //    container.soundEvent = this.soundEvent;
                //    container.soundContainer = this.soundEvent.internals.soundContainers[i];
                //    container.randomClipLast = new int[this.soundEvent.internals.soundContainers[i].internals.audioClips.Length / 2];
                //    container.timelineSoundContainerSetting = soundEvent.internals.GetTimelineSoundContainerSetting(i);
                //}
            }
        }

        public int GetPolyphonyLimit() {
            if (soundEvent.internals.data.soundPolyGroup != null) {
                // Is forced to the lower polyphony
                if (latestVoiceParameterInstance.currentModifier.polyphonyUse) {
                    // SoundPolyGroup or Modifier Polyphony
                    return Mathf.Min(soundEvent.internals.data.soundPolyGroup.internals.polyphonyLimit, latestVoiceParameterInstance.currentModifier.polyphony);
                } else {
                    // SoundPolyGroup or SoundEvent Polyphony
                    return Mathf.Min(soundEvent.internals.data.soundPolyGroup.internals.polyphonyLimit, soundEvent.internals.data.polyphony);
                }
            } else {
                if (latestVoiceParameterInstance.currentModifier.polyphonyUse) {
                    // Modifier Polyphony
                    return latestVoiceParameterInstance.currentModifier.polyphony;
                } else {
                    // SoundEvent Polyphony
                    return soundEvent.internals.data.polyphony;
                }
            }
        }

        public int StatisticsGetNumberOfUsedVoices() {
            int voices = 0;
            for (int i = 0; i < soundContainerInstance.Count; i++) {
                for (int ii = 0; ii < soundContainerInstance[i].voiceHolder.Length; ii++) {
                    if (soundContainerInstance[i].voiceHolder[ii].voice != null) {
                        voices++;
                    }
                }
            }
            return voices;
        }

        public float StatisticsGetAverageVolumeRatio() {
            float volume = 0f;
            int voices = 0;
            for (int i = 0; i < soundContainerInstance.Count; i++) {
                for (int ii = 0; ii < soundContainerInstance[i].voiceHolder.Length; ii++) {
                    VoiceHolder voiceHolder = soundContainerInstance[i].voiceHolder[ii];
                    if (voiceHolder.voice != null) {
                        voices++;
                        volume += voiceHolder.voice.GetVolumeRatioWithFade();
                    }
                }
            }
            // Avoid divide by zero
            if (voices > 0) {
                return volume / voices;
            } else {
                return volume;
            }
        }

        public class SoundEventInstancePlayValues {
            public SoundEventPlayType playType;
            public Transform instanceIDTransform;
            public Vector3? positionVector3;
            public Transform positionTransform;
            public SoundEventModifier soundEventModifierTrigger;
            public SoundEventModifier soundEventModifierSoundTag;
            public SoundParameterInternals[] soundParameters;
            public SoundParameterInternals soundParameterDistanceScale;
            public SoundTagBase localSoundTag;

            public void SetValues(
                SoundEventPlayType playType, Transform instanceIDTransform, Vector3? positionVector3, Transform positionTransform,
                SoundEventModifier soundEventModifierTrigger, SoundEventModifier soundEventModifierSoundTag,
                SoundParameterInternals[] soundParameters, SoundParameterInternals soundParameterDistanceScale, SoundTagBase localSoundTag) {

                this.playType = playType;
                this.instanceIDTransform = instanceIDTransform;
                this.positionVector3 = positionVector3;
                this.positionTransform = positionTransform;
                this.soundEventModifierTrigger = soundEventModifierTrigger;
                this.soundEventModifierSoundTag = soundEventModifierSoundTag;
                this.soundParameters = soundParameters;
                this.soundParameterDistanceScale = soundParameterDistanceScale;
                this.localSoundTag = localSoundTag;
            }
        }

        public void Play(
            SoundEventPlayType playType, Transform instanceIDTransform, Vector3? positionVector3, Transform positionTransform,
            SoundEventModifier soundEventModifierSoundPicker, SoundEventModifier soundEventModifierSoundTag,
            SoundParameterInternals[] soundParameters, SoundParameterInternals soundParameterDistanceScale, SoundTagBase localSoundTag) {

#if UNITY_EDITOR
            if (SoundManagerBase.Instance.Internals.debug.LogSoundEventsPlayEnabled(soundEvent)) {
                SoundManagerBase.Instance.Internals.debug.LogSoundEventsPlay(soundEvent, playType, instanceIDTransform, positionTransform, positionVector3);
            }
#endif

#if UNITY_EDITOR
            // If the SoundContainers array is changed while playing in the editor
            if (this.soundEvent.internals.soundContainers.Length != soundContainerInstance.Count) {
                SoundContainerInstancesReset();
                //soundContainerInstance = new SoundContainerInstance[this.soundEvent.internals.soundContainers.Length];
                //for (int s = 0; s < soundContainerInstance.Count; s++) {
                //    SoundContainerInstance container = new SoundContainerInstance();
                //    soundContainerInstance[s] = container;
                //    container.soundEvent = this.soundEvent;
                //    container.soundContainer = this.soundEvent.internals.soundContainers[s];
                //    container.randomClipLast = new int[this.soundEvent.internals.soundContainers[s].internals.audioClips.Length / 2];
                //    container.timelineSoundContainerSetting = soundEvent.internals.GetTimelineSoundContainerSetting(s);
                //}
            }
            // Updates SoundContainers if they are changed while playing in the editor
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                if (this.soundEvent.internals.soundContainers[s] != null && container != null && container.soundContainer != null) {
                    if (container.soundContainer != this.soundEvent.internals.soundContainers[s]) {
                        container.soundContainer = this.soundEvent.internals.soundContainers[s];
                        container.randomClipLast = new int[this.soundEvent.internals.soundContainers[s].internals.audioClips.Length / 2];
                    }
                }
            }
#endif
            // Unity 6000.5 removes Transform.InstanceID and replaces it with Transform.EntityId
            ownerTransformID = new TransformID(instanceIDTransform);

            if (soundEvent.internals.data.disableEnable) {
#if UNITY_EDITOR
                // Warning once if disabled
                if (!disabledWarned && ShouldDebug.Warnings()) {
                    disabledWarned = true;
                    Debug.LogWarning($"Sonity.{NameOf.SoundEvent}: The {soundEvent.internals.cachedName} is disabled.", soundEvent);
                }
#endif
                return;
            }

            // Probability range 0 to 100 %
            if (soundEvent.internals.data.probability == 100f || soundEvent.internals.data.probability > UnityEngine.Random.Range(0f, 100f)) {
                
                // Cooldown
                if (soundEvent.internals.data.cooldownTime == 0f || SoundTimeScale.GetTimeRuntime() - cooldownTimeCurrent > soundEvent.internals.data.cooldownTime) {
                    cooldownTimeCurrent = SoundTimeScale.GetTimeRuntime();
#if UNITY_EDITOR
                    // Warning once if muted
                    if (soundEvent.internals.data.muteEnable && !mutedWarned) {
                        mutedWarned = true;
                        if (ShouldDebug.Warnings()) {
                            Debug.LogWarning($"Sonity.{NameOf.SoundEvent}: The {soundEvent.internals.cachedName} is muted.", soundEvent);
                        }
                    }
#endif
                    // Nullcheck SoundParameters
                    if (soundParameters != null) {
                        for (int i = 0; i < soundParameters.Length; i++) {
                            if (soundParameters[i] == null) {
                                if (ShouldDebug.Warnings()) {
                                    Debug.LogWarning($"Sonity.{NameOf.SoundEvent}: The {soundEvent.internals.cachedName} has null {NameOf.SoundParameter}s.", soundEvent);
                                    break;
                                }
                            }
                        }
                    }

                    // Save Values for TriggerOn other SoundEvents
                    if (soundEvent.internals.data.triggerOnPlayEnable || soundEvent.internals.data.triggerOnStopEnable || soundEvent.internals.data.triggerOnTailEnable) {
                        playValuesLast.SetValues(
                            playType, instanceIDTransform, positionVector3, positionTransform, soundEventModifierSoundPicker,
                            soundEventModifierSoundTag, soundParameters, soundParameterDistanceScale, localSoundTag);
                    }

#if UNITY_EDITOR
                    // Warning if volume is over 0 SoundEvent
                    if (soundEvent.internals.data.volumeRatio > (VolumeScale.volumeIncrease24dbMaxRatio + 0.00001)) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundEvent}: {soundEvent.internals.cachedName} volume is over 0dB, please add scripting define symbol: \"SONITY_ENABLE_VOLUME_INCREASE\" or lower the volume.", soundEvent);
                    }
#endif

#if UNITY_EDITOR
                    // Warning if volume is over 0 SoundVolumeGroup
                    if (soundEvent.internals.data.soundVolumeGroup != null && soundEvent.internals.data.soundVolumeGroup.internals.volumeRatio > (VolumeScale.volumeIncrease12dbMaxRatio + 0.00001)) {
                        Debug.LogWarning($"Sonity.{NameOf.SoundVolumeGroup}: {soundEvent.internals.data.soundVolumeGroup.internals.cachedName} volume is over 0dB, please add scripting define symbol: \"SONITY_ENABLE_VOLUME_INCREASE\" or lower the volume.", soundEvent.internals.data.soundVolumeGroup);
                    }
#endif

                    // If there are no SoundContainers
                    if (soundEvent.internals.soundContainers.Length > 0) {

#if UNITY_EDITOR
                        // Warning if volume is over 0 SoundContainer
                        for (int i = 0; i < soundEvent.internals.soundContainers.Length; i++) {
                            SoundContainerBase soundContainerTemp = soundEvent.internals.soundContainers[i];
                            if (soundContainerTemp != null) {
                                if (soundContainerTemp.internals.data.volumeRatio > (VolumeScale.volumeIncrease24dbMaxRatio + 0.00001)) {
                                    Debug.LogWarning($"Sonity.{NameOf.SoundContainer}: {soundContainerTemp.internals.cachedName} volume is over 0dB, please add scripting define symbol: \"SONITY_ENABLE_VOLUME_INCREASE\" or lower the volume.", soundEvent.internals.soundContainers[i]);
                                }
                            }
                        }
#endif

#if UNITY_EDITOR
                        // For Statistics
                        SoundManagerBase.Instance.Internals.InternalStatisticsNumberOfPlays(soundEvent, true);
#endif
                        // Reset on Play
                        voicesNotPlaying = 0;
                        waitingForPooling = false;

                        // Used to calculate the time played
                        lastStartTime = SoundTimeScale.GetTimeRuntime();

                        // Modifiers Update
                        latestVoiceParameterInstance.ResetModifiers();

                        // Add SoundEvent Modifier
                        latestVoiceParameterInstance.ModifiersAddValuesToOffset(soundEvent.internals.data.soundEventModifier);
                        
                        // Tag Modifier
                        if (soundEvent.internals.data.soundTagEnable) {
                            if (soundEvent.internals.data.soundTagMode == SoundTagMode.Local && localSoundTag != null) {
                                for (int i = 0; i < soundEvent.internals.data.soundTagGroups.Length; i++) {
                                    SoundTagGroup soundTagGroup = soundEvent.internals.data.soundTagGroups[i];
                                    if (soundTagGroup.soundTag == localSoundTag) {
                                        latestVoiceParameterInstance.ModifiersAddValuesToOffset(soundTagGroup.soundEventModifierBase);
                                    }
                                }
                            } else if (soundEvent.internals.data.soundTagMode == SoundTagMode.Global && SoundManagerBase.Instance.Internals.settings.globalSoundTags != null) {
                                for (int i = 0; i < soundEvent.internals.data.soundTagGroups.Length; i++) {
                                    SoundTagGroup soundTagGroup = soundEvent.internals.data.soundTagGroups[i];
                                    if (SoundManagerBase.Instance.Internals.GetGlobalSoundTagContains(soundTagGroup.soundTag)) {
                                        latestVoiceParameterInstance.ModifiersAddValuesToOffset(soundTagGroup.soundEventModifierBase);
                                    }
                                }
                            }
                        }

                        // If this instance is triggered from a SoundTrigger or SoundPicker
                        latestVoiceParameterInstance.ModifiersAddValuesToOffset(soundEventModifierSoundPicker);
                        
                        // If this instance is a sub event in an SoundTag
                        latestVoiceParameterInstance.ModifiersAddValuesToOffset(soundEventModifierSoundTag);

                        // Adding SoundMix and their parents
                        if (soundEvent.internals.data.soundMix != null && !soundEvent.internals.data.soundMix.internals.CheckIsInfiniteLoop(soundEvent.internals.data.soundMix, false)) {
                            tempSoundMix = soundEvent.internals.data.soundMix;
                            while (tempSoundMix != null) {
                                latestVoiceParameterInstance.ModifiersAddValuesToOffset(tempSoundMix.internals.soundEventModifier);
                                tempSoundMix = tempSoundMix.internals.soundMixParent;
                            }
                        }

                        // Radius Handle SoundParameter
                        if (soundParameterDistanceScale != null) {
                            latestVoiceParameterInstance.offsetModifier.distanceScale *= soundParameterDistanceScale.internals.valueFloat;
                        }

                        // SoundParameter Update
                        latestVoiceParameterInstance.SetSoundParameters(soundParameters);
                        latestVoiceParameterInstance.SoundParameterUpdateOnce();
                        latestVoiceParameterInstance.SoundParameterUpdateContinuous();
#if UNITY_EDITOR
                        if (SoundManagerBase.Instance.Internals.debug.LogSoundEventsSoundParametersOnceEnabled(soundEvent)) {
                            SoundManagerBase.Instance.Internals.debug.LogSoundEventsSoundParameters(soundEvent, latestVoiceParameterInstance.soundParameters);
                        }
#endif

#if UNITY_EDITOR
                        // Intensity Debug
                        if (latestVoiceParameterInstance.currentModifier.intensityUse) {
                            if (soundEvent.internals.data.GetIntensityRecord()) {
                                soundEvent.internals.data.intensityDebugValueList.Add(latestVoiceParameterInstance.currentModifier.intensity);
                                // Warning once if intensity record
                                if (!intensityRecordWarned) {
                                    intensityRecordWarned = true;
                                    if (ShouldDebug.Warnings()) {
                                        Debug.LogWarning($"Sonity.{NameOf.SoundEvent}: The {soundEvent.internals.cachedName} has Intensity Record enabled.", soundEvent);
                                    }
                                }
                            }
                        }
#endif
                        // If its not under the threshold
                        if (!(latestVoiceParameterInstance.currentModifier.intensityUse && soundEvent.internals.data.intensityThresholdEnable 
                            && soundEvent.internals.data.GetScaledIntensity(latestVoiceParameterInstance.currentModifier.intensity) < soundEvent.internals.data.intensityThreshold
                            )) {
                            // Assign Voice
                            for (int s = 0; s < soundContainerInstance.Count; s++) {
                                SoundContainerInstance container = soundContainerInstance[s];
                                foundVoice = false;
                                for (int n = 0; n < container.nextVoices.Count; n++) {
                                    NextVoice nextVoice = container.nextVoices[n];
                                    // Found Voice
                                    if (!nextVoice.assinged) {
                                        nextVoice.Assign(
                                            latestVoiceParameterInstance, playType, instanceIDTransform, positionVector3, positionTransform, container.soundContainer, soundEvent);
                                        container.NextVoiceSetMaxRange(n);
                                        nextVoice.playTypeInstance.SetCachedDistancesAndAngle(nextVoice.maxRange, nextVoice.voiceParameter, true);
                                        // Delay
                                        nextVoice.startTimeAndDelay = SoundTimeScale.GetTimeRuntime() + soundEvent.internals.GetTimelineSoundContainerSetting(s).delay;
                                        if (latestVoiceParameterInstance.currentModifier.delayUse) {
                                            nextVoice.startTimeAndDelay += latestVoiceParameterInstance.currentModifier.delay;
                                        }
                                        foundVoice = true;
                                        break;
                                    }
                                }
                                // Not Found Voice
                                if (!foundVoice) {
                                    container.nextVoices.Add(new NextVoice());
                                    int newIndex = container.nextVoices.Count - 1;
                                    NextVoice newNextVoice = container.nextVoices[newIndex];
                                    newNextVoice.Assign(
                                        latestVoiceParameterInstance, playType, instanceIDTransform, positionVector3, positionTransform, container.soundContainer, soundEvent);
                                    container.NextVoiceSetMaxRange(newIndex);
                                    newNextVoice.playTypeInstance.SetCachedDistancesAndAngle(newNextVoice.maxRange, newNextVoice.voiceParameter, true);
                                    // Delay
                                    newNextVoice.startTimeAndDelay = SoundTimeScale.GetTimeRuntime() + soundEvent.internals.GetTimelineSoundContainerSetting(s).delay;
                                    if (latestVoiceParameterInstance.currentModifier.delayUse) {
                                        newNextVoice.startTimeAndDelay += latestVoiceParameterInstance.currentModifier.delay;
                                    }
                                }
                            }
                        }

                        // Remove or add voice containers
                        for (int s = 0; s < soundContainerInstance.Count; s++) {
                            SoundContainerInstance container = soundContainerInstance[s];
                            if (container.voiceHolder.Length != GetPolyphonyLimit()) {
                                if (container.voiceHolder.Length < GetPolyphonyLimit()) {
                                    // If there are too few voice containers
                                    Array.Resize(ref container.voiceHolder, GetPolyphonyLimit());
                                    for (int v = 0; v < container.voiceHolder.Length; v++) {
                                        if (container.voiceHolder[v] == null) {
                                            container.voiceHolder[v] = new VoiceHolder();
                                        }
                                    }
                                } else if (container.voiceHolder.Length > GetPolyphonyLimit()) {
                                    // If there are too many voice containers
                                    for (int v = 0; v < GetPolyphonyLimit() - container.voiceHolder.Length; v++) {
                                        container.voiceHolder[GetPolyphonyLimit() + v].PoolSingleVoice(false, false);
                                    }
                                    Array.Resize(ref container.voiceHolder, GetPolyphonyLimit());
                                    if (container.nextVoiceIndex >= GetPolyphonyLimit()) {
                                        container.nextVoiceIndex = 0;
                                    }
                                }
                            }
                        }

                        // Check if any should play
                        for (int s = 0; s < soundContainerInstance.Count; s++) {
                            SoundContainerInstance container = soundContainerInstance[s];
                            for (int n = 0; n < container.nextVoices.Count; n++) {
                                NextVoice nextVoice = container.nextVoices[n];
                                if (nextVoice.assinged) {
                                    // Check if delayed
                                    if (nextVoice.startTimeAndDelay <= SoundTimeScale.GetTimeRuntime() && nextVoice.playTypeInstance.GetSpeedOfSoundDistance() <= 0) {
                                        // Checking if the SoundContainer should play
                                        if (container.NextVoiceShouldPlay(n)) {
                                            container.voiceHolder[container.nextVoiceIndex].voiceIsToPlay = true;
                                            // Preparing Voice
                                            container.VoicePrepare(s, container.nextVoiceIndex, true, nextVoice.voiceParameter, nextVoice.playTypeInstance, false);
                                            nextVoice.ResetAssigned();
                                            break;
                                        } else {
                                            // Shouldn't play
                                            nextVoice.ResetAssigned();
                                        }
                                    }
                                }
                            }
                        }

                        // Playing the SoundContainer
                        for (int s = 0; s < soundContainerInstance.Count; s++) {
                            SoundContainerInstance container = soundContainerInstance[s];
                            if (container.voiceHolder[container.nextVoiceIndex].voiceIsToPlay) {
                                container.voiceHolder[container.nextVoiceIndex].voiceIsToPlay = false;
                                container.VoicePlay(container.nextVoiceIndex, GetPolyphonyLimit(), lastStartTime);
                                // TriggerOnTail
                                if (soundEvent.internals.data.triggerOnTailEnable) {
                                    // Only SC 0 should reset TriggerOnTail
                                    if (s == 0) {
                                        container.voiceHolder[container.lastPlayedVoiceIndex].triggerOnTailHasPlayed = false;
                                        triggerOnTailClipFound = false;
                                        triggerOnTailIsStopped = false;
                                    }
                                }
                                // Last played SoundContainer index to keep track of which was the last SoundContainer to play
                                lastPlayedSoundContainerIndex = s;
                            }
                        }
                    }

                    // SoundTag play other SoundEvent
                    if (soundEvent.internals.data.soundTagEnable) {
                        // Local SoundTag
                        if (soundEvent.internals.data.soundTagMode == SoundTagMode.Local && localSoundTag != null) {
                            for (int i = 0; i < soundEvent.internals.data.soundTagGroups.Length; i++) {
                                SoundTagGroup soundTagGroup = soundEvent.internals.data.soundTagGroups[i];
                                if (SonityGuidCompare.IsSameNotNull(soundTagGroup.soundTag, localSoundTag)) {
                                    for (int ii = 0; ii < soundTagGroup.soundEvent.Length; ii++) {
                                        SoundEventBase tagSoundEvent = soundTagGroup.soundEvent[ii];
                                        if (tagSoundEvent != null) {
                                            // Does not send SoundTag, so it can not repeat infinitely
                                            SoundManagerBase.Instance.Internals.InternalPlay(
                                                tagSoundEvent,
                                                playType,
                                                instanceIDTransform,
                                                positionVector3,
                                                positionTransform,
                                                soundEventModifierSoundPicker,
                                                soundTagGroup.soundEventModifierSoundTag,
                                                this.soundEvent.internals.data.passParameters ? soundParameters : null,
                                                soundParameterDistanceScale,
                                                null
                                                );
                                        }
                                    }
                                }
                            }
                        }
                        // Global SoundTag
                        else if (soundEvent.internals.data.soundTagMode == SoundTagMode.Global && SoundManagerBase.Instance.Internals.settings.globalSoundTags != null) {
                            for (int i = 0; i < soundEvent.internals.data.soundTagGroups.Length; i++) {
                                SoundTagGroup soundTagGroup = soundEvent.internals.data.soundTagGroups[i];
                                if (SoundManagerBase.Instance.Internals.GetGlobalSoundTagContains(soundTagGroup.soundTag)) {
                                    for (int ii = 0; ii < soundTagGroup.soundEvent.Length; ii++) {
                                        SoundEventBase tagSoundEvent = soundTagGroup.soundEvent[ii];
                                        if (tagSoundEvent != null) {
                                            // Does not send SoundTag, so it can not repeat infinitely
                                            SoundManagerBase.Instance.Internals.InternalPlay(
                                                tagSoundEvent,
                                                playType,
                                                instanceIDTransform,
                                                positionVector3,
                                                positionTransform,
                                                soundEventModifierSoundPicker,
                                                soundTagGroup.soundEventModifierSoundTag,
                                                this.soundEvent.internals.data.passParameters ? soundParameters : null,
                                                soundParameterDistanceScale,
                                                null
                                                );
                                        }
                                    }
                                }
                            }
                        }
                    }
                    // TriggerOnPlay
                    if (soundEvent.internals.data.triggerOnPlayEnable) {
                        if (!soundEvent.internals.data.CheckTriggerOnPlayIsInfiniteLoop(soundEvent, false)) {
                            TriggerOtherSoundEvent(soundEvent.internals.data.triggerOnPlaySoundEvents, soundEvent.internals.data.triggerOnPlayWhichToPlay, SoundEventTriggerOnType.TriggerOnPlay);
                        }
                    }
                }
            }
            if (isPausedLocal) {
                PauseUnpauseNormal(true, true);
            }
            if (AudioListener.pause) {
                PauseUnpauseGlobal(true);
            }
        }

        public SoundEventState GetSoundEventState() {
            if (isPausedGlobal || isPausedLocal) {
                return SoundEventState.Paused;
            }
            // Check if any over the voices are playing
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                if (soundContainerInstance[s].GetIsPlaying()) {
                    return SoundEventState.Playing;
                }
            }
            // Check if any of the voice are going to play
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                if (soundContainerInstance[s].GetNextVoiceListAnyAssigned()) {
                    return SoundEventState.Delayed;
                }
            }
            // If nothing is playing
            return SoundEventState.NotPlaying;
        }

        public float GetLastPlayedAudioSourceClipLength(bool pitchSpeed) {
            if (lastPlayedSoundContainerIndex >= soundContainerInstance.Count || soundContainerInstance[lastPlayedSoundContainerIndex] == null) {
                return Mathf.Infinity;
            } else {
                return soundContainerInstance[lastPlayedSoundContainerIndex].GetLastPlayedClipLength(pitchSpeed);
            }
        }

        public float GetLastPlayedAudioSourceTimeSeconds(bool pitchSpeed) {
            if (lastPlayedSoundContainerIndex >= soundContainerInstance.Count || soundContainerInstance[lastPlayedSoundContainerIndex] == null) {
                return 0f;
            } else {
                return soundContainerInstance[lastPlayedSoundContainerIndex].GetLastPlayedClipTimeSeconds(pitchSpeed);
            }
        }

        public float GetLastPlayedAudioSourceTimeRatio() {
            if (lastPlayedSoundContainerIndex >= soundContainerInstance.Count || soundContainerInstance[lastPlayedSoundContainerIndex] == null) {
                return 0f;
            } else {
                return soundContainerInstance[lastPlayedSoundContainerIndex].GetLastPlayedClipTimeRatio();
            }
        }

        public void GetSpectrumData(ref float[] samples, int channel, FFTWindow window, SpectrumDataFrom spectrumDataFrom) {
            if (spectrumDataFrom == SpectrumDataFrom.LastPlayedAudioSource) {
                if (lastPlayedSoundContainerIndex >= soundContainerInstance.Count || soundContainerInstance[lastPlayedSoundContainerIndex] == null) {
                    return;
                } else {
                    soundContainerInstance[lastPlayedSoundContainerIndex].GetLastPlayedAudioSourceSpectrumData(ref samples, channel, window);
                }
            }
        }

        public AudioSource GetLastPlayedAudioSource() {
            if (lastPlayedSoundContainerIndex >= soundContainerInstance.Count || soundContainerInstance[lastPlayedSoundContainerIndex] == null) {
                return null;
            } else {
                return soundContainerInstance[lastPlayedSoundContainerIndex].GetLastPlayedAudioSource();
            }
        }

        private void TriggerOnTailSetClip() {
            triggerOnTailClipFound = true;
            triggerOnTailClipLength = soundContainerInstance[0].GetLastPlayedClipLength(false);
        }

        private float TriggerOnTailGetClipLength() {
            return triggerOnTailClipLength;
        }

        private float TriggerOnTailGetClipTime() {
            return soundContainerInstance[0].GetLastPlayedClipTimeSeconds(false);
        }

        private bool TriggerOnTailGetHasPlayed(bool useLastPlayedVoiceIndex, int voiceIndex) {
            SoundContainerInstance container = soundContainerInstance[0];
            if (useLastPlayedVoiceIndex) {
                return container.voiceHolder[container.lastPlayedVoiceIndex].triggerOnTailHasPlayed;
            } else {
                return container.voiceHolder[voiceIndex].triggerOnTailHasPlayed;
            }
        }

        private void TriggerOnTailSetHasPlayed(bool useLastPlayedVoiceIndex, int voiceIndex) {
            SoundContainerInstance container = soundContainerInstance[0];
            if (useLastPlayedVoiceIndex) {
                container.voiceHolder[container.lastPlayedVoiceIndex].triggerOnTailHasPlayed = true;
            } else {
                container.voiceHolder[voiceIndex].triggerOnTailHasPlayed = true;
            }
        }

        private void TriggerOnTailUpdate(bool isPooledForcePlay, int pooledVoiceIndex = 0) {
            if (soundEvent.internals.data.triggerOnTailEnable) {
                if (!triggerOnTailNullChecked) {
                    triggerOnTailNullChecked = true;
                    if (soundEvent.internals.soundContainers.Length == 0 || soundEvent.internals.soundContainers[0] == null) {
                        triggerOnTailIsNull = true;
                        if (ShouldDebug.Warnings()) {
                            Debug.LogWarning($"Sonity: \"{soundEvent.internals.cachedName}\" TriggerOnTail: No {nameof(AudioClip)} found on the first {NameOf.SoundContainer}.", soundEvent);
                        }
                    }
                }
                if (!triggerOnTailIsNull && !TriggerOnTailGetHasPlayed(!isPooledForcePlay, pooledVoiceIndex) && !triggerOnTailIsStopped) {
                    if (!triggerOnTailClipFound) {
                        TriggerOnTailSetClip();
                    }
                    if (isPooledForcePlay || (triggerOnTailClipFound && TriggerOnTailGetClipTime() >= TriggerOnTailGetClipLength() - soundEvent.internals.data.triggerOnTailLength)) {
                        TriggerOnTailSetHasPlayed(!isPooledForcePlay, pooledVoiceIndex);
                        // Trigger On Tail
                        if (soundEvent.internals.data.triggerOnTailEnable && !soundEvent.internals.data.CheckTriggerOnTailLengthTooShort(soundEvent, false)) {
                            TriggerOtherSoundEvent(soundEvent.internals.data.triggerOnTailSoundEvents, soundEvent.internals.data.triggerOnTailWhichToPlay, SoundEventTriggerOnType.TriggerOnTail);
                        }
                    }
                }
            }
        }

        private float lastStartTime;

        public float GetTimePlayed() {
            return SoundTimeScale.GetTimeRuntime() - lastStartTime;
        }

        public void PoolSingleVoice(int s, int v, bool shouldRestartIfLoop, bool allowFadeOut, bool isCalledByStop, bool isCalledByOnDestroy) {
            // Trigger on tail
            if (s == 0 && !isCalledByStop && !isCalledByOnDestroy && soundContainerInstance[0].soundEvent.internals.data.triggerOnTailEnable) {
                // Only the last played voice index should trigger on tail
                if (v == soundContainerInstance[0].lastPlayedVoiceIndex) {
                    TriggerOnTailUpdate(true, v);
                }
            }
            // Pooling
            VoiceHolder voiceHolder = soundContainerInstance[s].voiceHolder[v];
            if (allowFadeOut) {
                voiceHolder.voiceFade.SetToFadeOut(voiceHolder.voiceParameter.currentModifier);
                voiceHolder.shouldRestartIfLoop = isCalledByStop ? false : shouldRestartIfLoop;
            } else {
                voiceHolder.PoolSingleVoice(isCalledByStop ? false : shouldRestartIfLoop, isCalledByOnDestroy);
            }
        }

        public void PoolAllVoices(bool allowFadeOut, bool isCalledByStop, bool isCalledByOnDestroy) {
            // TriggerOnTail dont play after stop
            // TriggerOnStop
            if (isCalledByStop && !isCalledByOnDestroy) {
#if UNITY_EDITOR
                if (SoundManagerBase.Instance.Internals.debug.LogSoundEventsStopEnabled(soundEvent)) {
                    SoundManagerBase.Instance.Internals.debug.LogSoundEventsStop(soundEvent, allowFadeOut);
                }
#endif
                triggerOnTailIsStopped = true;
                if (soundEvent.internals.data.triggerOnStopEnable) {
                    if (!soundEvent.internals.data.CheckTriggerOnStopIsInfiniteLoop(soundEvent, false)) {
                        TriggerOtherSoundEvent(soundEvent.internals.data.triggerOnStopSoundEvents, soundEvent.internals.data.triggerOnStopWhichToPlay, SoundEventTriggerOnType.TriggerOnStop);
                    }
                }
            }
            // Pooling
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                for (int v = 0; v < container.voiceHolder.Length; v++) {
                    PoolSingleVoice(s, v, false, allowFadeOut, isCalledByStop, isCalledByOnDestroy);
                }
                if (isCalledByStop) {
                    // Removing all delayed voices
                    for (int n = 0; n < container.nextVoices.Count; n++) {
                        container.nextVoices[n].ResetAssigned();
                    }
                }
            }
        }

        public void PoolVoicesWithPositionTransform(Transform positionTransform, bool allowFadeOut) {
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                for (int v = 0; v < container.voiceHolder.Length; v++) {
                    VoiceHolder voiceHolder = container.voiceHolder[v];
                    if (voiceHolder.playTypeInstance.playType == SoundEventPlayType.PlayAtTransform
                        && voiceHolder.playTypeInstance.positionTransform == positionTransform) {
                        PoolSingleVoice(s, v, false, allowFadeOut, true, false);
                    }
                }
            }
        }

        public void TriggerOtherSoundEvent(SoundEventBase[] soundEvents, WhichToPlay whichToPlay, SoundEventTriggerOnType triggerType) {
            if (soundEvents.Length == 0) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"Sonity: \"{soundEvent.internals.cachedName}\": The {triggerType.ToString()} has no {NameOf.SoundEvent}s.", soundEvent);
                }
            } else {
                if (whichToPlay == WhichToPlay.PlayAll) {
                    for (int i = 0; i < soundEvents.Length; i++) {
                        SoundEventBase otherSoundEvent = soundEvents[i];
                        if (otherSoundEvent == null) {
                            if (ShouldDebug.Warnings()) {
                                Debug.LogWarning($"Sonity: \"{soundEvent.internals.cachedName}\": The {triggerType.ToString()} has null {NameOf.SoundEvent}s.", soundEvent);
                            }
                        } else {
                            if (triggerType == SoundEventTriggerOnType.TriggerOnStop) {
                                SoundManagerBase.Instance.Internals.InternalPlayBuffered(
                                    otherSoundEvent,
                                    playValuesLast.playType,
                                    playValuesLast.instanceIDTransform,
                                    playValuesLast.positionVector3,
                                    playValuesLast.positionTransform,
                                    playValuesLast.soundEventModifierTrigger,
                                    null,
                                    this.soundEvent.internals.data.passParameters ? playValuesLast.soundParameters : null,
                                    playValuesLast.soundParameterDistanceScale,
                                    playValuesLast.localSoundTag
                                );
                            } else {
                                SoundManagerBase.Instance.Internals.InternalPlay(
                                    otherSoundEvent,
                                    playValuesLast.playType,
                                    playValuesLast.instanceIDTransform,
                                    playValuesLast.positionVector3,
                                    playValuesLast.positionTransform,
                                    playValuesLast.soundEventModifierTrigger,
                                    null,
                                    this.soundEvent.internals.data.passParameters ? playValuesLast.soundParameters : null,
                                    playValuesLast.soundParameterDistanceScale,
                                    playValuesLast.localSoundTag
                                );
                            }
                        }
                    }
                } else if (whichToPlay == WhichToPlay.PlayOneRandom) {
                    // Pseudo random function remembering which clips it last played to avoid repetition
                    int randomIndex = 0;
                    if (triggerType == SoundEventTriggerOnType.TriggerOnPlay) {
                        randomIndex = soundEvent.internals.data.GetTriggerOnPlayRandomSoundEvent();
                    } else if (triggerType == SoundEventTriggerOnType.TriggerOnStop) {
                        randomIndex = soundEvent.internals.data.GetTriggerOnStopRandomSoundEvent();
                    } else if (triggerType == SoundEventTriggerOnType.TriggerOnTail) {
                        randomIndex = soundEvent.internals.data.GetTriggerOnTailRandomSoundEvent();
                    }
                    SoundEventBase randomSoundEvent = soundEvents[randomIndex];
                    if (randomSoundEvent == null) {
                        if (ShouldDebug.Warnings()) {
                            Debug.LogWarning($"Sonity: \"{soundEvent.internals.cachedName}\": The {triggerType.ToString()} has null {NameOf.SoundEvent}s.", soundEvent);
                        }
                    } else {
                        if (triggerType == SoundEventTriggerOnType.TriggerOnStop) {
                            SoundManagerBase.Instance.Internals.InternalPlayBuffered(
                                randomSoundEvent,
                                playValuesLast.playType,
                                playValuesLast.instanceIDTransform,
                                playValuesLast.positionVector3,
                                playValuesLast.positionTransform,
                                playValuesLast.soundEventModifierTrigger,
                                null,
                                this.soundEvent.internals.data.passParameters ? playValuesLast.soundParameters : null,
                                playValuesLast.soundParameterDistanceScale,
                                playValuesLast.localSoundTag
                            );
                        } else {
                            SoundManagerBase.Instance.Internals.InternalPlay(
                                randomSoundEvent,
                                playValuesLast.playType,
                                playValuesLast.instanceIDTransform,
                                playValuesLast.positionVector3,
                                playValuesLast.positionTransform,
                                playValuesLast.soundEventModifierTrigger,
                                null,
                                this.soundEvent.internals.data.passParameters ? playValuesLast.soundParameters : null,
                                playValuesLast.soundParameterDistanceScale,
                                playValuesLast.localSoundTag
                            );
                        }
                    }
                }
            }
        }

        private bool isPausedLocal = false;
        private bool isPausedGlobal = false;

        public void PauseUnpauseNormal(bool pause, bool forcePause = false) {
            if (pause && (soundEvent.internals.data.ignoreLocalPause && !forcePause)) {
                return;
            }
            if (!isPausedGlobal && (isPausedLocal != pause || forcePause)) {
                isPausedLocal = pause;
#if UNITY_EDITOR
                if (SoundManagerBase.Instance.Internals.debug.LogSoundEventsPauseUnpauseEnabled(soundEvent)) {
                    SoundManagerBase.Instance.Internals.debug.LogSoundEventsPauseUnpause(soundEvent, pause);
                }
#endif
                for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                    for (int v = 0; v < container.voiceHolder.Length; v++) {
                        Voice voice = container.voiceHolder[v].voice;
                        if (voice != null) {
                            if (voice.cachedAudioSource != null) {
                                if (pause) {
                                    voice.cachedAudioSource.Pause();
                                } else {
                                    voice.cachedAudioSource.UnPause();
                                }
                            }
                        }
                    }
                }
            }
        }

        public void PauseUnpauseGlobal(bool pause) {
            if (soundEvent.internals.data.ignoreGlobalPause) {
                return;
            }
#if UNITY_EDITOR
            if (SoundManagerBase.Instance.Internals.debug.LogSoundEventsGlobalPauseUnpauseEnabled(soundEvent)) {
                SoundManagerBase.Instance.Internals.debug.LogSoundEventsGlobalPauseUnpause(soundEvent, pause);
            }
#endif
            isPausedGlobal = pause;
        }

        public void ManagedUpdate() {

            if (waitingForPooling) {
                return;
            }

            if (soundEvent.internals.data.disableEnable) {
                return;
            }

            if (isPausedLocal || isPausedGlobal) {
                return;
            }

#if UNITY_EDITOR
            // Intensity Continuous Debug
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                for (int v = 0; v < container.voiceHolder.Length; v++) {
                    VoiceHolder voiceHolder = container.voiceHolder[v];
                    if (soundEvent.internals.data.GetIntensityRecord() && voiceHolder.voiceParameter.currentModifier.intensityUse && voiceHolder.voiceParameter.SoundParametersHasContinuousIntensity()) {
                        // Intensity Debug
                        if (soundEvent.internals.data.GetIntensityRecord()) {
                            soundEvent.internals.data.intensityDebugValueList.Add(voiceHolder.voiceParameter.currentModifier.intensity);
                        }
                    }
                }
            }
#endif
            TriggerOnTailUpdate(false);

            // Update Continuous Parameters
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                for (int v = 0; v < container.voiceHolder.Length; v++) {
                    VoiceHolder voiceHolder = container.voiceHolder[v];
                    if (voiceHolder.voiceParameter != null) {
                        voiceHolder.voiceParameter.SoundParameterUpdateContinuous();
#if UNITY_EDITOR
                        if (SoundManagerBase.Instance.Internals.debug.LogSoundEventsSoundParametersContinuousEnabled(soundEvent)) {
                            SoundManagerBase.Instance.Internals.debug.LogSoundEventsSoundParameters(soundEvent, voiceHolder.voiceParameter.soundParameters);
                        }
#endif
                    }
                }
            }

            // Updates Distances
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                for (int v = 0; v < container.voiceHolder.Length; v++) {
                    VoiceHolder voiceHolder = container.voiceHolder[v];
                    if (voiceHolder.voice != null) {
                        // This needs to happen before positions are updated
                        voiceHolder.playTypeInstance.SetCachedDistancesAndAngle(voiceHolder.maxRange, voiceHolder.voiceParameter, false);
                    }
                }
            }

            // Update Positions
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                for (int v = 0; v < container.voiceHolder.Length; v++) {
                    VoiceHolder voiceHolder = container.voiceHolder[v];
                    // Stop if stopIfTransformIsNull
                    // Fade check it doesn't retrigger all the time
                    if (container.soundContainer.internals.data.stopIfTransformIsNull && voiceHolder.voiceFade.state != VoiceFadeState.FadePool && voiceHolder.voiceFade.state != VoiceFadeState.FadeOut) {
                        if (voiceHolder.playTypeInstance.instanceIDTransform == null) {
                            PoolSingleVoice(s, v, false, true, false, false);
                        } else if (voiceHolder.playTypeInstance.playType == SoundEventPlayType.PlayAtTransform && voiceHolder.playTypeInstance.positionTransform == null) {
                            PoolSingleVoice(s, v, false, true, false, false);
                        }
                    }
                    // Update Positions
                    if (voiceHolder.voice != null) {
                        // Force 2D disables position update
                        if (!(voiceHolder.voiceParameter.currentModifier.force2DUse && voiceHolder.voiceParameter.currentModifier.force2D)) {
                            if ((!voiceHolder.voiceParameter.currentModifier.followPositionUse && container.soundContainer.internals.data.followPosition)
                            || (voiceHolder.voiceParameter.currentModifier.followPositionUse && voiceHolder.voiceParameter.currentModifier.followPosition)) {
                                // PlayType.playAtVector doesnt need to follow position
                                if (voiceHolder.playTypeInstance.playType != SoundEventPlayType.PlayAtVector) {
                                    if (container.soundContainer.internals.data.lockAxisEnable) {
                                        voiceHolder.voice.SetPostion(AxisLock.Lock(voiceHolder.playTypeInstance.GetPosition(), container.soundContainer.internals.data.lockAxis, container.soundContainer.internals.data.lockAxisPosition));
                                    } else {
                                        voiceHolder.voice.SetPostion(voiceHolder.playTypeInstance.GetPosition());
                                    }
                                    // Set Rotation
                                    voiceHolder.voice.SetRotation(voiceHolder.playTypeInstance.GetRotation());
                                }
                            }
                        }
                    }
                }
            }

            // Checks if the SoundContainer should play delayed
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                for (int n = 0; n < container.nextVoices.Count; n++) {
                    NextVoice nextVoice = container.nextVoices[n];
                    if (nextVoice.assinged) {
                        nextVoice.playTypeInstance.SetCachedDistancesAndAngle(nextVoice.maxRange, nextVoice.voiceParameter, false);
                        if (nextVoice.startTimeAndDelay + nextVoice.playTypeInstance.GetSpeedOfSoundDistance() <= SoundTimeScale.GetTimeRuntime()) {
                            if (container.NextVoiceShouldPlay(n)) {
                                container.voiceHolder[container.nextVoiceIndex].voiceIsToPlay = true;
                                container.VoicePrepare(s, container.nextVoiceIndex, true, nextVoice.voiceParameter, nextVoice.playTypeInstance, false);
                            }
                            nextVoice.ResetAssigned();
                        } 
                    }
                }
            }

            // Restart looping SoundContainer if they were stopped by being out of range or too low volume or stolen
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                for (int v = 0; v < container.voiceHolder.Length; v++) {
                    VoiceHolder voiceHolder = container.voiceHolder[v];
                    if (container.soundContainer.internals.data.loopEnabled && voiceHolder.shouldRestartIfLoop && voiceHolder.voice == null && voiceHolder.voiceFade.state != VoiceFadeState.FadePool) {
                        voiceHolder.playTypeInstance.SetCachedDistancesAndAngle(voiceHolder.maxRange, voiceHolder.voiceParameter, false);
                        if (container.ShouldBePlaying(v, false)) {
                            voiceHolder.voiceIsToPlay = true;
                            container.VoicePrepare(s, v, false, null, null, true);
                        }
                    }
                }
            }

            // Play delayed and loop restart
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                for (int v = 0; v < container.voiceHolder.Length; v++) {
                    VoiceHolder voiceHolder = container.voiceHolder[v];
                    if (voiceHolder.voiceIsToPlay) {
                        voiceHolder.voiceIsToPlay = false;
                        if (container != null) {
                            container.VoicePlay(v, GetPolyphonyLimit(), lastStartTime);
                        }
                        // TriggerOnTail
                        if (soundEvent.internals.data.triggerOnTailEnable) {
                            // Only SC 0 should reset TriggerOnTail
                            if (s == 0) {
                                voiceHolder.triggerOnTailHasPlayed = false;
                                triggerOnTailClipFound = false;
                                triggerOnTailIsStopped = false;
                            }
                        }
                        lastPlayedSoundContainerIndex = s;
                    }
                }
            }

            // Reset
            voicesNotPlaying = 0;

            // Update curves or pool
            for (int s = 0; s < soundContainerInstance.Count; s++) {
                SoundContainerInstance container = soundContainerInstance[s];
                for (int v = 0; v < soundContainerInstance[s].voiceHolder.Length; v++) {
                    VoiceHolder voiceHolder = container.voiceHolder[v];
                    if (voiceHolder.voice == null) {
                        // Checks if there are no delayed SoundContainer to play
                        if (!container.GetNextVoiceListAnyAssigned()) {
                            // If loop shouldn't play again
                            if (!(container.soundContainer.internals.data.loopEnabled && voiceHolder.shouldRestartIfLoop)) {
                                voicesNotPlaying++;
                            }
                        }
                    } else {
                        if (voiceHolder.voice.GetVoiceIsPlaying()) {
                            if (container.ShouldBePlaying(v, true)) {
                                container.VoiceUpdate(v, false);
                            } else {
                                PoolSingleVoice(s, v, true, false,false, false);
                            }
                        } else {
                            // If its not waiting for delay
                            if (!container.GetNextVoiceListAnyAssigned()) {
                                PoolSingleVoice(s, v, true, false, false, false);
                            }
                        }
                    }
                }
            }

            // If no voices are playing
            if (voicesNotPlaying >= soundContainerInstance.Count * GetPolyphonyLimit()) {
                voicesNotPlaying = 0;

                if (!waitingForPooling) {
                    waitingForPooling = true;
#if UNITY_EDITOR
                    if (SoundManagerBase.Instance.Internals.debug.LogSoundEventsPoolEnabled(soundEvent)) {
                        SoundManagerBase.Instance.Internals.debug.LogSoundEventsPool(soundEvent);
                    }
#endif
                    PoolAllVoices(false, false, false);
                }
            }
        }
    }
}