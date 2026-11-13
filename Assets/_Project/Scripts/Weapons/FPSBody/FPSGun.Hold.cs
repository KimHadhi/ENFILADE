using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

// FPSGun: Where this gun is held, from the eyes (FPSBody.KeepGunInView), and its hands on KINEMATION's arms. One part of the class; the rest is in FPSGun.cs and the files beside it.
public sealed partial class FPSGun
{
    // The hold as FPSAF last held this gun clean (at the hip and fully aimed), from the eyes; until it has held it, the
    // bake's place from the eyes (GunBakes gunFromEyes), so a first draw has a hold to come up into. Kept per gun: the
    // body's last gun was another one's place mid-draw.
    private KTransform holdAtHip, holdAimed;

    private bool hasHold, hasHoldAimed;

    /// <summary>FPSAF has held this gun clean at the hip at least once on this body.</summary>
    internal bool HasLearnedHold { get; private set; }

    /// <summary>Where the bake put this gun from the eyes, until FPSAF has held it (FPSBody.AddGun).</summary>
    internal void SeedHold(Vector3 position, Quaternion rotation)
    {
        if (HasLearnedHold) return;
        holdAtHip = new KTransform(position, rotation, Vector3.one);
        hasHold = true;
    }

    /// <summary>How FPSAF holds this gun now from the eyes, at the hip or fully aimed (FPSBody.KeepGunInView).</summary>
    internal void LearnHold(KTransform fromEyes, bool aimed)
    {
        if (aimed)
        {
            holdAimed = fromEyes;
            hasHoldAimed = true;
            return;
        }
        holdAtHip = fromEyes;
        hasHold = HasLearnedHold = true;
    }

    /// <summary>The hold from the eyes, <paramref name="aim"/> of the way to the aimed one; false before any is known.</summary>
    internal bool HoldFromEyes(float aim, out KTransform kept)
    {
        kept = holdAtHip;
        if (!hasHold) return false;
        if (hasHoldAimed) kept = KTransform.Lerp(holdAtHip, holdAimed, aim);
        // Not yet held fully aimed on this body (a bolt worked after a shot fired on the way up, the first time): the sight on
        // the eyes, where FPSAF's Ads brings it, so the gun doesn't drop back to the hip meanwhile.
        else if (aim > 0f && AimedFromSight(out KTransform sighted)) kept = KTransform.Lerp(holdAtHip, sighted, aim);
        return true;
    }

    // Where the gun sits fully aimed, from the eyes, by its sight alone: the aim point on the eyes, looking where they look, as
    // FPSAF's Ads with no blend left holds it (FPSBody.ProfileFor). The gun hangs at rest under the IK weapon bone, so its
    // own place is the bone's.
    private bool AimedFromSight(out KTransform aimed)
    {
        aimed = default;
        var aim = AimPoint;
        if (aim == null || aim == transform) return false;
        Quaternion toGun = Quaternion.Inverse(transform.rotation);
        Quaternion turn = Quaternion.Inverse(toGun * aim.rotation);
        aimed = new KTransform(-(turn * (toGun * (aim.position - transform.position))), turn, Vector3.one);
        return true;
    }

    /// <summary>
    /// Both hands as this gun's pose clip has them on KINEMATION's arms, in the gun's frame, with each hand's finger ways
    /// (GunContact; FPSBody measures it once per gun), and the same with the gun's no-grip pose (FPSBodyAssets.Gun
    /// sourcePoseNoGrip) where it has one. Fields, not properties: FPSBody fills them in place.
    /// </summary>
    internal GunContact HoldContact, HoldContactNoGrip;

    internal bool HasHoldContact, HasHoldContactNoGrip, HoldContactTried;

    /// <summary>The gun's frame from KINEMATION's weapon bone (its rotationOffset), as the IK weapon bone carries it.</summary>
    internal Quaternion Frame => frame;

    /// <summary>The gun's entry in FPSBodyAssets: its own clips and the arms they are made for.</summary>
    internal FPSBodyAssets.Gun Entry => entry;

    /// <summary>Where the gun's pose clip holds it on KINEMATION's arms, in the gun's frame (FPSBody measures it once), and
    /// whether that hold looked like one (both hands at the gun), so its reloads can be followed.</summary>
    internal bool HasContactPose { get; private set; }

    internal bool ContactPoseHolds { get; private set; }

    internal Vector3 ContactPosePosition { get; private set; }

    internal Quaternion ContactPoseRotation { get; private set; } = Quaternion.identity;

    /// <summary>How long the reload's end eases back into the hold (s), its clip's blend out.</summary>
    internal float ContactSettle => contactSettle;
}
