using System;
using UnityEngine;

public readonly struct WeaponShot
{
    public readonly long AttackId;
    public readonly Vector3 Origin, Direction;
    public readonly AttackTraceResult Trace;
    public readonly Vector2 Recoil;
    public readonly int AmmoAfterShot;
    public WeaponShot(long id, Vector3 origin, Vector3 direction, AttackTraceResult trace, Vector2 recoil, int ammo)
    { AttackId = id; Origin = origin; Direction = direction; Trace = trace; Recoil = recoil; AmmoAfterShot = ammo; }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public sealed class WeaponController : MonoBehaviour
{
    [SerializeField] private WeaponData weaponData;
    [Tooltip("Optional MonoBehaviour implementing IWeaponInput; otherwise finds one on this object.")]
    [SerializeField] private MonoBehaviour inputSource;
    [Tooltip("Optional. Auto-finds the player's Camera; rays originate at the view centre.")]
    [SerializeField] private Transform aimOrigin;
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private bool logShots = true;
    [SerializeField] private bool drawRays = true;
    public WeaponRuntime Runtime { get; private set; }
    public WeaponData EquippedData => Runtime != null ? Runtime.Data : null;
    public WeaponShot LastShot { get; private set; }
    public event Action<WeaponShot> ShotFired;
    private IWeaponInput input;
    private PlayerDamageReceiver health;
    private GameObject owner;
    [NonSerialized] private bool initialized;
    [SerializeField, HideInInspector] private int savedAmmo = -1;
    [SerializeField, HideInInspector] private WeaponData savedAmmoData;

    private void Start() => Initialize();
    private void OnEnable() { if (Application.isPlaying) Initialize(); }
    private void Initialize()
    {
        if (Runtime != null || initialized) return;
        initialized = true;
        var player = GetComponentInParent<PlayerMovement>();
        owner = player != null ? player.gameObject : gameObject;
        health = owner.GetComponent<PlayerDamageReceiver>();
        if (aimOrigin == null)
        {
            var camera = owner.GetComponentInChildren<Camera>();
            if (camera != null) aimOrigin = camera.transform;
        }
        input = inputSource != null ? inputSource as IWeaponInput : GetComponent<IWeaponInput>();
        if (weaponData == null || aimOrigin == null || input == null)
        {
            Debug.LogError("WeaponController needs WeaponData, an aim Camera/Transform and IWeaponInput on its object.", this);
            return;
        }
        Runtime = new WeaponRuntime(weaponData, savedAmmoData == weaponData && savedAmmo >= 0 ? savedAmmo : (int?)null);
    }

    private void Update()
    {
        if (Runtime == null) return;
        if (health != null && !health.IsAlive) { Runtime.Interrupt(); return; }
        // Empty intent still lets an in-progress reload advance while the cursor is unlocked.
        ProcessFrame(Time.deltaTime, input.ReadInput());
    }

    /// <summary>Equip an existing slot runtime so switching never refills its magazine.</summary>
    public void Equip(WeaponRuntime weapon)
    {
        Initialize();
        Runtime?.Interrupt();
        Runtime = weapon;
        weaponData = weapon != null ? weapon.Data : null;
        savedAmmoData = weaponData;
        savedAmmo = weapon != null ? weapon.Ammo : -1;
        if (input is PlayerWeaponInput playerInput) playerInput.RequireTriggerRelease();
    }

    internal void ProcessFrame(float dt, WeaponInputFrame frame)
    {
        if (Runtime == null || aimOrigin == null || !isActiveAndEnabled || (health != null && !health.IsAlive)) return;
        bool wasReloading = Runtime.IsReloading;
        int shots = Runtime.Tick(dt, frame);
        if (logShots && wasReloading != Runtime.IsReloading)
            Debug.Log(Runtime.IsReloading ? "[Weapon] Reload started" : "[Weapon] Reload complete", this);
        for (int i = 0; i < shots; i++)
        {
            long id = DamageHit.NewAttackId();
            Vector3 origin = aimOrigin.position, direction = aimOrigin.forward;
            var attack = new DamageHit(id, weaponData.Damage, DamageKind.Attack, origin, direction, owner);
            var trace = AttackTrace.Trace(attack, origin, direction, weaponData.Range, hitLayers);
            var shot = new WeaponShot(id, origin, direction, trace, weaponData.RecoilPerShot, Runtime.Ammo + shots - i - 1);
            LastShot = shot;
            if (drawRays) Debug.DrawRay(origin, trace.End - origin, trace.HasHit ? Color.red : Color.cyan, 0.2f);
            if (logShots) Debug.Log($"[Weapon] {weaponData.DisplayName} shot={id} ammo={shot.AmmoAfterShot}/{weaponData.MagazineSize} "
                + $"hit={(trace.HasHit && trace.Hit.collider != null ? trace.Hit.collider.name : "miss")} result={trace.Damage}", this);
            ShotFired?.Invoke(shot);
        }
    }

    private void OnDisable()
    {
        if (Runtime == null) return;
        savedAmmo = Runtime.Ammo; savedAmmoData = Runtime.Data;
        Runtime.Interrupt();
    }
}
