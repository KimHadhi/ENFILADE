using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Map dressing, baked indirect light (the user, 2026-11-13: "APV 진행"): an Adaptive Probe Volume over the whole map,
/// baked with the map, when its Tables/MapLook row gives a probeSpacing (metres; empty or 0 bakes none, and the map keeps
/// the sky's flat ambient light as before). Until now every place, a tunnel or a closed room as much as a rooftop, took
/// the same light from the sky; with the probes the sky lights only what sees it, the sun's light bounces off the
/// ground and walls, and the lamps' light bounces round their rooms. The PC pipeline already samples probe volumes per
/// pixel (Light Probe System: Adaptive Probe Volumes, L1), and the avatars' shaders take them (lilToon's patch adds the
/// sun back, LightingGuide). Nothing is lightmapped: the boxes and models that stay put contribute to the bake and take
/// their own light from the probes too (Receive GI: Light Probes), so no lightmap UVs are needed and the bake is only the
/// probes. The sun and every lamp that stays put are Mixed with Baked Indirect: their direct light and shadows are drawn
/// live as before, only their bounce is baked. The lamps have no shadows (a hundred shadowed lamps would cost too much),
/// but a bake without them would let their bounce pass through walls and floors, so they cast soft shadows for the bake
/// only and lose them again after it. Unity keeps the probes per scene in a baking set beside it (the one the Lighting
/// window made, or a new one); its spacing and the bake's samples come from here. Bigger spacing bakes faster with less
/// data; smaller lets less light leak through thin walls.
/// </summary>
public sealed partial class GreyboxBuilder
{
    // The bake's samples per probe (LightingSettings; Unity's own are 32 / 512 / 256): the probes are smooth light, so
    // half the indirect samples bake in about half the time with no visible difference. Bounces as Unity's.
    private const int ProbeDirectSamples = 32, ProbeIndirectSamples = 256, ProbeEnvironmentSamples = 256, ProbeBounces = 2;
    // The coarsest probes are this many steps of three apart from the finest (Unity's default, 27 × the spacing).
    private const int ProbeSimplification = 3;
    // The spacing a row may set (m), and the probe bricks Unity's own sets use.
    private const float ProbeSpacingLeast = .5f, ProbeSpacingMost = 10f;
    private const int ProbeChunkBricks = 128;

    private float probeSpacing = -1f;
    // The lamps given shadows for the bake only, and what they had.
    private readonly List<(Light lamp, LightShadows shadows)> bakeShadows = new List<(Light, LightShadows)>();

    // This map's probe spacing (Tables/MapLook probeSpacing), 0 when it bakes no probes.
    private float ProbeSpacing()
    {
        if (probeSpacing >= 0f) return probeSpacing;
        float spacing = GameTables.TryGetRow(LookTable, sceneName, out var row) ? row.Number("probeSpacing", 0f) : 0f;
        return probeSpacing = spacing > 0f ? Mathf.Clamp(spacing, ProbeSpacingLeast, ProbeSpacingMost) : 0f;
    }

    private bool BakesProbes => ProbeSpacing() > 0f;

    // The bake's settings in this map's lighting asset (ApplyLook makes it): baked GI for the probes, Baked Indirect for
    // the mixed lights, no lightmaps to make. Unity 6 never bakes lighting by itself (LightingSettings.autoGenerate is
    // obsolete and does nothing), so only the build's own Lightmapping.Bake bakes it.
    private void ProbeLighting(LightingSettings lighting)
    {
        if (!BakesProbes) return;
        lighting.bakedGI = true;
        lighting.mixedBakeMode = MixedLightingMode.IndirectOnly;
        lighting.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        lighting.directSampleCount = ProbeDirectSamples;
        lighting.indirectSampleCount = ProbeIndirectSamples;
        lighting.environmentSampleCount = ProbeEnvironmentSamples;
        lighting.maxBounces = ProbeBounces;
        lighting.directionalityMode = LightmapsMode.NonDirectional;
    }

    // Before the first save: the volume over the whole map (Global: it takes the size of everything that contributes).
    private void AddProbeVolume()
    {
        if (!BakesProbes) return;
        var host = Root("◤━━ 간접광 (Adaptive Probe Volume, Tables/MapLook probeSpacing)");
        host.gameObject.AddComponent<ProbeVolume>().mode = ProbeVolume.Mode.Global;
    }

    // A renderer that stays put takes part in the bake (MarkStatic): it adds its bounce and shade, and takes its light
    // from the probes, not a lightmap.
    private void TakesPartInBake(Renderer shape, ref StaticEditorFlags flags)
    {
        if (!BakesProbes || !shape.enabled) return;
        flags |= StaticEditorFlags.ContributeGI;
        if (shape is MeshRenderer mesh) mesh.receiveGI = ReceiveGI.LightProbes;
    }

    // After the first save (the scene has its GUID): the scene's baking set with this map's spacing, and every light that
    // stays put mixed, the shadowless ones shadowed for the bake (RestoreBakeShadows takes that back). False when the map
    // bakes no probes or no baking set could be had.
    private bool PrepareProbeBake()
    {
        if (!BakesProbes) return false;
        string guid = AssetDatabase.AssetPathToGUID(ScenePath);
        var set = string.IsNullOrEmpty(guid) ? null : ProbeSetFor(guid);
        if (set == null)
        {
            Debug.LogWarning("[Greybox] " + sceneName + ": no probe baking set for the scene; the probes were not baked. Window > Rendering > Lighting " +
                             "> Adaptive Probe Volumes, Baking Mode Single Scene, then Generate Lighting.");
            return false;
        }
        float spacing = ProbeSpacing();
        set.minDistanceBetweenProbes = spacing;
        set.simplificationLevels = ProbeSimplification;
        set.skyOcclusion = false;
        var so = new SerializedObject(set);
        so.FindProperty("singleSceneMode").boolValue = true;
        // Probes left inside walls take their neighbours' light (dilation, as far as one spacing) after being pushed out
        // of the walls where they can be (virtual offset), so no black blots at the walls' feet.
        so.FindProperty("settings.dilationSettings.enableDilation").boolValue = true;
        so.FindProperty("settings.dilationSettings.dilationDistance").floatValue = spacing;
        so.FindProperty("settings.virtualOffsetSettings.useVirtualOffset").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssetIfDirty(set);
        bakeShadows.Clear();
        foreach (var lamp in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (lamp.gameObject.scene != scene || MovesWithGroup(lamp.gameObject)) continue;
            lamp.lightmapBakeType = LightmapBakeType.Mixed;
            if (lamp.type == LightType.Directional || lamp.shadows != LightShadows.None) continue;
            bakeShadows.Add((lamp, lamp.shadows));
            lamp.shadows = LightShadows.Soft;
        }
        return true;
    }

    // After the bake: the lamps drawn as before (no shadows), their baked bounce kept.
    private void RestoreBakeShadows()
    {
        foreach (var (lamp, shadows) in bakeShadows)
            if (lamp != null) lamp.shadows = shadows;
        bakeShadows.Clear();
    }

    // Whether the probes were baked at this spacing: the set keeps what it last baked with, and its cells' data.
    private bool ProbesBaked()
    {
        string guid = AssetDatabase.AssetPathToGUID(ScenePath);
        var set = string.IsNullOrEmpty(guid) ? null : FindProbeSet(guid);
        if (set == null) return false;
        var so = new SerializedObject(set);
        var baked = so.FindProperty("bakedMinDistanceBetweenProbes");
        var data = so.FindProperty("cellSharedDataAsset.m_AssetGUID");
        return baked != null && Mathf.Approximately(baked.floatValue, ProbeSpacing()) && data != null && !string.IsNullOrEmpty(data.stringValue);
    }

    // The baking set that holds this scene: the one the Lighting window made for it, else a new single-scene set beside
    // the scene (Unity makes one only while the Lighting window's probe tab is open).
    private ProbeVolumeBakingSet ProbeSetFor(string guid)
    {
        var known = FindProbeSet(guid);
        if (known != null) return known;
        var made = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
        made.name = sceneName + " Baking Set";
        // Unity's own new sets start from SetDefaults (the dilation and virtual offset settings, the default scenario,
        // the brick chunk size); it is internal, so it is called by name, and the same values are written after it.
        typeof(ProbeVolumeBakingSet).GetMethod("SetDefaults", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(made, null);
        if (!made.TryAddScene(guid))
        {
            Object.DestroyImmediate(made);
            return null;
        }
        AssetDatabase.CreateAsset(made, Folder + "/" + sceneName + "/" + made.name + ".asset");
        var so = new SerializedObject(made);
        var scenarios = so.FindProperty("m_LightingScenarios");
        if (scenarios.arraySize == 0)
        {
            scenarios.arraySize = 1;
            scenarios.GetArrayElementAtIndex(0).stringValue = "Default";
        }
        if (so.FindProperty("chunkSizeInBricks").intValue <= 0) so.FindProperty("chunkSizeInBricks").intValue = ProbeChunkBricks;
        so.ApplyModifiedPropertiesWithoutUndo();
        // Unity looks up which set holds a scene from a map it builds once; forgotten here, so the bake finds this set.
        var map = typeof(ProbeVolumeBakingSet).GetNestedType("SceneToBakingSet", BindingFlags.NonPublic)
            ?.GetField("sceneToBakingSet", BindingFlags.NonPublic | BindingFlags.Static);
        if (map != null) map.SetValue(null, null);
        else Debug.LogWarning("[Greybox] " + sceneName + ": made a probe baking set, but Unity may not see it until the editor reloads; " +
                              "if the probes are missing, build the map again.");
        return made;
    }

    private static ProbeVolumeBakingSet FindProbeSet(string guid)
    {
        foreach (string id in AssetDatabase.FindAssets("t:" + nameof(ProbeVolumeBakingSet)))
        {
            var set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(AssetDatabase.GUIDToAssetPath(id));
            if (set == null) continue;
            foreach (string sceneGuid in set.sceneGUIDs)
                if (sceneGuid == guid) return set;
        }
        return null;
    }

    // A part of a group that moves (a gate, a door, a pushback, a tower door): never static, never baked.
    private static bool MovesWithGroup(GameObject part)
        => part.GetComponentInParent<CampaignGate>() != null || part.GetComponentInParent<CampaignDoor>() != null
           || part.GetComponentInParent<CampaignPushback>() != null || part.GetComponentInParent<TowerDoor>() != null;

    // For the build's report.
    private string ProbeReport(bool baked)
        => !BakesProbes ? "" : "\n간접광(APV, Tables/MapLook probeSpacing " + ProbeSpacing().ToString("0.##") + " m): "
                               + (baked ? "구웠어요. 해·하늘·등의 반사광이 프로브에 들어갔어요."
                                   : "굽지 못했어요(콘솔을 보세요). 하늘의 고른 환경광은 그대로예요.");
}
