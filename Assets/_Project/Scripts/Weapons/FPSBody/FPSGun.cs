using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.FPSAnimationFramework.Runtime.Camera;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
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
public sealed partial class FPSGun : MonoBehaviour
{
    internal WeaponData Data { get; private set; }
    internal CasFPSWeapon Kinemation { get; private set; }
    internal FPSAnimatorEntity Entity { get; private set; }
    // The sight shown (GunSights, picked in the customize menu), else KINEMATION's own aim point.
    internal Transform AimPoint => Sights != null && Sights.AimPoint != null ? Sights.AimPoint
        : Kinemation != null && Kinemation.aimPoint != null ? Kinemation.aimPoint : transform;
    /// <summary>The sights the gun can wear (the Tactical Shooter Pack's guns); null for one sight only.</summary>
    internal GunSights Sights { get; private set; }
    internal Vector3 MuzzlePosition { get; private set; }
    internal Quaternion MuzzleRotation { get; private set; }
    internal bool Reloading => reloading;
    /// <summary>A draw, holster or inspect clip is playing.</summary>
    internal bool Acting => Time.time < actionUntil;
    /// <summary>
    /// A clip of the gun's own has the hands (a reload, a draw, a holster, an inspect, the bolt or pump worked after a shot),
    /// not the grips.
    /// </summary>
    internal bool HandsBusy => reloading || Acting || Cycling;
    /// <summary>The gun has a draw clip of its own, so FPSAF's equip motion stays out.</summary>
    internal bool HasDraw => FPSAnimationAsset.IsValid(drawClip);
    // The muzzle as this gun sits held clean at the hip, from the eyes (FPSBody.LearnHipMuzzle), for the wall rule.
    internal Vector3 HipMuzzle { get; set; }
    internal bool HasHipMuzzle { get; set; }
    // The same held clean fully down the sights, for the wall rule while aiming.
    internal Vector3 AimedMuzzle { get; set; }
    internal bool HasAimedMuzzle { get; set; }

    // The gun's real surface for the hands to touch, built from its meshes as it is set up (at load, not in play).
    internal GunSurface Surface { get; private set; }
    // Its magazine, a real one that a reload lets fall to the floor (GunMagazine; 2026-11-11 playtest).
    private GunMagazine magazine;
    // How FPSAF's Look shares a lean down the body's spine with this gun (FPSBody.LeanChainOf).
    internal System.Collections.Generic.List<(Transform bone, float share)> LeanChain { get; set; } = new();
    // FPSAF's layers that move the gun about on its own, with the alpha its profile gave them: Sway (the gun trailing the
    // look and the steps) and Additive (KINEMATION's recoil animation and the clips' additive bob). FPSBody turns them down.
    internal readonly System.Collections.Generic.List<(FPSAnimatorLayerSettings layer, float alpha, bool kick)> FeelLayers = new();
    // This gun's own layer that turns it onto the crosshair's point (GunConvergeLayerSettings); FPSBody feeds it each frame.
    internal GunConvergeLayerSettings Converge { get; set; }
    // This gun's own copy of FPSAF's Ads: its aiming speed follows the gun's aim time, which an angled grip shortens.
    internal KINEMATION.FPSAnimationFramework.Runtime.Layers.AdsLayer.AdsLayerSettings Ads { get; set; }

    /// <summary>
    /// Aiming, the gun holds still on the sight as Rainbow Six Siege's do: Sway goes from hipSway of its own at the hip to
    /// none down the sights, and Additive (the recoil animation's kick) to aimedKick of its own. Sets this frame's alphas;
    /// FPSAF reads them as it weighs each layer.
    /// </summary>
    internal void Steady(float aim, float hipSway, float aimedKick, float gunKick = 1f)
    {
        foreach (var (layer, alpha, kick) in FeelLayers)
            if (layer != null) layer.alpha = alpha * (kick ? gunKick * Mathf.Lerp(1f, aimedKick, aim) : Mathf.Lerp(hipSway, 0f, aim));
    }

    private FPSBodyAssets.Gun entry;
    private FPSAnimationAsset reloadTactical, reloadEmpty, reloadShell, reloadEnd;
    // Draw, holster and inspect on the body (FPSBody fits the first two to the gun's times), and their gun-part clips at
    // the same rate.
    private FPSAnimationAsset drawClip, holsterClip, inspectClip;
    private KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset partsDraw, partsHolster, partsInspect;
    private readonly System.Collections.Generic.List<Object> rated = new();
    private float actionUntil;
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
        FPSAnimationAsset tactical, FPSAnimationAsset empty, Quaternion gunFrame, FPSAnimationAsset shell = null, FPSAnimationAsset end = null)
    {
        entry = gun;
        frame = gunFrame;
        Data = gun.weapon;
        Kinemation = kinemation;
        reloadTactical = tactical;
        reloadEmpty = empty;
        reloadShell = shell;
        reloadEnd = end;
        partsPlayer = GetComponentInChildren<SimpleAnimationPlayer>(true);
        Sights = GetComponent<GunSights>();
        if (Sights != null && Sights.Current < 0) Sights.Select(Sights.initial);
        Attachments = GetComponent<GunAttachments>();
        if (Attachments != null) Attachments.Begin();
        this.muzzleDepth = muzzleDepth;
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
        MeasureMuzzle();
        Surface = GunSurface.Of(transform);
        FindParked();
        magazine = GunMagazine.For(this);
    }

    // ---- The reload's own clip, which the hands follow on the gun (FPSBody.Contacts, GunArms) ----

    // KINEMATION's clip of the reload, draw, holster or inspect playing (the one its camera motion plays too): from when,
    // how far in (s), how fast, for how long (s), whether it eases back into the hold at its end and over how long (s), and
    // whether it is a reload (FPSBody's reloadContacts) or one of the others (holdContacts).
    private AnimationClip contactClip;
    private float contactStart, contactFrom, contactRate = 1f, contactLength, contactSettle = .15f;
    private bool contactSettles, contactIsReload, contactIsCycle;

    /// <summary>The clip followed now is a reload's (not a draw, holster or inspect).</summary>
    internal bool ContactIsReload => contactIsReload;
    /// <summary>The clip followed now is the bolt or pump worked after a shot (Cycle).</summary>
    internal bool ContactIsCycle => contactIsCycle;
}
