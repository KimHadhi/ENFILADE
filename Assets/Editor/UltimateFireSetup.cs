using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class UltimateFireSetup
{
    private const string Root = "Assets/Sprites/UI/HUD/HPBar/CharacterHex/";
    private const string Request = "Library/UltimateFireInstall.request";
    static UltimateFireSetup()
    {
        if (File.Exists(Request)) EditorApplication.update += Process;
    }

    private static void Process()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= Process;
        File.Delete(Request);
        try
        {
            Check();
            Install();
            File.WriteAllText("Library/UltimateFireInstall.result.txt", "PASS: fire transitions, sprite sampling, interruption, recharge and cleanup; scene connected and saved.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText("Library/UltimateFireInstall.result.txt", "FAILED: " + e);
        }
    }

    private static AnimationClip Clip(string name)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + name + "/" + name + ".anim");
        if (clip == null || clip.length <= 0f) throw new InvalidOperationException("Missing fire clip: " + name);
        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        if (bindings.Length != 1 || bindings[0].path != "" || bindings[0].type != typeof(Image) || bindings[0].propertyName != "m_Sprite"
            || AnimationUtility.GetCurveBindings(clip).Length != 0)
            throw new InvalidOperationException(name + " must animate only the FireEffect Image sprite.");
        return clip;
    }

    [MenuItem("Tools/HUD/Connect Ultimate Fire Effect")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before connecting fire.");
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CombatFoundation.unity");
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open CombatFoundation first.");
        GameObject host = Find(scene, "HPUltRoot");
        Image image = Find(scene, "FireEffect")?.GetComponent<Image>();
        UltimateCharge source = host != null ? host.GetComponent<UltimateCharge>() : null;
        if (image == null || source == null) throw new InvalidOperationException("FireEffect Image and HPUltRoot UltimateCharge are required.");
        var start = Clip("FireStart"); var loop = Clip("FireLoop"); var end = Clip("FireEnd");
        UltimateHUDPreview.StopAll();
        var fire = host.GetComponent<UltimateFireEffect>();
        if (fire == null) fire = Undo.AddComponent<UltimateFireEffect>(host);
        Assign(fire, "source", source); Assign(fire, "fireImage", image);
        Assign(fire, "fireStart", start); Assign(fire, "fireLoop", loop); Assign(fire, "fireEnd", end);
        // The driver owns playback; preserve the existing controller asset for manual authoring.
        Animator animator = image.GetComponent<Animator>();
        if (animator != null) { Undo.RecordObject(animator, "Connect ultimate fire"); animator.enabled = false; }
        Undo.RecordObject(image, "Connect ultimate fire");
        image.enabled = false;
        image.raycastTarget = false;
        Undo.RecordObject(image.gameObject, "Connect ultimate fire");
        image.gameObject.SetActive(true);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save CombatFoundation.");
        Debug.Log("ULTIMATE_FIRE_CONNECTED: FireStart -> FireLoop; unavailable -> FireEnd -> hidden.");
    }

    [MenuItem("Tools/HUD/Run Ultimate Fire Checks")]
    public static void Check()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before checks.");
        AnimationClip start = Clip("FireStart"), loop = Clip("FireLoop"), end = Clip("FireEnd");
        Scene previous = SceneManager.GetActiveScene();
        Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(temporary);
        var root = new GameObject("Fire check");
        try
        {
            var source = root.AddComponent<UltimateCharge>();
            var image = new GameObject("FireEffect", typeof(RectTransform), typeof(Image), typeof(Animator)).GetComponent<Image>();
            image.transform.SetParent(root.transform, false);
            var fire = root.AddComponent<UltimateFireEffect>();
            Assign(fire, "source", source); Assign(fire, "fireImage", image);
            Assign(fire, "fireStart", start); Assign(fire, "fireLoop", loop); Assign(fire, "fireEnd", end);
            source.SetValues(99f, 120f, 100f); fire.RefreshNow(0f);
            Require(!image.enabled && fire.CurrentPhase == UltimateFireEffect.Phase.Hidden, "Below threshold must hide");
            source.SetValues(100f, 120f, 100f); fire.RefreshNow(0f);
            Require(image.enabled && fire.CurrentPhase == UltimateFireEffect.Phase.Starting, "Ready must start");
            var frames = AnimationUtility.GetObjectReferenceCurve(start, AnimationUtility.GetObjectReferenceCurveBindings(start)[0]);
            Require(image.sprite == frames[0].value, "First sprite must match authored clip");
            fire.RefreshNow(1f / start.frameRate + 0.001f);
            Require(image.sprite == frames[1].value, "Sprite animation must advance");
            fire.RefreshNow(start.length);
            Require(fire.CurrentPhase == UltimateFireEffect.Phase.Looping, "Start must enter loop");
            fire.RefreshNow(loop.length * 4f);
            Require(fire.CurrentPhase == UltimateFireEffect.Phase.Looping && image.enabled, "Loop must repeat");
            Require(source.TryConsume(), "Ready charge can be spent"); fire.RefreshNow(0f);
            Require(fire.CurrentPhase == UltimateFireEffect.Phase.Ending && image.enabled, "Spending must play end before hiding");
            source.SetValues(70f, 180f, 70f); fire.RefreshNow(0f);
            Require(fire.CurrentPhase == UltimateFireEffect.Phase.Starting, "Recharge during end must restart");
            source.SetValues(0f, 180f, 70f); fire.RefreshNow(0f);
            Require(fire.CurrentPhase == UltimateFireEffect.Phase.Ending, "Loss during start must end");
            fire.RefreshNow(end.length + 0.01f);
            Require(!image.enabled && fire.CurrentPhase == UltimateFireEffect.Phase.Hidden, "End must hide even when clip is authored looping");
            source.SetValues(180f, 180f, 70f); fire.RefreshNow(0f); fire.RefreshNow(start.length);
            source.TryConsume(); fire.RefreshNow(0f);
            Require(fire.CurrentPhase == UltimateFireEffect.Phase.Looping, "Enough retained charge must keep fire ready");
            fire.StopPreview();
            Require(image.enabled && image.sprite == null && image.GetComponent<Animator>().enabled, "Cleanup must restore original image and Animator");
            Debug.Log("ULTIMATE_FIRE_CHECKS_PASSED: thresholds, actual sprite frames, loop, spending, interruptions, upgraded cost, retained charge and cleanup.");
        }
        finally
        {
            Object.DestroyImmediate(root);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(temporary, true);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Assign(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
    private static GameObject Find(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child.gameObject;
        return null;
    }
}
