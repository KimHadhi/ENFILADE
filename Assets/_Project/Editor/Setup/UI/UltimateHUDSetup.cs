using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Explicit one-shot request lets the running editor wire its current scene safely after import.
[InitializeOnLoad]
public static class UltimateHUDSetup
{
    private const string Request = "Library/UltimateHudInstall.request";
    private const string Result = "Library/UltimateHudInstall.result.txt";

    static UltimateHUDSetup()
    {
        if (File.Exists(Request)) EditorApplication.update += ProcessRequest;
    }

    private static void ProcessRequest()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= ProcessRequest;
        File.Delete(Request);
        try
        {
            UltimateHUDChecks.Run();
            Install();
            File.WriteAllText(Result, "PASS: ultimate checks passed; CombatFoundation HUD connected and scene saved.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            File.WriteAllText(Result, "FAILED: " + exception);
        }
    }

    [MenuItem("Tools/HUD/Connect Ultimate HUD")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before connecting the HUD.");
        Scene scene = SceneManager.GetSceneByPath("Assets/_Project/Scenes/Development/CombatFoundation.unity");
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open CombatFoundation first.");
        GameObject host = Find(scene, "HPUltRoot");
        Image bar = Find(scene, "UltBar")?.GetComponent<Image>();
        RectTransform marker = Find(scene, "UltReadyThresholdIcon")?.GetComponent<RectTransform>();
        if (marker == null) marker = Find(scene, "UltMiniumIcon")?.GetComponent<RectTransform>();
        if (marker == null) marker = Find(scene, "UltMinimumIcon")?.GetComponent<RectTransform>();
        Graphic portrait = Find(scene, "CharacterBackground")?.GetComponent<Graphic>();
        if (host == null || bar == null || marker == null || portrait == null)
            throw new InvalidOperationException("Expected HPUltRoot, UltBar, UltReadyThresholdIcon and CharacterBackground in CombatFoundation.");
        Undo.SetCurrentGroupName("Connect ultimate HUD");
        UltimateCharge charge = host.GetComponent<UltimateCharge>();
        if (charge == null) charge = Undo.AddComponent<UltimateCharge>(host);
        UltimateHUD hud = host.GetComponent<UltimateHUD>();
        if (hud == null) hud = Undo.AddComponent<UltimateHUD>(host);
        UltimatePortraitGlow glow = host.GetComponent<UltimatePortraitGlow>();
        if (glow == null) glow = Undo.AddComponent<UltimatePortraitGlow>(host);
        Assign(hud, "source", charge);
        Assign(hud, "bar", bar);
        if (marker.name != "UltReadyThresholdIcon") { Undo.RecordObject(marker.gameObject, "Rename ultimate threshold icon"); marker.name = "UltReadyThresholdIcon"; }
        Assign(hud, "readyThresholdMarker", marker);
        Assign(glow, "source", charge);
        Assign(glow, "portrait", portrait);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save CombatFoundation.");
        Debug.Log("ULTIMATE_HUD_CONNECTED: HPUltRoot; capacity 120 / required 100 defaults. Existing positions preserved.");
    }

    private static void Assign(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }

    private static GameObject Find(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child.gameObject;
        return null;
    }
}
