// MIT License for Dependencies Hunter
// From https://github.com/AlexeyPerov/Unity-Dependencies-Hunter
// Copyright (c) 2021 Alexey Perov
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is furnished
// to do so, subject to the following conditions: The above copyright notice and
// this permission notice shall be included in all copies or substantial portions
// of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

// Toolbar Actions
// Tools/Sonity Tools 🛠/Reference Finder 🔍/Open Reference Finder Window
// Tools/Sonity Tools 🛠/Reference Finder 🔍/Find References for Selected Assets
// Assets/Sonity Tools 🛠/Reference Finder 🔍/Open Reference Finder Window
// Assets/Sonity Tools 🛠/Reference Finder 🔍/Find References for Selected Assets

// Default Shortcuts
// Ctrl+Shift+Alt+F - Reference Finder 🔍/Find References for Selected Assets

// Sonity Version 1.5 (By Victor Engström)
// Added clear project search windows text bar on select assets

// Sonity Version 1.4 (By Victor Engström)
// Improved UI

// Sonity Version 1.3 (By Jesper Söderlind)
// Added caching

// Sonity Version 1.2 (By Victor Engström)
// Added support for Live editing
// Added buttons for selecting all and hiding empty
// Sorted selected object and references names
// Made so buttons you've clicked becomes greyed out
// Added copy references list to clipboard
// More compact viewing with less whitespace
// Added folder paths info

// Sonity Version 1.1 (By Victor Engström)
// Fixed menu links & "New code"
// Added "Select all Unused Assets" button
// Added CancelableProgressBar
// Commented out Debug "Total Assets Count"
// Rounded search time to seconds with one decimal
// Fixed debugs
// Added label for find all references window

// Sonity Version 1.0 (By Alexey Perov)
// Original version by Alexey Perov

#if UNITY_EDITOR
#if SONITY_ENABLE_EDITOR_TOOL_REFERENCE_FINDER

// Uncomment this line to use addressables (also needs to have "Unity.Addressables.Editor" added to the assembly definition!)
// #define REFERENCE_FINDER_USE_ADDRESSABLES

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
//using Unity.VisualScripting;
using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif
#if REFERENCE_FINDER_USE_ADDRESSABLES
using UnityEditor.AddressableAssets;
#endif
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

// ReSharper disable once CheckNamespace
namespace Sonity.Internal.EditorTools {

    public static class EditorToolReferenceFinderMenuPath {

        // Shortcut key modifiers
		// Needs "_" eg: _S for single key shortcut
        // % -> Ctrl on Windows, Linux, CMD on MacOS
        // ^ -> Ctrl on Windows, Linux, MacOS
        // # -> Shift
        // & -> Alt

        public const string toolsFindReferences = "Tools/Sonity Tools 🛠/Reference Finder 🔍/Find References for Selected Assets";
        public const string toolsFindUnusedAssets = "Tools/Sonity Tools 🛠/Reference Finder 🔍/Find Unused Assets";
        public const string assetsFindReferences = "Assets/Sonity Tools 🛠/Reference Finder 🔍/Find References for Selected Assets ^&#F";
        public const string assetsFindUnusedAssets = "Assets/Sonity Tools 🛠/Reference Finder 🔍/Find Unused Assets";

        public const int toolsMenuPriority = 100;
        public const int assetsMenuPriority = 100;
    }

    public class ReferenceFinderCache {

        public static readonly string CacheFileName = "ReferenceFinderCache.dat";
        public static readonly string CacheFolderPath = "Library/Sonity/";
        public static readonly int CurrentCacheVersion = 1; // Needed in case of future file format changes

        public static string ProjectHash;
        public static long Timestamp;
        public static string UnityVersion;
        public static string ProjectId;
        public static Dictionary<string, List<string>> AssetReferences;

        public static bool ProjectHashesMatch() {
            var currentHash = ReferenceFinderCommonUtilities.GetCurrentProjectHash();
            return ProjectHash == currentHash;
        }

        public static bool CacheExists() { return File.Exists(CacheFolderPath + CacheFileName); }

        public static bool CacheIsValid() { return ProjectHash != null && Timestamp != 0 && UnityVersion != null && ProjectId != null && AssetReferences != null; }

        public static void SetData(Dictionary<string, List<string>> assets) {
            ProjectHash = ReferenceFinderCommonUtilities.GetCurrentProjectHash();
            Timestamp = DateTime.UtcNow.Ticks;
            UnityVersion = Application.unityVersion;
            ProjectId = Application.cloudProjectId;
            AssetReferences = assets;
        }

        public static void Save() {
            if (!Directory.Exists(CacheFolderPath))
                Directory.CreateDirectory(CacheFolderPath);

            var path = CacheFolderPath + CacheFileName;

            try {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var bw = new BinaryWriter(fs)) {
                    // Write header fields
                    bw.Write(CurrentCacheVersion);
                    bw.Write(ProjectHash ?? string.Empty);
                    bw.Write(Timestamp);
                    bw.Write(UnityVersion ?? string.Empty);
                    bw.Write(ProjectId ?? string.Empty);

                    // Write dictionary
                    var dict = AssetReferences;

                    if (dict == null) {
                        bw.Write(0);
                        Debug.Log($"Reference Finder: Saved empty cache to '{path}'.");
                        return;
                    }

                    bw.Write(dict.Count);

                    foreach (var kv in dict) {
                        bw.Write(kv.Key ?? string.Empty);

                        List<string> list = kv.Value ?? new List<string>();
                        bw.Write(list.Count);

                        foreach (string s in list)
                            bw.Write(s ?? string.Empty);
                    }
                }

                // Debug.Log($"Reference Finder: Successfully saved {ReferenceCacheData.Assets?.Count ?? 0} root items to '{path}'.");
            } catch (Exception ex) {
                Debug.LogError($"Reference Finder: Failed to save cache to '{path}'. Exception:\n{ex}");
            }
        }


        public static void Load() {
            var path = CacheFolderPath + CacheFileName;

            try {

                if (!File.Exists(path)) {
                    Debug.LogWarning($"Reference Finder: Cache file not found: '{path}'.");
                    AssetReferences = new Dictionary<string, List<string>>();
                    return;
                }

                bool cacheVersionMismatch = false;

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs)) {
                    var cacheVersion = br.ReadInt32();

                    if (cacheVersion == CurrentCacheVersion) {
                        ProjectHash = br.ReadString();
                        Timestamp = br.ReadInt64();
                        UnityVersion = br.ReadString();
                        ProjectId = br.ReadString();

                        int dictCount = br.ReadInt32();
                        var dict = new Dictionary<string, List<string>>(dictCount);

                        for (int i = 0; i < dictCount; i++) {
                            string key = br.ReadString();

                            int listCount = br.ReadInt32();
                            var list = new List<string>(listCount);

                            for (int j = 0; j < listCount; j++)
                                list.Add(br.ReadString());

                            dict[key] = list;
                        }

                        AssetReferences = dict;

                        // Debug.Log($"Reference Finder: Read {dictCount} items from cache at '{path}'.");
                    } else {
                        cacheVersionMismatch = true;
                    }
                }

                if (cacheVersionMismatch) {
                    Debug.LogWarning($"Reference Finder: Cache version mismatch. Expected: {CurrentCacheVersion}, Found: {ReferenceFinderCache.CurrentCacheVersion}. Please rebuild the cache.");
                    Clear();
                }

            } catch (Exception ex) {
                Debug.LogError($"Reference Finder: Failed to load cache from '{path}'. Exception:\n{ex}");

                // Failsafe: initialize empty dictionary so the system keeps working
                AssetReferences = new Dictionary<string, List<string>>();
            }
        }

        public static void Rebuild() {
            Dictionary<string, List<string>> cachedAssets;
            ReferencesMapUtilities.FillReverseReferencesMap(out cachedAssets);
            ReferenceFinderCache.SetData(cachedAssets);
            ReferenceFinderCache.Save();
            EditorUtility.ClearProgressBar();
        }

        public static void Clear() {
            File.Delete(CacheFolderPath + CacheFileName);
            ProjectHash = null;
            Timestamp = 0;
            UnityVersion = null;
            ProjectId = null;
            AssetReferences = null;
        }

        public static void GetAgeOutOfDateTime(out int days, out int hours) {
            days = 0;
            hours = 0;
            if (Timestamp == 0)
                return;
            var cacheDateTime = new DateTime(Timestamp, DateTimeKind.Utc);
            var timeSpan = DateTime.UtcNow - cacheDateTime;
            days = (int)timeSpan.TotalDays;
            hours = timeSpan.Hours;
        }

        public static string GetAgeStringFromDateTime() {
            if (Timestamp == 0)
                return "N/A";
            var cacheDateTime = new DateTime(Timestamp, DateTimeKind.Utc);
            var timeSpan = DateTime.UtcNow - cacheDateTime;
            int days = (int)timeSpan.TotalDays;
            int hours = timeSpan.Hours;
            int minutes = timeSpan.Minutes;
            if (days == 0) {
                if (hours == 0) {
                    return $"{minutes}m";
                } else {
                    return $"{hours}h";
                }
            } else {
                if (days == 1) {
                    return $"{days} day";
                } else {
                    return $"{days} days";
                }
            }
        }
    }

    public class ReferenceFinderUnusedAssets : EditorWindow {

        [MenuItem(EditorToolReferenceFinderMenuPath.toolsFindUnusedAssets, false, EditorToolReferenceFinderMenuPath.toolsMenuPriority)]
        public static void LaunchUnusedAssetsWindowTools() {
            var window = GetWindow<ReferenceFinderUnusedAssets>();
            window.Start();
        }

        [MenuItem(EditorToolReferenceFinderMenuPath.assetsFindUnusedAssets, false, EditorToolReferenceFinderMenuPath.assetsMenuPriority)]
        public static void LaunchUnusedAssetsWindowAssets() {
            var window = GetWindow<ReferenceFinderUnusedAssets>();
            window.Start();
        }

        private class Result {
            public List<AssetData> Assets { get; } = new List<AssetData>();
            public Dictionary<string, int> RefsByTypes { get; } = new Dictionary<string, int>();
            public string OutputDescription { get; set; }
        }

        public int DeleteUnusedAssets(List<AssetData> assets) {
            AssetDatabase.StartAssetEditing(); // New code
            int deletedAssetCount = 0;
            foreach (AssetData resultAsset in assets) {
                bool hasDeletedAsset = AssetDatabase.DeleteAsset($"Assets/{resultAsset.ShortPath}");
                if (hasDeletedAsset) {
                    ReferenceFinderCache.AssetReferences.Remove(resultAsset.Path);
                    deletedAssetCount += 1;
                }
            }
            AssetDatabase.StopAssetEditing(); // New code
            AssetDatabase.SaveAssets(); // New code
            AssetDatabase.Refresh(); // New code
            if (deletedAssetCount > 0) {
                ReferenceFinderCache.Save();
            }
            return deletedAssetCount;
        }

        private class AnalysisSettings {
            // ReSharper disable once StringLiteralTypo
            public readonly List<string> DefaultIgnorePatterns = new List<string>
            {
                @"/Resources/",
                @"/Editor/",
                @"/Editor Default Resources/",
                @"/ThirdParty/",
                @"ProjectSettings/",
                @"Packages/",
                @"\.asmdef$",
                @"link\.xml$",
                @"\.csv$",
                @"\.md$",
                @"\.json$",
                @"\.xml$",
                @"\.txt$"
            };
            // ReSharper disable once InconsistentNaming
            public const string PATTERNS_PREFS_KEY = "ReferenceFinderIgnorePatterns";
            public List<string> IgnoredPatterns { get; set; }
            public bool FindUnreferencedOnly { get; set; } = true;
        }

        private class OutputSettings {
            public const int PageSize = 50;
            public int? PageToShow { get; set; }
            public string PathFilter { get; set; }
            public string TypeFilter { get; set; }
            // ReSharper disable once IdentifierTypo
            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            public bool ShowAddressables { get; set; }
            public bool ShowUnreferencedOnly { get; set; }
            public bool ShowAssetsWithWarningsOnly { get; set; }

            /// <summary>
            /// Sorting types.
            /// By type: 0: A-Z, 1: Z-A
            /// By path: 2: A-Z, 3: Z-A
            /// By size: 4: A-Z, 5: Z-A
            ///  
            /// </summary>
            public int SortType { get; set; }
        }

        private ProjectAssetsAnalysisUtilities _service;

        private Result _result;
        private OutputSettings _outputSettings;
        private AnalysisSettings _analysisSettings;

        private Vector2 _pagesScroll = Vector2.zero;
        private Vector2 _typesScroll = Vector2.zero;
        private Vector2 _assetsScroll = Vector2.zero;
        private bool _analysisSettingsFoldout;

        private void PopulateUnusedAssetsList() {
            _result = new Result();
            _outputSettings = new OutputSettings();

            // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
            if (_service == null) {
                _service = new ProjectAssetsAnalysisUtilities();
            }

            Clear();

            if (!_analysisSettings.FindUnreferencedOnly) {
                _outputSettings.ShowUnreferencedOnly = false;
            }

            _outputSettings.PageToShow = 0;

            Show();

            EditorUtility.ClearProgressBar();

            var filteredOutput = new StringBuilder();
            filteredOutput.AppendLine("Assets ignored by pattern:");

            var count = 0;
            foreach (var mapElement in ReferenceFinderCache.AssetReferences) {
                EditorUtility.DisplayProgressBar("Unreferenced Assets", "Searching for unreferenced assets",
                    (float)count / ReferenceFinderCache.AssetReferences.Count);
                count++;

                var warning = string.Empty;
                var referencesCount = mapElement.Value.Count;

                if (referencesCount == 1) {
                    var type = AssetDatabase.GetMainAssetTypeAtPath(mapElement.Key);
                    if (type == typeof(Texture2D)) {
                        var reference = mapElement.Value[0];
                        var referenceType = AssetDatabase.GetMainAssetTypeAtPath(reference);
                        if (referenceType == typeof(SpriteAtlas)) {
                            warning = $"Sprite references only its atlas {reference}";
                            referencesCount = 0;
                        }
                    }
                }

                if (_analysisSettings.FindUnreferencedOnly && referencesCount != 0)
                    continue;

                var validForOutput = ProjectAssetsAnalysisUtilities.IsValidForOutput(mapElement.Key,
                    _analysisSettings.IgnoredPatterns);
                var validAssetType = _service.IsValidAssetType(mapElement.Key, validForOutput);

                if (!validAssetType)
                    continue;

                if (validForOutput) {
                    _result.Assets.Add(AssetData.Create(mapElement.Key, referencesCount, warning));
                } else {
                    filteredOutput.AppendLine(mapElement.Key);
                }
            }

            var types = _result.Assets.Select(x => x.TypeName);

            foreach (var type in types) {
                _result.RefsByTypes[type] = _result.Assets.Count(x => x.TypeName == type);
            }

#if REFERENCE_FINDER_USE_ADDRESSABLES
            if (_analysisSettings.FindUnreferencedOnly)
            {
                var addressablesCount = _result.Assets.Count(x => x.IsAddressable);

                var nonAddressablesCount = _result.Assets.Count - addressablesCount;
                _result.OutputDescription = $"Result. Unreferenced Assets: Total = {_result.Assets.Count} Addressables = {addressablesCount} Common = {nonAddressablesCount}";
            }
            else
            {
                var unreferencedTotalCount = _result.Assets.Count(x => x.ReferencesCount == 0);
                
                var unreferencedAddressablesCount = _result.Assets.Count(x => 
                    x.IsAddressable && x.ReferencesCount == 0);

                var unreferencedCommonCount = unreferencedTotalCount - unreferencedAddressablesCount;
                
                _result.OutputDescription = $"Result. Assets: Total = " +
                    $"{_result.Assets.Count} Unreferenced = {unreferencedTotalCount} Unreferenced Addressables = {unreferencedAddressablesCount} Unreferenced Common = {unreferencedCommonCount}";
            }
#else
            if (_analysisSettings.FindUnreferencedOnly) {
                _result.OutputDescription = $"Found {_result.Assets.Count} unreferenced assets.";
            } else {
                var unreferencedTotalCount = _result.Assets.Count(x => x.ReferencesCount == 0);
                _result.OutputDescription = $"Found {_result.Assets.Count} total assets. {unreferencedTotalCount} unreferenced assets.";
            }
#endif
            SortByPath();
            EditorUtility.ClearProgressBar();
            Debug.Log($"Reference Finder: {_result.OutputDescription}\n{filteredOutput.ToString()}");
            filteredOutput.Clear();
        }

        private static void Clear() {
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        private void Start() {
            // This is required for correctly formatting date/time according to the computers actual locale
            // Unity is weird
            var culture = new CultureInfo("");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            if (!ReferenceFinderCache.CacheIsValid()) {
                if (ReferenceFinderCache.CacheExists()) {
                    ReferenceFinderCache.Load();
                }
            }
        }

        private void OnGUI() {
            GUIUtilities.HorizontalLine();

            // Top info and buttons
            GUILayout.BeginHorizontal();

            string cacheInfo = "Please Build Cache";
            if (ReferenceFinderCache.AssetReferences != null) {
                int cacheRefs = ReferenceFinderCache.AssetReferences.Count;
                string cacheReferencesText = cacheRefs.ToString();
                if (cacheRefs > 1000000) {
                    // Millions
                    cacheRefs /= 1000000;
                    cacheReferencesText = $"{cacheRefs.ToString()}m";
                } else if (cacheRefs > 1000) {
                    // Thousands
                    cacheRefs /= 1000;
                    cacheReferencesText = $"{cacheRefs.ToString()}k";
                }
                cacheInfo = $"Reference Finder - Cache Age: {ReferenceFinderCache.GetAgeStringFromDateTime()}, Refs: {cacheReferencesText}";
            }

            GUILayout.Label(cacheInfo);

            if (!ReferenceFinderCache.CacheExists()) {
                if (GUILayout.Button("Build Cache", GUILayout.Width(80f))) {
                    ReferenceFinderCache.Rebuild();
                    Repaint();
                }
            }
            GUILayout.EndHorizontal();

            if (!ReferenceFinderCache.CacheIsValid()) {
                return;
            }

            if (ReferenceFinderCache.AssetReferences.Count == 0) {
                EditorGUILayout.LabelField("No assets found in cache. Please rebuild the cache.");
                return;
            }

            GUIUtilities.HorizontalLine();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal(); ;

            GUILayout.Label("Reference Finder - Find All Project Assets References");

            var prevColor = GUI.color;
            GUI.color = Color.green;
            if (GUILayout.Button("Run Analysis", GUILayout.Width(300f))) {
                PopulateUnusedAssetsList();
            }
            GUILayout.EndHorizontal();
            GUI.color = prevColor;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUIUtilities.HorizontalLine();

            OnAnalysisSettingsGUI();

            GUIUtilities.HorizontalLine();

            if (_result == null) {
                return;
            }
            if (_result.Assets.Count == 0) {
                EditorGUILayout.LabelField("No unreferenced found");
                return;
            }
            var filteredAssets = _result.Assets;
            if (!string.IsNullOrEmpty(_outputSettings.PathFilter)) {
                filteredAssets = filteredAssets.Where(x => x.Path.Contains(_outputSettings.PathFilter)).ToList();
            }
            if (!_outputSettings.ShowAddressables) {
                filteredAssets = filteredAssets.Where(x => !x.IsAddressable).ToList();
            }
            if (!string.IsNullOrEmpty(_outputSettings.TypeFilter)) {
                filteredAssets = filteredAssets.Where(x => x.TypeName == _outputSettings.TypeFilter).ToList();
            }
            if (_outputSettings.ShowAssetsWithWarningsOnly) {
                filteredAssets = filteredAssets.Where(x => !string.IsNullOrEmpty(x.Warning)).ToList();
            }
            if (!_analysisSettings.FindUnreferencedOnly && _outputSettings.ShowUnreferencedOnly) {
                filteredAssets = filteredAssets.Where(x => x.ReferencesCount == 0).ToList();
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_result.OutputDescription);

            if (filteredAssets.Count < 1000) {
                if (GUILayout.Button("Save to Clipboard", GUILayout.Width(250f))) {
                    StringBuilder toClipboard = new StringBuilder();

                    toClipboard.AppendLine($"Unreferenced Assets [{filteredAssets.Count}]:");

                    foreach (var asset in filteredAssets) {
                        toClipboard.AppendLine($"[{asset.TypeName}][{asset.ReadableSize}] {asset.Path}");
                    }

                    EditorGUIUtility.systemCopyBuffer = toClipboard.ToString();
                }
            }

            if (filteredAssets.Count > 0) {
                if (GUILayout.Button("Delete unused assets", GUILayout.Width(250f))) {
                    int deletedCount = DeleteUnusedAssets(filteredAssets);
                    Debug.Log($"Reference Finder: Deleted {deletedCount} assets");
                    EditorUtility.DisplayDialog("Reference Finder", $"Deleted {deletedCount} assets", "Ok");
                }

                if (GUILayout.Button("Select all unused assets", GUILayout.Width(250f))) {
                    UnityEngine.Object[] unityEngineObjectArray = new UnityEngine.Object[filteredAssets.Count];
                    for (int i = 0; i < filteredAssets.Count; i++) {
                        unityEngineObjectArray[i] = AssetDatabase.LoadAssetAtPath(filteredAssets[i].Path, typeof(UnityEngine.Object));
                    }
                    ReferenceFinderCommonUtilities.EditorClearProjectSearchText();
                    Selection.objects = unityEngineObjectArray;
                    Debug.Log($"Reference Finder: Selected all unused assets");
                    EditorUtility.DisplayDialog("Reference Finder", $"Select all unused assets", "Ok");
                }
            }
            EditorGUILayout.EndHorizontal();

            _pagesScroll = EditorGUILayout.BeginScrollView(_pagesScroll);

            EditorGUILayout.BeginHorizontal();

            prevColor = GUI.color;
            GUI.color = !_outputSettings.PageToShow.HasValue ? Color.yellow : Color.white;

            if (GUILayout.Button("All", GUILayout.Width(30f))) {
                _outputSettings.PageToShow = null;
            }

            GUI.color = prevColor;

            var totalCount = filteredAssets.Count;
            var pagesCount = totalCount / OutputSettings.PageSize + (totalCount % OutputSettings.PageSize > 0 ? 1 : 0);

            for (var i = 0; i < pagesCount; i++) {
                prevColor = GUI.color;
                GUI.color = _outputSettings.PageToShow == i ? Color.yellow : Color.white;

                if (GUILayout.Button((i + 1).ToString(), GUILayout.Width(30f))) {
                    _outputSettings.PageToShow = i;
                }

                GUI.color = prevColor;
            }

            if (_outputSettings.PageToShow.HasValue && _outputSettings.PageToShow > pagesCount - 1) {
                _outputSettings.PageToShow = pagesCount - 1;
            }

            if (_outputSettings.PageToShow.HasValue && pagesCount == 0) {
                _outputSettings.PageToShow = null;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();

            GUIUtilities.HorizontalLine();

            EditorGUILayout.BeginHorizontal();

            var textFieldStyle = EditorStyles.textField;
            var prevTextFieldAlignment = textFieldStyle.alignment;
            textFieldStyle.alignment = TextAnchor.MiddleCenter;

            _outputSettings.PathFilter = EditorGUILayout.TextField("Path Contains:",
                _outputSettings.PathFilter, GUILayout.Width(400f));

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();

#if REFERENCE_FINDER_USE_ADDRESSABLES
            _outputSettings.ShowAddressables = EditorGUILayout.Toggle("Show Addressables:", 
                _outputSettings.ShowAddressables);
#endif

            if (!_analysisSettings.FindUnreferencedOnly) {
                _outputSettings.ShowUnreferencedOnly = EditorGUILayout.Toggle("Unreferenced Only:",
                    _outputSettings.ShowUnreferencedOnly);
            }

            _outputSettings.ShowAssetsWithWarningsOnly = EditorGUILayout.Toggle("Implicitly Unused Only",
                _outputSettings.ShowAssetsWithWarningsOnly);

            textFieldStyle.alignment = prevTextFieldAlignment;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();

            prevColor = GUI.color;

            var sortType = _outputSettings.SortType;

            GUI.color = sortType == 0 || sortType == 1 ? Color.yellow : Color.white;
            var orderType = sortType == 1 ? "Z-A" : "A-Z";
            if (GUILayout.Button($"Sort by type {orderType}", GUILayout.Width(150f))) {
                SortByType();
            }

            GUI.color = sortType == 2 || sortType == 3 ? Color.yellow : Color.white;
            orderType = sortType == 3 ? "Z-A" : "A-Z";
            if (GUILayout.Button($"Sort by path {orderType}", GUILayout.Width(150f))) {
                SortByPath();
            }

            GUI.color = sortType == 4 || sortType == 5 ? Color.yellow : Color.white;
            orderType = sortType == 5 ? "Z-A" : "A-Z";
            if (GUILayout.Button($"Sort by size {orderType}", GUILayout.Width(150f))) {
                SortBySize();
            }

            GUI.color = prevColor;

            EditorGUILayout.EndHorizontal();

            GUIUtilities.HorizontalLine();

            _typesScroll = EditorGUILayout.BeginScrollView(_typesScroll);

            EditorGUILayout.BeginHorizontal();

            prevColor = GUI.color;
            GUI.color = string.IsNullOrEmpty(_outputSettings.TypeFilter) ? Color.yellow : Color.white;

            if (GUILayout.Button("All Types", GUILayout.Width(100f))) {
                _outputSettings.TypeFilter = string.Empty;
            }

            var prevAlignment = GUI.skin.button.alignment;
            GUI.skin.button.alignment = TextAnchor.MiddleLeft;

            foreach (var typeInfo in _result.RefsByTypes) {
                GUI.color = _outputSettings.TypeFilter == typeInfo.Key ? Color.yellow : Color.white;

                var typeName = typeInfo.Key;
                var dotIndex = typeName.LastIndexOf(".", StringComparison.Ordinal);

                if (dotIndex != -1 && dotIndex + 1 < typeName.Length - 3) {
                    typeName = typeName.Substring(dotIndex + 1);
                }

                if (GUILayout.Button($"[{typeInfo.Value}] {typeName}", GUILayout.Width(150f))) {
                    _outputSettings.TypeFilter = typeInfo.Key;
                }
            }

            GUI.skin.button.alignment = prevAlignment;
            GUI.color = prevColor;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();

            GUIUtilities.HorizontalLine();

            _assetsScroll = GUILayout.BeginScrollView(_assetsScroll);

            EditorGUILayout.BeginVertical();

            for (var i = 0; i < filteredAssets.Count; i++) {
                if (_outputSettings.PageToShow.HasValue) {
                    var page = _outputSettings.PageToShow.Value;
                    if (i < page * OutputSettings.PageSize || i >= (page + 1) * OutputSettings.PageSize) {
                        continue;
                    }
                }

                var asset = filteredAssets[i];
                EditorGUILayout.BeginHorizontal();

                prevColor = GUI.color;

                var color = Color.white;
                if (!asset.ValidType) {
                    color = Color.red;
                } else if (!string.IsNullOrEmpty(asset.Warning)) {
                    color = Color.yellow;
                }

                GUI.color = color;

                if (string.IsNullOrEmpty(asset.Warning)) {
                    EditorGUILayout.LabelField(i.ToString(), GUILayout.Width(40f));
                } else {
                    asset.Foldout = EditorGUILayout.Foldout(asset.Foldout, $"{i} (i)");
                }

                EditorGUILayout.LabelField(asset.TypeName, GUILayout.Width(75f));
                GUI.color = prevColor;

                if (asset.ValidType) {
                    var guiContent = EditorGUIUtility.ObjectContent(null, asset.Type);
                    guiContent.text = Path.GetFileName(asset.Path);

                    var alignment = GUI.skin.button.alignment;
                    GUI.skin.button.alignment = TextAnchor.MiddleLeft;

                    if (GUILayout.Button(guiContent, GUILayout.Width(300f), GUILayout.Height(18f))) {
                        ReferenceFinderCommonUtilities.EditorClearProjectSearchText();
                        Selection.objects = new[] { AssetDatabase.LoadMainAssetAtPath(asset.Path) };
                    }

                    GUI.skin.button.alignment = alignment;
                }

                EditorGUILayout.LabelField(asset.ReadableSize, GUILayout.Width(70f));

#if REFERENCE_FINDER_USE_ADDRESSABLES
                if (_outputSettings.ShowAddressables)
                {
                    EditorGUILayout.LabelField(asset.IsAddressable ? "Addressable" : string.Empty,
                        GUILayout.Width(70f));
                }
#endif

                prevColor = GUI.color;

                GUI.color = asset.ReferencesCount > 0 ? Color.white : Color.yellow;

                EditorGUILayout.LabelField($"Refs:{asset.ReferencesCount}",
                    GUILayout.Width(70f));

                GUI.color = prevColor;

                EditorGUILayout.LabelField(asset.ShortPath);

                EditorGUILayout.EndHorizontal();

                if (asset.Foldout) {
                    EditorGUILayout.LabelField($"[{asset.Warning}]");
                }
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void OnAnalysisSettingsGUI() {
            EnsurePatternsLoaded();

            _analysisSettingsFoldout = EditorGUILayout.Foldout(_analysisSettingsFoldout,
                $"Analysis Settings. Patterns Ignored in Output: {_analysisSettings.IgnoredPatterns.Count}. "
                + (_analysisSettings.FindUnreferencedOnly ? "Listing unreferenced assets only" : "Listing all assets"));

            if (!_analysisSettingsFoldout)
                return;

            EditorGUILayout.LabelField("Any changes here will be applied to the next run", GUILayout.Width(350f));

            GUIUtilities.HorizontalLine();

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("Show Unreferenced Assets Only");
            _analysisSettings.FindUnreferencedOnly = EditorGUILayout.Toggle(string.Empty,
                _analysisSettings.FindUnreferencedOnly);
            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("* Uncheck to list all assets with their references count", GUILayout.Width(350f));

            GUIUtilities.HorizontalLine();

            var isPatternsListDirty = false;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Format: RegExp patterns");
            if (GUILayout.Button("Set Default", GUILayout.Width(300f))) {
                _analysisSettings.IgnoredPatterns = _analysisSettings.DefaultIgnorePatterns.ToList();
                isPatternsListDirty = true;
            }

            if (GUILayout.Button("Save to Clipboard")) {
                var contents = _analysisSettings.IgnoredPatterns.Aggregate("Patterns:",
                    (current, t) => $"{current}\n{t}");

                EditorGUIUtility.systemCopyBuffer = contents;
            }

            EditorGUILayout.EndHorizontal();

            var newCount = Mathf.Max(0, EditorGUILayout.IntField("Count:", _analysisSettings.IgnoredPatterns.Count));

            if (newCount != _analysisSettings.IgnoredPatterns.Count) {
                isPatternsListDirty = true;
            }

            while (newCount < _analysisSettings.IgnoredPatterns.Count) {
                _analysisSettings.IgnoredPatterns.RemoveAt(_analysisSettings.IgnoredPatterns.Count - 1);
            }

            if (newCount > _analysisSettings.IgnoredPatterns.Count) {
                for (var i = _analysisSettings.IgnoredPatterns.Count; i < newCount; i++) {
                    _analysisSettings.IgnoredPatterns.Add(EditorPrefs.GetString($"{AnalysisSettings.PATTERNS_PREFS_KEY}_{i}"));
                }
            }

            for (var i = 0; i < _analysisSettings.IgnoredPatterns.Count; i++) {
                var newValue = EditorGUILayout.TextField(_analysisSettings.IgnoredPatterns[i]);
                if (_analysisSettings.IgnoredPatterns[i] != newValue) {
                    isPatternsListDirty = true;
                    _analysisSettings.IgnoredPatterns[i] = newValue;
                }
            }

            if (isPatternsListDirty) {
                SavePatterns();
            }
        }

        private void EnsurePatternsLoaded() {
            // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
            if (_analysisSettings == null) {
                _analysisSettings = new AnalysisSettings();
            }

            if (_analysisSettings.IgnoredPatterns != null) {
                return;
            }

            var count = EditorPrefs.GetInt(AnalysisSettings.PATTERNS_PREFS_KEY, -1);

            if (count == -1) {
                _analysisSettings.IgnoredPatterns = _analysisSettings.DefaultIgnorePatterns.ToList();
            } else {
                _analysisSettings.IgnoredPatterns = new List<string>();

                for (var i = 0; i < count; i++) {
                    _analysisSettings.IgnoredPatterns.Add(EditorPrefs.GetString($"{AnalysisSettings.PATTERNS_PREFS_KEY}_{i}"));
                }
            }
        }

        private void SavePatterns() {
            EditorPrefs.SetInt(AnalysisSettings.PATTERNS_PREFS_KEY, _analysisSettings.IgnoredPatterns.Count);

            for (var i = 0; i < _analysisSettings.IgnoredPatterns.Count; i++) {
                EditorPrefs.SetString($"{AnalysisSettings.PATTERNS_PREFS_KEY}_{i}", _analysisSettings.IgnoredPatterns[i]);
            }
        }

        private void SortByType() {
            if (_outputSettings.SortType == 0) {
                _outputSettings.SortType = 1;
                _result.Assets?.Sort((a, b) =>
                    string.Compare(b.TypeName, a.TypeName, StringComparison.Ordinal));
            } else {
                _outputSettings.SortType = 0;
                _result.Assets?.Sort((a, b) =>
                    string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal));
            }
        }

        private void SortByPath() {
            if (_outputSettings.SortType == 2) {
                _outputSettings.SortType = 3;
                _result.Assets?.Sort((a, b) =>
                    string.Compare(b.Path, a.Path, StringComparison.Ordinal));
            } else {
                _outputSettings.SortType = 2;
                _result.Assets?.Sort((a, b) =>
                    string.Compare(a.Path, b.Path, StringComparison.Ordinal));
            }
        }

        private void SortBySize() {
            if (_outputSettings.SortType == 4) {
                _outputSettings.SortType = 5;
                _result.Assets?.Sort((b, a) => a.BytesSize.CompareTo(b.BytesSize));
            } else {
                _outputSettings.SortType = 4;
                _result.Assets?.Sort((a, b) => a.BytesSize.CompareTo(b.BytesSize));
            }
        }

        private void OnDestroy() {
            Clear();
        }
    }

    /// <summary>
    /// Lists all references of the selected assets.
    /// </summary>
    public class ReferenceFinderSelection : EditorWindow {

        private SelectedAssetsAnalysisUtilities analysisService;

        //private float openWindowTime = 0f;
        //private bool projectChanged = false;

        private bool hideAllWithoutReferences = false;

        private Dictionary<Object, List<string>> referenceResults;

        private Object[] selectedObjects;
        private bool[] selectedObjectsFoldouts;
        private ReferenceObjectsData[] referenceObjectsData;

        private Vector2 scrollPosition = Vector2.zero;
        private Vector2[] selectedObjectsFoldoutScrolls;

        [MenuItem(EditorToolReferenceFinderMenuPath.toolsFindReferences, false, EditorToolReferenceFinderMenuPath.toolsMenuPriority)]
        public static void FindReferencesTools() {
            var window = GetWindow<ReferenceFinderSelection>();
            window.Start();
        }

        [MenuItem(EditorToolReferenceFinderMenuPath.assetsFindReferences, false, EditorToolReferenceFinderMenuPath.assetsMenuPriority)]
        public static void FindReferencesAssets() {
            var window = GetWindow<ReferenceFinderSelection>();
            window.Start();
        }

        public class ReferenceObjectsData {

            public bool parentHighlight = false;
            public bool[] referencesHighlights;

            public float referencesFolderPathsMaxWidth = 0f;
            public string[] referencesFolderPaths;
        }

        private void Start() {

            // This is required for correctly formatting date/time according to the computers actual locale
            // Unity is weird
            var culture = new CultureInfo("");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
            if (analysisService == null) {
                analysisService = new SelectedAssetsAnalysisUtilities();
            }

            Show();

            //Single startTime = Time.realtimeSinceStartup;
            //openWindowTime = Time.realtimeSinceStartup - startTime;

            // Order the selected objects by name
            selectedObjects = Selection.objects.OrderBy(x => x.name).ToArray();

            referenceResults = analysisService.GetReferences(selectedObjects);

            EditorUtility.DisplayProgressBar("Reference Finder", "Preparing Assets", 1f);
            EditorUtility.UnloadUnusedAssetsImmediate();
            EditorUtility.ClearProgressBar();

            // Foldouts
            selectedObjectsFoldouts = new bool[selectedObjects.Length];
            if (selectedObjectsFoldouts.Length == 1) {
                selectedObjectsFoldouts[0] = true;
            }
            selectedObjectsFoldoutScrolls = new Vector2[selectedObjectsFoldouts.Length];

            // References highlight and folder paths
            referenceObjectsData = new ReferenceObjectsData[selectedObjects.Length];
            for (int i = 0; i < referenceObjectsData.Length; i++) {
                referenceObjectsData[i] = new ReferenceObjectsData();
                referenceObjectsData[i].referencesHighlights = new bool[referenceResults[selectedObjects[i]].Count];
                referenceObjectsData[i].referencesFolderPaths = new string[referenceResults[selectedObjects[i]].Count];
                // Folder paths
                for (int ii = 0; ii < referenceResults[selectedObjects[i]].Count; ii++) {
                    string pathTemp = referenceResults[selectedObjects[i]][ii];
                    // Remove filename from path
                    pathTemp = pathTemp.Replace(Path.GetFileName(pathTemp), "");
                    pathTemp = pathTemp.TrimEnd('/');
                    pathTemp = pathTemp.Replace("Assets/", "");
                    if (pathTemp.Length > 50) {
                        pathTemp = $"... {pathTemp.Substring(pathTemp.Length - 50, 50)}";
                    }
                    referenceObjectsData[i].referencesFolderPaths[ii] = pathTemp;
                    float pathTextWidth = EditorStyles.label.CalcSize(new GUIContent(pathTemp)).x;
                    // Sets the length to the longest label width found
                    if (referenceObjectsData[i].referencesFolderPathsMaxWidth < pathTextWidth) {
                        referenceObjectsData[i].referencesFolderPathsMaxWidth = pathTextWidth;
                    }
                }
            }
        }

        private void Clear() {
            selectedObjects = null;
            analysisService = null;
            EditorUtility.UnloadUnusedAssetsImmediate();
        }

        private Color guiColorDefault;

        private Color guiColorClicked;
        private float guiColorClickedLightnessOffsetProSkin = -0.25f;
        private float guiColorClickedLightnessOffsetLightSkin = -0.15f;

        private Color guiColorNoReferences;
        private float guiColorNoReferencesLightnessOffsetProSkin = -0.35f;
        private float guiColorNoReferencesLightnessOffsetLightSkin = -0.2f;

        private GUIStyle guiStyleFoldout;

        public void GUIColorStart(Color color) {
            GUI.color = color;
        }

        public void GUIColorStartClicked() {
            GUI.color = guiColorClicked;
        }

        public void GUIColorStartNoReferences() {
            GUI.color = guiColorNoReferences;
        }

        /// <summary>
        /// Changes the lightness with the a bipolar offset
        /// </summary>
        public static Color ColorChangeLightness(Color color, float lightnessOffset) {
            float colorA = color.a;
            Color.RGBToHSV(color, out float colorH, out float colorS, out float colorV);
            colorV += lightnessOffset;
            Color outputColor = Color.HSVToRGB(colorH, colorS, colorV);
            outputColor.a = colorA;
            return outputColor;
        }

        public void GUIColorStop() {
            GUI.color = guiColorDefault;
        }

        private enum ActionsMenuChoice {
            CacheBuild,
            CacheClear,
            CopyToClipboard,
            SelectSearched,
            SelectAllReferences,
            ResetHighlight,
        }

        private class ActionsMenuObject {
            public ActionsMenuChoice choice;
            public ActionsMenuObject(ActionsMenuChoice choice) {
                this.choice = choice;
            }
        }

        private void ActionsMenuDraw() {
            GenericMenu menu = new GenericMenu();

            // Tooltips dont work for menu
            string timeText = "N/A";
            if (ReferenceFinderCache.AssetReferences != null) {
                DateTime timeLocal = new DateTime(ReferenceFinderCache.Timestamp, DateTimeKind.Utc).ToLocalTime();
                timeText = timeLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            string cacheText = "No Cache Saved";
            if (ReferenceFinderCache.CacheExists()) {
                cacheText = $"Cache Built at: {timeText}";
            }
            menu.AddDisabledItem(new GUIContent(cacheText));

            int cacheRefs = ReferenceFinderCache.AssetReferences.Count;
            string cacheReferencesText = cacheRefs.ToString();
            if (cacheRefs > 1000000) {
                // Millions
                cacheRefs /= 1000000;
                cacheReferencesText = $"{cacheRefs.ToString()}m";
            } else if (cacheRefs > 1000) {
                // Thousands
                cacheRefs /= 1000;
                cacheReferencesText = $"{cacheRefs.ToString()}k";
            }
            menu.AddDisabledItem(new GUIContent($"Cache Asset Count: {cacheReferencesText}"));

            if (ReferenceFinderCache.AssetReferences != null) {
                menu.AddItem(new GUIContent("Cache Rebuild"), false, ActionsMenuCallback, new ActionsMenuObject(ActionsMenuChoice.CacheBuild));
            } else {
                menu.AddItem(new GUIContent("Cache Build"), false, ActionsMenuCallback, new ActionsMenuObject(ActionsMenuChoice.CacheBuild));
            }
            menu.AddItem(new GUIContent("Cache Clear"), false, ActionsMenuCallback, new ActionsMenuObject(ActionsMenuChoice.CacheClear));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy To Clipboard"), false, ActionsMenuCallback, new ActionsMenuObject(ActionsMenuChoice.CopyToClipboard));
            menu.AddItem(new GUIContent("Select Searched"), false, ActionsMenuCallback, new ActionsMenuObject(ActionsMenuChoice.SelectSearched));
            menu.AddItem(new GUIContent("Select All References"), false, ActionsMenuCallback, new ActionsMenuObject(ActionsMenuChoice.SelectAllReferences));
            menu.AddItem(new GUIContent("Reset Highlights"), false, ActionsMenuCallback, new ActionsMenuObject(ActionsMenuChoice.ResetHighlight));
            menu.ShowAsContext();
        }

        private void ActionsMenuCallback(object obj) {
            try {
                ActionsMenuObject menuObject = (ActionsMenuObject)obj;
                if (menuObject == null) {
                    return;
                }
                switch (menuObject.choice) {
                    case ActionsMenuChoice.CacheBuild:
                        ReferenceFinderCache.Rebuild();
                        break;
                    case ActionsMenuChoice.CacheClear:
                        ReferenceFinderCache.Clear();
                        break;
                    case ActionsMenuChoice.CopyToClipboard:
                        ActionCopyToClipboard();
                        break;
                    case ActionsMenuChoice.SelectSearched:
                        ActionSelectSearched();
                        break;
                    case ActionsMenuChoice.SelectAllReferences:
                        ActionSelectAllReferences();
                        break;
                    case ActionsMenuChoice.ResetHighlight:
                        ActionResetHighlights();
                        break;
                }
                // Cache Clear Requires Repaint
                Repaint();
            } catch {
                return;
            }
        }

        private void ActionCopyToClipboard() {
            string textDebugReferences = "Reference Finder - Found References:\n";

            for (var i = 0; i < selectedObjects.Length; i++) {
                Object selectedObject = selectedObjects[i];

                textDebugReferences += $"\n{selectedObject.name}";

                List<string> references = referenceResults[selectedObject];
                if (references.Count == 0) {
                    textDebugReferences += $"\n^ No references found";
                } else {
                    foreach (string referencePath in references) {
                        textDebugReferences += $"\n^ {Path.GetFileNameWithoutExtension(referencePath)}";
                    }
                }
                // Linebreak add
                if (i < selectedObjects.Length) {
                    textDebugReferences += "\n";
                }
            }
            Debug.Log(textDebugReferences);
            EditorGUIUtility.systemCopyBuffer = textDebugReferences;
        }

        private void ActionSelectSearched() {
            ReferenceFinderCommonUtilities.EditorClearProjectSearchText();
            Selection.objects = selectedObjects;
        }

        private void ActionSelectAllReferences() {
            List<Object> referencedObjects = new List<Object>();
            for (var i = 0; i < selectedObjects.Length; i++) {
                Object selectedObject = selectedObjects[i];
                List<string> references = referenceResults[selectedObject];
                foreach (string referencePath in references) {
                    referencedObjects.Add(AssetDatabase.LoadMainAssetAtPath(referencePath));
                }
            }
            ReferenceFinderCommonUtilities.EditorClearProjectSearchText();
            Selection.objects = referencedObjects.ToArray();
        }

        private void ActionResetHighlights() {
            for (int i = 0; i < referenceObjectsData.Length; i++) {
                referenceObjectsData[i].parentHighlight = false;
                for (int ii = 0; ii < referenceObjectsData[i].referencesHighlights.Length; ii++) {
                    referenceObjectsData[i].referencesHighlights[ii] = false;
                }
            }
        }

        private void OnGUI() {
            if (referenceResults == null) {
                return;
            }

            if (selectedObjects == null || selectedObjects.Any(selectedObject => selectedObject == null)) {
                Clear();
                return;
            }

            guiColorDefault = GUI.color;
            if (EditorGUIUtility.isProSkin) {
                guiColorClicked = ColorChangeLightness(guiColorDefault, guiColorClickedLightnessOffsetProSkin);
                guiColorNoReferences = ColorChangeLightness(guiColorDefault, guiColorNoReferencesLightnessOffsetProSkin);
            } else {
                guiColorClicked = ColorChangeLightness(guiColorDefault, guiColorClickedLightnessOffsetLightSkin);
                guiColorNoReferences = ColorChangeLightness(guiColorDefault, guiColorNoReferencesLightnessOffsetLightSkin);
            }

            GUILayout.BeginVertical();

            // Top info and buttons
            GUILayout.BeginHorizontal();

            string cacheInfo = "Please Build Cache";
            if (ReferenceFinderCache.AssetReferences != null) {
                cacheInfo = $"Reference Finder - Cache Age: {ReferenceFinderCache.GetAgeStringFromDateTime()}";
            }

            GUILayout.Label(cacheInfo);
            
            if (!ReferenceFinderCache.CacheExists()) {
                if (GUILayout.Button("Build Cache", GUILayout.Width(80f))) {
                    ReferenceFinderCache.Rebuild();
                    Repaint();
                }
            }

            if (hideAllWithoutReferences) {
                if (GUILayout.Button("Show No References", GUILayout.Width(132f))) {
                    hideAllWithoutReferences = false;
                }
            } else {
                if (GUILayout.Button("Hide No References", GUILayout.Width(127f))) {
                    hideAllWithoutReferences = true;
                }
            }

            if (GUILayout.Button("Actions Menu", GUILayout.Width(90f))) {
                ActionsMenuDraw();
            }

            GUILayout.EndHorizontal();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            guiStyleFoldout = new GUIStyle(EditorStyles.foldout);
            guiStyleFoldout.fixedWidth = 145f;

            for (var i = 0; i < selectedObjectsFoldouts.Length; i++) {
                List<string> referencePaths = referenceResults[selectedObjects[i]];
                bool anyReferencesFound = referencePaths.Count > 0;
                if (!anyReferencesFound && hideAllWithoutReferences) {
                    continue;
                }
                GUIUtilities.HorizontalLine();
                GUILayout.BeginHorizontal();

                string referencesFoundText = anyReferencesFound ? $" References: {referencePaths.Count}" : " No References Found";

                if (!anyReferencesFound) {
                    GUIColorStartNoReferences();
                }
                selectedObjectsFoldouts[i] = GUILayout.Toggle(selectedObjectsFoldouts[i], new GUIContent(referencesFoundText), guiStyleFoldout);
                if (!anyReferencesFound) {
                    GUIColorStop();
                }

                EditorGUILayout.ObjectField(selectedObjects[i], typeof(Object), true);

                bool parentColorChanged = false;
                if (referenceObjectsData[i].parentHighlight) {
                    parentColorChanged = true;
                    GUIColorStartClicked();
                }
                if (anyReferencesFound) {
                    // Selects all the references found for the specific asset
                    if (GUILayout.Button("Select References", GUILayout.Width(125f))) {
                        referenceObjectsData[i].parentHighlight = true;
                        List<Object> referencedObjects = new List<Object>();

                        foreach (string referencePath in referencePaths) {
                            referencedObjects.Add(AssetDatabase.LoadMainAssetAtPath(referencePath));
                        }
                        ReferenceFinderCommonUtilities.EditorClearProjectSearchText();
                        Selection.objects = referencedObjects.ToArray();
                    }
                } else {
                    if (GUILayout.Button("No References", GUILayout.Width(125f))) {
                        referenceObjectsData[i].parentHighlight = true;
                    }
                }
                if (parentColorChanged) {
                    GUIColorStop();
                }

                GUILayout.EndHorizontal();

                if (selectedObjectsFoldouts[i]) {

                    // Calculating the height of the scroll view to match the number of objects
                    float scrollViewHeight = 20f + 5f;
                    if (referencePaths.Count > 0) {
                        scrollViewHeight = referencePaths.Count * 20f + 5f;
                    }
                    scrollViewHeight = Mathf.Clamp(scrollViewHeight, 0f, 500f);
                    selectedObjectsFoldoutScrolls[i] = GUILayout.BeginScrollView(selectedObjectsFoldoutScrolls[i], GUILayout.Height(scrollViewHeight));

                    for (int ii = 0; ii < referencePaths.Count; ii++) {
                        string referencePath = referencePaths[ii];

                        EditorGUILayout.BeginHorizontal();

                        // Space before the object for indentation
                        GUILayout.Space(20f);

                        // Select reference object
                        bool referenceColorChanged = false;
                        if (referenceObjectsData[i].referencesHighlights[ii]) {
                            referenceColorChanged = true;
                            GUIColorStartClicked();
                        }

                        // Showing icon from asset and asset name
                        Type type = AssetDatabase.GetMainAssetTypeAtPath(referencePath);
                        GUIContent guiContent = EditorGUIUtility.ObjectContent(null, type);
                        guiContent.text = Path.GetFileName(referencePath);

                        // Aligning the text to the left
                        TextAnchor originalButtonAlignment = GUI.skin.button.alignment;
                        GUI.skin.button.alignment = TextAnchor.MiddleLeft;
                        // Object reference button
                        if (GUILayout.Button(guiContent, GUILayout.MinWidth(300f), GUILayout.Height(18f))) {
                            // Highlight on click
                            referenceObjectsData[i].referencesHighlights[ii] = true;
                            ReferenceFinderCommonUtilities.EditorClearProjectSearchText();
                            Selection.objects = new[] { AssetDatabase.LoadMainAssetAtPath(referencePath) };
                        }
                        // Reset button alignment
                        GUI.skin.button.alignment = originalButtonAlignment;
                        if (referenceColorChanged) {
                            GUIColorStop();
                        }

                        // Showing the path of the reference with combined max width
                        EditorGUILayout.LabelField(referenceObjectsData[i].referencesFolderPaths[ii], GUILayout.Width(referenceObjectsData[i].referencesFolderPathsMaxWidth));

                        EditorGUILayout.EndHorizontal();
                    }
                    GUILayout.EndScrollView();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        //private void OnProjectChange() {
        //    projectChanged = true;
        //    // Allow for editing assets and still looking at the old references
        //    //Clear();
        //}

        private void OnDestroy() {
            Clear();
        }
    }

    public class ProjectAssetsAnalysisUtilities {
        private List<string> _iconPaths;

        public bool IsValidAssetType(string path, bool validForOutput) {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null) {
                if (validForOutput)
                    Debug.LogWarning($"Reference Finder: Invalid asset type found at {path}");
                return false;
            }
            if (type == typeof(MonoScript) || type == typeof(DefaultAsset)) {
                return false;
            }
            if (type == typeof(SceneAsset)) {
                var scenes = EditorBuildSettings.scenes;

                if (scenes.Any(scene => scene.path == path)) {
                    return false;
                }
            }
            return type != typeof(Texture2D) || !UsedAsProjectIcon(path);
        }

        public static bool IsValidForOutput(string path, List<string> ignoreInOutputPatterns) {
            return ignoreInOutputPatterns.All(pattern
                => string.IsNullOrEmpty(pattern) || !Regex.Match(path, pattern).Success);
        }

        private bool UsedAsProjectIcon(string texturePath) {
            if (_iconPaths == null) {
                FindAllIcons();
            }
            return _iconPaths.Contains(texturePath);
        }

        private void FindAllIcons() {
            _iconPaths = new List<string>();
            List<Texture2D> icons = new List<Texture2D>();
#if UNITY_2021_2_OR_NEWER
            foreach (var buildTargetField in typeof(NamedBuildTarget).GetFields(BindingFlags.Public | BindingFlags.Static)) {
                if (buildTargetField.Name == "Unknown") {
                    continue;
                }
                if (buildTargetField.FieldType != typeof(NamedBuildTarget)) {
                    continue;
                }
                NamedBuildTarget buildTarget = (NamedBuildTarget)buildTargetField.GetValue(null);
                icons.AddRange(PlayerSettings.GetIcons(buildTarget, IconKind.Any));
            }
#else
            foreach (var targetGroup in Enum.GetValues(typeof(BuildTargetGroup))) {
                icons.AddRange(PlayerSettings.GetIconsForTargetGroup((BuildTargetGroup) targetGroup));
            }
#endif
            foreach (var icon in icons) {
                _iconPaths.Add(AssetDatabase.GetAssetPath(icon));
            }
        }
    }

    public class SelectedAssetsAnalysisUtilities {

        private Dictionary<string, List<string>> _cachedAssetsMap;

        public Dictionary<Object, List<string>> GetReferences(Object[] selectedObjects) {
            if (selectedObjects == null) {
                Debug.Log("Reference Finder: No selected objects passed");
                return new Dictionary<Object, List<string>>();
            }
            if (ReferenceFinderCache.CacheExists()) {
                ReferenceFinderCache.Load();
                _cachedAssetsMap = ReferenceFinderCache.AssetReferences;
            }
            if (_cachedAssetsMap == null) {
                ReferencesMapUtilities.FillReverseReferencesMap(out _cachedAssetsMap);
                ReferenceFinderCache.SetData(_cachedAssetsMap);
                ReferenceFinderCache.Save();
            }
            EditorUtility.ClearProgressBar();
            GetReferences(selectedObjects, _cachedAssetsMap, out var result);
            return result;
        }

        private static void GetReferences(IEnumerable<Object> selectedObjects, IReadOnlyDictionary<string, List<string>> source, out Dictionary<Object, List<string>> results) {
            results = new Dictionary<Object, List<string>>();
            foreach (Object selectedObject in selectedObjects) {
                string selectedObjectPath = AssetDatabase.GetAssetPath(selectedObject);
                if (source.ContainsKey(selectedObjectPath)) {
                    results.Add(selectedObject, source[selectedObjectPath]);
                } else {
                    Debug.LogWarning("Reference Finder: Doesn't contain the specified object in the assets map", selectedObject);
                    results.Add(selectedObject, new List<string>());
                }
            }
        }
    }

    public static class ReferencesMapUtilities {
        public static void FillReverseReferencesMap(out Dictionary<string, List<string>> reverseReferences) {
            List<string> assetPaths = AssetDatabase.GetAllAssetPaths().ToList();

            // Order the selected objects by name
            // Analysis time in test project went from 31 sec to 30.2 sec with sorted
            List<string> sortedPaths = assetPaths;
            sortedPaths.Sort();
            assetPaths = sortedPaths;
            reverseReferences = assetPaths.ToDictionary(assetPath => assetPath, assetPath => new List<string>());
            //Debug.Log($"Reference Finder: Total Assets Count: {assetPaths.Count}");
            for (var i = 0; i < assetPaths.Count; i++) {
                if (EditorUtility.DisplayCancelableProgressBar("Reference Finder", "Creating a map of references", (float)i / assetPaths.Count)) {
                    break;
                }
                var assetReferences = AssetDatabase.GetDependencies(assetPaths[i], false);
                foreach (var assetReference in assetReferences) {
                    if (reverseReferences.ContainsKey(assetReference) && assetReference != assetPaths[i]) {
                        reverseReferences[assetReference].Add(assetPaths[i]);
                    }
                }
            }
        }
    }

    public class AssetData {
        public static AssetData Create(string path, int referencesCount, string warning) {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            string typeName;

            if (type != null) {
                typeName = type.ToString();
                typeName = typeName.Replace("UnityEngine.", string.Empty);
                typeName = typeName.Replace("UnityEditor.", string.Empty);
            } else {
                typeName = "Unknown Type";
            }

            var isAddressable = ReferenceFinderCommonUtilities.IsAssetAddressable(path);

            var fileInfo = new FileInfo(path);
            var bytesSize = fileInfo.Length;
            return new AssetData(path, type, typeName, bytesSize,
                ReferenceFinderCommonUtilities.GetReadableSize(bytesSize), isAddressable, referencesCount, warning);
        }

        private AssetData(string path, Type type, string typeName, long bytesSize,
            string readableSize, bool addressable, int referencesCount, string warning) {
            Path = path;
            ShortPath = Path.Replace("Assets/", string.Empty);
            Type = type;
            TypeName = typeName;
            BytesSize = bytesSize;
            ReadableSize = readableSize;
            IsAddressable = addressable;
            ReferencesCount = referencesCount;
            Warning = warning;
        }

        public string Path { get; }
        public string ShortPath { get; }
        public Type Type { get; }
        public string TypeName { get; }
        public long BytesSize { get; }
        public string ReadableSize { get; }
        public bool IsAddressable { get; }
        public int ReferencesCount { get; }
        public string Warning { get; }
        public bool ValidType => Type != null;
        public bool Foldout { get; set; }
    }

    public static class GUIUtilities {
        private static void HorizontalLine(
            int marginTop,
            int marginBottom,
            int height,
            Color color
        ) {
            EditorGUILayout.BeginHorizontal();
            var rect = EditorGUILayout.GetControlRect(
                false,
                height,
                new GUIStyle { margin = new RectOffset(0, 0, marginTop, marginBottom) }
            );

            EditorGUI.DrawRect(rect, color);
            EditorGUILayout.EndHorizontal();
        }

        public static void HorizontalLine(
            int marginTop = 5,
            int marginBottom = 5,
            int height = 2
        ) {
            HorizontalLine(marginTop, marginBottom, height, new Color(0.5f, 0.5f, 0.5f, 1));
        }
    }

    public static class ReferenceFinderCommonUtilities {

        public static void EditorClearProjectSearchText() {
            // Clear Project Text Search Bar
            System.Type pb = Type.GetType("UnityEditor.ProjectBrowser,UnityEditor");
            System.Object ins = pb.GetField("s_LastInteractedProjectBrowser", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            System.Reflection.MethodInfo method = pb.GetMethod("ClearSearch", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(ins, null);
        }

        public static string GetReadableSize(long bytesSize) {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytesSize;
            var order = 0;
            while (len >= 1024 && order < sizes.Length - 1) {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        public static bool IsAssetAddressable(string assetPath) {
#if REFERENCE_FINDER_USE_ADDRESSABLES
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(assetPath));
            return entry != null;
#else
            return false;
#endif
        }

        public static string GetCurrentProjectHash() {
#if UNITY_2022_2_OR_NEWER
            // Official, instant, covers assets + import settings + packages
            return AssetDatabase.GlobalArtifactDependencyVersion.ToString();
#else
            // Reliable fallback for 2021.x and older
            var hash = new Hash128();
            var guids = AssetDatabase.FindAssets("", new[] { "Assets", "Packages" });
            System.Array.Sort(guids); // Makes the result deterministic
            foreach (var guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) {
                    continue;
                }
                Hash128 depHash = AssetDatabase.GetAssetDependencyHash(path);
                // Unity 2021-safe: Hash128 has no ".hash" field
                hash.Append(depHash.ToString());
            }
            return hash.ToString();
#endif
        }
    }
}
#endif
#endif