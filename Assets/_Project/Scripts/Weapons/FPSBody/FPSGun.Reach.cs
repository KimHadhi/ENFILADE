using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using UnityEngine;

// FPSGun: The support hand on a bare handguard, for a short arm (FPSBody.FitToReach). One part of the class; the rest is in FPSGun.cs and the files beside it.
public sealed partial class FPSGun
{
    // How far the support palm comes back along the gun (m, along the gun frame's -z), from where along the gun the plain
    // hold has it (its z), and over how far behind that the slide fades out (m).
    private float reachSlide, reachSlideAt, reachSlideFade = .1f;

    /// <summary>
    /// The support palm comes <paramref name="slide"/> back along a bare handguard from the plain hold's place
    /// (<paramref name="at"/>, its z in the gun's frame), fading out over <paramref name="fade"/> behind it (FPSBody.FitToReach).
    /// </summary>
    internal void SetReachSlide(float slide, float at, float fade)
    {
        reachSlide = Mathf.Max(0f, slide);
        reachSlideAt = at;
        // At least twice the slide, so a palm further back never passes one ahead of it (the steepest smoothstep is 1.5).
        reachSlideFade = Mathf.Max(fade, 2f * reachSlide, .01f);
    }

    /// <summary>
    /// A support palm's place in the gun's frame, moved back along the gun (its -z) by the reach slide: all of it at the
    /// plain hold's place on the handguard and ahead of it, less and less behind, none from the fade's length back. So a
    /// reload's hand on the magazine or the loading port stays where its clip has it, a reload's end comes into the slid
    /// hold smoothly, and the hold is the same in every view. None while the gun wears a foregrip: the hand holds the grip
    /// where it was seated (GunAttachments.SeatGrips).
    /// </summary>
    internal Vector3 SlideSupport(Vector3 palm)
    {
        if (reachSlide <= 0f || GripKind != GunAttachments.Kind.None) return palm;
        float share = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reachSlideAt - reachSlideFade, reachSlideAt, palm.z));
        palm.z -= reachSlide * share;
        return palm;
    }

    internal void SetContactPose(Vector3 position, Quaternion rotation, bool holds)
    {
        ContactPosePosition = position;
        ContactPoseRotation = rotation;
        ContactPoseHolds = holds;
        HasContactPose = true;
    }

    /// <summary>
    /// KINEMATION's clip of the reload playing and how far into it it is now (s, at the body clip's pace), with
    /// <paramref name="settling"/> once it is in the stretch where it eases back into the hold. False when none is playing.
    /// </summary>
    internal bool ContactClip(out AnimationClip clip, out float time, out bool settling)
    {
        clip = contactClip;
        time = 0f;
        settling = false;
        if (clip == null) return false;
        float played = Time.time - contactStart;
        if (played >= contactLength)
        {
            contactClip = clip = null;
            return false;
        }
        time = Mathf.Min(contactFrom + played * contactRate, clip.length);
        settling = contactSettles && contactLength - played <= contactSettle;
        return true;
    }

    /// <summary>The body's clip was cut short (the sights coming up): the hands let go of its hold.</summary>
    internal void StopContacts() => contactClip = null;

    // A reload's, draw's, holster's, inspect's or worked action's clip begins: KINEMATION's own one runs alongside at the body
    // clip's pace, from the same share of the way.
    private void FollowClip(AnimationClip source, FPSAnimationAsset body, float from, bool settles, bool reload = true, bool cycle = false)
    {
        contactClip = source != null && FPSAnimationAsset.IsValid(body) ? source : null;
        if (contactClip == null) return;
        contactIsReload = reload;
        contactIsCycle = cycle;
        contactRate = Mathf.Max(.01f, body.blendTime.rateScale);
        contactFrom = Mathf.Clamp01(from) * contactClip.length;
        contactStart = Time.time;
        contactLength = (contactClip.length - contactFrom) / contactRate;
        contactSettles = settles;
        contactSettle = Mathf.Max(.01f, body.blendTime.blendOutTime);
    }

    private void OnDestroy()
    {
        if (shake != null) Destroy(shake);
        if (aimedRecoil != null) Destroy(aimedRecoil);
        foreach (var copy in rated) if (copy != null) Destroy(copy);
    }
}
