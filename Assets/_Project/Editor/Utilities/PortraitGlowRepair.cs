using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class PortraitGlowRepair
{
    private const string Request = "Library/PortraitGlowRepair.request";
    private const string Result = "Library/PortraitGlowRepair.result.txt";
    private const string MaterialPath = "Assets/_Project/Art/Materials/UI/HPHud/Mat_HexPortrait.mat";
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/HUD/PlayerHealthHUD.prefab";

    static PortraitGlowRepair() { EditorApplication.update += Tick; }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        File.Delete(Request);
        try { Repair(); File.WriteAllText(Result, "PASS: charging, heartbeat, masked rendering, material reassignment/recovery, disable cleanup, scene and prefab persistent references."); }
        catch (Exception e) { File.WriteAllText(Result, "FAIL: " + e); Debug.LogException(e); }
    }

    [MenuItem("Tools/HUD/Repair Portrait Glow")]
    public static void Repair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "CombatFoundation") throw new InvalidOperationException("Open CombatFoundation first.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) throw new InvalidOperationException("Portrait source material not found.");
        UltimateHUDPreview.StopAll();
        UltimateHUDChecks.RunPortraitGlowChecks();
        int repaired = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var glow in root.GetComponentsInChildren<UltimatePortraitGlow>(true))
            { Fix(glow, material); repaired++; }
        if (repaired == 0) throw new InvalidOperationException("No portrait glow found in scene.");
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (var glow in prefab.GetComponentsInChildren<UltimatePortraitGlow>(true)) Fix(glow, material);
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save repaired scene.");
        AssetDatabase.SaveAssets();
        UltimateHUDPreview.Refresh(0.033f);
        foreach (var root in scene.GetRootGameObjects())
            foreach (var glow in root.GetComponentsInChildren<UltimatePortraitGlow>(true))
            {
                var so = new SerializedObject(glow);
                var portrait = (Graphic)so.FindProperty("portrait").objectReferenceValue;
                if (!glow.enabled || portrait == null || !EditorUtility.IsPersistent(portrait.material))
                    throw new InvalidOperationException("Portrait source reference must survive preview and saving.");
            }
        Debug.Log("PORTRAIT_GLOW_REPAIR_PASSED");
    }

    private static void Fix(UltimatePortraitGlow glow, Material material)
    {
        glow.StopPreview();
        var so = new SerializedObject(glow);
        var portrait = (Graphic)so.FindProperty("portrait").objectReferenceValue;
        if (portrait == null) throw new InvalidOperationException("Portrait Graphic reference is missing.");
        so.FindProperty("portraitMaterial").objectReferenceValue = material;
        var intensity = so.FindProperty("intensityRange");
        if (intensity.vector2Value.y <= intensity.vector2Value.x)
            intensity.vector2Value = new Vector2(intensity.vector2Value.x, Mathf.Max(2.2f, intensity.vector2Value.x + 1f));
        so.ApplyModifiedPropertiesWithoutUndo();
        if (portrait.material == portrait.defaultMaterial || !portrait.material.HasProperty("_GlowBloomSpread") || !EditorUtility.IsPersistent(portrait.material))
            portrait.material = material;
        glow.enabled = true;
        EditorUtility.SetDirty(portrait);
        EditorUtility.SetDirty(glow);
    }
}
