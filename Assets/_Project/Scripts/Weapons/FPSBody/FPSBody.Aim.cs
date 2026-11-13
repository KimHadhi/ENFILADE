using KINEMATION.FPSAnimationFramework.Runtime.Layers.IkMotionLayer;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

// FPSBody: The gun turned onto the crosshair's point (GunConvergeLayerSettings). One part of the class; the rest is in FPSBody.cs and the files beside it.
public sealed partial class FPSBody
{
    // How fast the turn comes in and goes as the hands come free of a reload, a draw or a look over the gun (s, 0 to full),
    // so the gun eases back onto the point instead of snapping to it.
    private const float ConvergeEase = .2f;

    // How the distance to the crosshair's point is followed (half-lives, s) at the hip and down the sights: as before
    // KINEMATION (WeaponData's aimFollowHalfLife .055 and adsAimFollowHalfLife .025, 0.7.34), followed as its reciprocal, so
    // a swing from the sky onto a near wall doesn't hang far behind. The nearest it is taken (m), and without a point yet.
    private const float AimFollowHalfLife = .055f, AdsAimFollowHalfLife = .025f, NearestAim = .5f, DefaultAim = 20f;

    private float convergeWeight, inverseAimDistance;

    private bool hasAimDistance;

    // How far the gun is held out for the customize menu (0-1), and how long it takes to come out or go back (s).
    private float presentWeight;

    private const float PresentEase = .25f;

    private void FeedConverge(float dt, float aim)
    {
        var converge = held.Converge;
        if (converge == null) return;
        // The gun model hangs at rest under the IK weapon bone, so its own space is the bone's.
        converge.muzzle = held.MuzzlePosition;
        converge.muzzleForward = held.MuzzleRotation * Vector3.forward;
        converge.hasMuzzle = true;
        bool free = !held.HandsBusy && Framework >= .999f;
        convergeWeight = Mathf.MoveTowards(convergeWeight, free ? 1f : 0f, dt / ConvergeEase);
        // The customize menu holds the gun out to show it (ArcCW's customize view).
        presentWeight = Mathf.MoveTowards(presentWeight, controller.Customizing ? 1f : 0f, dt / PresentEase);
        converge.present = Mathf.SmoothStep(0f, 1f, presentWeight);
        // Down the sights Ads lines the sight up on the eyes; held back by a wall, its pose has the gun; shown off, the menu's.
        converge.weight = convergeWeight * (1f - aim) * (1f - cover) * (1f - presentWeight);

        float distance = DefaultAim;
        if (controller.HasAimPoint && eyes != null)
            distance = Mathf.Max(NearestAim, Vector3.Dot(controller.AimPoint - eyes.position, eyes.forward));
        float halfLife = Mathf.Lerp(AimFollowHalfLife, AdsAimFollowHalfLife, aim);
        if (!hasAimDistance) { inverseAimDistance = 1f / distance; hasAimDistance = true; }
        else inverseAimDistance = Mathf.Lerp(inverseAimDistance, 1f / distance, 1f - Mathf.Exp(-Mathf.Log(2f) * dt / halfLife));
        converge.distance = 1f / Mathf.Max(1e-4f, inverseAimDistance);
    }

    // The body's fit scale (PlayerBody scales the model to the camera height); the grip was measured at scale 1.
    private float ModelScale => animator != null ? Mathf.Abs(animator.transform.lossyScale.x) : 1f;

    // How much the body's right hand holds the gun (PlayerBodyHands, eased by the movement state).
    private float GunShare => hands != null ? Mathf.Clamp01(hands.GunWeight) : 1f;

    // How much both hands hold it.
    private float BothHands => hands != null ? Mathf.Clamp01(hands.BothHandsOnGun) : 1f;

    // FPSAF's share of the upper body: both hands on the gun, standing or crouched (PlayerBodyHands.FrameworkShare).
    private float Framework => hands != null ? Mathf.Clamp01(hands.FrameworkShare) : 1f;

    // How much of a hand's hold on the gun is the move's clip's rather than FPSAF's (0-1), once FPSAF's hold is known.
    private float Released(bool isLeft)
    {
        float hold = isLeft ? BothHands : GunShare;
        return HoldsGun && hasGunInView && hold > .001f ? Mathf.Clamp01(1f - Framework / hold) : 0f;
    }

    /// <summary>Where a hand holds the gun as it sits now, as FPSAF last held it (PlayerBodyHands puts the hand there).</summary>
    internal bool HandOnGun(bool isLeft, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (!HoldsGun || !hasGunInView || rig == null) return false;
        var gun = rig.IkWeaponBone;
        var grip = isLeft ? leftHandInGun : rightHandInGun;
        position = gun.TransformPoint(grip.position);
        rotation = gun.rotation * grip.rotation;
        return true;
    }

    // After the body's own hands (PlayerBodyHands, 320): where they took the right hand off FPSAF's pose (a ledge, a
    // rung), the gun goes with it, held as FPSAF holds it in that hand.
    private void AfterHands()
    {
        if (!HoldsGun) return;
        float share = GunShare;
        if (share < .999f) held.FollowHand(rig.WeaponBoneRight, rig.IkWeaponBone, 1f - share);
    }

    private void Motion(IkMotionLayerSettings motion)
    {
        if (motion != null && HoldsGun) framework.LinkAnimatorLayer(motion);
    }

    // This frame's view pitch (down-positive) and turn in degrees. Someone else's view turns in network steps, which
    // would only jolt FPSAF's sway, so theirs turns nothing.
    private void LookDelta(out float pitchDown, out Vector2 delta)
    {
        Transform frame = lookFrame != null ? lookFrame : eyes;
        pitchDown = 0f;
        delta = Vector2.zero;
        if (frame == null) return;
        Vector3 forward = frame.forward;
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        pitchDown = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        bool own = movement == null || (movement.IsLocal && !movement.IsNetworkDriven);
        if (hasLook && own) delta = new Vector2(Mathf.DeltaAngle(lastYaw, yaw), pitchDown - lastPitch);
        lastYaw = yaw;
        lastPitch = pitchDown;
        hasLook = true;
    }

    private void OnJumped()
    {
        if (!Sighted) Motion(assets.jumpingMotion);
    }

    // Down the sights or on the way in (aimedNoMotions): FPSAF's little moves stay out.
    private bool Sighted => HoldsGun && (controller.AimBlend > aimedNoMotions || aiming);

    // The sprint pose lets go this share of weightTime once the sights come up.
    private const float AimedSprintShare = .35f;

    // How quickly a little move still playing eases out as the sights come up, and a reload settling into the hold lets
    // go (s).
    private const float StillTime = .1f, SightsCut = .1f;

    private IkMotionLayerSettings stillMotion;

    private float reloadEndedAt = -100f;

    // An IK motion that moves nothing: linked, FPSAF eases whatever little move was playing back to rest over its blend
    // time (IkMotionLayerJob blends from where the last one had the gun).
    private IkMotionLayerSettings StillMotion
    {
        get
        {
            if (stillMotion != null || assets == null || assets.aimingMotion == null) return stillMotion;
            stillMotion = Instantiate(assets.aimingMotion);
            Keep(stillMotion);
            stillMotion.rotationCurves = VectorCurve.Constant(0f, StillTime, 0f);
            stillMotion.translationCurves = VectorCurve.Constant(0f, StillTime, 0f);
            stillMotion.blendTime = StillTime;
            stillMotion.playRate = 1f;
            return stillMotion;
        }
    }

    // The sights coming up take the gun as it is, as Rainbow Six Siege's come straight up: a draw still playing lets go
    // (CutDraw), a little move FPSAF is still playing (a stop, a landing, a crouch) eases out in StillTime, and a reload that
    // has just ended stops settling into the hold, so nothing but the sight moves the gun as it rises.
    private void SteadyForSights()
    {
        CutDraw();
        if (!HoldsGun) return;
        Motion(StillMotion);
        if (!held.HandsBusy && Time.time - reloadEndedAt < reloadSettle)
        {
            playables.StopAnimation(SightsCut);
            // The hands let go of the clip's hold with it.
            held.StopContacts();
        }
    }

    // The body placed the camera this frame (PlayerBody.Look), so FPSAF's shake and gun motion go on a fresh pose.
    private bool CameraPlaced() => body != null && !body.IsLimp && !body.IsDead;

    // After the body has been posed and before the weapon fires from its muzzle (300).
    private void LateUpdate()
    {
        if (!HoldsGun) return;
        KeepGunInView();
        // Through a reload, the gun where KINEMATION's own clip has it (FPSBody.Contacts); the weapon root rides it after.
        FollowContacts();
        // A pistol held in close comes in to the chest, canted, still on the crosshair's point (FPSBody.CloseHold).
        FollowCloseHold();
        // A shove's bash swings the gun on top of that (FPSBody.Bash).
        FollowBash();
        RideGun();
        if (firing && Time.time - lastShot > shotInterval * TriggerReleaseIntervals) StopFiring();
        if (held.Reloading && (controller.Runtime == null || !controller.Runtime.IsReloading)) held.StopReload(playables, reloadBlendOut, bodyCamera);
        // KINEMATION's sounds of a bolt or pump worked after a shot, as their moments come (FPSGun.Cycle).
        while (held.TakeCycleSound(out AudioClip worked)) Worked(worked);
    }

    // One of them, through the gun sounds' own player (WeaponFeedback), on every PC that shows the shot.
    private void Worked(AudioClip sound)
    {
        if (feedback == null && controller != null) feedback = controller.GetComponent<WeaponFeedback>();
        if (feedback != null) feedback.Worked(sound);
    }

    // While FPSAF holds the upper body, how it holds the gun is learnt: where the gun sits from the eyes (which the camera
    // rides and which stay put in the F5 view), at the hip and fully aimed, both hands on the gun and their fingers. When it
    // gives the body to the move's clip, the gun stays there as the eyes move (aimed as far as the player aims), the hands
    // are put back on it (PlayerBodyHands) and their fingers keep their grip, as the gun was held before KINEMATION. Before
    // the hands (320) and after the camera is placed (PlayerBody, 250).
    private void KeepGunInView()
    {
        var gun = rig != null ? rig.IkWeaponBone : null;
        var right = rig != null ? rig.RightHand : null;
        var left = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        if (gun == null || right == null || left == null || eyes == null) return;
        float aim = controller.AimBlend;
        if (Framework >= .999f)
        {
            // Mid-reload the hands are on the magazine and the bolt, not the grips.
            if (held.HandsBusy) return;
            var inView = new KTransform(eyes.InverseTransformPoint(gun.position), Quaternion.Inverse(eyes.rotation) * gun.rotation, Vector3.one);
            // Kept per gun (FPSGun.LearnHold): a draw followed on KINEMATION's clip comes up into this gun's own hold.
            if (aim <= .001f) held.LearnHold(inView, false);
            else if (aim >= .999f) held.LearnHold(inView, true);
            hasGunInView = held.HasLearnedHold;
            rightHandInGun = new KTransform(gun.InverseTransformPoint(right.position), Quaternion.Inverse(gun.rotation) * right.rotation, Vector3.one);
            leftHandInGun = new KTransform(gun.InverseTransformPoint(left.position), Quaternion.Inverse(gun.rotation) * left.rotation, Vector3.one);
            LearnFingers();
            return;
        }
        float released = Released(false);
        if (released <= 0f || !held.HoldFromEyes(aim, out KTransform kept)) return;
        gun.SetPositionAndRotation(Vector3.Lerp(gun.position, eyes.TransformPoint(kept.position), released),
            Quaternion.Slerp(gun.rotation, eyes.rotation * kept.rotation, released));
        float leftReleased = Released(true);
        for (int i = 0; i < fingers.Count; i++)
            fingers[i].bone.localRotation = Quaternion.Slerp(fingers[i].bone.localRotation, fingerGrip[i], fingers[i].left ? leftReleased : released);
    }

    private void LearnFingers()
    {
        if (fingers.Count == 0)
        {
            for (var bone = HumanBodyBones.LeftThumbProximal; bone <= HumanBodyBones.LeftLittleDistal; bone++)
                if (animator.GetBoneTransform(bone) is Transform finger) fingers.Add((finger, true));
            for (var bone = HumanBodyBones.RightThumbProximal; bone <= HumanBodyBones.RightLittleDistal; bone++)
                if (animator.GetBoneTransform(bone) is Transform finger) fingers.Add((finger, false));
        }
        fingerGrip.Clear();
        foreach (var finger in fingers) fingerGrip.Add(finger.bone.localRotation);
    }

    // Our gun root and muzzle follow the gun in the body's hands: the root on the gun with the sight line forward, the
    // muzzle on the gun's measured muzzle. The muzzle flash, light, tracer and shots start there.
    private void RideGun()
    {
        var gun = held.transform;
        var aimPoint = held.AimPoint;
        Quaternion forward = aimPoint != null ? Quaternion.LookRotation(aimPoint.forward, aimPoint.up) : gun.rotation;
        weaponRoot.SetPositionAndRotation(gun.position, forward);
        if (muzzle == null) return;
        muzzle.SetPositionAndRotation(gun.TransformPoint(held.MuzzlePosition), gun.rotation * held.MuzzleRotation);
        LearnHipMuzzle();
    }

    // Where the muzzle is when the gun is held clean at the hip, and fully down the sights, from the eyes (which stay put in
    // the F5 view), for the wall rule's probes (Prepare): learnt only with FPSAF holding the whole gun at rest, the draw's
    // motion over, no wall holding it back, no reload and not held in close (FPSBody.CloseHold), and only with the muzzle
    // ahead of the eyes. Kept per gun across draws. The probes once read the gun's own muzzle until the first clean frame
    // after a draw: a gun caught at the draw was raised and swung aside, and never learnt again while raised, so it stayed
    // raised and couldn't fire (user, 2026-11-07).
    private void LearnHipMuzzle()
    {
        float aim = controller.AimBlend;
        if (eyes == null || highReady || cover > 0f || closeWeight > 0f || held.HandsBusy || Framework < .999f
            || (aim > .001f && aim < .999f) || Time.time - drawnAt < drawSettle) return;
        Vector3 fromEyes = muzzle.position - eyes.position;
        if (Vector3.Angle(eyes.forward, fromEyes) > HipMuzzleCone) return;
        Vector3 local = eyes.InverseTransformPoint(muzzle.position);
        if (aim >= .999f)
        {
            held.AimedMuzzle = local;
            held.HasAimedMuzzle = true;
            return;
        }
        held.HipMuzzle = local;
        held.HasHipMuzzle = true;
    }

    /// <summary>A reload is playing (its own fingers hold the magazine and bolt).</summary>
    internal bool ReloadingGun => HoldsGun && held.HandsBusy;

    /// <summary>
    /// The held gun's real surface while FPSAF holds it: what the hands rest on and the fingers close round, each hand
    /// by how far it is on its grip (GripHold). Null otherwise. It once waited for the draw's motion to end and for no
    /// reload, so the hands sat off the gun through the draw and the reload and snapped onto it after (user, 2026-11-08).
    /// </summary>
    internal GunSurface HeldSurface
        => HoldsGun && Framework >= .999f && held.Surface != null && !held.Surface.Empty ? held.Surface : null;

    // A hand is on its grip within this of where the bake had it, and off it beyond that (m, at model scale 1).
    private const float GripNear = .06f, GripFar = .12f;

    /// <summary>
    /// How far a hand is on its grip (0-1): by how far it is from where the bake had it on the gun as the gun sits now,
    /// on it within GripNear, off it beyond GripFar (a reload's hand on the magazine or the bolt), between in between.
    /// The draw moves the gun and both hands together, so they count as on it from its first frame. A bake with no
    /// measured grip counts a hand on it except in a reload.
    /// </summary>
    internal float GripHold(bool isLeft)
    {
        if (!HoldsGun) return 0f;
        float fallback = held.HandsBusy ? 0f : 1f;
        if (!held.HasGrip || rig == null || animator == null) return fallback;
        held.GripGaps(animator.GetBoneTransform(HumanBodyBones.RightHand), animator.GetBoneTransform(HumanBodyBones.LeftHand),
            rig.IkWeaponBone, ModelScale, out float rightGap, out float leftGap);
        float gap = isLeft ? leftGap : rightGap;
        if (gap < 0f) return fallback;
        float scale = Mathf.Max(.01f, ModelScale);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GripNear * scale, GripFar * scale, gap));
    }

    // How long FPSAF's IK motion plays (s).
    private static float LengthOf(IkMotionLayerSettings motion)
        => motion == null ? 0f
            : Mathf.Max(motion.rotationCurves.GetCurveLength(), motion.translationCurves.GetCurveLength()) / Mathf.Max(.01f, motion.playRate);

    private void OnReloadStarted(bool empty)
    {
        if (!HoldsGun) return;
        StopFiring();
        CutDraw();
        CutBash();
        // A reload going on from where a cut-short one got to starts its clips there (WeaponRuntime.ReloadStartShare).
        held.Reload(empty, playables, bodyCamera, controller.Runtime != null ? controller.Runtime.ReloadStartShare : 0f);
    }

    // The gun used (a shot, an aim, a reload) once it is ready but before its draw clip is over: the rest of the draw lets
    // go and the gun eases into the hold. A look over the gun is ended by WeaponController the same way.
    private void CutDraw()
    {
        if (HoldsGun && held.Acting) held.StopAction(playables, reloadBlendOut, bodyCamera);
    }

    private void OnReloadCompleted()
    {
        if (held != null) held.ReloadDone();
        reloadEndedAt = Time.time;
    }

    // The gun going away before the next comes out (PlayerWeaponSlots waits for it): a reload under way lets go first.
    private void OnHolstering(float seconds)
    {
        if (!HoldsGun) return;
        StopFiring();
        CutBash();
        held.StopAction(playables, 0f, bodyCamera);
        held.StopReload(playables, 0f, bodyCamera);
        held.PlayHolster(playables, bodyCamera);
    }

    // Y: the gun looked over; WeaponController ends the look when its clip is over or the hands are wanted.
    private void OnInspectStarted()
    {
        float length = HoldsGun ? held.PlayInspect(playables, bodyCamera) : 0f;
        controller.SetInspectLength(length);
    }

    private void OnInspectStopped()
    {
        if (HoldsGun) held.StopAction(playables, reloadBlendOut, bodyCamera);
    }

    /// <summary>The gun of ours drawn now, for the customize menu (GunCustomizeMenu); null without one.</summary>
    internal FPSGun Held => HoldsGun ? held : null;

    /// <summary>This body's own copy of that gun, drawn or not (HolsteredGuns wears a copy of it); null when none was built.</summary>
    internal FPSGun GunFor(WeaponData data) => Find(data);

    // A friend's guns' skins as their PC sent them (GunSkins.LookText), for guns built later too.
    private string skinLook = "";

    /// <summary>A friend's copy: the skins their guns wear, from their PC (PlayerNetworkCombat).</summary>
    internal void WearSkins(string look)
    {
        skinLook = look ?? "";
        foreach (var gun in guns)
            if (gun != null) GunSkins.Wear(gun.gameObject, gun.Data, GunSkins.SkinIn(skinLook, gun.Data));
    }

    /// <summary>L: the held gun's flashlight on or off. False when it wears none.</summary>
    internal bool ToggleLight() => HoldsGun && held.ToggleLight();

    /// <summary>The held gun wears a suppressor.</summary>
    internal bool HeldSuppressed => held != null && held.Suppressed;

    /// <summary>
    /// That gun of ours: its sight, muzzle part and underbarrel part (-1 for no choice) and whether its flashlight is on,
    /// for friends' PCs.
    /// </summary>
    internal int SightOf(WeaponData data) => Find(data) is FPSGun gun ? gun.SightIndex : -1;

    internal int MuzzleOf(WeaponData data) => Find(data) is FPSGun gun ? gun.MuzzleIndex : -1;

    internal int UnderbarrelOf(WeaponData data) => Find(data) is FPSGun gun ? gun.UnderbarrelIndex : -1;

    internal int GripOf(WeaponData data) => Find(data) is FPSGun gun ? gun.GripIndex : -1;

    /// <summary>The held gun's grip (Siege's: a vertical grip steadies the climb, an angled one brings the sight up sooner).</summary>
    internal GunAttachments.Kind HeldGripKind => held != null ? held.GripKind : GunAttachments.Kind.None;

    /// <summary>The held gun is a handgun (its sound profile): the support hand cups the grip hand instead of a handguard.</summary>
    internal bool HeldPistol => held != null && held.Data != null && held.Data.SoundProfile == GunSoundProfile.Pistol;

    /// <summary>
    /// The held gun's grip takes the support hand (PlayerBodyHands.HoldGunGrip): where the palm goes and the ways the
    /// fingers point and the palm faces. False without such a grip, or while the hands are busy (a reload, a draw).
    /// </summary>
    internal bool LeftGrip(out Vector3 palm, out Vector3 fingers, out Vector3 facing)
    {
        palm = fingers = facing = Vector3.zero;
        return HoldsGun && held.LeftGrip(out palm, out fingers, out facing);
    }

    internal bool LightOf(WeaponData data) => Find(data) is FPSGun gun && gun.LightOn;

    /// <summary>
    /// Puts a sight, muzzle part, underbarrel part or flashlight state on that gun of ours, drawn or not: picked in the
    /// customize menu, or what a friend's PC shows (PlayerNetworkCombat). False when nothing changed.
    /// </summary>
    internal bool ShowSight(WeaponData data, int index) => Find(data) is FPSGun gun && gun.ShowSight(index);

    internal bool ShowMuzzle(WeaponData data, int index) => Find(data) is FPSGun gun && gun.ShowMuzzle(index);

    internal bool ShowUnderbarrel(WeaponData data, int index) => Find(data) is FPSGun gun && gun.ShowUnderbarrel(index);

    internal bool ShowGrip(WeaponData data, int index) => Find(data) is FPSGun gun && gun.ShowGrip(index);

    internal void ShowLight(WeaponData data, bool on)
    {
        if (Find(data) is FPSGun gun) gun.ShowLight(on);
    }

    // A shell reload's next shell or its end.
    private void OnReloadStepped(WeaponRuntime.ReloadStep step)
    {
        if (!HoldsGun || !held.Reloading) return;
        held.ReloadStep(step, playables, bodyCamera);
    }

    private void StopFiring()
    {
        if (!firing) return;
        firing = false;
        if (held != null) held.StopFire(recoil);
    }

    private void Report(string message)
    {
        if (reported.Add(message)) Debug.LogWarning("FPSBody: " + message, this);
    }
}
