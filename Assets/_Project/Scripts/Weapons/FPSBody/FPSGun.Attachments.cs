using KINEMATION.FPSAnimationFramework.Runtime.Camera;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using UnityEngine;

// FPSGun: Muzzle, grip and underbarrel parts (GunAttachments). One part of the class; the rest is in FPSGun.cs and the files beside it.
public sealed partial class FPSGun
{
    /// <summary>The parts the gun can wear besides sights; null for a gun without any.</summary>
    internal GunAttachments Attachments { get; private set; }

    private float muzzleDepth;

    /// <summary>A suppressor is on: the shot is quiet and throws no flash (WeaponController, WeaponFeedback, MuzzleFlash).</summary>
    internal bool Suppressed => Attachments != null && Attachments.Suppressed;

    internal int MuzzleIndex => Attachments != null ? Attachments.Muzzle : -1;

    internal bool LightOn => Attachments != null && Attachments.LightOn;

    internal int UnderbarrelIndex => Attachments != null ? Attachments.Underbarrel : -1;

    internal int GripIndex => Attachments != null ? Attachments.Grip : -1;

    internal GunAttachments.Kind GripKind => Attachments != null ? Attachments.GripKind : GunAttachments.Kind.None;

    private bool gripsSeated;

    /// <summary>
    /// Once per gun: its grips go no further back than KINEMATION's support hand holds it (<paramref name="supportPalm"/>,
    /// on <paramref name="weaponBone"/> as FPSBody's contacts keep a palm), GunAttachments.SeatGrips; the hands' surface is
    /// measured again if any moved.
    /// </summary>
    internal void SeatGrips(Transform weaponBone, Vector3 supportPalm)
    {
        if (gripsSeated || Attachments == null || weaponBone == null) return;
        gripsSeated = true;
        Vector3 palm = weaponBone.position + weaponBone.rotation * supportPalm;
        if (Attachments.SeatGrips(transform, palm, weaponBone.rotation * Vector3.forward) > 0f) Surface = GunSurface.Of(transform);
    }

    /// <summary>
    /// The grip on now takes the support hand: where the middle of its palm goes and the ways its fingers point and its
    /// palm faces, in the world as the gun sits now. False without such a grip.
    /// </summary>
    internal bool LeftGrip(out Vector3 palm, out Vector3 fingers, out Vector3 facing)
    {
        palm = fingers = facing = Vector3.zero;
        if (Attachments == null || !Attachments.GripHand(out var grip)) return false;
        palm = transform.TransformPoint(grip.handPalm);
        fingers = transform.TransformDirection(grip.handFingers);
        facing = transform.TransformDirection(grip.handFacing);
        return true;
    }

    /// <summary>Puts grip <paramref name="index"/> on, as ShowMuzzle does; the hands then hold the gun as it now is.</summary>
    internal bool ShowGrip(int index)
    {
        if (Attachments == null || index < 0 || index >= Attachments.grips.Length || index == Attachments.Grip) return false;
        Attachments.SelectGrip(index);
        Surface = GunSurface.Of(transform);
        return true;
    }

    /// <summary>
    /// Puts muzzle part <paramref name="index"/> on: picked in the customize menu (GunCustomizeMenu), or what a friend's PC
    /// has on their gun (PlayerNetworkCombat). False when it is already on or the gun has no such part.
    /// </summary>
    internal bool ShowMuzzle(int index)
    {
        if (Attachments == null || index < 0 || index >= Attachments.muzzle.Length || index == Attachments.Muzzle) return false;
        Attachments.SelectMuzzle(index);
        MeasureMuzzle();
        return true;
    }

    /// <summary>Puts underbarrel part <paramref name="index"/> on, as ShowMuzzle does. False when it is already on.</summary>
    internal bool ShowUnderbarrel(int index)
    {
        if (Attachments == null || index < 0 || index >= Attachments.underbarrel.Length || index == Attachments.Underbarrel) return false;
        Attachments.SelectUnderbarrel(index);
        return true;
    }

    /// <summary>L: the flashlight on or off. False when the gun wears none.</summary>
    internal bool ToggleLight() => Attachments != null && Attachments.SetLight(!Attachments.LightOn);

    /// <summary>A friend's flashlight as their PC has it.</summary>
    internal void ShowLight(bool on)
    {
        if (Attachments != null && Attachments.LightOn != on) Attachments.SetLight(on);
    }

    // The shots, the flash and the tracer leave from the front of whatever is on the muzzle now (GunMuzzle measures only the
    // parts worn). The flashlight's beam starts there too, so the gun throws no shadow into it.
    private void MeasureMuzzle()
    {
        GunMuzzle.Measure(transform, AimPoint, muzzleDepth, out Vector3 muzzle, out Quaternion muzzleRotation);
        MuzzlePosition = muzzle;
        MuzzleRotation = muzzleRotation;
        if (Attachments != null) Attachments.LightAhead(transform.TransformPoint(muzzle));
    }

    /// <summary>The sight shown (GunSights.Current), -1 for a gun without a choice of sights.</summary>
    internal int SightIndex => Sights != null ? Sights.Current : -1;

    /// <summary>
    /// Shows sight <paramref name="index"/>, held or not: picked in the customize menu, or the one a friend's PC has on their
    /// gun (PlayerNetworkCombat). FPSAF's Ads eases the view over to the new aim point by itself (its aimPointSpeed), as
    /// KINEMATION's demo switches sights. False when it is already up or the gun has no such sight.
    /// </summary>
    internal bool ShowSight(int index)
    {
        if (Sights == null || index < 0 || index >= Sights.Count || index == Sights.Current) return false;
        Sights.Select(index);
        if (Entity != null) Entity.defaultAimPoint = AimPoint;
        return true;
    }

    /// <summary>A magnified sight's lens shows its view while the gun is aimed (GunScope).</summary>
    internal void ShowScope(float aim, Camera view)
    {
        var scope = Sights != null ? Sights.Scope : null;
        if (scope != null) scope.Show(aim, view);
    }

    /// <summary>Put away (Weapon.OnUnEquip).</summary>
    internal void Unequip(FPSAnimator animator, RecoilAnimation recoil, FPSBodyAssets assets)
    {
        EndCycle();
        recoil.OrNull()?.Stop();
        if (assets.unEquipMotion != null) animator.LinkAnimatorLayer(assets.unEquipMotion);
    }

    /// <summary>Aiming starts or stops (Weapon.OnAimPressed / OnAimReleased); our look sets the field of view.</summary>
    internal void Aim(bool aiming, UserInputController input, RecoilAnimation recoil)
    {
        input.SetValue(FPSANames.IsAiming, aiming);
        if (recoil != null) recoil.isAiming = aiming;
    }

    /// <summary>
    /// One of our shots (Weapon.OnFire): the recoil animation, the camera shake, and the gun's fire clips. Down the sights
    /// KINEMATION's camera shake stays off: the view's own recoil (PlayerLook) is the kick there, and the shake only
    /// rocked the sight off what it was on.
    /// </summary>
    internal void Fire(bool lastRound, RecoilAnimation recoil, FPSCameraController camera, bool aimed = false)
    {
        if (camera != null && shake != null && !aimed) camera.PlayCameraShake(shake);
        if (recoil != null && recoil.isActiveAndEnabled) recoil.Play();
        if (Kinemation == null || partsPlayer == null) return;
        var settings = Kinemation.weaponSettings;
        bool lockBack = settings != null && settings.hasFireOut && lastRound;
        var pair = lockBack ? Kinemation.fireOut : Kinemation.fire;
        // A gun worked by hand works its parts (the bolt, the pump) at the pace the body works them (Cycle).
        var parts = !lockBack && partsCycle != null ? partsCycle : pair.weapon;
        // The last round's clip holds the slide back until a reload's clip takes over (it doesn't blend out by itself).
        if (parts != null && partsPlayer.PlayAnimation(parts)) slideHeld = lockBack;
    }
}
