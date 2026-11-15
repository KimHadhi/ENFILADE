using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

/// <summary>
/// The last of our jobs in the chain of FPSAF's layers on our body (FPSBodyBoneController): when the held gun changes, the
/// body's bones ease from where the animation left them on the frame before to where the new gun's layers have them, over
/// the new gun's profile's blend time. It does what KINEMATION's blend there did (AnimationBlendingJob, which still stands
/// after it and is never asked for on this body) but for two things, both read in one play's record
/// (Logs/gun-actions.txt, 2026-11-15) and then in KINEMATION's code:
/// - The gun's own bones are not eased. KINEMATION's eased every bone of the rig, the bone the gun hangs from among them,
///   so for the profile's blend time (0.25 to 0.3 s) a new gun slid in from where the last gun had been held. The
///   Herrington drawn after the MKR9 began where the MKR9 was (5.1, -11.8, 29.2 cm from the eyes) and went straight to its
///   own hold, never lower than 13.4 cm under the eyes, though its draw begins 37.5 cm under them. A gun has no place
///   before it is drawn, so there is nothing to ease it from: it is where its hold and its draw have it from its first
///   frame (GunActionLayer). While the bones above it ease, it keeps its place from the eyes (the camera rides the eyes,
///   and everything that aims the gun aims it by them) and its facing in the world.
/// - It keeps where it left every bone on every frame, so a change of gun takes effect on the frame it happens.
///   KINEMATION's remembered a pose only once asked, at the end of that frame, and the new gun's layers went in on the
///   next: for that one frame the new gun's model hung on the bone while the last gun's layers still ran.
/// </summary>
public struct BodyCrossFadeJob : IAnimationJob
{
    // The rig's bones but the gun's own, each under its parent: where this job left it on the last frame, and where it
    // stood on the frame before the fade under way began.
    private NativeArray<TransformStreamHandle> bones;
    private NativeArray<Pose> shown, from;
    // The bone the gun hangs from, and the eyes' place on the head (FPSAF's camera element).
    private TransformStreamHandle gun, eyes;
    private bool hasGun;
    private EaseMode ease;
    private float length, played;
    private bool begin, fading, hasShown;

    public BodyCrossFadeJob(Animator animator, IReadOnlyList<Transform> body, Transform gunBone, Transform eyePoint)
    {
        int count = body.Count;
        bones = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent);
        shown = new NativeArray<Pose>(count, Allocator.Persistent);
        from = new NativeArray<Pose>(count, Allocator.Persistent);
        for (int i = 0; i < count; i++) bones[i] = animator.BindStreamTransform(body[i]);
        hasGun = gunBone != null && eyePoint != null;
        gun = hasGun ? animator.BindStreamTransform(gunBone) : default;
        eyes = hasGun ? animator.BindStreamTransform(eyePoint) : default;
        ease = default;
        length = played = 0f;
        begin = fading = hasShown = false;
    }

    /// <summary>
    /// From the next frame the animation makes, the body eases from where it was left on the frame before, over
    /// <paramref name="seconds"/>. Said on a copy of the playable's job, which is then given back to it.
    /// </summary>
    public void Begin(float seconds, EaseMode easing)
    {
        begin = true;
        length = seconds;
        ease = easing;
    }

    /// <summary>The job's native data, once: the copies of the job share it.</summary>
    public void Dispose()
    {
        if (bones.IsCreated) bones.Dispose();
        if (shown.IsCreated) shown.Dispose();
        if (from.IsCreated) from.Dispose();
    }

    public void ProcessAnimation(AnimationStream stream)
    {
        if (!bones.IsCreated) return;
        if (begin)
        {
            begin = false;
            played = 0f;
            // Before the animation has made a frame through this job there is nothing to ease from.
            fading = hasShown && length > 0f;
            if (fading) from.CopyFrom(shown);
        }
        float share = 1f;
        if (fading)
        {
            played += stream.deltaTime;
            if (played >= length) fading = false;
            else share = KCurves.Ease(0f, 1f, played / length, ease);
        }

        // Where the layers have the gun from the eyes, before the bones it hangs under are eased.
        bool carried = fading && hasGun;
        Vector3 gunAt = default, eyesAt = default;
        Quaternion gunFacing = default;
        if (carried)
        {
            gunAt = gun.GetPosition(stream);
            gunFacing = gun.GetRotation(stream);
            eyesAt = eyes.GetPosition(stream);
        }

        int count = bones.Length;
        for (int i = 0; i < count; i++)
        {
            var bone = bones[i];
            var now = new Pose(bone.GetLocalPosition(stream), bone.GetLocalRotation(stream));
            if (fading)
            {
                var was = from[i];
                now = new Pose(Vector3.Lerp(was.position, now.position, share), Quaternion.Slerp(was.rotation, now.rotation, share));
                bone.SetLocalPosition(stream, now.position);
                bone.SetLocalRotation(stream, now.rotation);
            }
            shown[i] = now;
        }
        hasShown = true;

        if (!carried) return;
        gun.SetPosition(stream, gunAt + (eyes.GetPosition(stream) - eyesAt));
        gun.SetRotation(stream, gunFacing);
    }

    public void ProcessRootMotion(AnimationStream stream) { }
}
