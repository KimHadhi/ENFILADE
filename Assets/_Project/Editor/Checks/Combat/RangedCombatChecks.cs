using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class RangedCombatChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly WeaponInputFrame Press = new WeaponInputFrame(true, true);
    private static readonly WeaponInputFrame Hold = new WeaponInputFrame(false, true);
    private static readonly WeaponInputFrame Reload = new WeaponInputFrame(false, false, true);
    private static void Set(object o, string field, object value) => o.GetType().GetField(field, Any).SetValue(o, value);
    private static void Call(object o, string method) => o.GetType().GetMethod(method, Any).Invoke(o, null);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Combat/Run Ranged Combat Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit play mode first.");
        Scene previous = SceneManager.GetActiveScene();
        if (Application.isBatchMode && string.IsNullOrEmpty(previous.path))
            EditorSceneManager.SaveScene(previous, ProjectAssetPaths.DevelopmentScene("RangedCheckHost.unity"));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            ReloadAndReserve(); BurstTiming(); MuzzleAndCover(); HighReady(); Headshots(); AccuracyAndAds(); SurvivalCancel(); LeanVolumes(); RecoilTiming();
            WeaponAimFollowChecks.Run();
            Debug.Log("RANGED_COMBAT_CHECKS_PASSED: 9 groups");
        }
        finally { if (previous.IsValid()) SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
    }
    public static void RunBatch() { WeaponSlotsMeleeChecks.RunBatch(); Run(); }
    private static void RecoilTiming()
    {
        var spring = new RecoilSpring(); spring.Kick(Vector3.right, 24f);
        Require(spring.Value == Vector3.zero, "A recoil impulse cannot teleport the view");
        spring.Tick(1f / 24f, 24f);
        Require(Mathf.Abs(spring.Value.x - 1f) < .0001f, "A single kick reaches the authored peak smoothly");
        spring.Tick(.5f, 24f);
        Require(spring.Value.x >= 0f && spring.Value.x < .001f, "Recoil settles without oscillating below the aim");
        Vector3 reference = Vector3.zero;
        foreach (int hz in new[] { 30, 60, 120 })
        {
            spring.Reset();
            for (int shot = 0; shot < 10; shot++)
            {
                spring.Kick(new Vector3(1f, .3f, 0f), 24f);
                for (int frame = 0; frame < hz / 10; frame++) spring.Tick(1f / hz, 24f);
            }
            if (hz == 30) reference = spring.Value;
            else Require(Vector3.Distance(reference, spring.Value) < .0001f, "Automatic recoil response is consistent at " + hz + "Hz");
            var beforePause = spring.Value; spring.Tick(0f, 24f);
            Require(spring.Value == beforePause, "Pause cannot advance recoil");
            spring.Tick(5f, 24f);
            Require(spring.Value == Vector3.zero && spring.Velocity == Vector3.zero, "A long frame settles without spring explosion");
        }
    }
    private static void ReloadAndReserve()
    {
        var data = ScriptableObject.CreateInstance<WeaponData>();
        Set(data, "reloadDuration", 1f); Set(data, "emptyReloadDuration", 2f);
        try
        {
            var gun = new WeaponRuntime(data, 24, 3);
            gun.Tick(.01f, Reload); gun.Tick(.99f, default);
            Require(gun.Ammo == 24 && gun.ReserveAmmo == 3, "Tactical reload must not move ammo early");
            gun.Interrupt(); gun.Tick(2f, default);
            Require(gun.Ammo == 24 && gun.ReserveAmmo == 3, "Interrupted reload preserves both pools");
            gun.Tick(.01f, Reload); gun.Tick(1.01f, default);
            Require(gun.Ammo == 27 && gun.ReserveAmmo == 0, "Partial reserve fills only the available rounds");
            gun.Tick(.01f, Reload); Require(!gun.IsReloading, "No reserve cannot begin a reload");
            gun = new WeaponRuntime(data, 0, 40); gun.Tick(.01f, Reload); gun.Tick(1.1f, default);
            Require(gun.IsEmptyReload && gun.IsReloading && gun.Ammo == 0, "Empty reload is slower");
            gun.Tick(.91f, default); Require(gun.Ammo == 30 && gun.ReserveAmmo == 10, "Empty reload transfers once");
            gun.AddReserve(5); Require(gun.ReserveAmmo == 15, "Ammo pickup API adds reserve");
        }
        finally { Object.DestroyImmediate(data); }
    }
    private static void BurstTiming()
    {
        var data = ScriptableObject.CreateInstance<WeaponData>(); Set(data, "fireMode", WeaponFireMode.Burst);
        try
        {
            foreach (int hz in new[] { 30, 60, 120 })
            {
                var gun = new WeaponRuntime(data); int shots = gun.Tick(1f / hz, Press);
                for (int i = 0; i < hz; i++) shots += gun.Tick(1f / hz, Hold);
                Require(shots == 3 && gun.Ammo == 27, "Burst fires three rounds without repeating on hold at " + hz);
                Require(gun.Tick(.01f, Press) == 1, "Fresh press starts next burst");
                gun.Interrupt(); Require(gun.Tick(1f, default) == 0, "Interrupt discards queued burst rounds");
                gun = new WeaponRuntime(data, 2); shots = gun.Tick(.01f, Press) + gun.Tick(.3f, default);
                Require(shots == 2 && gun.Ammo == 0, "A short magazine cannot overdraw burst ammunition");
            }
        }
        finally { Object.DestroyImmediate(data); }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root, Owner;
        public readonly Transform View, Muzzle, Model;
        public readonly WeaponData Data;
        public readonly WeaponController Gun;
        public readonly Vector3 Origin = new Vector3(12000, 12000, 12000);
        public Fixture()
        {
            Root = new GameObject("Ranged checks"); Owner = new GameObject("Player"); Owner.transform.SetParent(Root.transform);
            Owner.transform.position = Origin; Owner.AddComponent<PlayerWeaponInput>();
            View = new GameObject("View").transform; View.SetParent(Owner.transform, false); View.localPosition = Vector3.up * 1.5f;
            View.gameObject.AddComponent<Camera>();
            Model = new GameObject("Gun").transform; Model.SetParent(View, false);
            Muzzle = new GameObject("Muzzle").transform; Muzzle.SetParent(Model, false); Muzzle.localPosition = new Vector3(.3f, -.18f, .8f);
            Data = ScriptableObject.CreateInstance<WeaponData>(); Set(Data, "hipSpread", 0f); Set(Data, "bloomPerShot", 0f);
            Gun = Owner.AddComponent<WeaponController>(); Set(Gun, "weaponData", Data); Set(Gun, "muzzlePoint", Muzzle);
            Set(Gun, "weaponRoot", Model); Set(Gun, "logShots", false); Call(Gun, "Initialize");
        }
        public GameObject Box(Vector3 local, Vector3 size)
        {
            var go = new GameObject("Surface"); go.transform.SetParent(Root.transform); go.transform.position = Origin + local;
            go.AddComponent<BoxCollider>().size = size; Physics.SyncTransforms(); return go;
        }
        public WeaponTrainingTarget Target()
        {
            var go = Box(new Vector3(0, 1.5f, 6), new Vector3(.4f, .4f, .1f));
            var result = go.AddComponent<WeaponTrainingTarget>(); result.ResetTarget(); return result;
        }
        public void Frame(float dt, WeaponInputFrame input) => typeof(WeaponController).GetMethod("ProcessFrame", Any).Invoke(Gun, new object[] { dt, input });
        public void Dispose() { Object.DestroyImmediate(Root); Object.DestroyImmediate(Data); }
    }
    private static void MuzzleAndCover()
    {
        using (var f = new Fixture())
        {
            var target = f.Target(); f.Frame(.1f, Press);
            Require(target.Health == 980f && f.Gun.LastShot.Origin.x > f.Origin.x + .2f, "Off-center muzzle converges on camera target");
            Require(Vector3.Angle(f.Muzzle.forward, f.Gun.LastShot.Direction) < .1f, "Model barrel follows the aim point");
            var cover = f.Box(new Vector3(.24f, 1.36f, 2), new Vector3(.3f, .15f, .15f));
            f.Frame(.11f, Press);
            Require(f.Gun.LastShot.Trace.Hit.collider.gameObject == cover && target.Health == 980f,
                "Low cover blocks muzzle shot even when the camera can see the target");
        }
    }
    private static void HighReady()
    {
        using (var f = new Fixture())
        {
            var target = f.Target(); var wall = f.Box(new Vector3(0, 1.5f, .4f), new Vector3(3, 3, .1f));
            f.Frame(.1f, Press);
            Require(f.Gun.IsHighReady && f.Gun.Runtime.Ammo == 30 && target.Health == 1000f, "Near wall raises gun without ammo loss or backwards fire");
            Object.DestroyImmediate(wall); Physics.SyncTransforms(); f.Frame(.3f, default); f.Frame(.11f, Press);
            Require(!f.Gun.IsHighReady && target.Health == 980f, "Gun lowers and resumes after leaving cover");
            var embedded = f.Box(f.Muzzle.position - f.Origin, Vector3.one * .1f);
            f.Frame(.11f, Press); Require(f.Gun.IsHighReady && f.Gun.Runtime.Ammo == 29, "Embedded muzzle never fires out through a wall");
        }
        var model = new GameObject("Model without a muzzle");
        try
        {
            model.transform.localPosition = new Vector3(.2f, -.3f, .4f);
            model.transform.localRotation = Quaternion.Euler(0, 10, 0);
            var pose = new WeaponPresentation(model.transform, null); pose.Reset();
            Require(model.transform.localPosition == new Vector3(.2f, -.3f, .4f)
                && Quaternion.Angle(model.transform.localRotation, Quaternion.Euler(0, 10, 0)) < .01f,
                "An unfinished model without a muzzle preserves its authored pose");
        }
        finally { Object.DestroyImmediate(model); }
        using (var f = new Fixture())
        {
            f.Target();
            f.Frame(.3f, new WeaponInputFrame(false, false, aimHeld: true));
            Vector3 adsMuzzle = f.Muzzle.position;
            f.Frame(.3f, default);
            var obstruction = f.Box(adsMuzzle - f.Origin, Vector3.one * .04f);
            f.Frame(.1f, default);
            Require(!f.Gun.IsHighReady, "Small sight-line obstruction leaves the hip muzzle clear");
            for (int i = 0; i < 60; i++)
            {
                f.Frame(1f / 60f, new WeaponInputFrame(false, false, aimHeld: true));
                Require(f.Gun.IsHighReady && f.Gun.AimBlend == 0f,
                    "An obstructed ADS-only pose cannot alternate between zoom and high ready");
            }
            Object.DestroyImmediate(obstruction); Physics.SyncTransforms();
            for (int i = 0; i < 60; i++) f.Frame(1f / 60f, new WeaponInputFrame(false, false, aimHeld: true));
            Require(!f.Gun.IsHighReady && f.Gun.AimBlend == 1f, "ADS resumes once its requested muzzle position clears");
        }
    }
    private static void Headshots()
    {
        using (var f = new Fixture())
        {
            var target = f.Target(); var zone = target.gameObject.AddComponent<WeaponHitRegion>(); Set(zone, "region", WeaponHitRegion.Region.Head);
            f.Frame(.11f, Press); Require(target.Health == 960f && f.Gun.LastShot.Trace.IsHeadshot, "Headshots apply weapon multiplier");
            Set(zone, "region", WeaponHitRegion.Region.Body); f.Frame(.11f, Press);
            Require(target.Health == 940f && !f.Gun.LastShot.Trace.IsHeadshot, "Body hits use base damage");
        }
    }
    private static void AccuracyAndAds()
    {
        using (var f = new Fixture())
        {
            Set(f.Data, "hipSpread", 2f); Set(f.Data, "bloomPerShot", .5f);
            f.Frame(.01f, Press); Require(f.Gun.CurrentSpread > 2f, "Shots expand bloom");
            f.Frame(1f, default); float hip = f.Gun.CurrentSpread;
            f.Frame(.2f, new WeaponInputFrame(false, false, aimHeld: true));
            Require(f.Gun.AimBlend == 1f && f.Gun.CurrentSpread < hip * .2f && f.Gun.MovementMultiplier < .7f, "ADS tightens accuracy and movement");
            var local = f.View.InverseTransformPoint(f.Muzzle.position);
            Require(Mathf.Abs(local.x) < .01f && Mathf.Abs(local.y + f.Data.SightHeight) < .01f, "ADS centers the barrel and sight");
            for (int i = 0; i < 360; i++)
            {
                var disk = new Vector2(Mathf.Cos(i), Mathf.Sin(i));
                var direction = WeaponAimSolver.SpreadDirection(Vector3.forward, 3f, disk);
                Require(Mathf.Abs(Vector3.Angle(Vector3.forward, direction) - 3f) < .01f, "Spread samples stay on the configured cone");
            }
        }
    }
    private static void SurvivalCancel()
    {
        using (var f = new Fixture())
        {
            f.Frame(.01f, Press); f.Frame(.01f, Reload); int reserve = f.Gun.Runtime.ReserveAmmo;
            f.Gun.InterruptForSurvival(true); f.Frame(.01f, Reload);
            Require(!f.Gun.Runtime.IsReloading && f.Gun.Runtime.ReserveAmmo == reserve, "Shove wins simultaneous reload input");
            f.Frame(.01f, Press); Require(f.Gun.Runtime.Ammo == 29, "Missed shove has brief recovery");
            f.Gun.ConfirmShoveHit(); f.Frame(.11f, Press); Require(f.Gun.Runtime.Ammo == 28, "Shove impact enables immediate follow-up fire");
            f.Frame(.01f, Reload); f.Gun.InterruptForSurvival(); f.Frame(.01f, Reload);
            Require(!f.Gun.Runtime.IsReloading && f.Gun.Runtime.Ammo == 28, "Dodge cancels reload and prevents same-frame restart");
        }
    }
    public static void LeanVolumes()
    {
        var root = new GameObject("Leaning combat body"); root.transform.position = new Vector3(1000, 50, 1000);
        try
        {
            var cc = root.AddComponent<CharacterController>(); cc.height = 2; cc.radius = .2f; cc.center = Vector3.zero; cc.skinWidth = .03f;
            if (Application.isPlaying) { cc.Move(Vector3.forward * .1f); cc.Move(Vector3.back * .1f); }
            Physics.SyncTransforms();
            bool originalBodyRay = cc.Raycast(new Ray(root.transform.position + new Vector3(0, .6f, -3), Vector3.forward), out _, 6);
            var lean = root.AddComponent<PlayerLeanHitboxes>();
            if (!Application.isPlaying) Call(lean, "Awake");
            lean.Sync(Vector3.right * .4f);
            var origin = root.transform.position + new Vector3(.4f, .6f, -3);
            Require(AttackTrace.FirstExternalHit(null, origin, Vector3.forward, 6, ~0, out var upper)
                && upper.collider.GetComponent<PlayerCombatHitbox>() != null, "Leaning upper body is hittable outside movement capsule");
            origin.x -= .4f;
            Require(!AttackTrace.FirstExternalHit(null, origin, Vector3.forward, 6, ~0, out _), "Old upper-body location no longer absorbs shots");
            origin.y -= 1f;
            Require(AttackTrace.FirstExternalHit(null, origin, Vector3.forward, 6, ~0, out _), "Lower body stays planted");
            lean.enabled = false; Call(lean, "OnDisable"); Physics.SyncTransforms(); origin.y += 1f;
            Require(!lean.UsesSplitBody && cc.enabled, "Disabling split volumes keeps locomotion active");
            // CharacterController's native collision shape is registered only in play mode.
            if (Application.isPlaying)
            {
                bool hit = AttackTrace.FirstExternalHit(null, origin, Vector3.forward, 6, ~0, out var restored);
                Require(hit && restored.collider == cc, "Disabling split volumes restores the physical capsule for damage: hit="
                    + hit + " collider=" + (hit ? restored.collider.name : "none") + " origin=" + origin + " bounds=" + cc.bounds
                    + " originalBodyRay=" + originalBodyRay + " bodyRay=" + cc.Raycast(new Ray(origin, Vector3.forward), out _, 6));
            }
        }
        finally { Object.DestroyImmediate(root); }
    }
}
