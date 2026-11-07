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
        public AnimationClip sourceReloadTactical;
        public AnimationClip sourceReloadEmpty;
        [Tooltip("FPSAF's animation assets: blend times and curves. Their clip is the one made for the body that plays them (GunBakes).")]
        public FPSAnimationAsset pose;
        public FPSAnimationAsset reloadTactical;
        public FPSAnimationAsset reloadEmpty;
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
