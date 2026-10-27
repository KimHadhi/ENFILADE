using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class WeaponFoundationChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int passed;
    [MenuItem("Tools/Combat/Run Foundation Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Scene previous = SceneManager.GetActiveScene();
        if (Application.isBatchMode && string.IsNullOrEmpty(previous.path)) EditorSceneManager.SaveScene(previous, ProjectAssetPaths.DevelopmentScene("CombatCheckHost.unity"));
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); passed = 0;
        try
        {
            Check("semi-auto fires only fresh presses and discards presses during cooldown", SemiAutomatic);
            Check("automatic RPM stays consistent at 30/60/120Hz", AutomaticTiming);
            Check("magazine cannot go negative and shared assets never share ammo", AmmoIsolation);
            Check("reload completes on its timer, blocks shots, and can be interrupted", ReloadTiming);
            Check("pause and long frame safety prevent burst backlog", TimerSafety);
            Check("controller ray damages a parent receiver once with a fresh ID per bullet", ControllerDamage);
            Check("first wall blocks damage and trace ends at that wall", Cover);
            Check("owner exclusion preserves same-layer enemies and handles more than 32 hits", OwnerAndSaturation);
            Check("range limits and triggers produce correct miss/hit events", RangeAndTriggers);
            Check("combat map owns its lifetime independently of movement input", InputIsolation);
            Check("disable interrupts reload without refilling the magazine", DisableReload);
        }
        finally { if (previous.IsValid()) SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
        Debug.Log("WEAPON_FOUNDATION_CHECKS_PASSED: " + passed);
    }
    private static void Check(string name, Action test) { test(); passed++; Debug.Log("PASS WEAPON: " + name); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Any).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Any).Invoke(target, null);
    private static WeaponData Data(WeaponFireMode mode = WeaponFireMode.Automatic, int magazine = 30)
    {
        var data = ScriptableObject.CreateInstance<WeaponData>();
        Set(data, "fireMode", mode); Set(data, "magazineSize", magazine); Set(data, "reloadDuration", 1f);
        // These are trace/ownership tests; accuracy is covered separately with seeded spread samples.
        Set(data, "hipSpread", 0f); Set(data, "bloomPerShot", 0f);
        return data;
    }
    private static readonly WeaponInputFrame Press = new WeaponInputFrame(true, true);
    private static readonly WeaponInputFrame Hold = new WeaponInputFrame(false, true);
    private static readonly WeaponInputFrame Reload = new WeaponInputFrame(false, false, true);

    private static void SemiAutomatic()
    {
        var data = Data(WeaponFireMode.SemiAutomatic);
        try
        {
            var gun = new WeaponRuntime(data); Require(gun.Tick(0.01f, Press) == 1, "First press fires");
            Require(gun.Tick(0.01f, Press) == 0, "Early press cannot bypass RPM");
            for (int i = 0; i < 100; i++) Require(gun.Tick(0.01f, Hold) == 0, "Held semi-auto cannot repeat or queue an early press");
            Require(gun.Tick(0.01f, Press) == 1 && gun.Ammo == 28, "Fresh press after cooldown fires once");
        }
        finally { Object.DestroyImmediate(data); }
    }
    private static void AutomaticTiming()
    {
        var data = Data(magazine: 500);
        try
        {
            foreach (int hz in new[] { 30, 60, 120 })
            {
                var gun = new WeaponRuntime(data); int shots = 0;
                for (int i = 0; i < hz * 5; i++) shots += gun.Tick(1f / hz, i == 0 ? Press : Hold);
                Require(Mathf.Abs(shots - 50) <= 1, "600 RPM over 5s at " + hz + "Hz: " + shots);
            }
        }
        finally { Object.DestroyImmediate(data); }
    }
    private static void AmmoIsolation()
    {
        var data = Data(magazine: 3);
        try
        {
            var first = new WeaponRuntime(data); var second = new WeaponRuntime(data); int shots = 0;
            for (int i = 0; i < 100; i++) shots += first.Tick(0.02f, Hold);
            Require(shots == 3 && first.Ammo == 0 && second.Ammo == 3 && data.MagazineSize == 3, "Ammo belongs to an instance, never to WeaponData");
        }
        finally { Object.DestroyImmediate(data); }
    }
    private static void ReloadTiming()
    {
        var data = Data();
        try
        {
            var gun = new WeaponRuntime(data); gun.Tick(0.01f, Press);
            Require(gun.Tick(0.01f, new WeaponInputFrame(true, true, true)) == 0 && gun.IsReloading, "Reload wins simultaneous input");
            Require(gun.Tick(0.9f, Hold) == 0 && gun.Ammo == 29, "No firing or early ammo grant while reloading");
            gun.Tick(0.11f, default); Require(!gun.IsReloading && gun.Ammo == 30, "Time alone finishes reload");
            gun.Tick(0.01f, Press); gun.Tick(0.01f, Reload); gun.Interrupt(); gun.Tick(2f, default);
            Require(gun.Ammo == 29 && !gun.IsReloading, "Interrupted reload never grants ammo");
        }
        finally { Object.DestroyImmediate(data); }
    }
    private static void TimerSafety()
    {
        var data = Data(magazine: 500);
        try
        {
            var gun = new WeaponRuntime(data);
            Require(gun.Tick(0f, Press) == 0 && gun.Ammo == 500, "Pause cannot fire");
            gun.Tick(100f, default); Require(gun.Tick(0.01f, Press) == 1, "Idle time never banks bullets");
            Require(gun.Tick(10f, Hold) == WeaponRuntime.MaxShotsPerUpdate, "Stall catch-up is bounded");
            Require(gun.Tick(0.001f, Hold) == 0, "Remaining stall debt is discarded");
            gun.Tick(0.01f, Reload); float remaining = gun.ReloadRemaining; gun.Tick(0f, Hold);
            Require(gun.ReloadRemaining == remaining, "Paused reload timer is unchanged");
        }
        finally { Object.DestroyImmediate(data); }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root, Owner;
        public readonly WeaponData Data;
        public readonly WeaponController Gun;
        public readonly Vector3 Origin = new Vector3(4000f, 4000f, 4000f);
        public Fixture()
        {
            Root = new GameObject("Weapon check fixtures"); Owner = new GameObject("Weapon owner"); Owner.transform.SetParent(Root.transform);
            Owner.transform.position = Origin; Owner.AddComponent<PlayerWeaponInput>();
            var camera = new GameObject("Aim"); camera.transform.SetParent(Owner.transform, false); camera.AddComponent<Camera>();
            Data = WeaponFoundationChecks.Data(); Gun = Owner.AddComponent<WeaponController>();
            Set(Gun, "weaponData", Data); Call(Gun, "Initialize");
        }
        public GameObject Box(float z, Transform parent = null, bool trigger = false)
        {
            var go = new GameObject("Surface " + z); go.transform.SetParent(parent != null ? parent : Root.transform);
            go.transform.position = Origin + Vector3.forward * z;
            var collider = go.AddComponent<BoxCollider>(); collider.size = new Vector3(0.5f, 0.5f, 0.05f); collider.isTrigger = trigger;
            Physics.SyncTransforms(); return go;
        }
        public WeaponTrainingTarget Target(float z)
        {
            var targetRoot = new GameObject("Damage receiver"); targetRoot.transform.SetParent(Root.transform); targetRoot.transform.position = Origin + Vector3.forward * z;
            var target = targetRoot.AddComponent<WeaponTrainingTarget>(); target.ResetTarget(); Box(z, targetRoot.transform); return target;
        }
        public void Fire(float dt = 0.11f) => Gun.GetType().GetMethod("ProcessFrame", Any).Invoke(Gun, new object[] { dt, Press });
        public void Dispose() { Object.DestroyImmediate(Root); Object.DestroyImmediate(Data); }
    }
    private static void ControllerDamage()
    {
        using (var f = new Fixture())
        {
            var target = f.Target(5f); int events = 0; f.Gun.ShotFired += _ => events++;
            f.Fire(); long first = f.Gun.LastShot.AttackId; f.Fire();
            Require(target.Health == 960f && events == 2 && f.Gun.Runtime.Ammo == 28, "Each shot hits parent receiver and consumes one round");
            Require(f.Gun.LastShot.AttackId != first && f.Gun.LastShot.Trace.Damage == DamageResult.Damaged, "Every bullet has a new attack ID");
        }
    }
    private static void Cover()
    {
        using (var f = new Fixture())
        {
            var wall = f.Box(3f); var target = f.Target(5f); f.Fire();
            Require(target.Health == 1000f && f.Gun.LastShot.Trace.Hit.collider.gameObject == wall, "Wall blocks target damage");
            Require(f.Gun.LastShot.Trace.End.z < f.Origin.z + 3.1f && f.Gun.Runtime.Ammo == 29, "Debug/near-miss segment stops at cover, and the shot costs ammo");
        }
    }
    private static void OwnerAndSaturation()
    {
        using (var f = new Fixture())
        {
            f.Owner.layer = 31; f.Box(1f, f.Owner.transform).layer = 31;
            var target = f.Target(5f); target.GetComponentInChildren<Collider>().gameObject.layer = 31;
            Physics.SyncTransforms(); f.Fire(); Require(target.Health == 980f, "Only owner is excluded, not all colliders on its layer");
            for (int i = 0; i < 40; i++) f.Box(0.15f + i * 0.07f, f.Owner.transform);
            var wall = f.Box(3.4f); f.Fire();
            Require(target.Health == 980f && f.Gun.LastShot.Trace.Hit.collider.gameObject == wall,
                "Saturated owner-skip buffer cannot omit the nearest external wall");
        }
    }
    private static void RangeAndTriggers()
    {
        using (var f = new Fixture())
        {
            Set(f.Data, "range", 3f); var target = f.Target(5f); f.Fire();
            Require(!f.Gun.LastShot.Trace.HasHit && target.Health == 1000f, "Targets beyond range cannot be damaged");
            Set(f.Data, "range", 10f); f.Box(2f, trigger: true); f.Fire();
            Require(target.Health == 980f, "Trigger does not block a solid target");
        }
    }
    private static void InputIsolation()
    {
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Project/Input/Player.inputactions");
        Require(asset != null && asset.FindAction("Combat/Fire") != null && asset.FindAction("Combat/Reload") != null, "Combat actions exist alongside Player");
        var go = new GameObject("Input check");
        try
        {
            var input = go.AddComponent<PlayerWeaponInput>(); Set(input, "inputActions", asset); Call(input, "OnEnable");
            var own = (InputActionMap)typeof(PlayerWeaponInput).GetField("ownedMap", Any).GetValue(input);
            Require(own != null && own.enabled && !ReferenceEquals(own, asset.FindActionMap("Combat")), "Input owns a cloned map");
            var frame = input.ReadInput(); Require(!frame.FirePressed && !frame.FireHeld, "Input is gated until focus/cursor and release are established");
            bool movementEnabled = asset.FindActionMap("Player").enabled; Call(input, "OnDisable");
            Require(asset.FindActionMap("Player").enabled == movementEnabled, "Disabling combat never disables movement input");
        }
        finally { Object.DestroyImmediate(go); }
    }
    private static void DisableReload()
    {
        using (var f = new Fixture())
        {
            f.Fire(); f.Gun.Runtime.Tick(0.01f, Reload); Call(f.Gun, "OnDisable"); f.Gun.Runtime.Tick(2f, default);
            Require(!f.Gun.Runtime.IsReloading && f.Gun.Runtime.Ammo == 29, "Disabling a controller preserves ammo and cancels reload");
        }
    }
}
