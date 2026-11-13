using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;

// FPSGun: Parts parked away from the gun. One part of the class; the rest is in FPSGun.cs and the files beside it.
public sealed partial class FPSGun
{
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
        EndCycle();
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
        EndCycle();
        float rate = Mathf.Max(.01f, clip.blendTime.rateScale);
        playables.PlayAnimation(clip, 0f);
        if (camera != null && source != null) camera.PlayGunMotion(source, rate);
        if (partsPlayer != null && parts != null) partsPlayer.PlayAnimation(parts);
        float length = clip.clip.length / rate;
        actionUntil = Time.time + length;
        // The gun and the hands follow KINEMATION's own clip, as a reload does (FPSBody.Contacts). A draw and a look over
        // the gun settle back into the hold at their end; a holster ends with the gun put away.
        FollowClip(source, clip, 0f, clip != holsterClip, false);
        return length;
    }

    /// <summary>A draw, holster or inspect cut short (a shot, an aim, a reload, a swap): its clips let go.</summary>
    internal void StopAction(IPlayablesController playables, float blendOut, FPSBodyCamera camera)
    {
        if (!Acting) return;
        actionUntil = 0f;
        // The hands let go of its hold with it (a reload's own clip, if one has begun, is kept).
        if (!contactIsReload) contactClip = null;
        playables?.StopAnimation(blendOut);
        if (camera != null) camera.StopGunMotion();
        if (partsPlayer != null && partsPlayer.enabled)
        {
            partsPlayer.enabled = false;
            partsPlayer.enabled = true;
        }
    }
}
