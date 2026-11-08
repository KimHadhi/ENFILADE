using UnityEngine;

/// <summary>
/// What shows the held gun for WeaponController each frame: KINEMATION FPS Animation Framework on the body (FPSBody).
/// WeaponController keeps the rules (ammo, fire rate, aim blend, cover); this only shows the gun and its muzzle, plays the
/// recoil, and says whether a wall holds the gun up. Sway is FPSAF's Sway layer.
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
    /// <summary>
    /// Poses the gun for this frame; returns whether it is held up by a wall. quickLower: the way is clear and the player
    /// wants to fire or aim, so a raised gun comes down at its quickest.
    /// </summary>
    bool Pose(GameObject owner, Transform view, Vector3 target, bool obstructed, WeaponRuntime runtime, float dt, int layers, bool quickLower);
}
