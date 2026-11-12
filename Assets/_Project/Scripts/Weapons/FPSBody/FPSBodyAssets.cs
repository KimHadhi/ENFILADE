using System;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.IkMotionLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

/// <summary>
/// What the body holds guns with: KINEMATION FPS Animation Framework's set for a humanoid player as its demo
/// (FPSHumanoidPlayer) has it: the input config, the rig, the upper-body mask, the IK motions, and per gun its animator
/// profile and animations. The guns are KINEMATION's FPS Animation Pack guns as their CAS integration ships them.
/// Their clips are made for KINEMATION's arms skeleton; KINEMATION Retarget Pro remakes them for each of our bodies in
/// the editor (GunClipRetarget, into GunBakes), and the animation assets here lend those clips their blend times and
/// curves. The original clips also move the camera with their camera bone (FPSGunCamera). Loaded from
/// Resources/Combat/FPSBody.
/// </summary>
[CreateAssetMenu(fileName = "FPSBody", menuName = "ENFILADE/Combat/FPS Body Assets")]
public sealed class FPSBodyAssets : ScriptableObject
{
    [Serializable]
    public struct Gun
    {
        [Tooltip("Our weapon (Data/Combat/Weapons).")]
        public WeaponData weapon;
        [Tooltip("The KINEMATION gun it shows (CharacterAnimationSystem/Integrations/FPSAnimationPack/Prefabs/Weapons): its model, sight, recoil, camera shake and gun-part clips.")]
        public GameObject prefab;
        [Tooltip("FPSAF's animator profile for this gun (from the demo's humanoid rifle or pistol profile).")]
        public FPSAnimatorProfile profile;
        [Tooltip("KINEMATION's own clips with this gun, for its arms skeleton (Generic): Retarget Pro remakes them for our bodies.")]
        public AnimationClip sourcePose;
        [Tooltip("KINEMATION's pose with no grip on the gun, where Source Pose holds one (the TR15's Idle_Pose_Grip and "
            + "Idle_Pose_Non_Grip): the support hand takes its place on the handguard from it while the gun wears no grip "
            + "(FPSBody.Contacts). Empty: Source Pose.")]
        public AnimationClip sourcePoseNoGrip;
        public AnimationClip sourceReloadTactical;
        public AnimationClip sourceReloadEmpty;
        [Tooltip("FPSAF's animation assets: blend times and curves. Their clip is the one made for the body that plays them (GunBakes).")]
        public FPSAnimationAsset pose;
        public FPSAnimationAsset reloadTactical;
        public FPSAnimationAsset reloadEmpty;

        [Header("Another KINEMATION pack's gun")]
        [Tooltip("KINEMATION's skeleton this gun's clips are made for, when it isn't the FPS Animation Pack's arms (Arms Model): "
            + "the Tactical Shooter Pack's SKM_Operator. Empty: Arms Model.")]
        public GameObject sourceArms;
        [Tooltip("A prefab of KINEMATION's with that skeleton and its first-person camera on it (the Tactical Shooter Pack's "
            + "TacticalShooterCharacter, whose FPCamera rides the head): where KINEMATION's camera sees the gun from. Empty: the FPS "
            + "Animation Pack's CameraSocket.")]
        public GameObject sourceCameraRig;

        [Header("Shell reload (WeaponData.ShellReload)")]
        [Tooltip("The clips of a shell reload: Source Reload Tactical and Source Reload Empty are its two starts, these its shell "
            + "(played once for every shell) and its end.")]
        public AnimationClip sourceReloadShell;
        public AnimationClip sourceReloadEnd;
        public FPSAnimationAsset reloadShell;
        public FPSAnimationAsset reloadEnd;
        [Tooltip("The gun-part clips of the shell and the end (CasFPSWeapon has fields for the two starts only).")]
        public KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset partsReloadShell;
        public KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset partsReloadEnd;
        [Tooltip("The gun-part clips of the two starts where the gun's CasFPSWeapon has none of its own for them: FPS Animation "
            + "Ultimate's guns loaded a round at a time (CasFPSManualWeapon: the Kar98k, the KXG12) start every reload with their "
            + "reloadStart. Empty: the CasFPSWeapon's own (its reloadTactical, reloadEmpty).")]
        public KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset partsReloadTactical;
        public KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset partsReloadEmpty;

        [Header("Draw, holster and inspect")]
        [Tooltip("KINEMATION's draw, holster and inspect clips with this gun (made for the same skeleton as its pose). The body plays "
            + "the draw and holster fitted to the gun's Draw Duration and Holster Duration, the inspect at its own pace.")]
        public AnimationClip sourceDraw;
        public AnimationClip sourceHolster;
        public AnimationClip sourceInspect;
        public FPSAnimationAsset draw;
        public FPSAnimationAsset holster;
        public FPSAnimationAsset inspect;
        [Tooltip("Their gun-part clips.")]
        public KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset partsDraw;
        public KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset partsHolster;
        public KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset partsInspect;

        [Header("The action worked after every shot")]
        [Tooltip("KINEMATION's clip of the body working the gun's action after a shot, for the same skeleton as its pose: a bolt "
            + "action's bolt (FPS Animation Ultimate's Kar98k, L96X), a pump gun's pump (its KXG12). The body plays it after every "
            + "shot, the CasFPSWeapon's own clip of the shot on the gun's parts, and the gun fires again once the hand is back "
            + "(FPSGun.Cycle). Empty: none, the shot moves the gun's parts alone.")]
        public AnimationClip sourceFire;
        public FPSAnimationAsset fire;
        [Tooltip("When the gun may fire again, seconds into Source Fire at its own pace. 0: measured, the moment both palms are back "
            + "where Source Pose holds them on KINEMATION's arms (WeaponCycle). A clip that takes no hand off the gun (a recoil "
            + "clip such as the X18's A_FP_X18_Fire) holds nothing.")]
        [Min(0f)] public float fireReady;

        /// <summary>KINEMATION's skeleton the gun's clips are made for.</summary>
        public GameObject ArmsOr(GameObject armsModel) => sourceArms != null ? sourceArms : armsModel;
    }

    [Header("FPS Animation Framework")]
    public UserInputConfig inputConfig;
    [Tooltip("The demo humanoid's rig: the body's bones are laid out in its order (FPSBodyRig).")]
    public KRig rig;
    [Tooltip("What FPSAF's own animations move: the upper body.")]
    public AvatarMask upperBody;
    [Tooltip("A profile with no layers: what FPSAF starts on and holds with no gun (an empty profile unlinks quietly).")]
    public FPSAnimatorProfile idleProfile;

    [Header("IK motions (FPSAF demo)")]
    public IkMotionLayerSettings aimingMotion;
    public IkMotionLayerSettings crouchingMotion;
    public IkMotionLayerSettings jumpingMotion;
    public IkMotionLayerSettings stopMotion;
    public IkMotionLayerSettings leanMotion;
    public IkMotionLayerSettings equipMotion;
    public IkMotionLayerSettings unEquipMotion;

    [Header("Guns")]
    [Tooltip("KINEMATION's arms skeleton (FPSAnimationPack/Character/SK_Arms_Mono), the one its gun clips are made for.")]
    public GameObject armsModel;
    public Gun[] guns = Array.Empty<Gun>();

    public const string ResourcePath = "Combat/FPSBody";
    private static FPSBodyAssets cached;

    public static FPSBodyAssets Instance => cached != null ? cached : cached = Resources.Load<FPSBodyAssets>(ResourcePath);
}
