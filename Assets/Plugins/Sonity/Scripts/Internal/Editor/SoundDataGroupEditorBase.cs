// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Sonity.Internal {

    public abstract class SoundDataGroupEditorBase : Editor {

        public SoundDataGroupBase mTarget;
        public SoundDataGroupBase[] mTargets;

        [NonSerialized] public SerializedProperty sonityGuidEditor;
        [NonSerialized] public SerializedProperty internals;
        [NonSerialized] public SerializedProperty notes;
        [NonSerialized] public SerializedProperty soundDataGroupChildren;

        [NonSerialized] public SerializedProperty soundEvents;
        [NonSerialized] public SerializedProperty soundContainers;
        [NonSerialized] public SerializedProperty audioClips;

        [NonSerialized] public SerializedProperty soundEventsExpanded;
        [NonSerialized] public SerializedProperty soundContainersExpanded;
        [NonSerialized] public SerializedProperty audioClipsExpanded;

        public void OnEnable() {
            FindProperties();
        }

        public void FindProperties() {
            internals = serializedObject.FindProperty(nameof(SoundDataGroupBase.internals));
            sonityGuidEditor = internals.FindPropertyRelative(EditorTextAssetGuid.sonityGuidEditorPropertyName);
            notes = internals.FindPropertyRelative(nameof(SoundDataGroupInternals.notes));
            soundDataGroupChildren = internals.FindPropertyRelative(nameof(SoundDataGroupInternals.soundDataGroupChildren));

            soundEvents = internals.FindPropertyRelative(nameof(SoundDataGroupInternals.soundEvents));
            soundContainers = internals.FindPropertyRelative(nameof(SoundDataGroupInternals.soundContainers));
            audioClips = internals.FindPropertyRelative(nameof(SoundDataGroupInternals.audioClips));

            soundEventsExpanded = internals.FindPropertyRelative(nameof(SoundDataGroupInternals.soundEventsExpanded));
            soundContainersExpanded = internals.FindPropertyRelative(nameof(SoundDataGroupInternals.soundContainersExpanded));
            audioClipsExpanded = internals.FindPropertyRelative(nameof(SoundDataGroupInternals.audioClipsExpanded));
        }

        public void BeginChange() {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
        }

        public void EndChange() {
            if (EditorGUI.EndChangeCheck()) {
                serializedObject.ApplyModifiedProperties();
            }
        }

        public Color defaultGuiColor;
        public GUIStyle guiStyleBoldCenter = new GUIStyle();

        public void StartBackgroundColor(Color color) {
            GUI.color = color;
            EditorGUILayout.BeginVertical("Button");
            GUI.color = defaultGuiColor;
        }

        public void StopBackgroundColor() {
            EditorGUILayout.EndVertical();
        }

        private void DragAndDropCallbackMultipleTypes<T>(T[] draggedObjects) where T : UnityEngine.Object {
            // If there are any objects of the right type dragged
            for (int i = 0; i < mTargets.Length; i++) {
                SoundDataGroupBase target = mTargets[i];
                Undo.RecordObject(target, $"Drag and Dropped Multiple Types");
                for (int ii = 0; ii < draggedObjects.Length; ii++) {
                    T newObject = draggedObjects[ii];
                    SoundEventBase newObjectSoundEvent = newObject as SoundEventBase;
                    if (newObjectSoundEvent != null && !target.internals.soundEvents.Contains(newObjectSoundEvent)) {
                        target.internals.soundEvents.Add(newObjectSoundEvent);
                        continue;
                    }
                    SoundContainerBase newObjectSoundContainer = newObject as SoundContainerBase;
                    if (newObjectSoundContainer != null && !target.internals.soundContainers.Contains(newObjectSoundContainer)) {
                        target.internals.soundContainers.Add(newObjectSoundContainer);
                        continue;
                    }
                    AudioClip newObjectAudioClip = newObject as AudioClip;
                    if (newObjectAudioClip != null && !target.internals.audioClips.Contains(newObjectAudioClip)) {
                        target.internals.audioClips.Add(newObjectAudioClip);
                        continue;
                    }
                }
                // Orders by name
                target.internals.soundEvents = target.internals.soundEvents.OrderBy(p => p.internals.cachedName).ToList();
                target.internals.soundContainers = target.internals.soundContainers.OrderBy(p => p.internals.cachedName).ToList();
                target.internals.audioClips = target.internals.audioClips.OrderBy(p => p.name).ToList();
                // Expands the list if its not too long
                // SoundEvent
                bool expandListSoundEvent = true;
                if (target.internals.soundEvents.Count > 20) {
                    expandListSoundEvent = false;
                }
                target.internals.soundEventsExpanded = expandListSoundEvent;
                // SoundContainer
                bool expandListSoundContainer = true;
                if (target.internals.soundContainers.Count > 20) {
                    expandListSoundContainer = false;
                }
                target.internals.soundContainersExpanded = expandListSoundContainer;
                // AudioClip
                bool expandListAudioClip = true;
                if (target.internals.audioClips.Count > 20) {
                    expandListAudioClip = false;
                }
                target.internals.audioClipsExpanded = expandListAudioClip;
                EditorUtility.SetDirty(target);
            }
        }

        private void DragAndDropCallbackSoundEvent<T>(T[] draggedObjects) where T : UnityEngine.Object {
            SoundEventBase[] newObjects = draggedObjects as SoundEventBase[];
            // If there are any objects of the right type dragged
            for (int i = 0; i < mTargets.Length; i++) {
                SoundDataGroupBase target = mTargets[i];
                Undo.RecordObject(target, $"Drag and Dropped {NameOf.SoundEvent}");
                for (int ii = 0; ii < newObjects.Length; ii++) {
                    SoundEventBase newObject = newObjects[ii];
                    if (!target.internals.soundEvents.Contains(newObject)) {
                        target.internals.soundEvents.Add(newObject);
                    }
                }
                // Orders by name
                target.internals.soundEvents = target.internals.soundEvents.OrderBy(p => p.internals.cachedName).ToList();
                // Expands the list if its not too long
                bool expandList = true;
                if (target.internals.soundEvents.Count > 20) {
                    expandList = false;
                }
                target.internals.soundEventsExpanded = expandList;
                EditorUtility.SetDirty(target);
            }
        }

        private void DragAndDropCallbackSoundContainer<T>(T[] draggedObjects) where T : UnityEngine.Object {
            SoundContainerBase[] newObjects = draggedObjects as SoundContainerBase[];
            // If there are any objects of the right type dragged
            for (int i = 0; i < mTargets.Length; i++) {
                SoundDataGroupBase target = mTargets[i];
                Undo.RecordObject(target, $"Drag and Dropped {NameOf.SoundEvent}");
                for (int ii = 0; ii < newObjects.Length; ii++) {
                    SoundContainerBase newObject = newObjects[ii];
                    if (!target.internals.soundContainers.Contains(newObject)) {
                        target.internals.soundContainers.Add(newObject);
                    }
                }
                // Orders by name
                target.internals.soundContainers = target.internals.soundContainers.OrderBy(p => p.internals.cachedName).ToList();
                // Expands the list if its not too long
                bool expandList = true;
                if (target.internals.soundContainers.Count > 20) {
                    expandList = false;
                }
                target.internals.soundContainersExpanded = expandList;
                EditorUtility.SetDirty(target);
            }
        }

        private void DragAndDropCallbackAudioClip<T>(T[] draggedObjects) where T : UnityEngine.Object {
            AudioClip[] newObjects = draggedObjects as AudioClip[];
            // If there are any objects of the right type dragged
            for (int i = 0; i < mTargets.Length; i++) {
                SoundDataGroupBase target = mTargets[i];
                Undo.RecordObject(target, $"Drag and Dropped {NameOf.SoundEvent}");
                for (int ii = 0; ii < newObjects.Length; ii++) {
                    AudioClip newObject = newObjects[ii];
                    if (!target.internals.audioClips.Contains(newObject)) {
                        target.internals.audioClips.Add(newObject);
                    }
                }
                // Orders by name
                target.internals.audioClips = target.internals.audioClips.OrderBy(p => p.name).ToList();
                // Expands the list if its not too long
                bool expandList = true;
                if (target.internals.audioClips.Count > 20) {
                    expandList = false;
                }
                target.internals.audioClipsExpanded = expandList;
                EditorUtility.SetDirty(target);
            }
        }

        public static T[] FindAllAssetsOfTypeWholeProject<T>() where T : UnityEngine.Object {
            return AssetDatabase
                .FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .ToArray();
        }

        public static T[] FindAllAssetsOfTypeInPath<T>(string path) where T : UnityEngine.Object {
            return AssetDatabase
                .FindAssets($"t:{typeof(T).Name}", new[] { path })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .ToArray();
        }

        public override void OnInspectorGUI() {

            mTarget = (SoundDataGroupBase)target;

            mTargets = new SoundDataGroupBase[targets.Length];
            for (int i = 0; i < targets.Length; i++) {
                mTargets[i] = (SoundDataGroupBase)targets[i];
            }

            defaultGuiColor = GUI.color;

            guiStyleBoldCenter.fontSize = 16;
            guiStyleBoldCenter.fontStyle = FontStyle.Bold;
            guiStyleBoldCenter.alignment = TextAnchor.MiddleCenter;
            if (EditorGUIUtility.isProSkin) {
                guiStyleBoldCenter.normal.textColor = EditorColorProSkin.GetDarkSkinTextColor();
            }

            EditorGUI.indentLevel = 1;

            EditorGuiFunction.DrawObjectNameBox((UnityEngine.Object)mTarget, NameOf.SoundDataGroup, EditorTextSoundDataGroup.soundDataGroupTooltip, true);
            EditorGuiAssetTopToolbarButtons.DrawTopButton(serializedObject);
            EditorTrial.InfoText();
            EditorGUILayout.Separator();

            // Notes
            EditorGUI.indentLevel = 0;
            Color previousColor = GUI.color;
            if (string.IsNullOrEmpty(notes.stringValue) || notes.stringValue == "Notes") {
                // Make less transparent if empty or default text
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
            }
            BeginChange();
            notes.stringValue = EditorGUILayout.TextArea(notes.stringValue);
            EndChange();
            GUI.color = previousColor;
            EditorGUILayout.Separator();

            // SoundDataGroup Child Array
            StartBackgroundColor(EditorColor.GetSettings(EditorColorProSkin.GetCustomEditorBackgroundAlpha()));

            EditorGUI.indentLevel = 0;
            EditorGUILayout.LabelField(new GUIContent(EditorTextSoundDataGroup.childSoundDataGroupsLabel, EditorTextSoundDataGroup.childSoundDataGroupsTooltip), EditorStyles.boldLabel);
            
            int lowestSoundDataGroupArrayLength = int.MaxValue;
            for (int n = 0; n < mTargets.Length; n++) {
                if (lowestSoundDataGroupArrayLength > mTargets[n].internals.soundDataGroupChildren.Length) {
                    lowestSoundDataGroupArrayLength = mTargets[n].internals.soundDataGroupChildren.Length;
                }
            }
            EditorGUI.indentLevel = 1;
            EditorGuiFunction.DrawReordableArray(soundDataGroupChildren, serializedObject, lowestSoundDataGroupArrayLength, false);

            EditorGUILayout.Separator();

            // Check for Infinite Loop
            // So that if soundDataGroupChildren is resized it wont get error when checking
            if (ShouldDebug.GuiWarnings()) {
                if (Event.current.type != EventType.DragPerform) {
                    if (mTarget.internals.GetIfInfiniteLoop(mTarget, out SoundDataGroupBase infiniteInstigator, out SoundDataGroupBase infinitePrevious)) {
                        EditorGUILayout.HelpBox($"\"{infiniteInstigator.internals.cachedName}\" in \"{infinitePrevious.internals.cachedName}\" creates an infinite loop", MessageType.Error);
                        EditorGUILayout.Separator();
                    }
                }
            }

            EditorGUI.indentLevel = 0;

            // Add All in Project
            EditorGUILayout.BeginHorizontal();
            //// For offsetting the buttons to the right
            //EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));
            BeginChange();
            if (GUILayout.Button(new GUIContent("Add All in Project", ""))) {
                for (int i = 0; i < mTargets.Length; i++) {
                    SoundDataGroupBase target = mTargets[i];
                    Undo.RecordObject(target, "Add All in Project");
                    target.internals.soundEvents = FindAllAssetsOfTypeWholeProject<SoundEventBase>().ToList();
                    target.internals.soundContainers = FindAllAssetsOfTypeWholeProject<SoundContainerBase>().ToList();
                    target.internals.audioClips = FindAllAssetsOfTypeWholeProject<AudioClip>().ToList();
                    // Orders by name
                    target.internals.soundEvents = target.internals.soundEvents.OrderBy(p => p.internals.cachedName).ToList();
                    target.internals.soundContainers = target.internals.soundContainers.OrderBy(p => p.internals.cachedName).ToList();
                    target.internals.audioClips = target.internals.audioClips.OrderBy(p => p.name).ToList();
                    // Expands the list if its not too long
                    // SoundEvent
                    bool expandListSoundEvent = true;
                    if (target.internals.soundEvents.Count > 20) {
                        expandListSoundEvent = false;
                    }
                    target.internals.soundEventsExpanded = expandListSoundEvent;
                    // SoundContainer
                    bool expandListSoundContainer = true;
                    if (target.internals.soundContainers.Count > 20) {
                        expandListSoundContainer = false;
                    }
                    target.internals.soundContainersExpanded = expandListSoundContainer;
                    // AudioClip
                    bool expandListAudioClip = true;
                    if (target.internals.audioClips.Count > 20) {
                        expandListAudioClip = false;
                    }
                    target.internals.audioClipsExpanded = expandListAudioClip;
                    EditorUtility.SetDirty(target);
                }
            }
            EndChange();
            // Remove All
            BeginChange();
            if (GUILayout.Button(new GUIContent("Remove All", ""))) {
                for (int i = 0; i < mTargets.Length; i++) {
                    SoundDataGroupBase target = mTargets[i];
                    Undo.RecordObject(target, "Remove All");
                    // Dont remove Child SoundDataGroups
                    target.internals.soundEvents = new List<SoundEventBase>(1);
                    target.internals.soundContainers = new List<SoundContainerBase>(1);
                    target.internals.audioClips = new List<AudioClip>(1);
                    target.internals.soundEventsExpanded = true;
                    target.internals.soundContainersExpanded = true;
                    target.internals.audioClipsExpanded = true;
                    EditorUtility.SetDirty(target);
                }
            }
            EndChange();
            EditorGUILayout.EndHorizontal();

            // Multiple Object Types Drag and Drop Area
            EditorDragAndDropArea.DrawDragAndDropAreaCustomEditor<UnityEngine.Object>(new EditorDragAndDropArea.DragAndDropAreaInfo($"Multiple Type"), DragAndDropCallbackMultipleTypes, true);

            // SoundEvents ////////////////////////////////////////////////////////////////////////////

            // SoundEvent Array
            int lowestSoundEventArrayLength = int.MaxValue;
            for (int n = 0; n < mTargets.Length; n++) {
                if (lowestSoundEventArrayLength > mTargets[n].internals.soundEvents.Count) {
                    lowestSoundEventArrayLength = mTargets[n].internals.soundEvents.Count;
                }
            }
            EditorGUI.indentLevel = 1;

            EditorGuiFunction.DrawReordableArray(
                soundEvents, serializedObject, lowestSoundEventArrayLength, false, 
                true, true, EditorGuiFunction.EditorGuiTypeIs.Property, 0, null, true, "", false, 
                true, soundEventsExpanded, EditorTextSoundDataGroup.soundEventsLabel, EditorTextSoundDataGroup.soundEventsTooltip, true
                );

            // SoundEvent Drag and Drop Area
            EditorDragAndDropArea.DrawDragAndDropAreaCustomEditor<SoundEventBase>(new EditorDragAndDropArea.DragAndDropAreaInfo($"{NameOf.SoundEvent}"), DragAndDropCallbackSoundEvent, true);

            // SoundContainers ////////////////////////////////////////////////////////////////////////////

            // SoundContainer Array
            int lowestSoundContainerArrayLength = int.MaxValue;
            for (int n = 0; n < mTargets.Length; n++) {
                if (lowestSoundContainerArrayLength > mTargets[n].internals.soundContainers.Count) {
                    lowestSoundContainerArrayLength = mTargets[n].internals.soundContainers.Count;
                }
            }
            EditorGUI.indentLevel = 1;

            EditorGuiFunction.DrawReordableArray(
                soundContainers, serializedObject, lowestSoundContainerArrayLength, false,
                true, true, EditorGuiFunction.EditorGuiTypeIs.Property, 0, null, true, "", false,
                true, soundContainersExpanded, "SoundContainers", "", true
                );

            // SoundContainer Drag and Drop Area
            EditorDragAndDropArea.DrawDragAndDropAreaCustomEditor<SoundContainerBase>(new EditorDragAndDropArea.DragAndDropAreaInfo($"{NameOf.SoundContainer}"), DragAndDropCallbackSoundContainer, true);

            // AudioClips ////////////////////////////////////////////////////////////////////////////

            // AudioClip Array
            int lowestAudioClipArrayLength = int.MaxValue;
            for (int n = 0; n < mTargets.Length; n++) {
                if (lowestAudioClipArrayLength > mTargets[n].internals.audioClips.Count) {
                    lowestAudioClipArrayLength = mTargets[n].internals.audioClips.Count;
                }
            }
            EditorGUI.indentLevel = 1;

            EditorGuiFunction.DrawReordableArray(
                audioClips, serializedObject, lowestAudioClipArrayLength, false,
                true, true, EditorGuiFunction.EditorGuiTypeIs.Property, 0, null, true, "", false,
                true, audioClipsExpanded, "AudioClips", "", true
                );

            // AudioClips Drag and Drop Area
            EditorDragAndDropArea.DrawDragAndDropAreaCustomEditor<AudioClip>(new EditorDragAndDropArea.DragAndDropAreaInfo($"AudioClip"), DragAndDropCallbackAudioClip, true);

            // End //////////////////////////////

            StopBackgroundColor();
            EditorGUILayout.Separator();

            EditorGUI.indentLevel = 0;
            // Transparent background so the offset will be right
            StartBackgroundColor(new Color(0f, 0f, 0f, 0f));
            EditorGUILayout.BeginHorizontal();
            // For offsetting the buttons to the right
            EditorGUILayout.LabelField(new GUIContent(""), GUILayout.Width(EditorGUIUtility.labelWidth));

            // Reset
            BeginChange();
            if (GUILayout.Button("Reset All")) {
                for (int i = 0; i < mTargets.Length; i++) {
                    Undo.RecordObject(mTargets[i], "Reset All");
                    mTargets[i].internals.soundDataGroupChildren = new SoundDataGroupBase[0];
                    mTargets[i].internals.soundEvents = new List<SoundEventBase>(1);
                    mTargets[i].internals.soundContainers = new List<SoundContainerBase>(1);
                    mTargets[i].internals.audioClips = new List<AudioClip>(1);
                    mTargets[i].internals.soundEventsExpanded = true;
                    mTargets[i].internals.soundContainersExpanded = true;
                    mTargets[i].internals.audioClipsExpanded = true;
                    EditorUtility.SetDirty(mTargets[i]);
                }
            }
            EndChange();
            EditorGUILayout.EndHorizontal();
            StopBackgroundColor();

            // Sonity GUID
            // Transparent background so the offset will be right
            StartBackgroundColor(new Color(0f, 0f, 0f, 0f));
            BeginChange();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(sonityGuidEditor, new GUIContent(EditorTextAssetGuid.sonityGuidLabel, EditorTextAssetGuid.sonityGuidTooltip));
            EditorGUI.EndDisabledGroup();
            EndChange();
            StopBackgroundColor();
        }
    }
}
#endif