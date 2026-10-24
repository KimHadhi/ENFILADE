using UnityEngine;

public enum WeaponFireMode { SemiAutomatic, Automatic, Burst }
public enum WeaponCategory { Ranged, Melee, Special }

[CreateAssetMenu(fileName = "Weapon", menuName = "Morphang/Combat/Weapon Data")]
public sealed class WeaponData : ScriptableObject
{
    [SerializeField] private string displayName = "Foundation Rifle";
    [SerializeField] private Sprite hudIcon;
    [SerializeField] private WeaponCategory category = WeaponCategory.Ranged;
    [SerializeField] private WeaponFireMode fireMode = WeaponFireMode.Automatic;
    [SerializeField, Min(0.01f)] private float damage = 20f;
    [SerializeField, Min(1f)] private float roundsPerMinute = 600f;
    [SerializeField, Min(1)] private int magazineSize = 30;
    [SerializeField, Min(0.01f)] private float reloadDuration = 1.5f;
    [SerializeField, Min(0.1f)] private float range = 100f;
    [Tooltip("Pitch / yaw in degrees. Positive pitch kicks the view upward.")]
    [SerializeField] private Vector2 recoilPerShot = new Vector2(1.2f, 0.4f);
    [Header("Ammunition / fire mode")]
    [SerializeField, Min(0)] private int initialReserveAmmo = 90;
    [SerializeField, Min(.01f)] private float emptyReloadDuration = 2f;
    [SerializeField, Min(1)] private int burstCount = 3;
    [SerializeField, Min(0f)] private float burstPause = .22f;
    [Header("Accuracy (cone half-angle in degrees)")]
    [SerializeField, Min(0f)] private float hipSpread = 1.2f;
    [SerializeField, Min(0f)] private float adsSpread = .12f;
    [SerializeField, Min(0f)] private float movingSpread = .8f;
    [SerializeField, Min(0f)] private float airborneSpread = 1.8f;
    [SerializeField, Min(0f)] private float bloomPerShot = .18f;
    [SerializeField, Min(0f)] private float maxBloom = 2.5f;
    [SerializeField, Min(0f)] private float bloomRecovery = 2.2f;
    [SerializeField, Min(1f)] private float headshotMultiplier = 2f;
    [Tooltip("Repeats while firing; an empty array uses Recoil Per Shot.")]
    [SerializeField] private Vector2[] recoilPattern = new Vector2[0];
    [Header("Recoil feel")]
    [Tooltip("Spring response in 1/seconds. 24 peaks about 42ms after firing; higher is snappier.")]
    [SerializeField, Range(8f, 50f)] private float recoilResponse = 24f;
    [Tooltip("Fraction retained as gradual aim climb. The rest springs back without smoothing mouse input.")]
    [SerializeField, Range(0f, 1f)] private float recoilRetention = .22f;
    [SerializeField, Range(0f, .1f)] private float weaponKickBack = .035f;
    [SerializeField, Range(0f, 6f)] private float weaponKickUp = 1.8f;
    [SerializeField, Range(8f, 50f)] private float weaponRecoilResponse = 28f;
    [SerializeField, Range(0f, 1f)] private float adsWeaponKickMultiplier = .45f;
    [Header("Aim down sights")]
    [SerializeField, Range(20f, 100f)] private float adsFov = 40f;
    [SerializeField, Range(.1f, 1f)] private float adsMoveMultiplier = .6f;
    [SerializeField, Min(.01f)] private float adsTransitionTime = .16f;
    [Tooltip("Sight height above the muzzle axis, in weapon-root local units.")]
    [SerializeField] private float sightHeight = .035f;
    [Header("Aim tracking feel")]
    [Tooltip("Seconds to halve the near/far aim transition. Zero restores instant tracking. Mouse look stays immediate.")]
    [SerializeField, Range(0f, .3f)] private float aimFollowHalfLife = .055f;
    [Tooltip("Faster sight convergence while aiming. Zero restores instant ADS tracking.")]
    [SerializeField, Range(0f, .3f)] private float adsAimFollowHalfLife = .025f;
    [Header("High-ready pose (camera space)")]
    [SerializeField] private Vector3 highReadyOffset = new Vector3(-.12f, -.32f, .32f);
    [SerializeField, Range(30f, 85f)] private float highReadyPitch = 65f;

    public string DisplayName => displayName;
    public Sprite HudIcon => hudIcon;
    public WeaponCategory Category => category;
    public WeaponFireMode FireMode => fireMode;
    public float Damage => Mathf.Max(0.01f, damage);
    public float ShotInterval => 60f / Mathf.Max(1f, roundsPerMinute);
    public int MagazineSize => Mathf.Max(1, magazineSize);
    public float ReloadDuration => Mathf.Max(0.01f, reloadDuration);
    public float Range => Mathf.Max(0.1f, range);
    public Vector2 RecoilPerShot => recoilPerShot;
    public float RecoilResponse => Mathf.Clamp(recoilResponse, 8f, 50f);
    public float RecoilRetention => Mathf.Clamp01(recoilRetention);
    public float WeaponKickBack => Mathf.Clamp(weaponKickBack, 0f, .1f);
    public float WeaponKickUp => Mathf.Clamp(weaponKickUp, 0f, 6f);
    public float WeaponRecoilResponse => Mathf.Clamp(weaponRecoilResponse, 8f, 50f);
    public float AdsWeaponKickMultiplier => Mathf.Clamp01(adsWeaponKickMultiplier);
    public int InitialReserveAmmo => Mathf.Max(0, initialReserveAmmo);
    public float EmptyReloadDuration => Mathf.Max(.01f, emptyReloadDuration);
    public int BurstCount => Mathf.Max(1, burstCount);
    public float BurstPause => Mathf.Max(0f, burstPause);
    public float HipSpread => Mathf.Max(0f, hipSpread);
    public float AdsSpread => Mathf.Max(0f, adsSpread);
    public float MovingSpread => Mathf.Max(0f, movingSpread);
    public float AirborneSpread => Mathf.Max(0f, airborneSpread);
    public float BloomPerShot => Mathf.Max(0f, bloomPerShot);
    public float MaxBloom => Mathf.Max(0f, maxBloom);
    public float BloomRecovery => Mathf.Max(0f, bloomRecovery);
    public float HeadshotMultiplier => Mathf.Max(1f, headshotMultiplier);
    public float AdsFov => Mathf.Clamp(adsFov, 20f, 100f);
    public float AdsMoveMultiplier => Mathf.Clamp(adsMoveMultiplier, .1f, 1f);
    public float AdsTransitionTime => Mathf.Max(.01f, adsTransitionTime);
    public float SightHeight => sightHeight;
    public float AimFollowHalfLife => Mathf.Clamp(aimFollowHalfLife, 0f, .3f);
    public float AdsAimFollowHalfLife => Mathf.Clamp(adsAimFollowHalfLife, 0f, .3f);
    public Vector3 HighReadyOffset => highReadyOffset;
    public float HighReadyPitch => Mathf.Clamp(highReadyPitch, 30f, 85f);
    public Vector2 RecoilAt(int index) => recoilPattern != null && recoilPattern.Length > 0
        ? recoilPattern[Mathf.Abs(index) % recoilPattern.Length] : recoilPerShot;
}
