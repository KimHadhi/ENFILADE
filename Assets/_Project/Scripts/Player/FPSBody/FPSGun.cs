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
    /// <summary>A clip of the gun's own has the hands (a reload, a draw, a holster, an inspect), not the grips.</summary>
    internal bool HandsBusy => reloading || Acting;
    /// <summary>The gun has a draw clip of its own, so FPSAF's equip motion stays out.</summary>
    internal bool HasDraw => FPSAnimationAsset.IsValid(drawClip);
    // The muzzle as this gun sits held clean at the hip, from the eyes (FPSBody.LearnHipMuzzle), for the wall rule.
    internal Vector3 HipMuzzle { get; set; }
    internal bool HasHipMuzzle { get; set; }

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

    // ---- Parts parked away from the gun ----

    // Parts the gun's own clips keep parked away from it while they are not in use: the R08's speedloader waits 65 cm off
    // the revolver in its idle, at full size. Out of the first-person view that was fine, but beside the player in the F5
    // view and on friends' screens it hung in the air (user picked hiding it, 2026-11-09). A part further than this (m) from
    // the sight line is shrunk to nothing after the clips have posed the gun, every frame it is away, since the clips key its
    // size themselves (the R08's reload grows it from 0 as it comes in); it shows again as it comes to the gun. The speedloader
    // parks 0.45 m off; the furthest part a gun really wears, the MX68's ammo box, about 0.27 m under a raised sight.
    private const float ParkedFrom = .4f;

    private struct Parkable
    {
        public Transform part;
        public Vector3 scale;
        // hidden: shrunk by us now; keyed: its clips put a size back over ours, so they show it again themselves.
        public bool hidden, keyed;
    }

    private readonly System.Collections.Generic.List<Parkable> parked = new();

    // The bones and parts that sit parked as the gun is set up (its idle pose).
    private void FindParked()
    {
        parked.Clear();
        var seen = new System.Collections.Generic.HashSet<Transform>();
        foreach (var skinned in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (var bone in skinned.bones)
                if (bone != null && seen.Add(bone) && Away(bone)) parked.Add(new Parkable { part = bone, scale = bone.localScale });
        foreach (var mesh in GetComponentsInChildren<MeshRenderer>(true))
            if (seen.Add(mesh.transform) && Away(mesh.transform)) parked.Add(new Parkable { part = mesh.transform, scale = mesh.transform.localScale });
    }

    private bool Away(Transform part)
    {
        var aim = AimPoint;
        Vector3 offset = part.position - aim.position, along = aim.forward;
        return (offset - along * Vector3.Dot(offset, along)).sqrMagnitude > ParkedFrom * ParkedFrom;
    }

    // After the gun's clips have posed it this frame, before it is drawn.
    private void LateUpdate()
    {
        for (int i = 0; i < parked.Count; i++)
        {
            var p = parked[i];
            if (p.part == null) continue;
            if (p.hidden && p.part.localScale != Vector3.zero) p.keyed = true;
            if (Away(p.part))
            {
                p.part.localScale = Vector3.zero;
                p.hidden = true;
            }
            else if (p.hidden)
            {
                if (!p.keyed) p.part.localScale = p.scale;
                p.hidden = false;
            }
            parked[i] = p;
        }
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

    /// <summary>
    /// Drawn: FPSAF links this gun's profile (Weapon.OnEquip) and its recoil gets our fire rate. Its recoil animation is
    /// KINEMATION's, aimed the Rainbow Six Siege way (AimedRecoil): <paramref name="aimedTurn"/> of its turn and
    /// <paramref name="aimedLift"/> of its lift and sideways push down the sights.
    /// </summary>
    internal void Equip(FPSAnimator animator, RecoilAnimation recoil, FPSBodyAssets assets, float roundsPerMinute, FireMode mode,
        FPSBodyCamera camera, float aimedTurn = 1f, float aimedLift = 1f)
    {
        reloading = false;
        EnsureRestPose();
        if (recoil != null && Kinemation != null && Kinemation.weaponSettings != null)
            recoil.Init(AimedRecoil(Kinemation.weaponSettings.recoilAnimData, aimedTurn, aimedLift), roundsPerMinute, mode);
        animator.LinkAnimatorProfile(gameObject);
        // A gun with a draw clip of its own comes out with it (FPSBody.Draw) instead of FPSAF's equip motion.
        if (assets.equipMotion != null && !HasDraw) animator.LinkAnimatorLayer(assets.equipMotion);
        if (camera != null) camera.HoldGunMotion(entry.sourcePose);
    }

    // KINEMATION's recoil animation for this gun with its aiming multipliers set the Rainbow Six Siege way: down the sights
    // the gun kicks straight back into the shoulder, hardly turns, lifts or wanders, so the sight stays on what the shot hit;
    // the view's own recoil (PlayerLook) carries the climb. Made once per gun and kept.
    private RecoilAnimData aimedRecoil;
    private RecoilAnimData AimedRecoil(RecoilAnimData source, float aimedTurn, float aimedLift)
    {
        if (source == null) return null;
        if (aimedRecoil != null) return aimedRecoil;
        aimedRecoil = Instantiate(source);
        aimedRecoil.hideFlags = HideFlags.DontSave;
        aimedRecoil.aimRot = source.aimRot * aimedTurn;
        aimedRecoil.aimLoc = new Vector3(source.aimLoc.x * aimedLift, source.aimLoc.y * aimedLift, source.aimLoc.z);
        aimedRecoil.noiseScalar = source.noiseScalar * aimedTurn;
        var sway = aimedRecoil.recoilSway;
        sway.adsScale *= aimedTurn;
        aimedRecoil.recoilSway = sway;
        return aimedRecoil;
    }

    /// <summary>The draw, holster and inspect clips made for this body (FPSBody, from GunBakes).</summary>
    internal void SetActions(FPSAnimationAsset draw, FPSAnimationAsset holster, FPSAnimationAsset inspect)
    {
        drawClip = draw;
        holsterClip = holster;
        inspectClip = inspect;
        partsDraw = RatedParts(entry.partsDraw, draw);
        partsHolster = RatedParts(entry.partsHolster, holster);
        partsInspect = RatedParts(entry.partsInspect, inspect);
    }

    // The gun-part clip at the rate the body's clip plays at, so the two stay together.
    private KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset RatedParts(
        KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset parts, FPSAnimationAsset body)
    {
        if (parts == null) return null;
        float rate = body != null ? body.blendTime.rateScale : 1f;
        if (Mathf.Approximately(rate, 1f)) return parts;
        var copy = Instantiate(parts);
        copy.hideFlags = HideFlags.DontSave;
        copy.playRate = parts.playRate * rate;
        rated.Add(copy);
        return copy;
    }

    /// <summary>The gun coming out: its draw clip. Returns how long it plays (s); 0 without one.</summary>
    internal float PlayDraw(IPlayablesController playables, FPSBodyCamera camera)
        => PlayAction(drawClip, entry.sourceDraw, partsDraw, playables, camera);

    /// <summary>The gun going away: its holster clip.</summary>
    internal float PlayHolster(IPlayablesController playables, FPSBodyCamera camera)
        => PlayAction(holsterClip, entry.sourceHolster, partsHolster, playables, camera);

    /// <summary>Y: the gun looked over: its inspect clip.</summary>
    internal float PlayInspect(IPlayablesController playables, FPSBodyCamera camera)
        => PlayAction(inspectClip, entry.sourceInspect, partsInspect, playables, camera);

    private float PlayAction(FPSAnimationAsset clip, AnimationClip source,
        KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset parts, IPlayablesController playables, FPSBodyCamera camera)
    {
        if (!FPSAnimationAsset.IsValid(clip) || playables == null) return 0f;
        float rate = Mathf.Max(.01f, clip.blendTime.rateScale);
        playables.PlayAnimation(clip, 0f);
        if (camera != null && source != null) camera.PlayGunMotion(source, rate);
        if (partsPlayer != null && parts != null) partsPlayer.PlayAnimation(parts);
        float length = clip.clip.length / rate;
        actionUntil = Time.time + length;
        return length;
    }

    /// <summary>A draw, holster or inspect cut short (a shot, an aim, a reload, a swap): its clips let go.</summary>
    internal void StopAction(IPlayablesController playables, float blendOut, FPSBodyCamera camera)
    {
        if (!Acting) return;
        actionUntil = 0f;
        playables?.StopAnimation(blendOut);
        if (camera != null) camera.StopGunMotion();
        if (partsPlayer != null && partsPlayer.enabled)
        {
            partsPlayer.enabled = false;
            partsPlayer.enabled = true;
        }
    }

    // ---- Muzzle, grip and underbarrel parts (GunAttachments) ----

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
        recoil?.Stop();
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
    /// Our reload began (Weapon.OnReload): its clip on the body, its gun-part clip on the gun, its camera motion, all from
    /// <paramref name="from"/> of the way through when it goes on from where a cut-short one got to (WeaponRuntime keeps
    /// that). Returns how long the body's whole clip plays (s).
    /// </summary>
    internal float Reload(bool empty, IPlayablesController playables, FPSBodyCamera camera, float from = 0f)
    {
        reloadEmptyPlaying = empty;
        reloading = true;
        from = Mathf.Clamp01(from);
        var clip = empty ? reloadEmpty : reloadTactical;
        if (FPSAnimationAsset.IsValid(clip)) playables.PlayAnimation(clip, from * clip.clip.length);
        if (camera != null)
            camera.PlayGunMotion(empty ? entry.sourceReloadEmpty : entry.sourceReloadTactical, clip != null ? clip.blendTime.rateScale : 1f, from);
        float length = clip != null && clip.clip != null ? clip.clip.length / Mathf.Max(.01f, clip.blendTime.rateScale) : 0f;
        // The old magazine will fall out as the clip takes it away (GunMagazine); going on past that, it is already out.
        if (magazine != null) magazine.Begin(empty, length, from);
        if (Kinemation == null || partsPlayer == null) return length;
        var pair = empty ? Kinemation.reloadEmpty : Kinemation.reloadTactical;
        // The reload's own clip takes the slide from the last round's and lets it go forward.
        if (pair.weapon != null && partsPlayer.PlayAnimation(pair.weapon, PartsTime(pair, from))) slideHeld = false;
        return length;
    }

    // Where in the gun-part clip a reload <paramref name="share"/> of the way through is: the pair plays fitted to the
    // character clip's length with the part clip at its own rate, as ReloadMoments measures it.
    private static float PartsTime(FPSAnimationPair pair, float share)
    {
        if (share <= 0f || pair.weapon == null || pair.weapon.clip == null) return 0f;
        var timing = pair.character != null ? pair.character : pair.weapon;
        float span = timing.GetPlayLength() * (Mathf.Approximately(pair.weapon.playRate, 0f) ? 1f : pair.weapon.playRate);
        return Mathf.Clamp(share * span, 0f, pair.weapon.clip.length);
    }

    /// <summary>
    /// A shell reload moved on (WeaponRuntime.ReloadStep): the shell clip again for the next shell, or the end clip, on the
    /// body, the gun's parts and the camera, as KINEMATION's TacticalShotgun plays its loop and end. Returns how long the
    /// body's clip plays (s).
    /// </summary>
    internal float ReloadStep(WeaponRuntime.ReloadStep step, IPlayablesController playables, FPSBodyCamera camera)
    {
        bool end = step == WeaponRuntime.ReloadStep.End;
        var clip = end ? reloadEnd : reloadShell;
        reloading = true;
        if (FPSAnimationAsset.IsValid(clip)) playables.PlayAnimation(clip, 0f);
        if (camera != null)
            camera.PlayGunMotion(end ? entry.sourceReloadEnd : entry.sourceReloadShell, clip != null ? clip.blendTime.rateScale : 1f);
        var parts = end ? entry.partsReloadEnd : entry.partsReloadShell;
        if (partsPlayer != null && parts != null) partsPlayer.PlayAnimation(parts);
        return clip != null && clip.clip != null ? clip.clip.length / Mathf.Max(.01f, clip.blendTime.rateScale) : 0f;
    }

    /// <summary>Our reload finished: the gun is loaded, so a slide still held back goes forward.</summary>
    internal void ReloadDone()
    {
        reloading = false;
        if (magazine != null) magazine.End();
        ReleaseSlide();
    }

    /// <summary>The reload was cut short (a dodge, a climb, death): its clips stop.</summary>
    internal void StopReload(IPlayablesController playables, float blendOut, FPSBodyCamera camera)
    {
        if (!reloading) return;
        reloading = false;
        // Cut short with the old magazine leaving, the hand lets it go and the gun stays without one until the next reload
        // brings the new one (WeaponRuntime.MagazineOut); cut short before that, the gun keeps its magazine.
        if (magazine != null) magazine.End(true);
        playables?.StopAnimation(blendOut);
        if (camera != null) camera.StopGunMotion();
        if (Kinemation == null) return;
        var pair = reloadEmptyPlaying ? Kinemation.reloadEmpty : Kinemation.reloadTactical;
        // KINEMATION's gun-part player stops its clip when it is switched off.
        if (partsPlayer != null && (pair.weapon != null || entry.partsReloadShell != null) && partsPlayer.enabled)
        {
            partsPlayer.enabled = false;
            partsPlayer.enabled = true;
        }
    }

    private void OnDestroy()
    {
        if (shake != null) Destroy(shake);
        if (aimedRecoil != null) Destroy(aimedRecoil);
        foreach (var copy in rated) if (copy != null) Destroy(copy);
    }
}
