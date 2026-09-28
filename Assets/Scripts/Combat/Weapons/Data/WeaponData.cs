using UnityEngine;

public enum WeaponFireMode { SemiAutomatic, Automatic }

[CreateAssetMenu(fileName = "Weapon", menuName = "Morphang/Combat/Weapon Data")]
public sealed class WeaponData : ScriptableObject
{
    [SerializeField] private string displayName = "Foundation Rifle";
    [SerializeField] private WeaponFireMode fireMode = WeaponFireMode.Automatic;
    [SerializeField, Min(0.01f)] private float damage = 20f;
    [SerializeField, Min(1f)] private float roundsPerMinute = 600f;
    [SerializeField, Min(1)] private int magazineSize = 30;
    [SerializeField, Min(0.01f)] private float reloadDuration = 1.5f;
    [SerializeField, Min(0.1f)] private float range = 100f;
    [Tooltip("Pitch / yaw magnitude in degrees. Phase 1 publishes this with the shot; animation/camera recoil is not applied yet.")]
    [SerializeField] private Vector2 recoilPerShot = new Vector2(1.2f, 0.4f);

    public string DisplayName => displayName;
    public WeaponFireMode FireMode => fireMode;
    public float Damage => Mathf.Max(0.01f, damage);
    public float ShotInterval => 60f / Mathf.Max(1f, roundsPerMinute);
    public int MagazineSize => Mathf.Max(1, magazineSize);
    public float ReloadDuration => Mathf.Max(0.01f, reloadDuration);
    public float Range => Mathf.Max(0.1f, range);
    public Vector2 RecoilPerShot => recoilPerShot;
}
