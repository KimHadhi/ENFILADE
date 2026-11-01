// Must live inside an "Editor" folder.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.VFX;

namespace DAVFX
{
    // Setup tool for the impact light burst: assign the particle system,
    // pick which VFX in the scene get it, tweak the particle system in place.
    public class DAVFXLightBurstWindow : EditorWindow
    {
        const string PrefKeyPrefabGuid = "DAVFX.LightBurst.PrefabGuid";
        const string PrefKeyOffset = "DAVFX.LightBurst.DefaultOffset";
        const string PrefKeyMaxLifetime = "DAVFX.LightBurst.DefaultMaxLifetime";
        static readonly string[] TabLabels = { "Scene Targets", "Edit Particle" };

        enum Tab { Setup, EditParticle }

        class VfxEntry
        {
            public VisualEffect vfx;
            public string path;
            public bool include;
            public bool alreadySetup;
        }

        ParticleSystem lightBurstPrefab;

        // Apply only writes the fields whose toggle is on, so values tuned on
        // individual prefabs survive a batch Apply. The prefab is always written.
        bool applyYOffset = true;
        float defaultYOffset = 0.3f;
        bool applyMaxLifetime = true;
        float defaultMaxLifetime = 1f;
        bool applyLightColor;
        Color batchLightColor = Color.white;
        bool applyIntensityScale;
        float batchIntensityScale = 1f;

        Tab currentTab;

        readonly List<VfxEntry> entries = new List<VfxEntry>();
        string commonPathPrefix = "";
        Vector2 listScroll;

        Editor particleEditor;
        Vector2 particleScroll;

        ParticleSystem previewInstance;
        float previewLastTick;

        [MenuItem("Tools/DAVFX/Light Burst Setup")]
        static void Open()
        {
            var window = GetWindow<DAVFXLightBurstWindow>("Light Burst Setup");
            window.minSize = new Vector2(460, 560);
        }

        void OnEnable()
        {
            string guid = EditorPrefs.GetString(PrefKeyPrefabGuid, "");
            if (!string.IsNullOrEmpty(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    lightBurstPrefab = AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
            }

            defaultYOffset = EditorPrefs.GetFloat(PrefKeyOffset, 0.3f);
            defaultMaxLifetime = EditorPrefs.GetFloat(PrefKeyMaxLifetime, 1f);

            EditorApplication.update += Repaint;
            EditorApplication.update += TickTestBurst;

            RescanScene();
        }

        void OnDisable()
        {
            EditorApplication.update -= Repaint;
            EditorApplication.update -= TickTestBurst;

            if (particleEditor != null)
                DestroyImmediate(particleEditor);

            DestroyPreview();
        }

        void OnFocus()
        {
            RescanScene();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Impact Light Burst", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            lightBurstPrefab = (ParticleSystem)EditorGUILayout.ObjectField(
                "Light Burst Prefab", lightBurstPrefab, typeof(ParticleSystem), false);

            DrawToggledFloat("Y Offset", "Lifts the light above the impact point.",
                ref applyYOffset, ref defaultYOffset);
            DrawToggledFloat("Max Lifetime", "Safety net, see the companion's Max Lifetime tooltip. 0 disables it.",
                ref applyMaxLifetime, ref defaultMaxLifetime);

            EditorGUILayout.BeginHorizontal();
            applyLightColor = EditorGUILayout.ToggleLeft(
                new GUIContent("Light Color", "Tints the light via the particle color."),
                applyLightColor, GUILayout.Width(EditorGUIUtility.labelWidth));
            using (new EditorGUI.DisabledScope(!applyLightColor))
                batchLightColor = EditorGUILayout.ColorField(batchLightColor);
            EditorGUILayout.EndHorizontal();

            DrawToggledFloat("Intensity Scale", "1 = particle system as-is, 2 = twice as bright.",
                ref applyIntensityScale, ref batchIntensityScale);

            if (EditorGUI.EndChangeCheck())
            {
                SavePrefs();
                RefreshParticleEditor();
            }

            if (lightBurstPrefab != null && lightBurstPrefab.main.loop)
            {
                EditorGUILayout.HelpBox(
                    "This particle system loops. A burst shouldn't - it'll get force-stopped after Max Lifetime " +
                    "on the companion instead of finishing on its own.", MessageType.Warning);
            }

            EditorGUILayout.Space(8);
            currentTab = (Tab)GUILayout.Toolbar((int)currentTab, TabLabels);
            EditorGUILayout.Space(6);

            switch (currentTab)
            {
                case Tab.Setup:
                    DrawSetupTab();
                    break;
                case Tab.EditParticle:
                    DrawEditParticleTab();
                    break;
            }
        }

        // Checkbox + value on one row, value greyed out while the checkbox is off.
        static void DrawToggledFloat(string label, string tooltip, ref bool apply, ref float value)
        {
            EditorGUILayout.BeginHorizontal();
            apply = EditorGUILayout.ToggleLeft(new GUIContent(label, tooltip), apply,
                GUILayout.Width(EditorGUIUtility.labelWidth));
            using (new EditorGUI.DisabledScope(!apply))
                value = EditorGUILayout.FloatField(value);
            EditorGUILayout.EndHorizontal();
        }

        void SavePrefs()
        {
            string guid = lightBurstPrefab != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(lightBurstPrefab))
                : "";

            EditorPrefs.SetString(PrefKeyPrefabGuid, guid);
            EditorPrefs.SetFloat(PrefKeyOffset, defaultYOffset);
            EditorPrefs.SetFloat(PrefKeyMaxLifetime, defaultMaxLifetime);
        }

        // ------------------------------------------------------------ Setup tab

        void DrawSetupTab()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan Scene"))
                RescanScene();
            using (new EditorGUI.DisabledScope(entries.Count == 0))
            {
                if (GUILayout.Button("All", GUILayout.Width(40))) SetAllInclude(true);
                if (GUILayout.Button("None", GUILayout.Width(50))) SetAllInclude(false);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("No VisualEffect found in the open scene(s).", MessageType.Info);
            }
            else
            {
                listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.ExpandHeight(true));
                int rowIndex = 0;
                foreach (var entry in entries)
                {
                    if (entry.vfx == null)
                        continue;

                    DrawEntryRow(entry, rowIndex);
                    rowIndex++;
                }
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Adds the companion to every checked VFX that's missing it and assigns the prefab. The other " +
                "fields are only written when their checkbox is on, so values tuned on individual prefabs " +
                "survive a batch Apply. Unchecked VFX are left alone.",
                MessageType.None);

            bool anyChecked = entries.Exists(e => e.include);
            using (new EditorGUI.DisabledScope(lightBurstPrefab == null || !anyChecked))
            {
                if (GUILayout.Button("Apply To Checked", GUILayout.Height(28)))
                    ApplyToChecked();
            }
        }

        static readonly Color RowStripeColor = new Color(1f, 1f, 1f, 0.05f);
        const float RowHeight = 18f;

        void DrawEntryRow(VfxEntry entry, int rowIndex)
        {
            string shortLabel = entry.path.Length > commonPathPrefix.Length
                ? entry.path.Substring(commonPathPrefix.Length)
                : entry.path;

            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
            if (rowIndex % 2 == 0)
                EditorGUI.DrawRect(rowRect, RowStripeColor);

            GUILayout.Space(4);

            var content = new GUIContent(shortLabel, entry.path);
            entry.include = EditorGUILayout.ToggleLeft(content, entry.include, GUILayout.ExpandWidth(true));

            if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(38)))
            {
                Selection.activeObject = entry.vfx.gameObject;
                EditorGUIUtility.PingObject(entry.vfx.gameObject);
            }

            using (new EditorGUI.DisabledScope(!entry.alreadySetup))
            {
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
                    RemoveFromEntry(entry);
            }

            GUILayout.Space(4);
            EditorGUILayout.EndHorizontal();
        }

        void SetAllInclude(bool value)
        {
            foreach (var entry in entries)
                entry.include = value;
        }

        void RescanScene()
        {
            // Runs on every OnFocus too, so keep the user's checkbox choices.
            var previousInclude = new Dictionary<VisualEffect, bool>();
            foreach (var e in entries)
            {
                if (e.vfx != null)
                    previousInclude[e.vfx] = e.include;
            }

            entries.Clear();

            foreach (var vfx in FindAllVisualEffects())
            {
                bool include = previousInclude.TryGetValue(vfx, out bool prevInclude) ? prevInclude : true;
                entries.Add(new VfxEntry
                {
                    vfx = vfx,
                    path = GetHierarchyPath(vfx.transform),
                    include = include,
                    alreadySetup = vfx.GetComponent<VFXLightBurstCompanion>() != null
                });
            }

            entries.Sort((a, b) => string.Compare(a.path, b.path, StringComparison.Ordinal));
            commonPathPrefix = ComputeCommonPrefix(entries);
        }

        // Shared root of all paths, stripped from each row to keep them short.
        static string ComputeCommonPrefix(List<VfxEntry> list)
        {
            if (list.Count < 2)
                return "";

            string prefix = list[0].path;
            for (int i = 1; i < list.Count && prefix.Length > 0; i++)
            {
                string other = list[i].path;
                int len = Mathf.Min(prefix.Length, other.Length);
                int j = 0;
                while (j < len && prefix[j] == other[j])
                    j++;
                prefix = prefix.Substring(0, j);
            }

            int lastSlash = prefix.LastIndexOf('/');
            return lastSlash >= 0 ? prefix.Substring(0, lastSlash + 1) : "";
        }

        static VisualEffect[] FindAllVisualEffects()
        {
#if UNITY_2022_2_OR_NEWER
#if UNITY_6000_5_OR_NEWER
            return FindObjectsByType<VisualEffect>(FindObjectsInactive.Include);
#else
            return FindObjectsByType<VisualEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
#else
            return FindObjectsOfType<VisualEffect>(true);
#endif
        }

        static string GetHierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent)
                parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        void ApplyToChecked()
        {
            var processedSources = new HashSet<GameObject>();
            var touchedPrefabRoots = new HashSet<GameObject>();
            int added = 0, repointed = 0, sceneOnly = 0;

            foreach (var entry in entries)
            {
                if (!entry.include || entry.vfx == null)
                    continue;

                GameObject go = entry.vfx.gameObject;

                if (PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    GameObject sourceGo = PrefabUtility.GetCorrespondingObjectFromSource(go) as GameObject;
                    if (sourceGo == null)
                    {
                        Debug.LogWarning($"DAVFX: couldn't resolve the prefab source for '{go.name}', skipped.", go);
                        continue;
                    }

                    if (!processedSources.Add(sourceGo))
                    {
                        entry.alreadySetup = true;
                        continue; // another instance of the same prefab already handled it
                    }

                    var comp = sourceGo.GetComponent<VFXLightBurstCompanion>();
                    if (comp == null)
                    {
                        comp = sourceGo.AddComponent<VFXLightBurstCompanion>();
                        added++;
                    }
                    else
                    {
                        repointed++;
                    }

                    WriteSettings(comp);
                    EditorUtility.SetDirty(sourceGo);
                    touchedPrefabRoots.Add(sourceGo.transform.root.gameObject);
                }
                else
                {
                    var comp = go.GetComponent<VFXLightBurstCompanion>();
                    if (comp == null)
                    {
                        comp = go.AddComponent<VFXLightBurstCompanion>();
                        sceneOnly++;
                    }
                    else
                    {
                        repointed++;
                    }

                    WriteSettings(comp);
                    EditorSceneManager.MarkSceneDirty(go.scene);
                }

                entry.alreadySetup = true;
            }

            foreach (var root in touchedPrefabRoots)
                PrefabUtility.SavePrefabAsset(root);

            Debug.Log($"DAVFX Light Burst: {added} prefab(s) newly set up, {repointed} already-set-up target(s) " +
                      $"repointed, {sceneOnly} scene-only object(s) touched.");
        }

        void WriteSettings(VFXLightBurstCompanion comp)
        {
            comp.lightBurstPrefab = lightBurstPrefab;

            if (applyYOffset)
                comp.localYOffset = defaultYOffset;

            if (applyMaxLifetime)
                comp.maxLifetime = defaultMaxLifetime;

            if (applyLightColor)
            {
                comp.overrideLightColor = true;
                comp.lightColor = batchLightColor;
            }

            if (applyIntensityScale)
                comp.lightIntensityScale = batchIntensityScale;
        }

        void RemoveFromEntry(VfxEntry entry)
        {
            if (entry.vfx == null)
                return;

            GameObject go = entry.vfx.gameObject;

            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                GameObject sourceGo = PrefabUtility.GetCorrespondingObjectFromSource(go) as GameObject;
                if (sourceGo == null)
                    return;

                var comp = sourceGo.GetComponent<VFXLightBurstCompanion>();
                if (comp != null)
                {
                    DestroyImmediate(comp, true);
                    EditorUtility.SetDirty(sourceGo);
                    PrefabUtility.SavePrefabAsset(sourceGo.transform.root.gameObject);
                }
            }
            else
            {
                var comp = go.GetComponent<VFXLightBurstCompanion>();
                if (comp != null)
                {
                    DestroyImmediate(comp);
                    EditorSceneManager.MarkSceneDirty(go.scene);
                }
            }

            entry.alreadySetup = false;
        }

        // ------------------------------------------------------ Edit Particle tab

        void DrawEditParticleTab()
        {
            if (lightBurstPrefab == null)
            {
                EditorGUILayout.HelpBox("Assign a prefab above to edit it here.", MessageType.Info);
                return;
            }

            if (particleEditor == null)
                RefreshParticleEditor();

            particleScroll = EditorGUILayout.BeginScrollView(particleScroll, GUILayout.ExpandHeight(true));
            particleEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            if (GUILayout.Button(previewInstance != null ? "Stop Test Burst" : "Test Burst Here", GUILayout.Height(24)))
                ToggleTestBurst();
            EditorGUILayout.HelpBox(
                "Spawns one burst near wherever the Scene view is currently looking, so you can tune it without " +
                "needing a real VFX impact to trigger it.", MessageType.None);
        }

        void RefreshParticleEditor()
        {
            if (particleEditor != null)
            {
                DestroyImmediate(particleEditor);
                particleEditor = null;
            }

            if (lightBurstPrefab != null)
                particleEditor = Editor.CreateEditor(lightBurstPrefab);
        }

        void ToggleTestBurst()
        {
            if (previewInstance != null)
            {
                DestroyPreview();
                return;
            }

            if (lightBurstPrefab == null)
                return;

            Vector3 pos = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            previewInstance = Instantiate(lightBurstPrefab, pos, Quaternion.identity);
            previewInstance.gameObject.name = "DAVFX Test Burst Preview";

            if (Application.isPlaying)
            {
                // Play Mode already ticks it, simulating on top would double it up.
                previewInstance.Play();
            }
            else
            {
                previewInstance.gameObject.hideFlags = HideFlags.DontSave;
                previewInstance.Simulate(0f, true, true, false);
                previewLastTick = Time.realtimeSinceStartup;
            }
        }

        void TickTestBurst()
        {
            if (previewInstance == null)
                return;

            if (!Application.isPlaying)
            {
                float now = Time.realtimeSinceStartup;
                float dt = now - previewLastTick;
                if (dt > 0f)
                {
                    previewInstance.Simulate(dt, true, false, false);
                    previewLastTick = now;
                }
            }

            if (!previewInstance.IsAlive(true))
                DestroyPreview();
        }

        void DestroyPreview()
        {
            if (previewInstance == null)
                return;

            if (Application.isPlaying)
                Destroy(previewInstance.gameObject);
            else
                DestroyImmediate(previewInstance.gameObject);

            previewInstance = null;
        }
    }
}
#endif