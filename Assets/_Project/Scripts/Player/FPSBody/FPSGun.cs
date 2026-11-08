using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.FPSAnimationFramework.Runtime.Camera;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using UnityEngine;

/// <summary>
/// One gun in the body's hands, as KINEMATION FPS Animation Framework's demo weapon (Weapon) handles it: equipping
/// links its animator profile (FPSAnimatorEntity) and plays the equip IK motion, aiming sets FPSAF's IsAiming and the
/// recoil animation's aim, a shot plays the recoil animation and the camera shake, a reload plays its clip on the body
/// (FPSAF's playables), the gun-part clip on the gun and KINEMATION's camera motion on the camera. Our WeaponController
/// decides when (ammo, fire rate, reload time); this only shows it.
/// The gun, its sight, recoil, camera shake and gun-part clips are KINEMATION's FPS Animation Pack gun (CasFPSWeapon,
/// switched off and kept as its data). Its clips on the body are KINEMATION's own, remade for this body by Retarget Pro
/// (GunBakes), so both hands hold it as KINEMATION's arms do.
/// </summary>
[DisallowMultipleComponent]
public sealed class FPSGun : MonoBehaviour
{
    internal WeaponData Data { get; private set; }
    internal CasFPSWeapon Kinemation { get; private set; }
    internal FPSAnimatorEntity Entity { get; private set; }
    internal Transform AimPoint => Kinemation != null && Kinemation.aimPoint != null ? Kinemation.aimPoint : transform;
    internal Vector3 MuzzlePosition { get; private set; }
    internal Quaternion MuzzleRotation { get; private set; }
    internal bool Reloading => reloading;
    // The muzzle as this gun sits held clean at the hip, from the eyes (FPSBody.LearnHipMuzzle), for the wall rule.
    internal Vector3 HipMuzzle { get; set; }
    internal bool HasHipMuzzle { get; set; }

    // The gun's real surface for the hands to touch, built from its meshes as it is set up (at load, not in play).
    internal GunSurface Surface { get; private set; }
    // How FPSAF's Look shares a lean down the body's spine with this gun (FPSBody.LeanChainOf).
    internal System.Collections.Generic.List<(Transform bone, float share)> LeanChain { get; set; } = new();

    private FPSBodyAssets.Gun entry;
    private FPSAnimationAsset reloadTactical, reloadEmpty;
    private SimpleAnimationPlayer partsPlayer;
    private FPSCameraShake shake;
    private bool reloadEmptyPlaying, reloading;
    // The gun-part player's rest pose has been seen to (EnsureRestPose), and the last shot's clip is holding the slide back.
    private bool restChecked, slideHeld;
    // The gun's frame from KINEMATION's weapon bone (its rotationOffset), as the Pose Sampler's weapon bone offset turns it.
    private Quaternion frame = Quaternion.identity;
    // Where the hands sit on the gun in the pose made for this body (GunBakes), in the gun bone's own axes, for the probe.
    // Measured on the gun, not on the hand: the humanoid clip turns the hand bone a little differently from the bake (its
    // twist is shared out anew), and a gun held by the hand's own axes lay on its side (user, 2026-11-07: "갱스터 그립").
    private bool hasGrip;
    private Vector3 rightGripPosition, leftGripPosition;

    internal bool HasGrip => hasGrip;

    /// <summary>The grip measured where the clips were made for this body; another body's grip would not fit its hands.</summary>
    internal void SetGrip(in GunBakes.Entry bake, bool exact)
    {
        hasGrip = exact && bake.hasGrip;
        rightGripPosition = bake.rightGripPosition;
        leftGripPosition = bake.leftGripPosition;
    }

    /// <summary>How far each hand is from where the bake had it on the gun as the gun now sits (m), for the probe.</summary>
    internal void GripGaps(Transform rightHand, Transform leftHand, Transform ikWeaponBone, float scale, out float right, out float left)
    {
        right = left = -1f;
        if (!hasGrip || ikWeaponBone == null) return;
        Quaternion bone = ikWeaponBone.rotation * Quaternion.Inverse(frame);
        if (rightHand != null) right = Vector3.Distance(ikWeaponBone.position + bone * (rightGripPosition * scale), rightHand.position);
        if (leftHand != null) left = Vector3.Distance(ikWeaponBone.position + bone * (leftGripPosition * scale), leftHand.position);
    }

    /// <summary>Sets the gun up on its KINEMATION model, which is the same game object, with this body's profile and reloads.</summary>
    internal void Set(FPSBodyAssets.Gun gun, CasFPSWeapon kinemation, float muzzleDepth, FPSAnimatorProfile profile,
        FPSAnimationAsset tactical, FPSAnimationAsset empty, Quaternion gunFrame)
    {
        entry = gun;
        frame = gunFrame;
        Data = gun.weapon;
        Kinemation = kinemation;
        reloadTactical = tactical;
        reloadEmpty = empty;
        partsPlayer = GetComponentInChildren<SimpleAnimationPlayer>(true);
        Entity = gameObject.GetComponent<FPSAnimatorEntity>();
        if (Entity == null) Entity = gameObject.AddComponent<FPSAnimatorEntity>();
        Entity.animatorProfile = profile;
        Entity.defaultAimPoint = AimPoint;
        // KINEMATION's FPS Animation Pack shake, as FPSAF's camera plays it (the two assets have the same fields).
        if (kinemation != null && kinemation.weaponSettings != null)
        {
            object source = CameraShakeOf(kinemation);
            if (source is Object asset && asset != null)
            {
                shake = ScriptableObject.CreateInstance<FPSCameraShake>();
                shake.name = asset.name;
                shake.hideFlags = HideFlags.DontSave;
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(asset), shake);
            }
        }
        GunMuzzle.Measure(transform, AimPoint, muzzleDepth, out Vector3 muzzle, out Quaternion muzzleRotation);
        MuzzlePosition = muzzle;
        MuzzleRotation = muzzleRotation;
        Surface = GunSurface.Of(transform);
    }

    // CasFPSWeapon keeps its camera shake protected; it is read through the serialized data like every other field.
    private static object CameraShakeOf(CasFPSWeapon weapon)
    {
        var field = typeof(CasFPSWeapon).GetField("cameraShake",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        return field != null ? field.GetValue(weapon) : null;
    }

    /// <summary>
    /// Takes the gun <paramref name="weight"/> of the way onto the right hand as it is now: once the body's own hands have
    /// moved it (to a ledge, a rung), where FPSAF had the hand in the graph is no longer where it is. FPSAF's right-hand
    /// weapon bone holds the gun as its Pose Sampler found it in that hand. FPSAF poses the IK weapon bone again next
    /// frame, so nothing builds up. The hand's weapon bone has KINEMATION's bone axes; the gun hangs in its own frame.
    /// </summary>
    internal void FollowHand(Transform weaponBoneRight, Transform ikWeaponBone, float weight)
    {
        if (weaponBoneRight == null || ikWeaponBone == null || weight <= 0f) return;
        ikWeaponBone.SetPositionAndRotation(Vector3.Lerp(ikWeaponBone.position, weaponBoneRight.position, weight),
            Quaternion.Slerp(ikWeaponBone.rotation, weaponBoneRight.rotation * frame, weight));
    }

    /// <summary>Drawn: FPSAF links this gun's profile (Weapon.OnEquip) and its recoil gets our fire rate.</summary>
    internal void Equip(FPSAnimator animator, RecoilAnimation recoil, FPSBodyAssets assets, float roundsPerMinute, FireMode mode,
        FPSBodyCamera camera)
    {
        reloading = false;
        EnsureRestPose();
        if (recoil != null && Kinemation != null && Kinemation.weaponSettings != null)
            recoil.Init(Kinemation.weaponSettings.recoilAnimData, roundsPerMinute, mode);
        animator.LinkAnimatorProfile(gameObject);
        if (assets.equipMotion != null) animator.LinkAnimatorLayer(assets.equipMotion);
        if (camera != null) camera.HoldGunMotion(entry.sourcePose);
    }

    /// <summary>Put away (Weapon.OnUnEquip).</summary>
    internal void Unequip(FPSAnimator animator, RecoilAnimation recoil, FPSBodyAssets assets)
    {
        recoil?.Stop();
        if (assets.unEquipMotion != null) animator.LinkAnimatorLayer(assets.unEquipMotion);
    }

    /// <summary>Aiming starts or stops (Weapon.OnAimPressed / OnAimReleased); our look sets the field of view.</summary>
    internal void Aim(bool aiming, UserInputController input, RecoilAnimation recoil)
    {
        input.SetValue(FPSANames.IsAiming, aiming);
        if (recoil != null) recoil.isAiming = aiming;
    }

    /// <summary>One of our shots (Weapon.OnFire): the recoil animation, the camera shake, and the gun's fire clips.</summary>
    internal void Fire(bool lastRound, RecoilAnimation recoil, FPSCameraController camera)
    {
        if (camera != null && shake != null) camera.PlayCameraShake(shake);
        if (recoil != null && recoil.isActiveAndEnabled) recoil.Play();
        if (Kinemation == null || partsPlayer == null) return;
        var settings = Kinemation.weaponSettings;
        bool lockBack = settings != null && settings.hasFireOut && lastRound;
        var pair = lockBack ? Kinemation.fireOut : Kinemation.fire;
        // The last round's clip holds the slide back until a reload's clip takes over (it doesn't blend out by itself).
        if (pair.weapon != null && partsPlayer.PlayAnimation(pair.weapon)) slideHeld = lockBack;
    }

    /// <summary>
    /// KINEMATION's M1911 comes with no rest pose on its gun-part player (its MX16A4 has one, A_W_MX16A4_Pose): once a clip
    /// blended out, nothing put the parts back, so a slide the last round locked back stayed back through the reload
    /// (user, 2026-11-08). A gun without one gets its fire clip held at its first frame, the gun at rest, as the pose under
    /// every clip. Done once, the first time the gun is out (its player is built when the gun first switches on).
    /// </summary>
    private void EnsureRestPose()
    {
        if (restChecked || partsPlayer == null || Kinemation == null) return;
        restChecked = true;
        var field = typeof(SimpleAnimationPlayer).GetField("defaultClipPose",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (field != null && field.GetValue(partsPlayer) is AnimationClip own && own != null) return;
        var rest = Kinemation.fire.weapon != null ? Kinemation.fire.weapon.clip : null;
        if (rest != null) partsPlayer.PlayBasePlayable(rest);
    }

    // The slide is still held back by the last round's clip although the gun has been loaded (its reload clip never
    // played: the reload began while the body wasn't holding the gun up). The parts let go of it and go back to rest.
    private void ReleaseSlide()
    {
        if (!slideHeld || partsPlayer == null || !partsPlayer.enabled) return;
        slideHeld = false;
        partsPlayer.enabled = false;
        partsPlayer.enabled = true;
    }

    /// <summary>The trigger let go (Weapon.OnFireReleased): the recoil settles.</summary>
    internal void StopFire(RecoilAnimation recoil) => recoil?.Stop();

    /// <summary>
    /// Our reload began (Weapon.OnReload): its clip on the body, its gun-part clip on the gun, its camera motion. Returns how
    /// long the body's clip plays (s).
    /// </summary>
    internal float Reload(bool empty, IPlayablesController playables, FPSBodyCamera camera)
    {
        reloadEmptyPlaying = empty;
        reloading = true;
        var clip = empty ? reloadEmpty : reloadTactical;
        if (FPSAnimationAsset.IsValid(clip)) playables.PlayAnimation(clip, 0f);
        if (camera != null)
            camera.PlayGunMotion(empty ? entry.sourceReloadEmpty : entry.sourceReloadTactical, clip != null ? clip.blendTime.rateScale : 1f);
        float length = clip != null && clip.clip != null ? clip.clip.length / Mathf.Max(.01f, clip.blendTime.rateScale) : 0f;
        if (Kinemation == null || partsPlayer == null) return length;
        var pair = empty ? Kinemation.reloadEmpty : Kinemation.reloadTactical;
        // The reload's own clip takes the slide from the last round's and lets it go forward.
        if (pair.weapon != null && partsPlayer.PlayAnimation(pair.weapon)) slideHeld = false;
        return length;
    }

    /// <summary>Our reload finished: the gun is loaded, so a slide still held back goes forward.</summary>
    internal void ReloadDone()
    {
        reloading = false;
        ReleaseSlide();
    }

    /// <summary>The reload was cut short (a dodge, a climb, death): its clips stop.</summary>
    internal void StopReload(IPlayablesController playables, float blendOut, FPSBodyCamera camera)
    {
        if (!reloading) return;
        reloading = false;
        playables?.StopAnimation(blendOut);
        if (camera != null) camera.StopGunMotion();
        if (Kinemation == null) return;
        var pair = reloadEmptyPlaying ? Kinemation.reloadEmpty : Kinemation.reloadTactical;
        // KINEMATION's gun-part player stops its clip when it is switched off.
        if (partsPlayer != null && pair.weapon != null && partsPlayer.enabled)
        {
            partsPlayer.enabled = false;
            partsPlayer.enabled = true;
        }
    }

    private void OnDestroy()
    {
        if (shake != null) Destroy(shake);
    }
}
