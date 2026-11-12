using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

/// <summary>
/// Through a reload the gun and both hands go where KINEMATION's own clip has them, on any body (the user, 2026-11-12, on
/// whether a hand offset per character would make every clip fit: "그대로 가죠" to holding by contact points instead).
/// The reload's remade clip (GunBakes) carries joint angles only. On a body whose arms aren't the reference's, the same
/// angles put the hands somewhere else at every moment, a different miss at each, so no single offset could fix them; and
/// FPS Animation Framework kept the gun in the right hand (the clips have no weapon bone curve), so a gun the clip turns or
/// hands over moved wrong on every body.
/// - The gun: KINEMATION's clip plays on a hidden copy of its arms at the same moment (GunArms). How it moves the gun from
///   where its pose clip holds it, in the gun's own frame, goes onto the gun as this body holds it in view (KeepGunInView's
///   hold from the eyes), so the reload moves the gun as KINEMATION's animators made it and starts from where it is.
/// - The hands: where each palm is on the gun at that moment, the way the fingers point and the palm faces. PlayerBodyHands
///   puts this body's palms there by IK (HoldGunContacts), so a hand on the magazine is on the magazine whatever the arms'
///   length, and a bigger hand takes its wrist further back by itself (ArmIK.WristAtPalm). Tables/GunHands.csv moves a
///   character's palms a little if its hands are thicker or longer.
/// It eases in with the clip (contactBlend), eases back into the hold at a reload's end as the clip does, lets go as fast
/// as a cut-short reload (reloadBlendOut), and only where FPSAF holds the upper body (standing, crouched).
/// </summary>
public sealed partial class FPSBody
{
    [SerializeField, Tooltip("Reloads hold the gun and put the palms where KINEMATION's own clip has them (GunArms). Off: the remade "
        + "clip alone, with the gun kept in the right hand.")]
    private bool reloadContacts = true;
    [SerializeField, Min(.01f), Tooltip("How quickly a reload's hold on the gun comes in (s).")]
    private float contactBlend = .15f;

    // A pose clip holds the gun in both hands when the support palm is this close to the weapon bone (m).
    private const float PoseReach = .8f;

    // One hidden copy per KINEMATION arms model the guns' clips are made for; null where a model lacks the bones.
    private readonly Dictionary<GameObject, GunArms> gunArms = new();
    private GunContact contact;
    private float contactWeight;
    private bool hasContact;
    private FPSGun contactGun;

    // After KeepGunInView, before RideGun and the hands (PlayerBodyHands, 320): the gun to where the reload's clip has it.
    private void FollowContacts()
    {
        if (held != contactGun)
        {
            contactGun = held;
            contactWeight = 0f;
            hasContact = false;
        }
        bool playing = false, settling = false;
        if (reloadContacts && hasGunInView && rig != null && eyes != null
            && held.ContactClip(out AnimationClip clip, out float clipTime, out settling)
            && SampleContact(held, clip, clipTime, out GunContact now))
        {
            contact = now;
            hasContact = true;
            playing = true;
        }
        float target = playing && !settling ? 1f : 0f;
        float ease = target > contactWeight ? contactBlend : settling ? held.ContactSettle : reloadBlendOut;
        contactWeight = Mathf.MoveTowards(contactWeight, target, Time.deltaTime / Mathf.Max(.01f, ease));
        if (contactWeight <= 0f) hasContact = false;
        // Letting go, the last moment read is held while the weight runs out.
        float weight = hasContact ? contactWeight * Framework : 0f;
        if (weight <= 0f) return;
        var gun = rig.IkWeaponBone;
        var kept = hasGunAimed ? KTransform.Lerp(gunAtHip, gunAimed, controller.AimBlend) : gunAtHip;
        Quaternion hold = eyes.rotation * kept.rotation;
        Vector3 at = eyes.TransformPoint(kept.position) + hold * contact.GunMove;
        gun.SetPositionAndRotation(Vector3.Lerp(gun.position, at, weight), Quaternion.Slerp(gun.rotation, hold * contact.GunTurn, weight));
    }

    /// <summary>
    /// Where a hand's palm goes on the gun as the reload's clip has it this frame: the palm's middle, the way the fingers
    /// point and the way the palm faces, in the world as the gun sits now, and how much of the hand it takes (0-1). False
    /// when no reload is being followed.
    /// </summary>
    internal bool HandContact(bool isLeft, out Vector3 palm, out Vector3 fingers, out Vector3 facing, out float weight)
    {
        palm = fingers = facing = Vector3.zero;
        weight = hasContact && HoldsGun && rig != null ? contactWeight * Framework : 0f;
        if (weight <= 0f) return false;
        var gun = rig.IkWeaponBone;
        Quaternion turn = gun.rotation;
        palm = gun.position + turn * (isLeft ? contact.LeftPalm : contact.RightPalm);
        fingers = turn * (isLeft ? contact.LeftFingers : contact.RightFingers);
        facing = turn * (isLeft ? contact.LeftFacing : contact.RightFacing);
        return true;
    }

    // The clip's moment on the gun's arms: its move and turn from the pose clip's hold (measured once per gun), and the
    // palms on the gun.
    private bool SampleContact(FPSGun gun, AnimationClip clip, float time, out GunContact sample)
    {
        sample = default;
        var arms = ArmsFor(gun);
        if (arms == null || clip == null) return false;
        if (!gun.HasContactPose)
        {
            if (!arms.Sample(gun.Entry.sourcePose, 0f)) return false;
            arms.Gun(gun.Frame, out Vector3 posePosition, out Quaternion poseRotation);
            // The pose holds the gun in both hands: the support palm is within PoseReach of the weapon bone (a handguard is
            // under 0.6 m from it). If the clip never took on the copy, its rest pose would hold the arms out wide, and
            // following that would throw the hands off the gun; the reloads then stay on the remade clips.
            bool holds = arms.Palm(true, posePosition, Quaternion.Inverse(poseRotation), out Vector3 support, out _, out _)
                && support.magnitude < PoseReach;
            if (!holds) Report($"KINEMATION's arms didn't take {gun.name}'s pose clip ({gun.Entry.sourcePose.name}); its reloads stay on the remade clip alone.");
            gun.SetContactPose(posePosition, poseRotation, holds);
        }
        if (!gun.ContactPoseHolds || !arms.Sample(clip, time)) return false;
        arms.Gun(gun.Frame, out Vector3 at, out Quaternion turn);
        Quaternion fromPose = Quaternion.Inverse(gun.ContactPoseRotation), toGun = Quaternion.Inverse(turn);
        sample.GunMove = fromPose * (at - gun.ContactPosePosition);
        sample.GunTurn = fromPose * turn;
        return arms.Palm(false, at, toGun, out sample.RightPalm, out sample.RightFingers, out sample.RightFacing)
            && arms.Palm(true, at, toGun, out sample.LeftPalm, out sample.LeftFingers, out sample.LeftFacing);
    }

    private GunArms ArmsFor(FPSGun gun)
    {
        var prefab = assets != null ? gun.Entry.ArmsOr(assets.armsModel) : null;
        if (prefab == null) return null;
        // A model without the bones is kept as null, so it is tried once.
        if (!gunArms.TryGetValue(prefab, out var arms)) gunArms[prefab] = arms = GunArms.Make(prefab);
        return arms;
    }

    // The body went: nothing is being followed. The copies of the arms stay for the next body.
    private void ForgetContacts()
    {
        contactGun = null;
        contactWeight = 0f;
        hasContact = false;
    }

    private void ReleaseContacts()
    {
        ForgetContacts();
        foreach (var arms in gunArms.Values) arms?.Release();
        gunArms.Clear();
    }
}
