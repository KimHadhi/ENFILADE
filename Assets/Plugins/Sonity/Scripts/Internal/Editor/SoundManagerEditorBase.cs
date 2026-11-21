// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Sonity.Internal {

    public abstract class SoundManagerEditorBase : Editor {

        public SoundManagerBase mTarget;
        public SoundManagerBase[] mTargets;

        public GUIStyle guiStyleBoldCenter = new GUIStyle();

        public GUIStyle[] statisticsStyles = new GUIStyle[5];
        public string[] statisticsStrings = new string[5];

        public Color defaultGuiStyleTextColor;
        public Color defaultGuiColor;

        [NonSerialized] public SerializedProperty internals;
        [NonSerialized] public SerializedProperty settings;
        [NonSerialized] public SerializedProperty editorTools;
        [NonSerialized] public SerializedProperty debug;
        [NonSerialized] public SerializedProperty statistics;

        [NonSerialized] public SerializedProperty debugExpand;
        [NonSerialized] public SerializedProperty forceDebug;
        [NonSerialized] public SerializedProperty drawSoundEventsInSceneViewEnabled;
        [NonSerialized] public SerializedProperty drawSoundEventsInGameViewEnabled;
        [NonSerialized] public SerializedProperty drawSoundEventsHideIfCloserThan;
        [NonSerialized] public SerializedProperty drawSoundEventsFontSize;
        [NonSerialized] public SerializedProperty drawSoundEventsVolumeToOpacity;
        [NonSerialized] public SerializedProperty drawSoundEventsLifetimeToOpacity;
        [NonSerialized] public SerializedProperty drawSoundEventsLifetimeFadeLength;
        [NonSerialized] public SerializedProperty drawSoundEventsColorStart;
        [NonSerialized] public SerializedProperty drawSoundEventsColorEnd;
        [NonSerialized] public SerializedProperty drawSoundEventsColorOutline;

        [NonSerialized] public SerializedProperty logSoundEventsEnabled;
        [NonSerialized] public SerializedProperty logSoundEventsLogType;
        [NonSerialized] public SerializedProperty logSoundEventsSelectObject;
        [NonSerialized] public SerializedProperty logSoundEventsPlay;
        [NonSerialized] public SerializedProperty logSoundEventsStop;
        [NonSerialized] public SerializedProperty logSoundEventsPool;
        [NonSerialized] public SerializedProperty logSoundEventsPause;
        [NonSerialized] public SerializedProperty logSoundEventsUnpause;
        [NonSerialized] public SerializedProperty logSoundEventsGlobalPause;
        [NonSerialized] public SerializedProperty logSoundEventsGlobalUnpause;
        [NonSerialized] public SerializedProperty logSoundEventsSoundParametersOnce;
        [NonSerialized] public SerializedProperty logSoundEventsSoundParametersContinuous;

        [NonSerialized] public SerializedProperty settingsExpandBase;
        [NonSerialized] public SerializedProperty settingsExpandAdvanced;
        [NonSerialized] public SerializedProperty dontDestoyOnLoad;
        [NonSerialized] public SerializedProperty debugWarnings;
        [NonSerialized] public SerializedProperty debugInPlayMode;
        [NonSerialized] public SerializedProperty guiWarnings;
        [NonSerialized] public SerializedProperty disablePlayingSounds;
        [NonSerialized] public SerializedProperty asyncStartup;
        [NonSerialized] public SerializedProperty soundManagerManualUpdate;
        [NonSerialized] public SerializedProperty playOneShotOptimization;

        [NonSerialized] public SerializedProperty steamAudioIntegrationEnable;
        [NonSerialized] public SerializedProperty steamAudioSpatializeEnabledDefault;

        [NonSerialized] public SerializedProperty playmakerIntegrationEnable;

        [NonSerialized] public SerializedProperty soundEventLibraryEnable;

        [NonSerialized] public SerializedProperty entitySoundBaseEnable;
        [NonSerialized] public SerializedProperty entitySoundLibraryEnable;

        [NonSerialized] public SerializedProperty soundTimeScale;
        [NonSerialized] public SerializedProperty globalPause;
        [NonSerialized] public SerializedProperty globalVolumeRatio;
        [NonSerialized] public SerializedProperty globalVolumeDecibel;
        [NonSerialized] public SerializedProperty volumeIncreaseEnable;
        [NonSerialized] public SerializedProperty globalSoundTag; // Legacy
        [NonSerialized] public SerializedProperty globalSoundTags;
        [NonSerialized] public SerializedProperty globalSoundTagsExpanded;
        [NonSerialized] public SerializedProperty distanceScale;
        [NonSerialized] public SerializedProperty overrideListenerDistance;
        [NonSerialized] public SerializedProperty overrideListenerDistanceAmount;
        [NonSerialized] public SerializedProperty speedOfSoundEnabled;
        [NonSerialized] public SerializedProperty speedOfSoundScale;

        [NonSerialized] public SerializedProperty addressableAudioMixerUse;
        [NonSerialized] public SerializedProperty addressableAudioMixerAsyncLoadInEditor;

#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
        [NonSerialized] public SerializedProperty addressableAudioMixerReference;
#endif

        [NonSerialized] public SerializedProperty voicePreload;
        [NonSerialized] public SerializedProperty voiceLimit;
        [NonSerialized] public SerializedProperty voiceEffectLimit;
        [NonSerialized] public SerializedProperty voiceEffectDisable;
        [NonSerialized] public SerializedProperty voiceStopTime;

        [NonSerialized] public SerializedProperty editorToolExpand;
        [NonSerialized] public SerializedProperty editorToolSelectionHistoryEnable;
        [NonSerialized] public SerializedProperty editorToolReferenceFinderEnable;
        [NonSerialized] public SerializedProperty editorToolSelectSameTypeEnable;

        [NonSerialized] public SerializedProperty statisticsExpandMain;
        [NonSerialized] public SerializedProperty statisticsExpandGeneral;
        [NonSerialized] public SerializedProperty statisticsExpandInstances;
        [NonSerialized] public SerializedProperty statisticsSorting;

        [NonSerialized] public SerializedProperty statisticsInfoActive;
        [NonSerialized] public SerializedProperty statisticsInfoVoices;
        [NonSerialized] public SerializedProperty statisticsInfoPlays;
        [NonSerialized] public SerializedProperty statisticsInfoVolume;

        [NonSerialized] public SerializedProperty statisticsFilterShowActive;
        [NonSerialized] public SerializedProperty statisticsFilterShowInactive;

        private void OnEnable() {
            for (int i = 0; i < statisticsStyles.Length; i++) {
                if (statisticsStyles[i] == null) {
                    statisticsStyles[i] = new GUIStyle();
                }
            }
            FindProperties();
        }

        private void FindProperties() {

            internals = serializedObject.FindProperty(nameof(SoundManagerBase.Internals).ToLowerInvariant());
            settings = internals.FindPropertyRelative(nameof(SoundManagerInternals.settings));
            statistics = internals.FindPropertyRelative(nameof(SoundManagerInternals.statistics));
            debug = internals.FindPropertyRelative(nameof(SoundManagerInternals.debug));
            editorTools = internals.FindPropertyRelative(nameof(SoundManagerInternals.editorTools));

            debugExpand = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.debugExpand));

            forceDebug = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.forceDebug));

            // Draw SoundEvents
            drawSoundEventsInSceneViewEnabled = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.drawSoundEventsInSceneViewEnabled));
            drawSoundEventsInGameViewEnabled = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.drawSoundEventsInGameViewEnabled));
            drawSoundEventsHideIfCloserThan = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.drawSoundEventsHideIfCloserThan));
            drawSoundEventsFontSize = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.drawSoundEventsFontSize));
            drawSoundEventsVolumeToOpacity = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.drawSoundEventsVolumeToOpacity));
            drawSoundEventsLifetimeToOpacity = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.drawSoundEventsLifetimeToOpacity));
            drawSoundEventsLifetimeFadeLength = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.drawSoundEventsLifetimeFadeLength));
            drawSoundEventsColorStart = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.drawSoundEventsColorStart));
            drawSoundEventsColorEnd = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.drawSoundEventsColorEnd));
            drawSoundEventsColorOutline = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.drawSoundEventsColorOutline));

            // Log SoundEvents
            logSoundEventsEnabled = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsEnabled));
            logSoundEventsLogType = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsLogType));
            logSoundEventsSelectObject = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsSelectObject));
            logSoundEventsPlay = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsPlay));
            logSoundEventsStop = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsStop));
            logSoundEventsPool = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsPool));
            logSoundEventsPause = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsPause));
            logSoundEventsUnpause = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsUnpause));
            logSoundEventsGlobalPause = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsGlobalPause));
            logSoundEventsGlobalUnpause = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsGlobalUnpause));
            logSoundEventsSoundParametersOnce = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsSoundParametersOnce));
            logSoundEventsSoundParametersContinuous = debug.FindPropertyRelative(nameof(SoundManagerInternalsDebug.logSoundEventsSoundParametersContinuous));

            settingsExpandBase = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.settingExpandBase));
            settingsExpandAdvanced = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.settingsExpandAdvanced));
            dontDestoyOnLoad = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.dontDestroyOnLoad));
            debugWarnings = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.debugWarnings));
            debugInPlayMode = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.debugInPlayMode));
            guiWarnings = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.guiWarnings));
            disablePlayingSounds = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.disablePlayingSounds));
            asyncStartup = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.asyncStartup));
            soundManagerManualUpdate = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.soundManagerManualUpdate));
            playOneShotOptimization = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.playOneShotOptimization));

            steamAudioIntegrationEnable = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.steamAudioIntegrationEnable));
            steamAudioSpatializeEnabledDefault = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.steamAudioSpatializeEnabledDefault));
            playmakerIntegrationEnable = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.playmakerIntegrationEnable));
            soundEventLibraryEnable = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.soundEventLibraryEnable));
            entitySoundBaseEnable = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.entitySoundBaseEnable));
            entitySoundLibraryEnable = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.entitySoundLibraryEnable));
            soundTimeScale = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.soundTimeScale));
            globalPause = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.globalPause));
            globalVolumeRatio = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.globalVolumeRatio));
            globalVolumeDecibel = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.globalVolumeDecibel));
            volumeIncreaseEnable = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.volumeIncreaseEnable));
            globalSoundTag = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.globalSoundTag)); // Legacy
            globalSoundTags = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.globalSoundTags));
            globalSoundTagsExpanded = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.globalSoundTagsExpanded));
            distanceScale = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.distanceScale));
            overrideListenerDistance = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.overrideListenerDistance));
            overrideListenerDistanceAmount = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.overrideListenerDistanceAmount));
            speedOfSoundEnabled = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.speedOfSoundEnabled));
            speedOfSoundScale = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.speedOfSoundScale));

            addressableAudioMixerUse = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.addressableAudioMixerUse));
            addressableAudioMixerAsyncLoadInEditor = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.addressableAudioMixerAsyncLoadInEditor));

#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
            addressableAudioMixerReference = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.addressableAudioMixerReference));
#endif

            voicePreload = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.voicePreload));
            voiceLimit = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.voiceLimit));
            voiceEffectLimit = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.voiceEffectLimit));
            voiceEffectDisable = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.voiceEffectDisable));
            voiceStopTime = settings.FindPropertyRelative(nameof(SoundManagerInternalsSettings.voiceStopTime));

            editorToolExpand = editorTools.FindPropertyRelative(nameof(SoundManagerInternalsEditorTools.editorToolExpand));
            editorToolSelectionHistoryEnable = editorTools.FindPropertyRelative(nameof(SoundManagerInternalsEditorTools.editorToolSelectionHistoryEnable));
            editorToolReferenceFinderEnable = editorTools.FindPropertyRelative(nameof(SoundManagerInternalsEditorTools.editorToolReferenceFinderEnable));
            editorToolSelectSameTypeEnable = editorTools.FindPropertyRelative(nameof(SoundManagerInternalsEditorTools.editorToolSelectSameTypeEnable));

            statisticsExpandMain = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.statisticsExpandMain));
            statisticsExpandGeneral = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.statisticsExpandGeneral));
            statisticsExpandInstances = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.statisticsExpandInstances));
            statisticsSorting = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.statisticsSorting));

            statisticsInfoActive = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.statisticsInfoActive));
            statisticsInfoVoices = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.statisticsInfoVoices));
            statisticsInfoPlays = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.statisticsInfoPlays));
            statisticsInfoVolume = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.statisticsInfoVolume));

            statisticsFilterShowActive = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.statisticsFilterShowActive));
            statisticsFilterShowInactive = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.statisticsFilterShowInactive));
        }

        private void BeginChange() {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
        }

        private void EndChange() {
            if (EditorGUI.EndChangeCheck()) {
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void StartBackgroundColor(Color color) {
            GUI.color = color;
            EditorGUILayout.BeginVertical("Button");
            GUI.color = defaultGuiColor;
        }

        private void StopBackgroundColor() {
            EditorGUILayout.EndVertical();
        }

        public FontStyle fontStylePrevious;

        public void FontStyleOverrideStart(FontStyle fontStyle) {
            fontStylePrevious = EditorStyles.label.fontStyle;
            EditorStyles.label.fontStyle = fontStyle;
        }

        public void FontStyleOverrideStop() {
            EditorStyles.label.fontStyle = fontStylePrevious;
        }


        public override void OnInspectorGUI() {

            mTarget = (SoundManagerBase)target;

            mTargets = new SoundManagerBase[targets.Length];
            for (int i = 0; i < targets.Length; i++) {
                mTargets[i] = (SoundManagerBase)targets[i];
            }

            defaultGuiColor = GUI.color;
            if (EditorGUIUtility.isProSkin) {
                defaultGuiStyleTextColor = EditorColorProSkin.GetDarkSkinTextColor();
            } else {
                defaultGuiStyleTextColor = new GUIStyle().normal.textColor;
            }

            guiStyleBoldCenter.fontStyle = FontStyle.Bold;
            guiStyleBoldCenter.alignment = TextAnchor.MiddleCenter;
            if (EditorGUIUtility.isProSkin) {
                guiStyleBoldCenter.normal.textColor = EditorColorProSkin.GetDarkSkinTextColor();
            }

            EditorGUI.indentLevel = 0;

            EditorGuiFunction.DrawObjectNameBox((UnityEngine.Object)mTarget, NameOf.SoundManager, EditorTextSoundManager.soundManagerTooltip, false);
            EditorGuiAssetTopToolbarButtons.DrawTopButton(serializedObject);
            EditorTrial.InfoText();
            EditorGUILayout.Separator();

            if (SonityEditorPrefs.SonityLegacyMissingSonityGuids) {
                EditorGUILayout.Separator();
                EditorGUILayout.HelpBox(EditorTextSoundManager.missingSonityGuidsWarning, MessageType.Warning);
                EditorGUILayout.Separator();
                // Gui Style Bold Center Red
                GUIStyle guiStyleBoldCenterRed = new GUIStyle();
                guiStyleBoldCenterRed.fontSize = 16;
                guiStyleBoldCenterRed.fontStyle = FontStyle.Bold;
                guiStyleBoldCenterRed.alignment = TextAnchor.MiddleCenter;
                StartBackgroundColor(Color.red);
                BeginChange();
                if (GUILayout.Button(new GUIContent(EditorTextSoundManager.missingSonityGuidsReserializeAllAssetsButtonLabel, EditorTextSoundManager.missingSonityGuidsReserializeAllAssetsButtonTooltip), guiStyleBoldCenterRed)) {
                    SonityEditorPrefs.SonityLegacyMissingSonityGuids = false;
                    EditorToolsBase.ReserializeAllSonityAssets();
                }
                EndChange();
                StopBackgroundColor();
                EditorGUILayout.Separator();
            }

            DrawSettings();
            EditorGUILayout.Separator();

            DrawDebug();
            EditorGUILayout.Separator();

            DrawStatistics();
            EditorGUILayout.Separator();

            DrawEditorTools();

            // Transparent background so the offset will be right
            StartBackgroundColor(new Color(0f, 0f, 0f, 0f));
            EditorGUILayout.BeginHorizontal();
            // For offsetting the buttons to the right
            EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
            BeginChange();
            if (GUILayout.Button(new GUIContent(EditorTextSoundManager.resetAllLabel, EditorTextSoundManager.resetAllTooltip))) {
                for (int i = 0; i < mTargets.Length; i++) {
                    Undo.RecordObject(mTargets[i], "Reset All");
                    mTargets[i].Internals.settings = new SoundManagerInternalsSettings();
                    mTargets[i].Internals.editorTools = new SoundManagerInternalsEditorTools();
                    mTargets[i].Internals.debug = new SoundManagerInternalsDebug();
                    mTargets[i].Internals.statistics = new SoundManagerInternalsStatistics();
                    EditorUtility.SetDirty(mTargets[i]);
                }
            }
            EndChange();
            EditorGUILayout.EndHorizontal();
            StopBackgroundColor();
        }

        // Legacy, copies the old single SoundTag to the array
        private void LegacyAssignSoundTagToArray() {
            for (int i = 0; i < targets.Length; i++) {
                SoundManagerBase mTarget = (SoundManagerBase)targets[i];
                if (mTarget.Internals.settings.globalSoundTag != null
                    && mTarget.Internals.settings.globalSoundTags.Count > 0
                    && mTarget.Internals.settings.globalSoundTags[0] == null
                    ) {
                    // Set new index 0
                    mTarget.Internals.settings.globalSoundTags[0] = mTarget.Internals.settings.globalSoundTag;
                    // Set to null so it doesn't trigger again
                    mTarget.Internals.settings.globalSoundTag = null;
                    Undo.RecordObject(mTarget, "Legacy automatic convert SoundTag to array");
                    EditorUtility.SetDirty(mTarget);
                    Debug.LogWarning($"Sonity.{NameOf.SoundManager}: Converted Legacy Global SoundTag to Array - {mTarget.gameObject.name}");
                }
            }
        }

        private void DrawSettings() {
            StartBackgroundColor(EditorColor.GetSettings(EditorColorProSkin.GetCustomEditorBackgroundAlpha()));

            EditorGUI.indentLevel = 1;
            BeginChange();
            EditorGuiFunction.DrawFoldout(settingsExpandBase, "Settings");
            EndChange();

            if (settingsExpandBase.boolValue) {

                // Free Trial Button
                if (EditorTrial.isTrial) {
                    // Enable Sound in Builds is disabled
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.Toggle(new GUIContent(EditorTextSoundManager.enableSoundInBuildsLabel, EditorTextSoundManager.enableSoundInBuildsTooltip), false);
                    EditorGUI.EndDisabledGroup();

                    // Warning
                    EditorGUILayout.Separator();
                    EditorGUILayout.HelpBox(EditorTextSoundManager.enableSoundInBuildsWarning, MessageType.Warning);
                    EditorGUILayout.Separator();
                }

                //// SoundEvent Library
                //BeginChange();
                //EditorGUILayout.PropertyField(soundEventLibraryEnable, new GUIContent("SoundEvent Library", ""));
                //EndChange();

                //if (soundEventLibraryEnable.boolValue) {
                //    if (!EditorScriptingDefineSymbols.SoundEventLibraryExists()) {
                //        EditorGUILayout.BeginHorizontal();
                //        // For offsetting the buttons to the right
                //        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                //        BeginChange();
                //        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.soundEventLibraryScriptingDefineSymbolAddLabel, EditorTextSoundManager.soundEventLibraryScriptingDefineSymbolAddTooltip))) {
                //            EditorScriptingDefineSymbols.SoundEventLibraryShouldExist(true);
                //        }
                //        EndChange();
                //        EditorGUILayout.EndHorizontal();
                //    }
                //} else {
                //    if (EditorScriptingDefineSymbols.SoundEventLibraryExists()) {
                //        EditorGUILayout.BeginHorizontal();
                //        // For offsetting the buttons to the right
                //        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                //        BeginChange();
                //        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.soundEventLibraryScriptingDefineSymbolRemoveLabel, EditorTextSoundManager.soundEventLibraryScriptingDefineSymbolRemoveTooltip))) {
                //            EditorScriptingDefineSymbols.SoundEventLibraryShouldExist(false);
                //        }
                //        EndChange();
                //        EditorGUILayout.EndHorizontal();
                //    }
                //}

                if (EditorTrial.isTrial || EditorTrial.isEducational) {
                    // Increase Volume Disabled
                    EditorGUI.BeginDisabledGroup(true);
                    BeginChange();
                    EditorGUILayout.PropertyField(volumeIncreaseEnable, new GUIContent(EditorTextSoundManager.audioListenerVolumeIncreaseEnableLabel, EditorTextSoundManager.audioListenerVolumeIncreaseEnableTooltip));
                    EndChange();
                    EditorGUI.EndDisabledGroup();

                    // DLL doesnt support volume increase warning
                    EditorGUILayout.Separator();
                    if (EditorTrial.isTrial) {
                        EditorGUILayout.HelpBox(EditorTextSoundManager.audioListenerVolumeIncreaseFreeTrialWarningLabel, MessageType.Warning);
                    }
                    if (EditorTrial.isEducational) {
                        EditorGUILayout.HelpBox(EditorTextSoundManager.audioListenerVolumeIncreaseEducationalVersionWarningLabel, MessageType.Warning);
                    }
                    EditorGUILayout.Separator();
                } else {

                    // Increase Volume
                    BeginChange();
                    EditorGUILayout.PropertyField(volumeIncreaseEnable, new GUIContent(EditorTextSoundManager.audioListenerVolumeIncreaseEnableLabel, EditorTextSoundManager.audioListenerVolumeIncreaseEnableTooltip));
                    EndChange();

                    if (volumeIncreaseEnable.boolValue) {
                        if (!EditorScriptingDefineSymbols.AudioListenerVolumeIncreaseExists()) {
                            EditorGUILayout.BeginHorizontal();
                            // For offsetting the buttons to the right
                            EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                            BeginChange();
                            if (GUILayout.Button(new GUIContent(EditorTextSoundManager.audioListenerVolumeIncreaseScriptingDefineSymbolAddLabel, EditorTextSoundManager.audioListenerVolumeIncreaseScriptingDefineSymbolAddTooltip))) {
                                EditorScriptingDefineSymbols.AudioListenerVolumeIncreaseAddRemove(true);
                            }
                            EndChange();
                            EditorGUILayout.EndHorizontal();
                        }
                    } else {
                        if (EditorScriptingDefineSymbols.AudioListenerVolumeIncreaseExists()) {
                            EditorGUILayout.BeginHorizontal();
                            // For offsetting the buttons to the right
                            EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                            BeginChange();
                            if (GUILayout.Button(new GUIContent(EditorTextSoundManager.audioListenerVolumeIncreaseScriptingDefineSymbolRemoveLabel, EditorTextSoundManager.audioListenerVolumeIncreaseScriptingDefineSymbolRemoveTooltip))) {
                                EditorScriptingDefineSymbols.AudioListenerVolumeIncreaseAddRemove(false);
                            }
                            EndChange();
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                }

                // Global Volume
                BeginChange();
                EditorGUILayout.Slider(globalVolumeDecibel, VolumeScale.lowestVolumeDecibel, 0f, new GUIContent(EditorTextSoundManager.globalVolumeLabel, EditorTextSoundManager.globalVolumeTooltip));
                if (globalVolumeDecibel.floatValue <= VolumeScale.lowestVolumeDecibel) {
                    globalVolumeDecibel.floatValue = Mathf.NegativeInfinity;
                }
                if (globalVolumeRatio.floatValue != VolumeScale.ConvertDecibelToRatio(globalVolumeDecibel.floatValue)) {
                    globalVolumeRatio.floatValue = VolumeScale.ConvertDecibelToRatio(globalVolumeDecibel.floatValue);
                }
                EndChange();

                // Set AudioListener.volume after global volume and increase volume is set
                if (Application.isPlaying) {
#if SONITY_ENABLE_VOLUME_INCREASE
                    if (AudioListener.volume != globalVolumeRatio.floatValue * VolumeScale.volumeIncrease60dbAudioListenerRatioMax) {
                        AudioListener.volume = globalVolumeRatio.floatValue * VolumeScale.volumeIncrease60dbAudioListenerRatioMax;
                    }
#else
                    if (AudioListener.volume != globalVolumeRatio.floatValue) {
                        AudioListener.volume = globalVolumeRatio.floatValue;
                    }
#endif
                }

                // Global Pause
                BeginChange();
                bool tempGlobalPauseValue = EditorGUILayout.Toggle(new GUIContent(EditorTextSoundManager.globalPauseLabel, EditorTextSoundManager.globalPauseTooltip), globalPause.boolValue);
                if (globalPause.boolValue != tempGlobalPauseValue) {
                    globalPause.boolValue = tempGlobalPauseValue;
                    mTarget.Internals.InternalSetGlobalPauseUnpause(tempGlobalPauseValue);
                }
                EndChange();

                // Distance Scale
                BeginChange();
                float distanceScaleValue = distanceScale.floatValue;
                distanceScaleValue = EditorGUILayout.FloatField(new GUIContent(EditorTextSoundManager.distanceScaleLabel, EditorTextSoundManager.distanceScaleTooltip), distanceScaleValue);
                if (distanceScaleValue > 0f) {
                    distanceScale.floatValue = distanceScaleValue;
                }
                EndChange();

                // Legacy, copies the old single SoundTag to the array
                LegacyAssignSoundTagToArray();

                // SoundTags
                int lowestArrayLength = int.MaxValue;
                for (int n = 0; n < mTargets.Length; n++) {
                    if (lowestArrayLength > mTargets[n].Internals.settings.globalSoundTags.Count) {
                        lowestArrayLength = mTargets[n].Internals.settings.globalSoundTags.Count;
                    }
                }
                EditorGuiFunction.DrawReordableArray(
                    globalSoundTags, serializedObject, lowestArrayLength, false, true, false,
                    EditorGuiFunction.EditorGuiTypeIs.Property, 0, null, false, "", true, true,
                    globalSoundTagsExpanded, EditorTextSoundManager.globalSoundTagsLabel, EditorTextSoundManager.globalSoundTagsTooltip
                    );

                // Integrations
                EditorGUILayout.Separator();
                EditorGUILayout.LabelField(new GUIContent(EditorTextSoundManager.integrationsLabel, EditorTextSoundManager.integrationsTooltip), EditorStyles.boldLabel);

                // SteamAudio
                BeginChange();
                EditorGUILayout.PropertyField(steamAudioIntegrationEnable, new GUIContent(EditorTextSoundManager.steamAudioLabel, EditorTextSoundManager.steamAudioTooltip));
                EndChange();

                if (steamAudioIntegrationEnable.boolValue) {
                    if (!EditorScriptingDefineSymbols.IntegrationSteamAudioExists()) {
                        EditorGUILayout.BeginHorizontal();
                        // For offsetting the buttons to the right
                        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                        BeginChange();
                        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.steamAudioScriptingDefineSymbolAddLabel, EditorTextSoundManager.steamAudioScriptingDefineSymbolAddTooltip))) {
                            EditorScriptingDefineSymbols.IntegrationSteamAudioShouldExist(true);
                        }
                        EndChange();
                        EditorGUILayout.EndHorizontal();
                    }
                } else {
                    if (EditorScriptingDefineSymbols.IntegrationSteamAudioExists()) {
                        EditorGUILayout.BeginHorizontal();
                        // For offsetting the buttons to the right
                        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                        BeginChange();
                        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.steamAudioScriptingDefineSymbolRemoveLabel, EditorTextSoundManager.steamAudioScriptingDefineSymbolRemoveTooltip))) {
                            EditorScriptingDefineSymbols.IntegrationSteamAudioShouldExist(false);
                        }
                        EndChange();
                        EditorGUILayout.EndHorizontal();
                    }
                }

#if SONITY_ENABLE_INTEGRATION_STEAM_AUDIO
                if (voicePreload.intValue > 0 && !mTarget.Internals.settings.asyncStartup) {
                    EditorGUILayout.Separator();
                    EditorGUILayout.HelpBox(EditorTextSoundManager.steamAudioPreloadVoicesAsyncLoadWarning, MessageType.Warning);
                    EditorGUILayout.Separator();
                }
#endif

                if (steamAudioIntegrationEnable.boolValue) {
                    EditorGUI.indentLevel++;
                    BeginChange();
                    EditorGUILayout.PropertyField(steamAudioSpatializeEnabledDefault, new GUIContent(EditorTextSoundManager.steamAudioSpatializeDefaultOnLabel, EditorTextSoundManager.steamAudioSpatializeDefaultOnTooltip));
                    EndChange();
                    EditorGUI.indentLevel--;
                }

                // Playmaker
                BeginChange();
                EditorGUILayout.PropertyField(playmakerIntegrationEnable, new GUIContent(EditorTextSoundManager.playMakerLabel, EditorTextSoundManager.playMakerTooltip));
                EndChange();

                if (playmakerIntegrationEnable.boolValue) {
                    if (!EditorScriptingDefineSymbols.IntegrationPlayMakerExists()) {
                        EditorGUILayout.BeginHorizontal();
                        // For offsetting the buttons to the right
                        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                        BeginChange();
                        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.playMakerScriptingDefineSymbolAddLabel, EditorTextSoundManager.playMakerScriptingDefineSymbolAddTooltip))) {
                            EditorScriptingDefineSymbols.IntegrationPlayMakerShouldExist(true);
                        }
                        EndChange();
                        EditorGUILayout.EndHorizontal();
                    }
                } else {
                    if (EditorScriptingDefineSymbols.IntegrationPlayMakerExists()) {
                        EditorGUILayout.BeginHorizontal();
                        // For offsetting the buttons to the right
                        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                        BeginChange();
                        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.playMakerScriptingDefineSymbolRemoveLabel, EditorTextSoundManager.playMakerScriptingDefineSymbolRemoveTooltip))) {
                            EditorScriptingDefineSymbols.IntegrationPlayMakerShouldExist(false);
                        }
                        EndChange();
                        EditorGUILayout.EndHorizontal();
                    }
                }

                //// EntitySound Base
                //BeginChange();
                //EditorGUILayout.PropertyField(entitySoundBaseEnable, new GUIContent("EntitySound Base", ""));
                //EndChange();

                //if (entitySoundBaseEnable.boolValue) {
                //    if (!EditorScriptingDefineSymbols.EntitySoundBaseExists()) {
                //        EditorGUILayout.BeginHorizontal();
                //        // For offsetting the buttons to the right
                //        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                //        BeginChange();
                //        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.entitySoundBaseScriptingDefineSymbolAddLabel, EditorTextSoundManager.entitySoundBaseScriptingDefineSymbolAddTooltip))) {
                //            EditorScriptingDefineSymbols.EntitySoundBaseShouldExist(true);
                //        }
                //        EndChange();
                //        EditorGUILayout.EndHorizontal();
                //    }
                //} else {
                //    if (EditorScriptingDefineSymbols.EntitySoundBaseExists()) {
                //        EditorGUILayout.BeginHorizontal();
                //        // For offsetting the buttons to the right
                //        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                //        BeginChange();
                //        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.entitySoundBaseScriptingDefineSymbolRemoveLabel, EditorTextSoundManager.entitySoundBaseScriptingDefineSymbolRemoveTooltip))) {
                //            EditorScriptingDefineSymbols.EntitySoundBaseShouldExist(false);
                //            // Remove EntitySoundLibrary also
                //            EditorScriptingDefineSymbols.EntitySoundLibraryShouldExist(false);
                //        }
                //        EndChange();
                //        EditorGUILayout.EndHorizontal();
                //    }
                //}

                //if (entitySoundBaseEnable.boolValue) {

                //    // EntitySound Library
                //    EditorGUI.indentLevel++;
                //    BeginChange();
                //    EditorGUILayout.PropertyField(entitySoundLibraryEnable, new GUIContent("EntitySound Library", ""));
                //    EndChange();
                //    EditorGUI.indentLevel--;

                //    // Library requires base
                //    if (entitySoundBaseEnable.boolValue && entitySoundLibraryEnable.boolValue) {
                //        if (!EditorScriptingDefineSymbols.EntitySoundLibraryExists()) {
                //            EditorGUILayout.BeginHorizontal();
                //            // For offsetting the buttons to the right
                //            EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                //            BeginChange();
                //            if (GUILayout.Button(new GUIContent(EditorTextSoundManager.entitySoundLibraryScriptingDefineSymbolAddLabel, EditorTextSoundManager.entitySoundLibraryScriptingDefineSymbolAddTooltip))) {
                //                EditorScriptingDefineSymbols.EntitySoundLibraryShouldExist(true);
                //                // Add EntitySound Base also
                //                EditorScriptingDefineSymbols.EntitySoundBaseShouldExist(true);
                //            }
                //            EndChange();
                //            EditorGUILayout.EndHorizontal();
                //        }
                //    } else {
                //        if (EditorScriptingDefineSymbols.EntitySoundLibraryExists()) {
                //            EditorGUILayout.BeginHorizontal();
                //            // For offsetting the buttons to the right
                //            EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                //            BeginChange();
                //            if (GUILayout.Button(new GUIContent(EditorTextSoundManager.entitySoundLibraryScriptingDefineSymbolRemoveLabel, EditorTextSoundManager.entitySoundLibraryScriptingDefineSymbolRemoveTooltip))) {
                //                EditorScriptingDefineSymbols.EntitySoundLibraryShouldExist(false);
                //            }
                //            EndChange();
                //            EditorGUILayout.EndHorizontal();
                //        }
                //    }
                //}

                EditorGUILayout.Separator();

                // Advanced Settings
                BeginChange();
                EditorGuiFunction.DrawFoldout(settingsExpandAdvanced, "Advanced", "", 0, false, false, true);
                EndChange();

                // Advanced Settings
                if (settingsExpandAdvanced.boolValue) {

                    // DontDestroyOnLoad
                    BeginChange();
                    EditorGUILayout.PropertyField(dontDestoyOnLoad, new GUIContent(EditorTextSoundManager.dontDestoyOnLoadLabel, EditorTextSoundManager.dontDestoyOnLoadTooltip));
                    EndChange();

                    // Sound Time Scale
                    BeginChange();
                    EditorGUILayout.PropertyField(soundTimeScale, new GUIContent(EditorTextSoundManager.soundTimeScaleLabel, EditorTextSoundManager.soundTimeScaleTooltip));
                    EndChange();

                    // Audio Listener Distance Use
                    BeginChange();
                    EditorGUILayout.PropertyField(overrideListenerDistance, new GUIContent(EditorTextSoundManager.overrideListenerDistanceLabel, EditorTextSoundManager.overrideListenerDistanceTooltip));
                    EndChange();

                    if (overrideListenerDistance.boolValue) {
                        EditorGUI.indentLevel++;
                        BeginChange();
                        overrideListenerDistanceAmount.floatValue = EditorGUILayout.Slider(new GUIContent(EditorTextSoundManager.overrideListenerDistanceAmountLabel, EditorTextSoundManager.overrideListenerDistanceAmountTooltip), overrideListenerDistanceAmount.floatValue, 0f, 100f);
                        EndChange();
                        EditorGUI.indentLevel--;
                    }

                    // Speed of Sound
                    BeginChange();
                    EditorGUILayout.PropertyField(speedOfSoundEnabled, new GUIContent(EditorTextSoundManager.speedOfSoundEnabledLabel, EditorTextSoundManager.speedOfSoundEnabledTooltip));
                    EndChange();

                    if (speedOfSoundEnabled.boolValue) {
                        EditorGUI.indentLevel++;
                        BeginChange();
                        EditorGUILayout.PropertyField(speedOfSoundScale, new GUIContent(EditorTextSoundManager.speedOfSoundScaleLabel, EditorTextSoundManager.speedOfSoundScaleTooltip));
                        if (speedOfSoundScale.floatValue <= 0f) {
                            if (speedOfSoundScale.floatValue < 0f) {
                                speedOfSoundScale.floatValue = 0f;
                            }
                            if (ShouldDebug.GuiWarnings()) {
                                EditorGUILayout.Separator();
                                EditorGUILayout.HelpBox(EditorTextSoundManager.speedOfSoundScaleWarning, MessageType.Warning);
                                EditorGUILayout.Separator();
                            }
                        }
                        EndChange();
                        EditorGUI.indentLevel--;
                    }

                    // Addressable AudioMixer
                    if (EditorTrial.isTrial || EditorTrial.isEducational) {

                        // Trial and Education Version Editor /////////////////////////////////////////////////////////

                        // Addressable AudioMixer Disabled
                        EditorGUI.BeginDisabledGroup(true);
                        BeginChange();
                        EditorGUILayout.PropertyField(addressableAudioMixerUse, new GUIContent(EditorTextSoundManager.addressableAudioMixerUseLabel, EditorTextSoundManager.addressableAudioMixerUseTooltip));
                        EndChange();

#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
                        if (!mTarget.Internals.settings.asyncStartup) {
                            EditorGUILayout.Separator();
                            EditorGUILayout.HelpBox(EditorTextSoundManager.addressableAudioMixerAsyncLoadWarning, MessageType.Warning);
                            EditorGUILayout.Separator();
                        }
#endif
                        // Addressable AudioMixer Async Load In Editor
                        if (addressableAudioMixerUse.boolValue && asyncStartup.boolValue) {
                            EditorGUI.indentLevel++;
                            BeginChange();
                            EditorGUILayout.PropertyField(addressableAudioMixerAsyncLoadInEditor, new GUIContent(EditorTextSoundManager.addressableAudioMixerAsyncLoadInEditorLabel, EditorTextSoundManager.addressableAudioMixerAsyncLoadInEditorTooltip));
                            EndChange();
                            EditorGUI.indentLevel--;
                        }

                        EditorGUI.EndDisabledGroup();

                        // DLL doesnt support addressable AudioMixer warning
                        EditorGUILayout.Separator();
                        if (EditorTrial.isTrial) {
                            EditorGUILayout.HelpBox(EditorTextSoundManager.addressableAudioMixerFreeTrialWarningLabel, MessageType.Warning);
                        }
                        if (EditorTrial.isEducational) {
                            EditorGUILayout.HelpBox(EditorTextSoundManager.addressableAudioMixerEducationalVersionWarningLabel, MessageType.Warning);
                        }
                        EditorGUILayout.Separator();

                    } else {

                        // Full Version Editor /////////////////////////////////////////////////////////

                        // Addressable AudioMixer
                        BeginChange();
                        EditorGUILayout.PropertyField(addressableAudioMixerUse, new GUIContent(EditorTextSoundManager.addressableAudioMixerUseLabel, EditorTextSoundManager.addressableAudioMixerUseTooltip));
                        EndChange();

                        if (addressableAudioMixerUse.boolValue) {
                            if (!EditorScriptingDefineSymbols.AddressableAudioMixerExists()) {
                                EditorGUILayout.BeginHorizontal();
                                // For offsetting the buttons to the right
                                EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                                BeginChange();
                                if (GUILayout.Button(new GUIContent(EditorTextSoundManager.addressableAudioMixerScriptingDefineSymbolAddLabel, EditorTextSoundManager.addressableAudioMixerScriptingDefineSymbolAddTooltip))) {
                                    EditorScriptingDefineSymbols.AddressableAudioMixerAddRemove(true);
                                }
                                EndChange();
                                EditorGUILayout.EndHorizontal();
                            }
#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
                            EditorGUI.indentLevel++;
                            BeginChange();
                            EditorGUILayout.PropertyField(addressableAudioMixerReference, new GUIContent(EditorTextSoundManager.addressableAudioMixerReferenceLabel, EditorTextSoundManager.addressableAudioMixerReferenceTooltip));
                            EndChange();
                            EditorGUI.indentLevel--;
#endif
                        } else {
                            if (EditorScriptingDefineSymbols.AddressableAudioMixerExists()) {
                                EditorGUILayout.BeginHorizontal();
                                // For offsetting the buttons to the right
                                EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                                BeginChange();
                                if (GUILayout.Button(new GUIContent(EditorTextSoundManager.addressableAudioMixerScriptingDefineSymbolRemoveLabel, EditorTextSoundManager.addressableAudioMixerScriptingDefineSymbolRemoveTooltip))) {
                                    EditorScriptingDefineSymbols.AddressableAudioMixerAddRemove(false);
                                }
                                EndChange();
                                EditorGUILayout.EndHorizontal();
                            }
                        }
                    }

                    // Addressable AudioMixer Async Load In Editor
                    if (addressableAudioMixerUse.boolValue && asyncStartup.boolValue) {
                        EditorGUI.indentLevel++;
                        BeginChange();
                        EditorGUILayout.PropertyField(addressableAudioMixerAsyncLoadInEditor, new GUIContent(EditorTextSoundManager.addressableAudioMixerAsyncLoadInEditorLabel, EditorTextSoundManager.addressableAudioMixerAsyncLoadInEditorTooltip));
                        EndChange();
                        EditorGUI.indentLevel--;
                    }

                    // Full Version & Trail Educational Versions Editor //////////////////////////////////////////////////////////////

                    // Async Startup
                    BeginChange();
                    EditorGUILayout.PropertyField(asyncStartup, new GUIContent(EditorTextSoundManager.asyncStartupLabel, EditorTextSoundManager.asyncStartupTooltip));
                    EndChange();

                    // Debug Warnings
                    BeginChange();
                    EditorGUILayout.PropertyField(debugWarnings, new GUIContent(EditorTextSoundManager.debugWarningsLabel, EditorTextSoundManager.debugWarningsTooltip));
                    EndChange();
                    if (debugWarnings.boolValue) {
                        EditorGUI.indentLevel++;
                        BeginChange();
                        EditorGUILayout.PropertyField(debugInPlayMode, new GUIContent(EditorTextSoundManager.debugInPlayModeLabel, EditorTextSoundManager.debugInPlayModeTooltip));
                        EndChange();
                        EditorGUI.indentLevel--;
                    }

                    // Gui Warnings
                    BeginChange();
                    EditorGUILayout.PropertyField(guiWarnings, new GUIContent(EditorTextSoundManager.guiWarningsLabel, EditorTextSoundManager.guiWarningsTooltip));
                    EndChange();

                    // Disable Playing Sounds
                    BeginChange();
                    EditorGUILayout.PropertyField(disablePlayingSounds, new GUIContent(EditorTextSoundManager.disablePlayingSoundsLabel, EditorTextSoundManager.disablePlayingSoundsTooltip));
                    EndChange();

                    if (ShouldDebug.GuiWarnings()) {
                        if (disablePlayingSounds.boolValue) {
                            EditorGUILayout.Separator();
                            EditorGUILayout.HelpBox(EditorTextSoundManager.disablePlayingSoundsWarning, MessageType.Warning);
                            EditorGUILayout.Separator();
                        }
                    }

                    EditorGUILayout.Separator();

                    EditorGUI.indentLevel = 1;
                    EditorGUILayout.LabelField(new GUIContent("Performance"), EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;

                    // PlayOneShot Optimize
                    BeginChange();
                    EditorGUILayout.PropertyField(playOneShotOptimization, new GUIContent(EditorTextSoundManager.playOneShotOptimizeLabel, EditorTextSoundManager.playOneShotOptimizeTooltip));
                    EndChange();

                    // SoundManager Manual Update
                    BeginChange();
                    EditorGUILayout.PropertyField(soundManagerManualUpdate, new GUIContent(EditorTextSoundManager.soundManagerManualUpdateLabel, EditorTextSoundManager.soundManagerManualUpdateTooltip));
                    EndChange();

                    if (soundManagerManualUpdate.boolValue) {
                        if (!EditorScriptingDefineSymbols.SoundManagerManualUpdateExists()) {
                            EditorGUILayout.BeginHorizontal();
                            // For offsetting the buttons to the right
                            EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                            BeginChange();
                            if (GUILayout.Button(new GUIContent(EditorTextSoundManager.soundManagerManualUpdateScriptingDefineSymbolAddLabel, EditorTextSoundManager.soundManagerManualUpdateScriptingDefineSymbolAddTooltip))) {
                                EditorScriptingDefineSymbols.SoundManagerManualUpdateShouldExist(true);
                            }
                            EndChange();
                            EditorGUILayout.EndHorizontal();
                        }
                    } else {
                        if (EditorScriptingDefineSymbols.SoundManagerManualUpdateExists()) {
                            EditorGUILayout.BeginHorizontal();
                            // For offsetting the buttons to the right
                            EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                            BeginChange();
                            if (GUILayout.Button(new GUIContent(EditorTextSoundManager.soundManagerManualUpdateScriptingDefineSymbolRemoveLabel, EditorTextSoundManager.soundManagerManualUpdateScriptingDefineSymbolRemoveTooltip))) {
                                EditorScriptingDefineSymbols.SoundManagerManualUpdateShouldExist(false);
                            }
                            EndChange();
                            EditorGUILayout.EndHorizontal();
                        }
                    }

                    // Voice Limit
                    BeginChange();
                    EditorGUILayout.PropertyField(voiceLimit, new GUIContent(EditorTextSoundManager.voiceLimitLabel, EditorTextSoundManager.voiceLimitTooltip));
                    if (voiceLimit.intValue < 1) {
                        voiceLimit.intValue = 1;
                    }
                    // Sets the voice limit to the voice preload
                    if (voicePreload.intValue > voiceLimit.intValue) {
                        voicePreload.intValue = voiceLimit.intValue;
                    }
                    EndChange();

                    // Source Preload On Awake
                    BeginChange();
                    EditorGUILayout.PropertyField(voicePreload, new GUIContent(EditorTextSoundManager.voicePreloadLabel, EditorTextSoundManager.voicePreloadTooltip));
                    if (voicePreload.intValue < 0) {
                        voicePreload.intValue = 0;
                    }
                    // Sets the voice limit to the voice preload
                    if (voiceLimit.intValue < voicePreload.intValue) {
                        voiceLimit.intValue = voicePreload.intValue;
                    }
                    EndChange();

                    // Project Audio Settings Info
                    int oldIndentLevel = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;
                    EditorGUILayout.BeginHorizontal();
                    // For offsetting the buttons to the right
                    EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                    EditorGUILayout.LabelField(new GUIContent(
                        $"Project Audio Settings:\n" +
                        $"{AudioSettings.GetConfiguration().numRealVoices.ToString()} {EditorTextSoundManager.audioSettingsRealVoicesLabel}\n" +
                        $"{AudioSettings.GetConfiguration().numVirtualVoices.ToString()} {EditorTextSoundManager.audioSettingsVirtualVoicesLabel}",
                        EditorTextSoundManager.audioSettingsRealAndVirtualVoicesTooltip),
                        EditorStyles.helpBox);
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.indentLevel = oldIndentLevel;

                    // Apply voice limit to audio settings
                    EditorGUILayout.BeginHorizontal();
                    // For offsetting the buttons to the right
                    EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                    BeginChange();
                    if (GUILayout.Button(new GUIContent(EditorTextSoundManager.applyVoiceLimitToAudioSettingsLabel, EditorTextSoundManager.applyVoiceLimitToAudioSettingsTooltip))) {
                        // Load the AudioManager asset
                        UnityEngine.Object audioManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0];
                        SerializedObject serializedManager = new SerializedObject(audioManager);
                        // Set RealVoiceCount
                        SerializedProperty m_RealVoiceCount = serializedManager.FindProperty("m_RealVoiceCount");
                        m_RealVoiceCount.intValue = voiceLimit.intValue;
                        // Apply properties
                        serializedManager.ApplyModifiedProperties();
                    }
                    EndChange();
                    EditorGUILayout.EndHorizontal();

                    // If Real Voices are lower than Voice Limit
                    if (ShouldDebug.GuiWarnings()) {
                        if (AudioSettings.GetConfiguration().numRealVoices < voiceLimit.intValue) {
                            EditorGUILayout.Separator();
                            EditorGUILayout.HelpBox(EditorTextSoundManager.audioSettingsRealVoicesWarning, MessageType.Warning);
                            EditorGUILayout.Separator();
                        }

                        // If Virtual Voices are lower than Real Voices
                        if (AudioSettings.GetConfiguration().numVirtualVoices < AudioSettings.GetConfiguration().numRealVoices) {
                            EditorGUILayout.Separator();
                            EditorGUILayout.HelpBox(EditorTextSoundManager.audioSettingsVirtualVoicesWarning, MessageType.Warning);
                            EditorGUILayout.Separator();
                        }
                    }

                    // Voice Stop Time
                    BeginChange();
                    EditorGUILayout.PropertyField(voiceStopTime, new GUIContent(EditorTextSoundManager.voiceStopTimeLabel, EditorTextSoundManager.voiceStopTimeTooltip));
                    if (voiceStopTime.floatValue < 0f) {
                        voiceStopTime.floatValue = 0f;
                    }
                    EndChange();

                    BeginChange();
                    EditorGUILayout.PropertyField(voiceEffectLimit, new GUIContent(EditorTextSoundManager.voiceEffectLimitLabel, EditorTextSoundManager.voiceEffectLimitTooltip));
                    if (voiceEffectLimit.intValue < 0) {
                        voiceEffectLimit.intValue = 0;
                    }
                    EndChange();

                    BeginChange();
                    EditorGUILayout.PropertyField(voiceEffectDisable, new GUIContent(EditorTextSoundManager.voiceEffectDisableLabel, EditorTextSoundManager.voiceEffectDisableTooltip));
                    EndChange();

                    if (voiceEffectDisable.boolValue) {
                        if (!EditorScriptingDefineSymbols.DisableVoiceEffectsExists()) {
                            EditorGUILayout.BeginHorizontal();
                            // For offsetting the buttons to the right
                            EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                            BeginChange();
                            if (GUILayout.Button(new GUIContent(EditorTextSoundManager.voiceEffectDisableScriptDefineAddLabel, EditorTextSoundManager.voiceEffectDisableScriptDefineAddTooltip))) {
                                EditorScriptingDefineSymbols.DisableVoiceEffectsShouldExist(true);
                            }
                            EndChange();
                            EditorGUILayout.EndHorizontal();
                        }
                    } else {
                        if (EditorScriptingDefineSymbols.DisableVoiceEffectsExists()) {
                            // Show remove button if symbol exists, regardless if its on/off
                            if (EditorScriptingDefineSymbols.DisableVoiceEffectsExists()) {
                                EditorGUILayout.BeginHorizontal();
                                // For offsetting the buttons to the right
                                EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                                BeginChange();
                                if (GUILayout.Button(new GUIContent(EditorTextSoundManager.voiceEffectDisableScriptDefineRemoveLabel, EditorTextSoundManager.voiceEffectDisableScriptDefineRemoveTooltip))) {
                                    EditorScriptingDefineSymbols.DisableVoiceEffectsShouldExist(false);
                                }
                                EndChange();
                                EditorGUILayout.EndHorizontal();
                            }
                        }
                    }
                    EditorGUILayout.Separator();
                }

                EditorGUILayout.BeginHorizontal();
                // For offsetting the buttons to the right
                EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                BeginChange();
                if (GUILayout.Button(new GUIContent(EditorTextSoundManager.resetSettingsLabel, EditorTextSoundManager.resetSettingsTooltip))) {
                    for (int i = 0; i < mTargets.Length; i++) {
                        Undo.RecordObject(mTargets[i], "Reset Settings");
                        mTargets[i].Internals.settings = new SoundManagerInternalsSettings();
                        EditorUtility.SetDirty(mTargets[i]);
                    }
                }
                EndChange();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Separator();
            }
            StopBackgroundColor();
        }

        private void DrawEditorTools() {
            StartBackgroundColor(EditorColor.GetEditorTools(EditorColorProSkin.GetCustomEditorBackgroundAlpha()));

            EditorGUI.indentLevel = 1;
            BeginChange();
            EditorGuiFunction.DrawFoldout(editorToolExpand, EditorTextSoundManager.editorToolHeaderLabel, EditorTextSoundManager.editorToolHeaderTooltip);
            EndChange();

            if (editorToolExpand.boolValue) {

                // Editor Tools
                EditorGUI.indentLevel = 1;
                EditorGUILayout.LabelField(new GUIContent("Enable Tool"), EditorStyles.boldLabel);
                EditorGUI.indentLevel++;

                // Reference Finder
                BeginChange();
                EditorGUILayout.PropertyField(editorToolReferenceFinderEnable, new GUIContent(EditorTextSoundManager.editorToolReferenceFinderEnableLabel, EditorTextSoundManager.editorToolReferenceFinderEnableTooltip));
                EndChange();

                if (editorToolReferenceFinderEnable.boolValue) {
                    if (!EditorScriptingDefineSymbols.EditorToolReferenceFinderExists()) {
                        EditorGUILayout.BeginHorizontal();
                        // For offsetting the buttons to the right
                        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                        BeginChange();
                        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.editorToolReferenceFinderAddLabel, EditorTextSoundManager.editorToolReferenceFinderAddTooltip))) {
                            EditorScriptingDefineSymbols.EditorToolReferenceFinderShouldExist(true);
                        }
                        EndChange();
                        EditorGUILayout.EndHorizontal();
                    }
                } else {
                    if (EditorScriptingDefineSymbols.EditorToolReferenceFinderExists()) {
                        EditorGUILayout.BeginHorizontal();
                        // For offsetting the buttons to the right
                        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                        BeginChange();
                        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.editorToolReferenceFinderRemoveLabel, EditorTextSoundManager.editorToolReferenceFinderRemoveTooltip))) {
                            EditorScriptingDefineSymbols.EditorToolReferenceFinderShouldExist(false);
                        }
                        EndChange();
                        EditorGUILayout.EndHorizontal();
                    }
                }

                // Select Same Type
                BeginChange();
                EditorGUILayout.PropertyField(editorToolSelectSameTypeEnable, new GUIContent(EditorTextSoundManager.editorToolSelectSameTypeEnableLabel, EditorTextSoundManager.editorToolSelectSameTypeEnableTooltip));
                EndChange();

                if (editorToolSelectSameTypeEnable.boolValue) {
                    if (!EditorScriptingDefineSymbols.EditorToolSelectSameTypeExists()) {
                        EditorGUILayout.BeginHorizontal();
                        // For offsetting the buttons to the right
                        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                        BeginChange();
                        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.editorToolSelectSameTypeAddLabel, EditorTextSoundManager.editorToolReferenceFinderAddTooltip))) {
                            EditorScriptingDefineSymbols.EditorToolSelectSameTypeShouldExist(true);
                        }
                        EndChange();
                        EditorGUILayout.EndHorizontal();
                    }
                } else {
                    if (EditorScriptingDefineSymbols.EditorToolSelectSameTypeExists()) {
                        EditorGUILayout.BeginHorizontal();
                        // For offsetting the buttons to the right
                        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                        BeginChange();
                        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.editorToolSelectSameTypeRemoveLabel, EditorTextSoundManager.editorToolReferenceFinderRemoveTooltip))) {
                            EditorScriptingDefineSymbols.EditorToolSelectSameTypeShouldExist(false);
                        }
                        EndChange();
                        EditorGUILayout.EndHorizontal();
                    }
                }

                // Selection History
                BeginChange();
                EditorGUILayout.PropertyField(editorToolSelectionHistoryEnable, new GUIContent(EditorTextSoundManager.editorToolSelectionHistoryEnableLabel, EditorTextSoundManager.editorToolSelectionHistoryEnableTooltip));
                EndChange();

                if (editorToolSelectionHistoryEnable.boolValue) {
                    if (!EditorScriptingDefineSymbols.EditorToolSelectionHistoryExists()) {
                        EditorGUILayout.BeginHorizontal();
                        // For offsetting the buttons to the right
                        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                        BeginChange();
                        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.editorToolSelectionHistoryAddLabel, EditorTextSoundManager.editorToolSelectionHistoryAddTooltip))) {
                            EditorScriptingDefineSymbols.EditorToolSelectionHistoryShouldExist(true);
                        }
                        EndChange();
                        EditorGUILayout.EndHorizontal();
                    }
                } else {
                    if (EditorScriptingDefineSymbols.EditorToolSelectionHistoryExists()) {
                        EditorGUILayout.BeginHorizontal();
                        // For offsetting the buttons to the right
                        EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                        BeginChange();
                        if (GUILayout.Button(new GUIContent(EditorTextSoundManager.editorToolSelectionHistoryRemoveLabel, EditorTextSoundManager.editorToolSelectionHistoryRemoveTooltip))) {
                            EditorScriptingDefineSymbols.EditorToolSelectionHistoryShouldExist(false);
                        }
                        EndChange();
                        EditorGUILayout.EndHorizontal();
                    }
                }

                // Add Remove All Editor Tools
                EditorGUILayout.Separator();
                EditorGUILayout.BeginHorizontal();
                // For offsetting the buttons to the right
                EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                BeginChange();
                if (GUILayout.Button(new GUIContent(EditorTextSoundManager.editorToolsAddAllLabel, EditorTextSoundManager.editorToolsAddAllTooltip))) {
                    for (int i = 0; i < mTargets.Length; i++) {
                        Undo.RecordObject(mTargets[i], "Editor Tools Add All");
                        mTargets[i].Internals.editorTools.editorToolSelectionHistoryEnable = true;
                        mTargets[i].Internals.editorTools.editorToolReferenceFinderEnable = true;
                        mTargets[i].Internals.editorTools.editorToolSelectSameTypeEnable = true;
                        EditorScriptingDefineSymbols.EditorToolSelectionHistoryShouldExist(true);
                        EditorScriptingDefineSymbols.EditorToolReferenceFinderShouldExist(true);
                        EditorScriptingDefineSymbols.EditorToolSelectSameTypeShouldExist(true);
                        EditorUtility.SetDirty(mTargets[i]);
                    }
                }
                EndChange();
                BeginChange();
                if (GUILayout.Button(new GUIContent(EditorTextSoundManager.editorToolsRemoveAllLabel, EditorTextSoundManager.editorToolsRemoveAllTooltip))) {
                    for (int i = 0; i < mTargets.Length; i++) {
                        Undo.RecordObject(mTargets[i], "Editor Tools Remove All");
                        mTargets[i].Internals.editorTools.editorToolSelectionHistoryEnable = false;
                        mTargets[i].Internals.editorTools.editorToolReferenceFinderEnable = false;
                        mTargets[i].Internals.editorTools.editorToolSelectSameTypeEnable = false;
                        EditorScriptingDefineSymbols.EditorToolSelectionHistoryShouldExist(false);
                        EditorScriptingDefineSymbols.EditorToolReferenceFinderShouldExist(false);
                        EditorScriptingDefineSymbols.EditorToolSelectSameTypeShouldExist(false);
                        EditorUtility.SetDirty(mTargets[i]);
                    }
                }
                EndChange();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Separator();
            }
            StopBackgroundColor();
        }

        private void DrawDebug() {
            StartBackgroundColor(EditorColor.GetDebug(EditorColorProSkin.GetCustomEditorBackgroundAlpha()));

            EditorGUI.indentLevel = 1;
            BeginChange();
            EditorGuiFunction.DrawFoldout(debugExpand, EditorTextSoundManager.debugExpandLabel, EditorTextSoundManager.debugExpandTooltip);
            EndChange();

            if (debugExpand.boolValue) {

                // Show Current Cached AudioListener & AudioListener Distance
                EditorGUI.BeginDisabledGroup(true);

                EditorGUILayout.ObjectField(new GUIContent(EditorTextSoundManager.debugAudioListenerLabel, EditorTextSoundManager.debugAudioListenerTooltip), mTarget.Internals.cachedAudioListener, typeof(UnityEngine.Object), true);

                if (overrideListenerDistance.boolValue) {
                    EditorGUILayout.ObjectField(new GUIContent(EditorTextSoundManager.debugAudioListenerDistanceLabel, EditorTextSoundManager.debugAudioListenerDistanceTooltip), mTarget.Internals.cachedAudioListenerDistance, typeof(UnityEngine.Object), true);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.Separator();

                EditorGUILayout.BeginHorizontal();
                // For offsetting the buttons to the right
                EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                if (SonityEditorPrefs.SonitySoundManagerDebugEnable) {
                    if (GUILayout.Button(new GUIContent(EditorTextSoundManager.editorPrefsDebugAllDisableLabel, EditorTextSoundManager.editorPrefsDebugAllTooltip))) {
                        SonityEditorPrefs.SonitySoundManagerDebugEnable = false;
                    }
                } else {
                    if (GUILayout.Button(new GUIContent(EditorTextSoundManager.editorPrefsDebugAllEnableLabel, EditorTextSoundManager.editorPrefsDebugAllTooltip))) {
                        SonityEditorPrefs.SonitySoundManagerDebugEnable = true;
                    }
                }
                EditorGUILayout.EndHorizontal();

                // Override Local Debug
                BeginChange();
                EditorGUILayout.PropertyField(forceDebug, new GUIContent(EditorTextSoundManager.overrideLocalDebugLabel, EditorTextSoundManager.overrideLocalDebugTooltip));
                EndChange();

                // Cannot nest disabled groups
                if (!SonityEditorPrefs.SonitySoundManagerDebugEnable && !forceDebug.boolValue) {
                    EditorGUI.BeginDisabledGroup(true);
                }

                // Log SoundEvents
                EditorGUI.indentLevel = 1;
                EditorGUILayout.LabelField(new GUIContent(EditorTextSoundManager.logSoundEventsHeaderLabel, EditorTextSoundManager.logSoundEventsHeaderTooltip), EditorStyles.boldLabel);
                EditorGUI.indentLevel++;

                BeginChange();
                EditorGUILayout.PropertyField(logSoundEventsEnabled, new GUIContent(EditorTextSoundManager.logSoundEventsEnableLabel, EditorTextSoundManager.logSoundEventsEnableTooltip));
                EndChange();

                if (logSoundEventsEnabled.boolValue) {

                    BeginChange();
                    EditorGUILayout.PropertyField(logSoundEventsLogType, new GUIContent(EditorTextSoundManager.logSoundEventsLogTypeLabel, EditorTextSoundManager.logSoundEventsLogTypeTooltip));
                    EndChange();

                    BeginChange();
                    EditorGUILayout.PropertyField(logSoundEventsSelectObject, new GUIContent(EditorTextSoundManager.logSoundEventsSelectObjectLabel, EditorTextSoundManager.logSoundEventsSelectObjectTooltip));
                    EndChange();

                    EditorGUILayout.BeginHorizontal();
                    // For offsetting the buttons to the right
                    EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                    BeginChange();
                    if (GUILayout.Button(new GUIContent(EditorTextSoundManager.logSoundEventsSettingsLabel, EditorTextSoundManager.logSoundEventsSettingsTooltip))) {
                        GenericMenu menu = new GenericMenu();

                        // Tooltips dont work for menu
                        menu.AddItem(new GUIContent("SoundEvent Play"), logSoundEventsPlay.boolValue, SoundEventLogMenuCallback, SoundEventLogButtonType.Play);
                        menu.AddItem(new GUIContent("SoundEvent Stop"), logSoundEventsStop.boolValue, SoundEventLogMenuCallback, SoundEventLogButtonType.Stop);
                        menu.AddItem(new GUIContent("SoundEvent Pool"), logSoundEventsPool.boolValue, SoundEventLogMenuCallback, SoundEventLogButtonType.Pool);
                        menu.AddItem(new GUIContent("SoundEvent Pause"), logSoundEventsPause.boolValue, SoundEventLogMenuCallback, SoundEventLogButtonType.Pause);
                        menu.AddItem(new GUIContent("SoundEvent Unpause"), logSoundEventsUnpause.boolValue, SoundEventLogMenuCallback, SoundEventLogButtonType.Unpause);
                        menu.AddItem(new GUIContent("SoundEvent Global Pause"), logSoundEventsGlobalPause.boolValue, SoundEventLogMenuCallback, SoundEventLogButtonType.GlobalPause);
                        menu.AddItem(new GUIContent("SoundEvent Global Unpause"), logSoundEventsGlobalUnpause.boolValue, SoundEventLogMenuCallback, SoundEventLogButtonType.GlobalUnpause);
                        menu.AddItem(new GUIContent("SoundParameters Once"), logSoundEventsSoundParametersOnce.boolValue, SoundEventLogMenuCallback, SoundEventLogButtonType.SoundParametersOnce);
                        menu.AddItem(new GUIContent("SoundParameters Continuous"), logSoundEventsSoundParametersContinuous.boolValue, SoundEventLogMenuCallback, SoundEventLogButtonType.SoundParametersContinuous);

                        menu.ShowAsContext();
                    }
                    EndChange();
                    BeginChange();
                    if (GUILayout.Button(new GUIContent(EditorTextSoundManager.logSoundEventsResetLabel, EditorTextSoundManager.logSoundEventsResetTooltip))) {
                        for (int i = 0; i < mTargets.Length; i++) {
                            Undo.RecordObject(mTargets[i], "Reset Debug Log Settings");
                            mTargets[i].Internals.debug.logSoundEventsLogType = DebugSoundEventsToLogLogType.DebugLog;
                            mTargets[i].Internals.debug.logSoundEventsSelectObject = DebugSoundEventsToLogSelectObject.Owner;
                            mTargets[i].Internals.debug.logSoundEventsPlay = true;
                            mTargets[i].Internals.debug.logSoundEventsStop = true;
                            mTargets[i].Internals.debug.logSoundEventsPool = false;
                            mTargets[i].Internals.debug.logSoundEventsPause = true;
                            mTargets[i].Internals.debug.logSoundEventsUnpause = true;
                            mTargets[i].Internals.debug.logSoundEventsGlobalPause = false;
                            mTargets[i].Internals.debug.logSoundEventsGlobalUnpause = false;
                            mTargets[i].Internals.debug.logSoundEventsSoundParametersOnce = true;
                            mTargets[i].Internals.debug.logSoundEventsSoundParametersContinuous = false;
                            EditorUtility.SetDirty(mTargets[i]);
                        }
                    }
                    EndChange();
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Separator();

                // Draw SoundEvents
                EditorGUI.indentLevel = 1;
                EditorGUILayout.LabelField(new GUIContent(EditorTextSoundManager.drawSoundEventsLabel, EditorTextSoundManager.drawSoundEventsTooltip), EditorStyles.boldLabel);
                EditorGUI.indentLevel++;

                // Draw SoundEvents in Scene View
                BeginChange();
                EditorGUILayout.PropertyField(drawSoundEventsInSceneViewEnabled, new GUIContent(EditorTextSoundManager.drawSoundEventsInSceneViewEnabledLabel, EditorTextSoundManager.drawSoundEventsInSceneViewEnabledTooltip));
                EndChange();

                // Draw SoundEvents in Game View
                BeginChange();
                EditorGUILayout.PropertyField(drawSoundEventsInGameViewEnabled, new GUIContent(EditorTextSoundManager.drawSoundEventsInGameViewEnabledLabel, EditorTextSoundManager.drawSoundEventsInGameViewEnabledTooltip));
                EndChange();

                if (drawSoundEventsInGameViewEnabled.boolValue) {
                    EditorGUI.indentLevel++;
                    BeginChange();
                    EditorGUILayout.PropertyField(drawSoundEventsHideIfCloserThan, new GUIContent(EditorTextSoundManager.drawSoundEventsHideIfCloserThanLabel, EditorTextSoundManager.drawSoundEventsHideIfCloserThanTooltip));
                    if (drawSoundEventsHideIfCloserThan.floatValue < 0f) {
                        drawSoundEventsHideIfCloserThan.floatValue = 0f;
                    }
                    EndChange();
                    EditorGUI.indentLevel--;
                }

                if (drawSoundEventsInSceneViewEnabled.boolValue || drawSoundEventsInGameViewEnabled.boolValue) {
                    EditorGUILayout.LabelField(new GUIContent("Style"));
                    EditorGUI.indentLevel++;
                    BeginChange();
                    EditorGUILayout.IntSlider(drawSoundEventsFontSize, 1, 100, new GUIContent(EditorTextSoundManager.drawSoundEventsFontSizeLabel, EditorTextSoundManager.drawSoundEventsFontSizeTooltip));
                    EndChange();
                    BeginChange();
                    drawSoundEventsVolumeToOpacity.floatValue = EditorGUILayout.Slider(new GUIContent(EditorTextSoundManager.drawSoundEventsVolumeToOpacityLabel, EditorTextSoundManager.drawSoundEventsVolumeToOpacityTooltip), drawSoundEventsVolumeToOpacity.floatValue, 0f, 1f);
                    EndChange();
                    BeginChange();
                    drawSoundEventsLifetimeToOpacity.floatValue = EditorGUILayout.Slider(new GUIContent(EditorTextSoundManager.drawSoundEventsLifetimeToOpacityLabel, EditorTextSoundManager.drawSoundEventsLifetimeToOpacityTooltip), drawSoundEventsLifetimeToOpacity.floatValue, 0f, 1f);
                    EndChange();
                    BeginChange();
                    drawSoundEventsLifetimeFadeLength.floatValue = EditorGUILayout.Slider(new GUIContent(EditorTextSoundManager.drawSoundEventsLifetimeFadeLengthLabel, EditorTextSoundManager.drawSoundEventsLifetimeFadeLengthTooltip), drawSoundEventsLifetimeFadeLength.floatValue, 0f, 10f);
                    EndChange();
                    BeginChange();
                    EditorGUILayout.PropertyField(drawSoundEventsColorStart, new GUIContent(EditorTextSoundManager.drawSoundEventsColorStartLabel, EditorTextSoundManager.drawSoundEventsColorStartTooltip));
                    EndChange();
                    BeginChange();
                    EditorGUILayout.PropertyField(drawSoundEventsColorEnd, new GUIContent(EditorTextSoundManager.drawSoundEventsColorEndLabel, EditorTextSoundManager.drawSoundEventsColorEndTooltip));
                    EndChange();
                    BeginChange();
                    EditorGUILayout.PropertyField(drawSoundEventsColorOutline, new GUIContent(EditorTextSoundManager.drawSoundEventsColorOutlineLabel, EditorTextSoundManager.drawSoundEventsColorOutlineTooltip));
                    EndChange();
                    EditorGUI.indentLevel--;

                    EditorGUILayout.BeginHorizontal();
                    // For offsetting the buttons to the right
                    EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                    BeginChange();
                    if (GUILayout.Button(new GUIContent(EditorTextSoundManager.drawSoundEventsResetLabel, EditorTextSoundManager.drawSoundEventsResetTooltip))) {
                        for (int i = 0; i < mTargets.Length; i++) {
                            Undo.RecordObject(mTargets[i], "Reset Style");
                            mTargets[i].Internals.debug.drawSoundEventsFontSize = 16;
                            mTargets[i].Internals.debug.drawSoundEventsVolumeToOpacity = 0.5f;
                            mTargets[i].Internals.debug.drawSoundEventsLifetimeToOpacity = 0.75f;
                            mTargets[i].Internals.debug.drawSoundEventsLifetimeFadeLength = 1f;
                            mTargets[i].Internals.debug.drawSoundEventsColorStart = EditorColor.GetVolumeMax(1f);
                            mTargets[i].Internals.debug.drawSoundEventsColorEnd = EditorColor.GetVolumeMin(1f);
                            mTargets[i].Internals.debug.drawSoundEventsColorOutline = Color.black;
                            EditorUtility.SetDirty(mTargets[i]);
                        }
                    }
                    EndChange();
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Separator();
                EditorGUILayout.BeginHorizontal();
                // For offsetting the buttons to the right
                EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                BeginChange();
                if (GUILayout.Button(new GUIContent(EditorTextSoundManager.debugResetAllLabel, EditorTextSoundManager.debugResetAllTooltip))) {
                    for (int i = 0; i < mTargets.Length; i++) {
                        Undo.RecordObject(mTargets[i], "Reset All Debug Settings");
                        mTargets[i].Internals.debug.forceDebug = false;
                        // Draw SoundEvents
                        mTargets[i].Internals.debug.drawSoundEventsInSceneViewEnabled = false;
                        mTargets[i].Internals.debug.drawSoundEventsInGameViewEnabled = false;
                        mTargets[i].Internals.debug.drawSoundEventsHideIfCloserThan = 0.01f;
                        mTargets[i].Internals.debug.drawSoundEventsFontSize = 16;
                        mTargets[i].Internals.debug.drawSoundEventsVolumeToOpacity = 0.5f;
                        mTargets[i].Internals.debug.drawSoundEventsLifetimeToOpacity = 0.75f;
                        mTargets[i].Internals.debug.drawSoundEventsLifetimeFadeLength = 1f;
                        mTargets[i].Internals.debug.drawSoundEventsColorStart = EditorColor.GetVolumeMax(1f);
                        mTargets[i].Internals.debug.drawSoundEventsColorEnd = EditorColor.GetVolumeMin(1f);
                        mTargets[i].Internals.debug.drawSoundEventsColorOutline = Color.black;
                        // Log SoundEvents
                        mTargets[i].Internals.debug.logSoundEventsEnabled = false;
                        mTargets[i].Internals.debug.logSoundEventsLogType = DebugSoundEventsToLogLogType.DebugLog;
                        mTargets[i].Internals.debug.logSoundEventsSelectObject = DebugSoundEventsToLogSelectObject.Owner;
                        mTargets[i].Internals.debug.logSoundEventsPlay = true;
                        mTargets[i].Internals.debug.logSoundEventsStop = true;
                        mTargets[i].Internals.debug.logSoundEventsPool = false;
                        mTargets[i].Internals.debug.logSoundEventsPause = true;
                        mTargets[i].Internals.debug.logSoundEventsUnpause = true;
                        mTargets[i].Internals.debug.logSoundEventsGlobalPause = false;
                        mTargets[i].Internals.debug.logSoundEventsGlobalUnpause = false;
                        mTargets[i].Internals.debug.logSoundEventsSoundParametersOnce = true;
                        mTargets[i].Internals.debug.logSoundEventsSoundParametersContinuous = false;
                        EditorUtility.SetDirty(mTargets[i]);
                    }
                }
                EndChange();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Separator();

                if (!SonityEditorPrefs.SonitySoundManagerDebugEnable && !forceDebug.boolValue) {
                    EditorGUI.EndDisabledGroup();
                }
            }
            StopBackgroundColor();
        }

        List<StatisticsInstanceInfo> instancesInfoList = new List<StatisticsInstanceInfo>();

        private void DrawStatistics() {
            StartBackgroundColor(EditorColor.GetSoundManagerStatistics(EditorColorProSkin.GetCustomEditorBackgroundAlpha()));

            EditorGUI.indentLevel = 1;
            BeginChange();
            EditorGuiFunction.DrawFoldout(statisticsExpandMain, EditorTextSoundManager.statisticsExpandLabel, EditorTextSoundManager.statisticsExpandTooltip);
            EndChange();

            if (statisticsExpandMain.boolValue) {

                if (Application.isPlaying) {
                    // Repaint so its updated all the time
                    Repaint();
                }
                
                // Statistics Overall
                BeginChange();
                EditorGuiFunction.DrawFoldout(statisticsExpandGeneral, EditorTextSoundManager.statisticsOverallLabel, EditorTextSoundManager.statisticsOverallTooltip, 0, false, false, true);
                EndChange();

                if (statisticsExpandGeneral.boolValue) {

                    // SoundEvent Statistics
                    int soundEventInstancesActive = mTarget.Internals.soundEventInstanceList.CountActive;
                    int soundEventInstancesInactive = mTarget.Internals.soundEventInstanceList.CountInactive;
                    int soundEventInstancesTotal = mTarget.Internals.soundEventInstanceList.CountTotal;

                    float frontWidth = 100f;
                    float rightPadding = 50f;
                    float currentWidthMinusRight = EditorGUIUtility.currentViewWidth - rightPadding;
                    // Haft Front
                    float widthHalfMinusFront = (currentWidthMinusRight - frontWidth) / 2f;
                    float widthHalfMinusFrontPlusRight = widthHalfMinusFront + rightPadding;
                    // Third Front
                    float widthThirdMinusFront = (currentWidthMinusRight - frontWidth) / 3f;
                    float widthThirdMinusFrontPlusRight = widthThirdMinusFront + rightPadding;
                    // Third No Front
                    float widthThird = currentWidthMinusRight / 3f;
                    float widthThirdPlusRight = widthThird + rightPadding;
                    // Fourth Front
                    float widthFourthMinusFront = (currentWidthMinusRight - frontWidth) / 4f;
                    float widthFourthMinusFrontPlusRight = widthFourthMinusFront + rightPadding;
                    // Fourth No Front
                    float widthFourth = currentWidthMinusRight / 4f;
                    float widthFourthPlusRight = widthFourth + rightPadding;

                    Color normalTextColor = EditorColorProSkin.GetTextDefaultDarkOrLight();

                    GUIStyle guiStyleTempColored = new GUIStyle();
                    // Assign normal text color as default, so it becomes correct even if the application isnt playing
                    guiStyleTempColored.normal.textColor = normalTextColor;

                    // Assigning a guiStyle changes to the text doesnt cut when overlapping
                    GUIStyle guiStyleNormal = new GUIStyle();
                    guiStyleNormal.normal.textColor = normalTextColor;

                    Color normalTextColorOpacityHalf = normalTextColor;
                    normalTextColorOpacityHalf.a = 0.5f;

                    // SoundEvent Instances
                    // If combine then use: GUILayout.Width(frontWidth)
                    EditorGUILayout.LabelField(new GUIContent(EditorTextSoundManager.statisticsSoundEventsLabel, EditorTextSoundManager.statisticsSoundEventsTooltip), EditorStyles.boldLabel);
                    EditorGUILayout.BeginHorizontal();
                    if (Application.isPlaying) {
                        if (soundEventInstancesActive > 0) {
                            guiStyleTempColored.normal.textColor = EditorColorProSkin.GetTextGreen();
                        } else if (soundEventInstancesActive == 0) {
                            guiStyleTempColored.normal.textColor = normalTextColorOpacityHalf;
                        } else {
                            guiStyleTempColored.normal.textColor = normalTextColor;
                        }
                    }
                    // SoundEvent Instances Active, Inactive, Total
                    EditorGUILayout.LabelField(new GUIContent($"{soundEventInstancesActive}\tActive", EditorTextSoundManager.statisticsSoundEventsActiveTooltip), guiStyleTempColored, GUILayout.Width(widthThird));
                    EditorGUILayout.LabelField(new GUIContent($"{soundEventInstancesInactive}\tInactive", EditorTextSoundManager.statisticsSoundEventsInactiveTooltip), guiStyleNormal, GUILayout.Width(widthThird));
                    EditorGUILayout.LabelField(new GUIContent($"{soundEventInstancesTotal}\tTotal", EditorTextSoundManager.statisticsSoundEventsTotalTooltip), guiStyleNormal, GUILayout.Width(widthThirdPlusRight));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Separator();

                    int activeVoices = 0;
                    int pausedVoices = 0;
                    int stoppedVoices = 0;

                    for (int i = 0; i < mTarget.Internals.voicePool.voices.Length; i++) {
                        Voice voice = mTarget.Internals.voicePool.voices[i];
                        // Voices Active
                        if (voice.isAssigned) {
                            activeVoices++;
                        }
                        // Voices Paused
                        if (voice.GetState() == VoiceState.Pause) {
                            pausedVoices++;
                        }
                        // Voices Stopped
                        else if (voice.GetState() == VoiceState.Stop) {
                            stoppedVoices++;
                        }
                    }
                    // Set after setting previous, is dependant on Active Voices
                    // Voices Inactive
                    int inactiveVoices = mTarget.Internals.voicePool.voices.Length - activeVoices;
                    // Voices Max Simultaneous
                    if (mTarget.Internals.statistics.statisticsMaxSimultaneousVoices < activeVoices) {
                        mTarget.Internals.statistics.statisticsMaxSimultaneousVoices = activeVoices;
                    }
                    // Voices
                    EditorGUILayout.LabelField(new GUIContent(EditorTextSoundManager.statisticsVoicesLabel, EditorTextSoundManager.statisticsVoicesTooltip), EditorStyles.boldLabel);
                    if (Application.isPlaying) {
                        if (activeVoices > 0) {
                            guiStyleTempColored.normal.textColor = EditorColorProSkin.GetTextGreen();
                        } else if (inactiveVoices > 0 && activeVoices == 0) {
                            guiStyleTempColored.normal.textColor = normalTextColorOpacityHalf;
                        } else {
                            guiStyleTempColored.normal.textColor = normalTextColor;
                        }
                    }
                    // Voices Active, Inactive, Total
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(new GUIContent($"{activeVoices}\tActive", EditorTextSoundManager.statisticsVoicesActiveTooltip), guiStyleTempColored, GUILayout.Width(widthThird));
                    EditorGUILayout.LabelField(new GUIContent($"{inactiveVoices}\tInactive", EditorTextSoundManager.statisticsVoicesInactiveTooltip), guiStyleNormal, GUILayout.Width(widthThird));
                    EditorGUILayout.LabelField(new GUIContent($"{mTarget.Internals.voicePool.voices.Length}\tTotal", EditorTextSoundManager.statisticsVoicesTotalTooltip), guiStyleNormal, GUILayout.Width(widthThirdPlusRight));
                    EditorGUILayout.EndHorizontal();
                    // Voices Played, Max, Stolen
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(new GUIContent($"{pausedVoices}\tPaused", EditorTextSoundManager.statisticsVoicesPausedTooltip), guiStyleNormal, GUILayout.Width(widthThird));
                    EditorGUILayout.LabelField(new GUIContent($"{stoppedVoices}\tStopped", EditorTextSoundManager.statisticsVoicesStoppedTooltip), guiStyleNormal, GUILayout.Width(widthThird));
                    // Voices Stolen
                    if (Application.isPlaying) {
                        if (mTarget.Internals.voicePool.statisticsVoicesStolen > 0) {
                            guiStyleTempColored.normal.textColor = EditorColorProSkin.GetTextRed();
                        } else {
                            guiStyleTempColored.normal.textColor = normalTextColor;
                        }
                    }
                    EditorGUILayout.LabelField(new GUIContent($"{mTarget.Internals.voicePool.statisticsVoicesStolen}\tStolen", EditorTextSoundManager.statisticsVoicesStolenTooltip), guiStyleNormal, GUILayout.Width(widthThirdPlusRight));
                    EditorGUILayout.EndHorizontal();
                    // Voices Paused, Stopped
                    EditorGUILayout.BeginHorizontal();
                    // Filler
                    EditorGUILayout.LabelField(new GUIContent($"{mTarget.Internals.statistics.statisticsVoicesPlayed}\tPlayed", EditorTextSoundManager.statisticsVoicesPlayedTooltip), guiStyleNormal, GUILayout.Width(widthThird));
                    EditorGUILayout.LabelField(new GUIContent($"{mTarget.Internals.statistics.statisticsMaxSimultaneousVoices}\tMax Simultaneous", EditorTextSoundManager.statisticsVoicesMaxSimultaneousTooltip), guiStyleNormal, GUILayout.Width(widthThird));
                    EditorGUILayout.LabelField(new GUIContent("", ""), EditorStyles.boldLabel, GUILayout.Width(widthThirdPlusRight));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Separator();

                    int activeVoiceEffects = 0;
#if !SONITY_DISABLE_VOICE_EFFECTS
                    for (int i = 0; i < mTarget.Internals.voiceEffectPool.voiceEffects.Length; i++) {
                        if (mTarget.Internals.voiceEffectPool.voiceEffects[i] != null) {
                            if (mTarget.Internals.voiceEffectPool.voiceEffects[i].cachedVoiceEffect.GetEnabled()) {
                                activeVoiceEffects++;
                            }
                        }
                    }
#endif
                    int availableVoiceEffects = mTarget.Internals.settings.voiceEffectLimit - activeVoiceEffects;

                    // Voice Effect Statistics
                    EditorGUILayout.LabelField(new GUIContent(EditorTextSoundManager.statisticsVoiceEffectsLabel, EditorTextSoundManager.statisticsVoiceEffectsTooltip), EditorStyles.boldLabel);
                    EditorGUILayout.BeginHorizontal();
                    if (Application.isPlaying) {
                        if (activeVoiceEffects > 0) {
                            guiStyleTempColored.normal.textColor = EditorColorProSkin.GetTextGreen();
                        } else if (availableVoiceEffects > 0 && activeVoiceEffects == 0) {
                            guiStyleTempColored.normal.textColor = normalTextColorOpacityHalf;
                        } else {
                            guiStyleTempColored.normal.textColor = normalTextColor;
                        }
                    }
                    guiStyleTempColored.clipping = new TextClipping();
                    // Voice Effects Active, Available, Total
                    EditorGUILayout.LabelField(new GUIContent($"{activeVoiceEffects}\tActive", EditorTextSoundManager.statisticsVoiceEffectsActiveTooltip), guiStyleTempColored, GUILayout.Width(widthThird));
                    EditorGUILayout.LabelField(new GUIContent($"{availableVoiceEffects}\tAvailable", EditorTextSoundManager.statisticsVoiceEffectsAvailableTooltip), guiStyleNormal, GUILayout.Width(widthThird));
                    EditorGUILayout.LabelField(new GUIContent($"{mTarget.Internals.settings.voiceEffectLimit}\tTotal", EditorTextSoundManager.statisticsVoiceEffectsTotalTooltip), guiStyleNormal, GUILayout.Width(widthThirdPlusRight));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Separator();
                }

                // Statistics Instances
                BeginChange();
                EditorGuiFunction.DrawFoldout(statisticsExpandInstances, EditorTextSoundManager.statisticsInstanceLabel, EditorTextSoundManager.statisticsInstanceTooltip, 0, false, false, true);
                EndChange();

                if (statisticsExpandInstances.boolValue) {

                    BeginChange();
                    EditorGUILayout.PropertyField(statisticsSorting, new GUIContent(EditorTextSoundManager.statisticsSortingLabel, EditorTextSoundManager.statisticsSortingTooltip));
                    EndChange();

                    // Statistics Dropdown Menu
                    EditorGUILayout.BeginHorizontal();
                    // For offsetting the buttons to the right
                    EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                    BeginChange();
                    if (GUILayout.Button(new GUIContent(EditorTextSoundManager.statisticsShowLabel, EditorTextSoundManager.statisticsShowTooltip))) {
                        GenericMenu menu = new GenericMenu();
                        // Tooltips dont work for menu
                        menu.AddItem(new GUIContent("Info: Active"), statisticsInfoActive.boolValue, StatisticsMenuCallback, StatisticsButtonType.InfoActive);
                        menu.AddItem(new GUIContent("Info: Voices"), statisticsInfoVoices.boolValue, StatisticsMenuCallback, StatisticsButtonType.InfoVoices);
                        menu.AddItem(new GUIContent("Info: Volume"), statisticsInfoVolume.boolValue, StatisticsMenuCallback, StatisticsButtonType.InfoVolume);
                        menu.AddItem(new GUIContent("Info: Plays"), statisticsInfoPlays.boolValue, StatisticsMenuCallback, StatisticsButtonType.InfoPlays);
                        menu.AddSeparator("");
                        menu.AddItem(new GUIContent("Filter: Show Active"), statisticsFilterShowActive.boolValue, StatisticsMenuCallback, StatisticsButtonType.FilterShowActive);
                        menu.AddItem(new GUIContent("Filter: Show Inactive"), statisticsFilterShowInactive.boolValue, StatisticsMenuCallback, StatisticsButtonType.FilterShowInactive);
                        menu.ShowAsContext();
                    }
                    EndChange();
                    BeginChange();
                    if (GUILayout.Button(new GUIContent(EditorTextSoundManager.statisticsInstancesResetLabel, EditorTextSoundManager.statisticsInstancesResetTooltip))) {
                        for (int i = 0; i < mTargets.Length; i++) {
                            Undo.RecordObject(mTargets[i], "Reset Statistics Instances");
                            mTargets[i].Internals.statistics.statisticsSorting = SoundManagerStatisticsSorting.Name;
                            mTargets[i].Internals.statistics.statisticsInfoActive = true;
                            mTargets[i].Internals.statistics.statisticsInfoVoices = true;
                            mTargets[i].Internals.statistics.statisticsInfoVolume = true;
                            mTargets[i].Internals.statistics.statisticsInfoPlays = true;
                            mTargets[i].Internals.statistics.statisticsFilterShowActive = true;
                            mTargets[i].Internals.statistics.statisticsFilterShowInactive = false;
                            EditorUtility.SetDirty(mTargets[i]);
                        }
                    }
                    EndChange();
                    EditorGUILayout.EndHorizontal();

                    if (!Application.isPlaying) {
                        FontStyleOverrideStart(FontStyle.Italic);
                        EditorGUILayout.LabelField(new GUIContent("Available in Playmode"));
                        FontStyleOverrideStop();
                    } else {
                        // Only when application is playing

                        // Local list (is later copied by reference)
                        instancesInfoList.Clear();

                        // Iterating over all the SoundEvents
                        var enumerator = mTarget.Internals.soundEventGroupDictionary.GetEnumerator();
                        while (enumerator.MoveNext()) {
                            SoundEventInstanceGroup instanceGroup = enumerator.Current.Value;

                            // Filter hide inactive
                            if (!statisticsFilterShowInactive.boolValue && instanceGroup.activeInstances.Count == 0) {
                                continue;
                            }

                            // Filter hide active
                            if (!statisticsFilterShowActive.boolValue && instanceGroup.activeInstances.Count > 0) {
                                continue;
                            }

                            StatisticsInstanceInfo info = new StatisticsInstanceInfo();
                            info.soundEvent = instanceGroup.soundEvent;

                            // Active
                            info.instancesActive = instanceGroup.activeInstances.Count;

                            // Volume
                            int instancesCount = instanceGroup.activeInstances.Count;
                            float volumeAverage = 0f;

                            for (int ii = 0; ii < instanceGroup.activeInstances.Count; ii++) {
                                SoundEventInstance instance = instanceGroup.activeInstances[ii];
                                // Number of voices
                                info.voicesUsed += instance.StatisticsGetNumberOfUsedVoices();
                                // Average Volume
                                volumeAverage += instance.StatisticsGetAverageVolumeRatio();
                            }
                            // Avoid divide by zero
                            if (instancesCount > 0) {
                                info.volumeAverageLinear = volumeAverage / instancesCount;
                            } else {
                                info.volumeAverageLinear = volumeAverage;
                            }
                            instancesInfoList.Add(info);
                        }

                        // Sorting Instances
                        // All but Chronological should be sorted by name
                        if ((SoundManagerStatisticsSorting)statisticsSorting.enumValueIndex != SoundManagerStatisticsSorting.Chronological) {
                            // Sort by name
                            instancesInfoList = instancesInfoList.OrderBy(parameter => parameter.soundEvent.internals.cachedName).ToList();
                        }

                        if ((SoundManagerStatisticsSorting)statisticsSorting.enumValueIndex == SoundManagerStatisticsSorting.Voices) {
                            // Sort by voices
                            instancesInfoList = instancesInfoList.OrderByDescending(parameter => parameter.voicesUsed).ToList();
                        } else if ((SoundManagerStatisticsSorting)statisticsSorting.enumValueIndex == SoundManagerStatisticsSorting.Plays) {
                            // Sort by number of plays
                            instancesInfoList = instancesInfoList.OrderByDescending(parameter => SoundManagerBase.Instance.Internals.InternalStatisticsNumberOfPlays(parameter.soundEvent, false)).ToList();
                        } else if ((SoundManagerStatisticsSorting)statisticsSorting.enumValueIndex == SoundManagerStatisticsSorting.Volume) {
                            // Sort by volume
                            instancesInfoList = instancesInfoList.OrderByDescending(parameter => parameter.volumeAverageLinear).ToList();
                        }
                        // Need to set the mTarget list also after sorting
                        // Is set by Reference
                        mTarget.Internals.statistics.instancesInfo = instancesInfoList;

                        // Statistics Instances Drawing
                        bool nothingActive = instancesInfoList.Count == 0;

                        if (nothingActive) {
                            FontStyleOverrideStart(FontStyle.Italic);
                            EditorGUILayout.LabelField(new GUIContent("Nothing is Playing"));
                            FontStyleOverrideStop();
                        } else {

                            SerializedProperty instancesInfo = statistics.FindPropertyRelative(nameof(SoundManagerInternalsStatistics.instancesInfo));

                            // Hidden is not added to the list
                            if (instancesInfo.arraySize == 0) {
                                FontStyleOverrideStart(FontStyle.Italic);
                                EditorGUILayout.LabelField(new GUIContent("All Instances are Hidden by Filters"));
                                FontStyleOverrideStop();
                            } else {
                                for (int i = 0; i < instancesInfo.arraySize; i++) {

                                    // Check for not running outside the list length
                                    if (i >= instancesInfoList.Count) {
                                        break;
                                    }

                                    SerializedProperty instance = instancesInfo.GetArrayElementAtIndex(i);
                                    SerializedProperty instancesActive = instance.FindPropertyRelative(nameof(StatisticsInstanceInfo.instancesActive));
                                    SerializedProperty voicesUsed = instance.FindPropertyRelative(nameof(StatisticsInstanceInfo.voicesUsed));
                                    SerializedProperty volumeAverageLinear = instance.FindPropertyRelative(nameof(StatisticsInstanceInfo.volumeAverageLinear));
                                    SerializedProperty soundEvent = instance.FindPropertyRelative(nameof(StatisticsInstanceInfo.soundEvent));

                                    // Note: How to get SoundEvent.name - soundEvent.objectReferenceValue.name;
                                    string soundEventInfo = $"";

                                    // Instances
                                    if (statisticsInfoActive.boolValue) {
                                        soundEventInfo += $"{instancesActive.intValue} Inst\t";
                                    }
                                    // Voices
                                    if (statisticsInfoVoices.boolValue) {
                                        soundEventInfo += $"{voicesUsed.intValue} Voic\t";
                                    }
                                    // Volume
                                    if (statisticsInfoVolume.boolValue) {
                                        float volume = VolumeScale.ConvertRatioToDecibel(volumeAverageLinear.floatValue);
                                        volume = Mathf.RoundToInt(volume);
                                        string volumeString = volume.ToString();
                                        if (volume == 0) {
                                            // Prepend minus if zero
                                            volumeString = $"-{volumeString}";
                                        }
                                        if (volume < -140f) {
                                            volumeString = "-inf";
                                        }
                                        soundEventInfo += $"{volumeString} dB\t";
                                    }
                                    // Plays
                                    if (statisticsInfoPlays.boolValue) {
                                        soundEventInfo += $"{SoundManagerBase.Instance.Internals.InternalStatisticsNumberOfPlays((SoundEventBase)soundEvent.objectReferenceValue, false)} Plays\t";
                                    }

                                    // If not active
                                    bool isActive = instancesActive.intValue > 0;
                                    if (!isActive) {
                                        EditorGUI.BeginDisabledGroup(true);
                                    }
                                    if (soundEvent.objectReferenceValue != null) {
                                        EditorGUILayout.BeginHorizontal();
                                        EditorGUILayout.PropertyField(soundEvent, new GUIContent(soundEventInfo));
                                        EditorGUILayout.EndHorizontal();
                                    }
                                    if (!isActive) {
                                        EditorGUI.EndDisabledGroup();
                                    }

                                }
                            }
                        }
                    }
                }
                // Reset Statistics All
                EditorGUILayout.BeginHorizontal();
                // For offsetting the buttons to the right
                EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
                BeginChange();
                if (GUILayout.Button(new GUIContent(EditorTextSoundManager.statisticsAllResetLabel, EditorTextSoundManager.statisticsAllResetTooltip))) {
                    for (int i = 0; i < mTargets.Length; i++) {
                        Undo.RecordObject(mTargets[i], "Reset All Statistics");
                        mTargets[i].Internals.statistics = new SoundManagerInternalsStatistics();
                        EditorUtility.SetDirty(mTargets[i]);
                    }
                }
                EndChange();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Separator();
            }
            StopBackgroundColor();
        }

        private enum SoundEventLogButtonType {
            Play,
            Stop,
            Pool,
            Pause,
            Unpause,
            SoundParametersOnce,
            SoundParametersContinuous,
            GlobalStop,
            GlobalPause,
            GlobalUnpause,
        }

        private void SoundEventLogMenuCallback(object obj) {

            SoundEventLogButtonType buttonType;

            try {
                buttonType = (SoundEventLogButtonType)obj;
            } catch {
                return;
            }

            bool tempToggle = false;

            for (int i = 0; i < mTargets.Length; i++) {
                if (mTargets[i] != null) {
                    Undo.RecordObject(mTargets[i], "SoundEvent Log");
                    // Show Active
                    if (buttonType == SoundEventLogButtonType.Play) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.debug.logSoundEventsPlay;
                        }
                        mTargets[i].Internals.debug.logSoundEventsPlay = tempToggle;
                    }
                    if (buttonType == SoundEventLogButtonType.Stop) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.debug.logSoundEventsStop;
                        }
                        mTargets[i].Internals.debug.logSoundEventsStop = tempToggle;
                    }
                    if (buttonType == SoundEventLogButtonType.Pool) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.debug.logSoundEventsPool;
                        }
                        mTargets[i].Internals.debug.logSoundEventsPool = tempToggle;
                    }
                    if (buttonType == SoundEventLogButtonType.Pause) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.debug.logSoundEventsPause;
                        }
                        mTargets[i].Internals.debug.logSoundEventsPause = tempToggle;
                    }
                    if (buttonType == SoundEventLogButtonType.Unpause) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.debug.logSoundEventsUnpause;
                        }
                        mTargets[i].Internals.debug.logSoundEventsUnpause = tempToggle;
                    }
                    if (buttonType == SoundEventLogButtonType.GlobalPause) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.debug.logSoundEventsGlobalPause;
                        }
                        mTargets[i].Internals.debug.logSoundEventsGlobalPause = tempToggle;
                    }
                    if (buttonType == SoundEventLogButtonType.GlobalUnpause) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.debug.logSoundEventsGlobalUnpause;
                        }
                        mTargets[i].Internals.debug.logSoundEventsGlobalUnpause = tempToggle;
                    }
                    if (buttonType == SoundEventLogButtonType.SoundParametersOnce) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.debug.logSoundEventsSoundParametersOnce;
                        }
                        mTargets[i].Internals.debug.logSoundEventsSoundParametersOnce = tempToggle;
                    }
                    if (buttonType == SoundEventLogButtonType.SoundParametersContinuous) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.debug.logSoundEventsSoundParametersContinuous;
                        }
                        mTargets[i].Internals.debug.logSoundEventsSoundParametersContinuous = tempToggle;
                    }
                    EditorUtility.SetDirty(mTargets[i]);
                }
            }
        }

        private enum StatisticsButtonType {
            InfoActive,
            InfoVoices,
            InfoPlays,
            InfoVolume,
            FilterShowActive,
            FilterShowInactive,
        }

        private void StatisticsMenuCallback(object obj) {

            StatisticsButtonType buttonType;

            try {
                buttonType = (StatisticsButtonType)obj;
            } catch {
                return;
            }

            bool tempToggle = false;

            for (int i = 0; i < mTargets.Length; i++) {
                if (mTargets[i] != null) {
                    Undo.RecordObject(mTargets[i], "Statistics Setting");
                    // Info Active
                    if (buttonType == StatisticsButtonType.InfoActive) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.statistics.statisticsInfoActive;
                        }
                        mTargets[i].Internals.statistics.statisticsInfoActive = tempToggle;
                    }
                    // Info Voices
                    if (buttonType == StatisticsButtonType.InfoVoices) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.statistics.statisticsInfoVoices;
                        }
                        mTargets[i].Internals.statistics.statisticsInfoVoices = tempToggle;
                    }
                    // Info Volume
                    if (buttonType == StatisticsButtonType.InfoVolume) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.statistics.statisticsInfoVolume;
                        }
                        mTargets[i].Internals.statistics.statisticsInfoVolume = tempToggle;
                    }
                    // Info Plays
                    if (buttonType == StatisticsButtonType.InfoPlays) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.statistics.statisticsInfoPlays;
                        }
                        mTargets[i].Internals.statistics.statisticsInfoPlays = tempToggle;
                    }
                    // Filter Show Active
                    if (buttonType == StatisticsButtonType.FilterShowActive) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.statistics.statisticsFilterShowActive;
                        }
                        mTargets[i].Internals.statistics.statisticsFilterShowActive = tempToggle;
                    }
                    // Filter Show Inactive
                    if (buttonType == StatisticsButtonType.FilterShowInactive) {
                        if (i == 0) {
                            tempToggle = !mTargets[0].Internals.statistics.statisticsFilterShowInactive;
                        }
                        mTargets[i].Internals.statistics.statisticsFilterShowInactive = tempToggle;
                    }
                    EditorUtility.SetDirty(mTargets[i]);
                }
            }
        }
    }
}
#endif