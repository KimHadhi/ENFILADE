using UnityEngine;

/// <summary>
/// Fires MuzzleFlame at the muzzle on every shot of this WeaponController: one's own gun and someone else's copy alike,
/// whose shots arrive through PlayerNetworkCombat.ShotRpc → WeaponController.ShowShot → ShotFired. The flame rides the
/// muzzle, so it keeps up with every recoil and sway, and runs on the shooter's time, so it slows with them. How far the
/// muzzle is from the camera picks its size: the shooter's own eyes see the first-person flame, the F5 view and teammates
/// the bigger third-person one. Built while the map loads; the light it throws is MuzzleLight's. Added by WeaponController.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.ShotEffects)]
public sealed class MuzzleFlash : MonoBehaviour
{
    private WeaponController weapon;
    private PlayerMovement movement;
    private MuzzleFlame flame;
    private Transform mount;
    private Camera view;

    private void Awake()
    {
        weapon = GetComponent<WeaponController>();
        movement = GetComponentInParent<PlayerMovement>();
    }

    // What the first shot would otherwise stop to build. This runs while the map loads.
    private void Start()
    {
        MuzzleFlame.Prepare();
        flame = MuzzleFlame.Create("Muzzle Flame");
        if (flame == null) return;
        Transform muzzle = weapon != null ? weapon.MuzzlePoint : null;
        if (muzzle != null) Mount(muzzle);
        else FeelAssets.GroupRuntimeRoot(flame.gameObject, "World", gameObject);
    }

    private void OnEnable()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) weapon.ShotFired += OnShot;
    }

    private void OnDisable()
    {
        if (weapon != null) weapon.ShotFired -= OnShot;
        if (flame != null) flame.Clear();
    }

    private void OnDestroy()
    {
        if (flame != null) Destroy(flame.gameObject);
    }

    private void OnShot(WeaponShot shot)
    {
        var data = weapon != null ? weapon.EquippedData : null;
        Transform muzzle = weapon != null ? weapon.MuzzlePoint : null;
        if (flame == null || data == null || data.MuzzleFlashScale <= 0f || !GunFeelSettings.Instance.muzzleEffects
            || muzzle == null || !muzzle.gameObject.activeInHierarchy) return;
        Mount(muzzle);
        flame.Fire(data.MuzzleFlashScale, ViewDistance(muzzle));
    }

    // After WeaponController (300) has posed the gun and fired this frame's shots.
    private void LateUpdate()
    {
        if (flame != null) flame.Advance(movement != null ? movement.DeltaTime : Time.deltaTime);
    }

    // A child of the muzzle at a world scale of 1, so the flame is measured in metres whatever the gun model's scale.
    private void Mount(Transform muzzle)
    {
        Transform host = flame.transform;
        if (mount != muzzle) { host.SetParent(muzzle, false); mount = muzzle; }
        host.localPosition = Vector3.zero;
        host.localRotation = Quaternion.identity;
        Vector3 scale = muzzle.lossyScale;
        host.localScale = new Vector3(1f / Safe(scale.x), 1f / Safe(scale.y), 1f / Safe(scale.z));
    }

    // From the camera drawing this screen: the shooter's own eyes in first person, further away in F5 or for someone else.
    private float ViewDistance(Transform muzzle)
    {
        if (view == null || !view.isActiveAndEnabled) view = Camera.main;
        return view != null ? Vector3.Distance(view.transform.position, muzzle.position) : float.MaxValue;
    }

    private static float Safe(float value) => Mathf.Abs(value) < .0001f ? 1f : value;
}
