using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Run in an isolated project; exercises the actual scene HUD and finite ammunition.</summary>
[InitializeOnLoad]
public static class AmmoHUDChecks
{
    private const string Pending = "Morphang.AmmoHUDChecks";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static AmmoHUDChecks() => EditorApplication.playModeStateChanged += StateChanged;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        RangedCombatChecks.RunBatch();
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Development/CombatFoundation.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        try { Check(); Debug.Log("AMMO_HUD_CHECKS_PASSED"); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static T Field<T>(AmmoHUD hud, string name) => (T)typeof(AmmoHUD).GetField(name, Fields).GetValue(hud);
    private static string Text(AmmoHUD hud, string name) => Field<TMP_Text>(hud, name).text;
    private static void Check()
    {
        var hud = Object.FindFirstObjectByType<AmmoHUD>();
        Require(hud != null, "Authored scene has an ammo HUD");
        var slots = (PlayerWeaponSlots)typeof(AmmoHUD).GetField("weaponSlots", Fields).GetValue(hud);
        slots.SelectSlot(0); hud.RefreshNow();
        var runtime = slots.GetRuntime(0);
        Require(Text(hud, "magazine") == "30" && Text(hud, "reserve") == "90", "Magazine and reserve are displayed separately");
        Require(Text(hud, "capacity") == "/ 30", "Capacity is not confused with reserve");
        Require(Field<CanvasGroup>(hud, "group").alpha == 1f, "Gun HUD visible");
        var camera = slots.GetComponentInChildren<Camera>();
        Capture(hud, camera, "ammo-normal", 1920, 1080);
        while (runtime.Ammo > 5) runtime.Tick(1f, new WeaponInputFrame(true, true));
        hud.RefreshNow(); Require(Text(hud, "status") == "[R]  RELOAD", "Low ammo prompts a reload");
        Capture(hud, camera, "ammo-low", 1280, 720);
        while (runtime.Ammo > 0) runtime.Tick(1f, new WeaponInputFrame(true, true));
        runtime.Tick(.01f, new WeaponInputFrame(false, false, true));
        runtime.Tick(runtime.ReloadDuration * .5f, default); hud.RefreshNow();
        Require(Mathf.Abs(Field<RectTransform>(hud, "fill").sizeDelta.x - 130f) < .1f,
            "Empty reload bar uses the empty reload duration");
        Require(Text(hud, "reserve") == "90", "Reserve stays unchanged until reload completes");
        Capture(hud, camera, "ammo-reload", 1920, 1080);
        runtime.Interrupt(); hud.RefreshNow();
        Require(Text(hud, "status") == "[R]  RELOAD" && Field<RectTransform>(hud, "fill").sizeDelta.x == 0,
            "Cancelled reload immediately clears progress");
        runtime.Tick(.01f, new WeaponInputFrame(false, false, true)); runtime.Tick(5f, default); hud.RefreshNow();
        Require(Text(hud, "magazine") == "30" && Text(hud, "reserve") == "60", "Reload transfers finite reserve and updates both numbers");
        runtime.Tick(1f, new WeaponInputFrame(true, true));
        runtime.Tick(.01f, new WeaponInputFrame(false, false, true));
        runtime.Tick(runtime.ReloadDuration * .5f, default); hud.RefreshNow();
        Require(Mathf.Abs(Field<RectTransform>(hud, "fill").sizeDelta.x - 130f) < .1f,
            "Tactical reload also reports half progress at half its own duration");
        runtime.Interrupt();
        slots.SelectSlot(1); hud.RefreshNow();
        Require(Text(hud, "magazine") == "12" && Text(hud, "reserve") == "48", "Swapping reads the selected gun's ammo");
        slots.SelectSlot(-1); hud.RefreshNow(); Require(Field<CanvasGroup>(hud, "group").alpha == 0f, "Holster hides ammo");
        slots.SelectSlot(2); hud.RefreshNow(); Require(Field<CanvasGroup>(hud, "group").alpha == 0f, "Unarmed hides ammo");
        slots.SelectSlot(0);
        while (runtime.ReserveAmmo > 0 || runtime.Ammo > 0)
        {
            if (runtime.Ammo > 0) runtime.Tick(1f, new WeaponInputFrame(true, true));
            else { runtime.Tick(.01f, new WeaponInputFrame(false, false, true)); runtime.Tick(5f, default); }
        }
        hud.RefreshNow();
        Require(int.Parse(Text(hud, "reserve")) == 0 && Text(hud, "status") == "OUT OF AMMO", "Exhaustion explicitly explains why reloading is impossible");
        Capture(hud, camera, "ammo-empty", 1920, 1080);
        runtime.AddReserve(3); runtime.Tick(.01f, new WeaponInputFrame(false, false, true)); runtime.Tick(5f, default); hud.RefreshNow();
        Require(int.Parse(Text(hud, "magazine")) == 3 && Text(hud, "status") == "NO RESERVE", "Partial final reload shows remaining usable rounds and no reserve");
        Require(hud.GetComponentsInChildren<Canvas>(true).Length == 0, "Uses the authored canvas without spawning another HUD root");
    }
    private static void Capture(AmmoHUD hud, Camera camera, string name, int width, int height)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var canvas = hud.GetComponentInParent<Canvas>();
        var mode = canvas.renderMode; var oldCamera = canvas.worldCamera; float distance = canvas.planeDistance;
        var previous = camera.targetTexture; var active = RenderTexture.active;
        var rt = new RenderTexture(width, height, 24);
        try
        {
            camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .5f;
            Canvas.ForceUpdateCanvases(); hud.RefreshNow(); Canvas.ForceUpdateCanvases();
            // Camera target resolutions must keep the HUD inside the screen, including 720p.
            var corners = new Vector3[4]; ((RectTransform)hud.transform).GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                Vector3 screen = camera.WorldToViewportPoint(corner);
                Require(screen.x >= 0f && screen.x <= 1f && screen.y >= 0f && screen.y <= 1f, "Ammo HUD fits the viewport");
            }
            camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.GetFullPath(name + ".png"), image.EncodeToPNG()); Object.DestroyImmediate(image);
        }
        finally
        {
            canvas.renderMode = mode; canvas.worldCamera = oldCamera; canvas.planeDistance = distance;
            camera.targetTexture = previous; RenderTexture.active = active; rt.Release(); Object.DestroyImmediate(rt);
        }
    }
}
