using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

/// <summary>
/// An FPS Animation Framework layer of ours: FPSAF's Collision layer (the same blocking poses, the same space; the
/// secondary pose for looking down is off) driven by our wall rule instead of its own ray. FPSAF's
/// ray hits trigger volumes and the player's own colliders here; our WeaponController already probes the wall that
/// stops the shot, and FPSBody eases how far the gun is held back at the weapon's high-ready speeds (Weapons.csv) into
/// the input property below.
/// </summary>
[CreateAssetMenu(fileName = "GunCoverLayer", menuName = FPSANames.FileMenuLayers + "Gun Cover (ENFILADE)")]
public sealed class GunCoverLayerSettings : FPSAnimatorLayerSettings
{
    public KRigElement weaponIkBone = new KRigElement(-1, FPSANames.IkWeaponBone);
    public KTransform primaryPose = KTransform.Identity;
    public KTransform secondaryPose = KTransform.Identity;
    public ESpaceType targetSpace = ESpaceType.ComponentSpace;
    // Off in our profiles: a blocked gun only ever goes up. Looking down swapped it to the secondary pose (the rifle's
    // turned left, the pistol's lowered), with a jump wherever the view crossed level (user, 2026-11-09).
    [Tooltip("Looking down takes the secondary pose instead of the primary one.")]
    public bool useSecondaryPose;
    [InputProperty, Tooltip("How far a wall holds the gun back, 0 to 1 (FPSBody sets it).")]
    public string coverInputProperty = "CoverWeight";

    public override IAnimationLayerJob CreateAnimationJob() => new GunCoverLayerJob();

#if UNITY_EDITOR
    public override void OnRigUpdated() => UpdateRigElement(ref weaponIkBone);
#endif
}
