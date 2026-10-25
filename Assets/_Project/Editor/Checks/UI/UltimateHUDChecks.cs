using System;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class UltimateHUDChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int passed;

    [MenuItem("Tools/HUD/Run Ultimate HUD Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before running HUD checks.");
        Scene previous = SceneManager.GetActiveScene();
        Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(temporary);
        passed = 0;
        try
        {
            Check("charge thresholds, retained overflow, level-up and events", Resource);
            Check("zero capacity, impossible thresholds and invalid inputs", EdgeCases);
            Check("authored left edge, dynamic capacity, arrow and width cap", Layout);
            Check("private portrait material, charging and heartbeat", Glow);
            Check("edit preview restores authored values before save and avoids double scaling", PreviewLifecycle);
            Check("health and shield update immediately and restore on disable", HealthLive);
        }
        finally
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(temporary, true);
        }
        Debug.Log("ULTIMATE_HUD_CHECKS_PASSED: " + passed);
    }

    private static void Check(string name, Action action) { action(); passed++; Debug.Log("PASS ULTIMATE: " + name); }

    private static void HealthLive()
    {
        var root = new GameObject("Health live fixture", typeof(RectTransform));
        try
        {
            var bar = new GameObject("HPHealthBar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(root.transform, false);
            var block = new GameObject("HealthBlock", typeof(RectTransform));
            block.transform.SetParent(bar, false);
            var hp = new GameObject("HPText", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            var max = new GameObject("MaxHPText", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            var shield = new GameObject("ShieldText", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            hp.transform.SetParent(root.transform, false);
            max.transform.SetParent(root.transform, false);
            shield.transform.SetParent(root.transform, false);
            hp.text = "authored";
            var hud = root.AddComponent<AdaptiveHealthHUD>();
            Set(hud, "matchExistingBlocks", false);
            hud.SetValues(75f, 300f, 25f);
            UltimateHUDPreview.Refresh(0.033f);
            Require(hp.text == "75" && max.text == "300" && shield.text == "+25", "HP values update outside Play");
            Require(!block.activeSelf, "Original blocks hidden during rendering");
            var visual = (RectTransform)typeof(AdaptiveHealthHUD).GetField("visualRoot", Any).GetValue(hud);
            float width = visual.sizeDelta.x;
            hud.SetValues(50f, 5000f, 0f);
            UltimateHUDPreview.Refresh(0.033f);
            Near(visual.sizeDelta.x, width, "Large HP keeps capped width");
            Require(hp.text == "50" && !shield.gameObject.activeSelf, "HP and shield visibility update");
            hud.enabled = false;
            Require(block.activeSelf && hp.text == "authored" && shield.gameObject.activeSelf, "Disabling restores authored UI");
            Require(bar.childCount == 1, "Temporary health graphics removed");
        }
        finally { Object.DestroyImmediate(root); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(float actual, float expected, string message) => Require(Mathf.Abs(actual - expected) < 0.001f, message + ": " + actual + " expected " + expected);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Any).SetValue(target, value);
    private static void Call(object target, string method) => target.GetType().GetMethod(method, Any).Invoke(target, null);

    private static void Resource()
    {
        var charge = new GameObject("Resource fixture").AddComponent<UltimateCharge>();
        int transitions = 0;
        charge.ReadinessChanged += _ => transitions++;
        charge.SetValues(99f, 120f, 100f);
        Require(!charge.IsReady, "99 must not be usable");
        Near(charge.ReadinessRatio, 0.99f, "Progress uses cast cost, not capacity");
        charge.AddCharge(1f);
        Require(charge.IsReady && transitions == 2, "Crossing 100 must publish readiness");
        charge.SetValues(100f, 120f, 100f);
        Require(transitions == 2, "Identical values must not retrigger readiness");
        Require(charge.TryConsume() && !charge.IsReady, "A cast spends its cost");
        Near(charge.CurrentCharge, 0f, "Exact charge is consumed");
        charge.SetValues(120f, 120f, 100f);
        Require(charge.TryConsume(), "Full charge can cast");
        Near(charge.CurrentCharge, 20f, "Overflow must remain after casting");
        Require(!charge.TryConsume(), "Cannot cast again without enough charge");
        charge.SetValues(80f, 120f, 100f);
        charge.SetLimits(180f, 70f);
        Near(charge.CurrentCharge, 80f, "Level-up must preserve stored charge");
        Require(charge.IsReady && charge.TryConsume(), "Lowering cost can immediately enable a cast");
        Near(charge.CurrentCharge, 10f, "Spend the upgraded cost");
        charge.AddCharge(999f);
        Near(charge.CurrentCharge, 180f, "Charge is capped");
        Object.DestroyImmediate(charge.gameObject);
    }

    private static void EdgeCases()
    {
        var charge = new GameObject("Edge fixture").AddComponent<UltimateCharge>();
        charge.SetValues(120f, 120f, 130f);
        Require(!charge.IsReady, "Cost above capacity is unavailable");
        charge.SetLimits(50f, 70f);
        Near(charge.CurrentCharge, 50f, "Reducing capacity clamps charge");
        charge.SetValues(20f, 0f, 0f);
        Require(!charge.IsReady && !charge.TryConsume(), "Zero capacity disables the ability");
        Near(charge.FillRatio, 0f, "Zero capacity has finite empty fill");
        charge.SetValues(0f, 120f, 0f);
        Require(charge.IsReady && charge.TryConsume(), "An explicit zero-cost ability is usable");
        charge.SetValues(float.NaN, float.PositiveInfinity, -5f);
        Near(charge.CurrentCharge, 0f, "Invalid values are sanitized");
        Require(!charge.IsReady, "Invalid capacity cannot activate the ability");
        Object.DestroyImmediate(charge.gameObject);
    }

    private static RectTransform Rect(string name, Transform parent = null)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        if (parent != null) rect.SetParent(parent, false);
        return rect;
    }

    private static void Layout()
    {
        RectTransform root = Rect("Layout fixture");
        root.pivot = Vector2.zero; root.sizeDelta = new Vector2(600f, 100f);
        var charge = root.gameObject.AddComponent<UltimateCharge>();
        RectTransform barRect = Rect("Bar", root);
        barRect.anchorMin = barRect.anchorMax = Vector2.zero;
        barRect.sizeDelta = new Vector2(120f, 5f); barRect.anchoredPosition = new Vector2(90f, 20f);
        Image bar = barRect.gameObject.AddComponent<Image>();
        RectTransform marker = Rect("Arrow", root);
        marker.localPosition = new Vector3(70f, 10f, 0f);
        UltimateHUD view = root.gameObject.AddComponent<UltimateHUD>();
        Set(view, "source", charge); Set(view, "bar", bar); Set(view, "minimumMarker", marker);
        try
        {
            charge.SetValues(60f, 120f, 100f); view.RefreshNow();
            Near(barRect.rect.width, 120f, "Initial bar width is preserved");
            Near(marker.localPosition.x, 130f, "Arrow marks 100 from the bar left edge");
            Near(marker.localPosition.y, 10f, "Arrow height is preserved");
            var fill = (RectTransform)barRect.Find("Ultimate Fill (Runtime)");
            Near(fill.anchorMax.x, 0.5f, "Fill ratio");
            charge.SetValues(80f, 240f, 70f); view.RefreshNow();
            Near(barRect.rect.width, 240f, "Capacity upgrade grows the bar");
            Near(barRect.localPosition.x + barRect.rect.xMin, 30f, "Bar's left edge cannot move");
            Near(marker.localPosition.x, 100f, "Cost 70 moves the arrow left");
            charge.SetLimits(600f, 70f); view.RefreshNow();
            Near(barRect.rect.width, 480f, "Width cap prevents unlimited HUD growth");
            Near(marker.localPosition.x, 86f, "Arrow rescales when width is capped");
            charge.SetValues(0f, 0f, 100f); view.RefreshNow();
            Require(!marker.gameObject.activeSelf, "Impossible threshold marker is hidden");
            Near(fill.anchorMax.x, 0f, "Zero capacity is empty");
        }
        finally { Call(view, "RestorePresentation"); Object.DestroyImmediate(root.gameObject); }
    }

    private static void Glow()
    {
        Shader shader = Shader.Find("UI/Custom/HexGlowFrame");
        Require(shader != null, "Portrait shader exists");
        Material shared = new Material(shader);
        shared.SetFloat("_GlowIntensity", 10f);
        RectTransform root = Rect("Glow fixture");
        root.gameObject.AddComponent<Canvas>();
        var charge = root.gameObject.AddComponent<UltimateCharge>();
        RectTransform masked = Rect("Mask", root);
        masked.gameObject.AddComponent<Image>();
        masked.gameObject.AddComponent<Mask>();
        Image portrait = Rect("Portrait", masked).gameObject.AddComponent<Image>();
        portrait.material = shared;
        var glow = root.gameObject.AddComponent<UltimatePortraitGlow>();
        Set(glow, "source", charge); Set(glow, "portrait", portrait); Set(glow, "transitionSeconds", 0f);
        try
        {
            charge.SetValues(50f, 120f, 100f); glow.RefreshNow();
            Require(portrait.material == shared, "Serialized portrait reference must remain the source material");
            Require(portrait.materialForRendering != shared, "Rendering uses a private material");
            Near(shared.GetFloat("_GlowIntensity"), 10f, "Shared asset must stay unchanged");
            Near(portrait.materialForRendering.GetFloat("_GlowBloomSpread"), 0.0225f, "Half charge expands the spread halfway");
            charge.SetValues(100f, 120f, 100f); glow.RefreshNow(0.12f / 1.1f);
            float peak = portrait.materialForRendering.GetFloat("_GlowIntensity");
            Require(peak > 3.5f, "First heartbeat is visible at readiness");
            Near(portrait.materialForRendering.GetFloat("_GlowIntensity"), peak, "Stencil UI material also receives animation");
            Color ready = portrait.materialForRendering.GetColor("_GlowColor");
            Require(ready.r > 0.99f && ready.g < 0.02f, "Ready color is saturated red");
            glow.RefreshNow(0.5f);
            Require(portrait.materialForRendering.GetFloat("_GlowIntensity") < peak - 1f, "Heartbeat returns to its valley");
            charge.TryConsume(); glow.RefreshNow(0.2f);
            Near(portrait.materialForRendering.GetFloat("_GlowIntensity"), 0.65f, "Spending removes readiness pulse");
            Call(glow, "ReleaseMaterial");
            Require(portrait.material == shared, "Disabling restores the authored material");
            // Replacing or clearing a material must not permanently switch glow off.
            portrait.material = null;
            Set(glow, "warnedInvalidMaterial", true);
            glow.RefreshNow();
            Require(glow.enabled, "Missing material cannot disable automatic recovery");
            portrait.material = shared;
            glow.RefreshNow();
            Near(portrait.materialForRendering.GetFloat("_GlowIntensity"), 0.65f, "Reassignment resumes glow");
            Set(glow, "portraitMaterial", shared);
            portrait.material = null;
            glow.RefreshNow();
            Require(portrait.material == shared, "Missing reference recovers from persistent fallback");
            glow.enabled = false;
            Require(portrait.material == shared, "Disable preserves material reference");
            Require(portrait.GetComponent<UltimatePortraitMaterial>() == null, "Disable removes temporary renderer modifier");
        }
        finally { Call(glow, "ReleaseMaterial"); Object.DestroyImmediate(root.gameObject); Object.DestroyImmediate(shared); }
    }

    public static void RunPortraitGlowChecks() { Glow(); }

    private static void PreviewLifecycle()
    {
        string scenePath = ProjectAssetPaths.DevelopmentScene("UltimatePreviewCheck_" + Guid.NewGuid().ToString("N") + ".unity");
        RectTransform root = Rect("Preview fixture");
        root.pivot = Vector2.zero; root.sizeDelta = new Vector2(600f, 100f);
        var charge = root.gameObject.AddComponent<UltimateCharge>();
        RectTransform barRect = Rect("Preview Bar", root);
        barRect.anchorMin = barRect.anchorMax = Vector2.zero;
        barRect.sizeDelta = new Vector2(120f, 5f); barRect.anchoredPosition = new Vector2(90f, 20f);
        Image bar = barRect.gameObject.AddComponent<Image>();
        RectTransform marker = Rect("Preview Arrow", root);
        marker.localPosition = new Vector3(70f, 10f, 0f);
        var view = root.gameObject.AddComponent<UltimateHUD>();
        Set(view, "source", charge); Set(view, "bar", bar); Set(view, "minimumMarker", marker);
        try
        {
            charge.SetValues(80f, 240f, 70f);
            UltimateHUDPreview.Refresh(0.033f);
            Near(barRect.rect.width, 240f, "Edit preview must respond to capacity");
            Near(marker.localPosition.x, 100f, "Edit preview must move the threshold");
            var fill = barRect.Find("Ultimate Fill (Runtime)");
            Require(fill != null && (fill.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0, "Preview fill cannot be serialized");
            Require(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath), "Preview scene saves");
            Near(barRect.rect.width, 120f, "Saving restores authored width before serialization");
            Near(marker.localPosition.x, 70f, "Saving restores authored marker placement");
            Require(!File.ReadAllText(scenePath).Contains("Ultimate Fill (Runtime)"), "Saved scene cannot contain preview objects");
            UltimateHUDPreview.Refresh(0.033f);
            Near(barRect.rect.width, 240f, "Preview after save must not double the reference width");
            UltimateHUDPreview.StopAll();
            Near(barRect.rect.width, 120f, "Stopping preview restores width");
            Require(barRect.Find("Ultimate Fill (Runtime)") == null, "Stopping preview removes the temporary fill");
            UltimateHUDPreview.Refresh(0.033f);
            view.enabled = false;
            Near(barRect.rect.width, 120f, "Disabling the component cleans up edit-mode presentation");
        }
        finally
        {
            UltimateHUDPreview.StopAll();
            Object.DestroyImmediate(root.gameObject);
            if (File.Exists(scenePath)) AssetDatabase.DeleteAsset(scenePath);
        }
    }
}
