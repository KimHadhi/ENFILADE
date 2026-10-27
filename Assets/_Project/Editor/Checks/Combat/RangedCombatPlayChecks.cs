using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class RangedCombatPlayChecks
{
    private const string Pending = "Morphang.RangedPlayChecks";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static RangedCombatPlayChecks() => EditorApplication.playModeStateChanged += StateChanged;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated Unity batch project.");
        RangedCombatChecks.RunBatch();
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Development/CombatFoundation.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        try { Check(); Debug.Log("RANGED_COMBAT_PLAY_CHECKS_PASSED"); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Frame(WeaponController gun, float dt, WeaponInputFrame input)
        => typeof(WeaponController).GetMethod("ProcessFrame", Any).Invoke(gun, new object[] { dt, input });
    private static void Check()
    {
        RangedCombatChecks.LeanVolumes();
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var slots = player.GetComponent<PlayerWeaponSlots>(); var gun = player.GetComponent<WeaponController>();
        slots.SelectSlot(0);
        var gunRoot = (Transform)typeof(WeaponController).GetField("weaponRoot", Any).GetValue(gun);
        Require(gunRoot != null, "Authored scene has a weapon presentation root");
        gunRoot.gameObject.SetActive(true);
        var muzzle = (Transform)typeof(WeaponController).GetField("muzzlePoint", Any).GetValue(gun);
        var view = player.GetComponentInChildren<Camera>();
        var crosshair = Object.FindFirstObjectByType<DynamicCrosshairView>();
        Require(crosshair != null, "Scene has the gameplay crosshair");
        var dot = (UnityEngine.UI.Image)typeof(DynamicCrosshairView).GetField("centerDot", Any).GetValue(crosshair);
        var parts = (RectTransform)typeof(DynamicCrosshairView).GetField("partsContainer", Any).GetValue(crosshair);
        var profile = (CrosshairProfile)typeof(DynamicCrosshairView).GetField("currentProfile", Any).GetValue(crosshair);
        bool showDot = profile.showDot, showParts = profile.showParts;
        float hipFov = view.fieldOfView;
        Require(muzzle != null && muzzle != view.transform && gun.EquippedData != null, "Authored scene auto-wires real muzzle and data");
        player.transform.position = new Vector3(2000, 5, 2000); Physics.SyncTransforms();
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        target.name = "Ranged native target"; target.transform.position = view.transform.position + view.transform.forward * 8;
        target.transform.localScale = Vector3.one * 2;
        var receiver = target.AddComponent<WeaponTrainingTarget>(); receiver.ResetTarget(); Physics.SyncTransforms();
        Frame(gun, .3f, default); Capture(view, "ranged-hip");
        Frame(gun, .3f, new WeaponInputFrame(false, false, aimHeld: true));
        Require(!dot.gameObject.activeSelf && !parts.gameObject.activeSelf, "ADS hides both crosshair dot and arms");
        crosshair.ApplyProfile(profile);
        Require(!dot.gameObject.activeSelf && !parts.gameObject.activeSelf && profile.showDot == showDot && profile.showParts == showParts,
            "Refreshing the crosshair profile during ADS keeps it hidden without changing user settings");
        var effects = player.GetComponentInChildren<PlayerCameraEffects>();
        Require(effects != null, "Scene camera owns ADS FOV composition");
        typeof(PlayerCameraEffects).GetMethod("UpdateDynamicFov", Any).Invoke(effects, new object[] { 1f });
        Require(Mathf.Abs(view.fieldOfView - gun.EquippedData.AdsFov) < 1f, "Native camera zooms to the weapon ADS FOV");
        Require(view.fieldOfView <= hipFov - 10f, "ADS noticeably magnifies the scene");
        CheckLeanAds(player, gun, effects, view, muzzle);
        var adsMuzzle = view.transform.InverseTransformPoint(muzzle.position);
        Require(Mathf.Abs(adsMuzzle.x) < .01f && Mathf.Abs(adsMuzzle.y + gun.EquippedData.SightHeight) < .01f,
            "Authored rotated gun model centers on the sight line");
        Capture(view, "ranged-ads");
        float pitch = view.transform.eulerAngles.x;
        Frame(gun, .11f, new WeaponInputFrame(true, true, aimHeld: true));
        Require(receiver.Health < 1000 && gun.Runtime.Ammo == gun.EquippedData.MagazineSize - 1, "Scene gun damages immediately from its actual muzzle");
        Require(Mathf.Abs(Mathf.DeltaAngle(pitch, view.transform.eulerAngles.x)) < .001f, "Shot impulse does not snap the camera");
        var look = player.GetComponent<PlayerLook>();
        typeof(PlayerLook).GetMethod("TickRecoil", Any).Invoke(look, new object[] { .045f });
        float peakPitch = view.transform.eulerAngles.x;
        Require(Mathf.Abs(Mathf.DeltaAngle(pitch, peakPitch)) > .1f, "Recoil smoothly moves the actual aim within 45ms");
        float muzzleDepth = view.transform.InverseTransformPoint(muzzle.position).z;
        Frame(gun, .045f, new WeaponInputFrame(false, false, aimHeld: true));
        Require(view.transform.InverseTransformPoint(muzzle.position).z < muzzleDepth - .003f, "Weapon springs rearward after the shot");
        Vector3 point = WeaponAimSolver.AimPoint(player.gameObject, view.transform, gun.EquippedData.Range, ~0);
        Require(Vector3.Angle(muzzle.forward, point - muzzle.position) < .1f, "ADS barrel remains aligned with camera aim during recoil");
        Capture(view, "ranged-recoil-peak");
        typeof(PlayerLook).GetMethod("TickRecoil", Any).Invoke(look, new object[] { .4f });
        Require(Mathf.Abs(Mathf.DeltaAngle(pitch, view.transform.eulerAngles.x)) < Mathf.Abs(Mathf.DeltaAngle(pitch, peakPitch)) * .5f,
            "Recoil recovers most of the kick while retaining controlled aim climb");
        float beforeClear = view.transform.eulerAngles.x;
        look.ClearRecoil();
        typeof(PlayerLook).GetMethod("TickRecoil", Any).Invoke(look, new object[] { .01f });
        Require(Mathf.Abs(Mathf.DeltaAngle(beforeClear, view.transform.eulerAngles.x)) < .001f, "Clearing recoil preserves the visible aim");
        Frame(gun, .3f, default);
        typeof(PlayerCameraEffects).GetMethod("UpdateDynamicFov", Any).Invoke(effects, new object[] { .02f });
        Require(Mathf.Abs(view.fieldOfView - hipFov) < 1f
            && dot.gameObject.activeSelf == (profile.showDot && profile.dotSize > 0)
            && parts.gameObject.activeSelf == (profile.showParts && profile.partCount > 0),
            "Releasing ADS restores normal FOV and the user's crosshair");
        Frame(gun, .01f, new WeaponInputFrame(false, false, true));
        Require(gun.Runtime.IsReloading, "R begins native reload");
        var abilities = (MovementAbilities)typeof(PlayerMovement).GetProperty("Abilities", Any).GetValue(player);
        abilities.Dodge.Reset();
        Require(abilities.Dodge.TryStart(DodgeKind.Micro, -1, .2f, .3f), "Q/E micro window starts during reload");
        Frame(gun, .01f, default);
        Require(gun.Runtime.IsReloading, "Q/E peeking does not cancel a tactical reload");
        abilities.Dodge.Reset();
        abilities.Dodge.TryStart(DodgeKind.Step, 0, .2f, .3f);
        Frame(gun, .01f, new WeaponInputFrame(false, false, true));
        Require(!gun.Runtime.IsReloading && gun.Runtime.ReserveAmmo == gun.EquippedData.InitialReserveAmmo, "Actual dodge activation cancels reload without consuming reserve");
        abilities.Dodge.Reset();
        Frame(gun, .01f, new WeaponInputFrame(false, false, true));
        typeof(PlayerCombatActions).GetMethod("TryShove", Any).Invoke(abilities.Combat, null);
        Frame(gun, .01f, default);
        Require(!gun.Runtime.IsReloading, "Actual armed shove cancels reload");
        Frame(gun, .3f, default);
        Frame(gun, .01f, new WeaponInputFrame(false, false, true));
        Frame(gun, 3f, default);
        int ammo = gun.Runtime.Ammo, reserve = gun.Runtime.ReserveAmmo;
        Frame(gun, .3f, new WeaponInputFrame(false, false, aimHeld: true));
        slots.SelectSlot(1); slots.SelectSlot(0);
        Require(dot.gameObject.activeSelf == (profile.showDot && profile.dotSize > 0)
            && parts.gameObject.activeSelf == (profile.showParts && profile.partCount > 0), "Switching weapons cannot leave the crosshair hidden");
        Require(gun.Runtime.Ammo == ammo && gun.Runtime.ReserveAmmo == reserve, "Switching slots preserves magazine and finite reserve");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Near wall";
        wall.transform.position = view.transform.position + view.transform.forward * .4f;
        wall.transform.rotation = view.transform.rotation; wall.transform.localScale = new Vector3(3, 3, .1f);
        Physics.SyncTransforms(); Frame(gun, .3f, new WeaponInputFrame(true, true, aimHeld: true));
        Require(gun.IsHighReady && gun.Runtime.Ammo == ammo, "Native scene wall raises the gun and blocks fire");
        for (int i = 0; i < 30; i++)
        {
            Frame(gun, 1f / 60f, new WeaponInputFrame(false, false, aimHeld: true));
            Require(gun.IsHighReady && gun.AimBlend == 0f && view.fieldOfView >= hipFov - 1f,
                "Holding RMB at a wall cannot zoom or flicker out of high ready");
        }
        Require(dot.gameObject.activeSelf == (profile.showDot && profile.dotSize > 0)
            && parts.gameObject.activeSelf == (profile.showParts && profile.partCount > 0), "High ready restores the hip crosshair");
        wall.GetComponent<Renderer>().enabled = false;
        Capture(view, "ranged-high-ready");
        wall.SetActive(false); Physics.SyncTransforms();
        for (int i = 0; i < 30; i++) Frame(gun, 1f / 60f, new WeaponInputFrame(false, false, aimHeld: true));
        Require(!gun.IsHighReady && gun.AimBlend == 1f && Mathf.Abs(view.fieldOfView - gun.EquippedData.AdsFov) < 1f,
            "Leaving cover restores held ADS after lowering the gun, in the same frame as the weapon pose");
        CheckAimFollow(player, gun, view, muzzle);
        Debug.Log("RANGED_NATIVE: muzzle=" + muzzle.name + " reserve=" + reserve + " ADS FOV=" + view.fieldOfView);
    }
    private static void CheckAimFollow(PlayerMovement player, WeaponController gun, Camera view, Transform muzzle)
    {
        Frame(gun, .7f, default);
        var near = GameObject.CreatePrimitive(PrimitiveType.Cube);
        near.name = "Aim depth transition target";
        near.transform.position = view.transform.position + view.transform.forward * 2f;
        near.transform.rotation = view.transform.rotation; near.transform.localScale = new Vector3(1.5f, 1.5f, .05f);
        var receiver = near.AddComponent<WeaponTrainingTarget>(); receiver.ResetTarget(); Physics.SyncTransforms();
        Frame(gun, 1f / 60f, default);
        Vector3 target = WeaponAimSolver.AimPoint(player.gameObject, view.transform, gun.EquippedData.Range, ~0);
        float initialError = Vector3.Angle(muzzle.forward, target - muzzle.position);
        Require(!gun.IsHighReady && initialError > .1f, "A sudden near target starts a visible convergence transition");
        Capture(view, "ranged-aim-follow-start");
        // The visual delay must not delay the hitscan target or spend ammunition on stale intent.
        Frame(gun, 1f / 60f, new WeaponInputFrame(true, true));
        Require(receiver.Health < 1000f && gun.LastShot.Trace.Hit.collider.gameObject == near,
            "Hitscan immediately hits the new aimed target while the model is still following");
        for (int i = 0; i < 10; i++) Frame(gun, 1f / 60f, default);
        Capture(view, "ranged-aim-follow-settling");
        Frame(gun, .7f, default);
        target = WeaponAimSolver.AimPoint(player.gameObject, view.transform, gun.EquippedData.Range, ~0);
        Require(Vector3.Angle(muzzle.forward, target - muzzle.position) < .1f, "Native model settles on the current aim point");
        // Even with a recently smoothed nearby point, a wall must suppress fire and ADS immediately.
        near.transform.position = view.transform.position + view.transform.forward * .35f; Physics.SyncTransforms();
        int ammo = gun.Runtime.Ammo;
        Frame(gun, 1f / 60f, new WeaponInputFrame(true, true, aimHeld: true));
        Require(gun.IsHighReady && gun.AimBlend == 0f && gun.Runtime.Ammo == ammo,
            "Aim filtering never delays near-wall safety or high-ready zoom suppression");
        Object.DestroyImmediate(near); Physics.SyncTransforms();
    }
    private static void CheckLeanAds(PlayerMovement player, WeaponController gun, PlayerCameraEffects effects, Camera view, Transform muzzle)
    {
        var abilities = (MovementAbilities)typeof(PlayerMovement).GetProperty("Abilities", Any).GetValue(player);
        var lean = abilities.Lean; var dodge = abilities.Dodge;
        var leanValue = typeof(MovementLean).GetField("lean", Any);
        foreach (int side in new[] { -1, 1 })
        {
            dodge.Reset();
            var impulse = (Vector3)typeof(PlayerCameraEffects).GetField("impulseRotVelocity", Any).GetValue(effects);
            float fovImpulse = (float)typeof(PlayerCameraEffects).GetField("impulseFovVelocity", Any).GetValue(effects);
            Require(dodge.TryStart(DodgeKind.Micro, side, .2f, .3f), "Q/E micro dodge starts");
            Require((Vector3)typeof(PlayerCameraEffects).GetField("impulseRotVelocity", Any).GetValue(effects) == impulse
                && (float)typeof(PlayerCameraEffects).GetField("impulseFovVelocity", Any).GetValue(effects) == fovImpulse,
                "Micro dodge adds no step-dodge rotation or FOV punch");
            leanValue.SetValue(lean, (float)side);
            typeof(PlayerCameraEffects).GetMethod("ApplyCameraRotation", Any).Invoke(effects, null);
            typeof(PlayerCameraEffects).GetMethod("ApplyLeanPosition", Any).Invoke(effects, null);
            Frame(gun, 1f / 60f, new WeaponInputFrame(false, false, aimHeld: true));
            Require(gun.AimBlend == 1f && Mathf.Abs(view.fieldOfView - gun.EquippedData.AdsFov) < 1f,
                "Q/E keeps ADS fully zoomed while its micro invulnerability window is active");
            Vector3 local = view.transform.InverseTransformPoint(muzzle.position);
            Require(Mathf.Abs(local.x) < .01f && Mathf.Abs(local.y + gun.EquippedData.SightHeight) < .01f,
                "Both lean directions keep the weapon sight centered on the camera");
            Capture(view, side < 0 ? "ranged-ads-lean-left" : "ranged-ads-lean-right");
        }
        dodge.Reset(); leanValue.SetValue(lean, 0f);
        typeof(PlayerCameraEffects).GetMethod("ApplyCameraRotation", Any).Invoke(effects, null);
        typeof(PlayerCameraEffects).GetMethod("ApplyLeanPosition", Any).Invoke(effects, null);
        Frame(gun, .01f, new WeaponInputFrame(false, false, aimHeld: true));
    }
    private static void Capture(Camera camera, string name)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var previous = camera.targetTexture; var active = RenderTexture.active;
        var rt = new RenderTexture(1280, 720, 24); camera.targetTexture = rt;
        try
        {
            camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.GetFullPath(name + ".png"), image.EncodeToPNG()); Object.DestroyImmediate(image);
        }
        finally { camera.targetTexture = previous; RenderTexture.active = active; rt.Release(); Object.DestroyImmediate(rt); }
    }
}
