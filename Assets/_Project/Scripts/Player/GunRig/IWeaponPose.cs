using UnityEngine;

/// <summary>
/// What poses the held gun model for WeaponController each frame: the procedural placeholder (WeaponPresentation) or
/// KINEMATION's arms rig (GunRig). WeaponController keeps the rules (ammo, fire rate, aim blend, cover); this only places
/// the model and its muzzle, shows the recoil, and says whether a wall holds the gun up.
/// </summary>
public interface IWeaponPose
{
    /// <summary>The gun is raised clear of a wall it would poke into, so it may not fire.</summary>
    bool IsHighReady { get; }
    void SetVisible(bool visible);
    void Reset();
    void OnShot(WeaponData data, Vector2 shotRecoil);
    /// <summary>Where the muzzle would be at this aim blend, for the cover probes; may be called several times a frame.</summary>
    Vector3 Prepare(Transform view, WeaponData data, float ads);
    /// <summary>Poses the gun for this frame; returns whether it is held up by a wall.</summary>
    bool Pose(GameObject owner, Transform view, Vector3 target, bool obstructed, WeaponRuntime runtime, float dt, int layers);
    /// <summary>The look and move sway, after the shots used the plain pose.</summary>
    void ApplySway(Transform view, Vector3 offset, Quaternion turn);
}
