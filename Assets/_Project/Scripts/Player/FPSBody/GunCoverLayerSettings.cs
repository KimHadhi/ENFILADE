using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

/// <summary>
/// An FPS Animation Framework layer of ours: FPSAF's Collision layer (the same blocking poses, the same space, the
/// primary pose looking up and the secondary one looking down) driven by our wall rule instead of its own ray. FPSAF's
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
    public bool useSecondaryPose = true;
    [InputProperty, Tooltip("How far a wall holds the gun back, 0 to 1 (FPSBody sets it).")]
    public string coverInputProperty = "CoverWeight";

    public override IAnimationLayerJob CreateAnimationJob() => new GunCoverLayerJob();

#if UNITY_EDITOR
    public override void OnRigUpdated() => UpdateRigElement(ref weaponIkBone);
#endif
}
