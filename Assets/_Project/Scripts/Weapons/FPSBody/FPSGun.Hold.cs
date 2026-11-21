using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

// FPSGun: Where this gun is held, from the eyes (FPSBody.KeepGunInView), and its hands on KINEMATION's arms. One part of the class; the rest is in FPSGun.cs and the files beside it.
public sealed partial class FPSGun
{
    // The hold as FPSAF last held this gun clean (at the hip and fully aimed), from the eyes; until it has held it, the
    // bake's place from the eyes (GunBakes gunFromEyes), so a first draw has a hold to come up into. Kept per gun: the
    // body's last gun was another one's place mid-draw.
    private KTransform holdAtHip;

    private bool hasHold;

    /// <summary>FPSAF has held this gun clean at the hip at least once on this body.</summary>
    internal bool HasLearnedHold { get; private set; }

    /// <summary>Where the bake put this gun from the eyes, until FPSAF has held it (FPSBody.AddGun).</summary>
    internal void SeedHold(Vector3 position, Quaternion rotation)
    {
        if (HasLearnedHold) return;
        holdAtHip = new KTransform(position, rotation, Vector3.one);
        hasHold = true;
    }

    /// <summary>How FPSAF holds this gun now from the eyes at the hip (FPSBody.KeepGunInView). Aimed it is never FPSAF's
    /// since 0.9.318: the gun's aim clips hold it (FPSBody.AimOnClip).</summary>
    internal void LearnHold(KTransform fromEyes)
    {
        holdAtHip = fromEyes;
        hasHold = HasLearnedHold = true;
    }

    /// <summary>The hold from the eyes, <paramref name="aim"/> of the way to the aimed one; false before any is known.</summary>
    internal bool HoldFromEyes(float aim, out KTransform kept)
    {
        kept = holdAtHip;
        if (!hasHold) return false;
        // Aimed off the gun's aim clips (a move whose own clip has the arms): the sight on the eyes, where FPSAF's Ads
        // would bring it.
        if (aim > 0f && AimedFromSight(out KTransform sighted)) kept = KTransform.Lerp(holdAtHip, sighted, aim);
        return true;
    }

    // Where the gun sits fully aimed, from the eyes, by its sight alone: the eye point on the eyes (the sight's own, or the
    // hold's behind it: EyePoint), looking where they look, as FPSAF's Ads with no blend left holds it (FPSBody.ProfileFor).
    // The gun hangs at rest under the IK weapon bone, so its own place is the bone's.
    private bool AimedFromSight(out KTransform aimed)
    {
        aimed = default;
        var aim = EyePoint;
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

    /// <summary>The gun's actions as numbers (GunTrackSet, read in the editor from KINEMATION's own clips): what each does
    /// to the gun and where it has the hands. Null for a gun whose clips were never read.</summary>
    internal GunTrackSet Tracks { get; private set; }

    /// <summary>The gun has tracks and its pose clip holds it in both hands (GunTrackSet.holds), so its own clips can be
    /// followed; else its hands stay on the remade clips alone.</summary>
    internal bool TracksHold => Tracks != null && Tracks.holds;

    /// <summary>FPSBody has said once that this gun's own clips can't be followed.</summary>
    internal bool ToldNoHold;
}
