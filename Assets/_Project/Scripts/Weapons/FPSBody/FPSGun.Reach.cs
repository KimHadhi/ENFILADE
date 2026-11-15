using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using UnityEngine;

// FPSGun: This body's hold of the gun (GunHold) and the clip its hands follow. One part of the class; the rest is in FPSGun.cs and the files beside it.
public sealed partial class FPSGun
{
    // How far back along the gun this body's support palm holds it from where KINEMATION's does (GunHold's slide, set with
    // the hold: SetHold). None until the gun's hold is made.
    private SupportSlide supportSlide;

    // How far the palm at the hold's place comes back (m), for the grips drawn back with the hand (SeatGrips).
    private float reachSlide => supportSlide.Back;

    /// <summary>
    /// A support palm's place in the gun's frame, moved back along the gun (its -z) by this body's slide (SupportSlide: all of
    /// it at the plain hold's place on the handguard and ahead of it, less and less behind, none from the fade's length
    /// back, so a reload's hand on the magazine or the loading port stays where its clip has it). None while the gun wears a
    /// foregrip: the hand holds the grip where it was seated (GunAttachments.SeatGrips).
    /// </summary>
    internal Vector3 SlideSupport(Vector3 palm) => GripKind != GunAttachments.Kind.None ? palm : supportSlide.Of(palm);

    // ---- How this body holds the gun (GunHold) ----

    // The gun's hold and the body it was made for, kept for what depends on them later: what the gun turns about onto the
    // crosshair's point, and where the eye rests behind each sight.
    private GunHold hold;
    private HoldBody holder;
    // For each sight's own eye point, the point FPSAF lines up with the eyes: the same, or one behind it on the sight line.
    private readonly System.Collections.Generic.Dictionary<Transform, Transform> eyePoints = new();

    /// <summary>
    /// This body's hold of the gun (FPSBody.ArmReach, GunHold) and what it asks of the support hand: <paramref name="slide"/>
    /// back along the gun from the plain hold's place (<paramref name="at"/>, its z in the gun's frame), fading out over
    /// <paramref name="fade"/> behind it (SlideSupport).
    /// </summary>
    internal void SetHold(GunHold hold, HoldBody holder, float slide, float at, float fade)
    {
        this.hold = hold;
        this.holder = holder;
        supportSlide = new SupportSlide(slide, at, fade);
        foreach (var point in eyePoints)
            if (point.Value != null && point.Value != point.Key) Destroy(point.Value.gameObject);
        eyePoints.Clear();
        if (Entity != null) Entity.defaultAimPoint = EyePoint;
    }

    /// <summary>
    /// The point of the gun the body holds it by, in the gun's frame (GunHold.Pivot): it stays put while the barrel turns
    /// onto the crosshair's point (GunConvergeLayer). False for a gun whose hold couldn't be made; it turns about its muzzle.
    /// </summary>
    internal bool HoldPivot(out Vector3 pivot)
    {
        pivot = hold != null ? hold.Pivot : Vector3.zero;
        return hold != null;
    }

    /// <summary>
    /// What FPSAF's Ads lines up with the eyes: the shown sight's own eye point, or a point behind it on the sight line by the
    /// hold's eye relief on this body (GunHold.EyeRelief: a handgun whose sight wants the eye nearer than the arms can fold).
    /// One per sight, so another sight is another point and FPSAF eases the view across as it does between aim points.
    /// </summary>
    internal Transform EyePoint
    {
        get
        {
            var sight = AimPoint;
            if (hold == null || holder == null || sight == null || sight == transform) return sight;
            if (eyePoints.TryGetValue(sight, out var known) && known != null) return known;
            Quaternion toGun = Quaternion.Inverse(transform.rotation);
            float relief = hold.EyeRelief(holder, toGun * (sight.position - transform.position), toGun * sight.rotation);
            Transform point = sight;
            if (relief > .001f)
            {
                point = new GameObject("Eye point").transform;
                point.SetParent(sight, false);
                point.SetPositionAndRotation(sight.position - sight.forward * relief, sight.rotation);
            }
            return eyePoints[sight] = point;
        }
    }

    private void OnDestroy()
    {
        if (shake != null) Destroy(shake);
        if (aimedRecoil != null) Destroy(aimedRecoil);
        foreach (var copy in rated) if (copy != null) Destroy(copy);
    }
}
