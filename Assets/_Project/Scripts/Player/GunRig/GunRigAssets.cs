using System;
using KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers;
using UnityEngine;

/// <summary>
/// The KINEMATION assets the gun rig runs on: the arms rig of the FPS Animation Pack x CAS integration, the KINEMATION gun
/// each of our weapons shows, and the IK motions KINEMATION's own player (CasFPSPlayerController) plays when aiming
/// starts or stops and when moving starts or stops. Loaded from Resources/Combat/GunRig.
/// </summary>
[CreateAssetMenu(fileName = "GunRig", menuName = "ENFILADE/Combat/Gun Rig Assets")]
public sealed class GunRigAssets : ScriptableObject
{
    [Serializable]
    public struct Gun
    {
        [Tooltip("Our weapon (Data/Combat/Weapons).")]
        public WeaponData weapon;
        [Tooltip("The KINEMATION gun it shows (CharacterAnimationSystem/Integrations/FPSAnimationPack/Prefabs/Weapons).")]
        public GameObject prefab;
    }

    [Tooltip("KINEMATION's arms skeleton with its CAS components (SK_Arms_Mono_Skeleton).")]
    public GameObject armsRig;
    public Gun[] guns = Array.Empty<Gun>();
    [Tooltip("Played when aiming starts or stops, as CasFPSPlayerController.OnAim does.")]
    public IkMotionSettings aimingMotion;
    [Tooltip("Played when moving starts or stops, as CasFPSPlayerController.OnMovementChange does.")]
    public IkMotionSettings movementMotion;

    private const string ResourcePath = "Combat/GunRig";
    private static GunRigAssets cached;

    public static GunRigAssets Instance => cached != null ? cached : cached = Resources.Load<GunRigAssets>(ResourcePath);
}
