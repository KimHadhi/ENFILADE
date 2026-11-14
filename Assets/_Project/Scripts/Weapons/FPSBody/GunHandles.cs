using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A gun as a thing to hold: the points of it a hold needs, in the gun's own frame in metres (the gun model's, which the
/// body's IK weapon bone carries: KINEMATION's weapon bone turned by the gun's rotationOffset).
/// - Where each palm goes on it and the way its fingers point: read from KINEMATION's own pose clip on KINEMATION's arms
///   (GunArms), the support hand's from the no-grip pose where the gun has one, as a bare gun is held.
/// - Its back end (a stock's butt) and the way its barrel points: from its shape (GunMuzzle.Back).
/// What kind of hold a gun takes follows from these alone (GunHold.For): a gun whose back end reaches well behind the trigger
/// hand has a stock and goes to the shoulder; one whose doesn't is held out before the eyes. No list of guns, no sound
/// profile: a gun added later, or a mod's, is held by its shape.
/// The same for every body, so each gun is read once a session.
/// </summary>
internal sealed class GunHandles
{
    /// <summary>A gun has a stock when its back end reaches this far behind the trigger hand's palm (m). A pistol's back end
    /// is at the palm or ahead of it; the shortest stocked gun of the packs reaches over twice this.</summary>
    internal const float StockReach = .1f;
    // How deep the gun's back face is taken when finding the butt's middle (m).
    private const float ButtDepth = .015f;
    // The support hand is on the gun when its palm is within this of the barrel's line through the trigger palm (m). On a
    // rifle or under a pistol's grip hand it is a few centimetres off that line; the Kolibri's pose holds the pistol in one
    // hand, the other 45 cm away.
    private const float OnGun = .2f;

    /// <summary>The gun's frame from KINEMATION's weapon bone.</summary>
    internal Quaternion Frame { get; private set; } = Quaternion.identity;
    internal Vector3 RightPalm { get; private set; }
    internal Vector3 RightFingers { get; private set; }
    internal Vector3 LeftPalm { get; private set; }
    internal Vector3 LeftFingers { get; private set; }
    /// <summary>The middle of the gun's back face, and the way the barrel points (unit).</summary>
    internal Vector3 Butt { get; private set; }
    internal Vector3 Barrel { get; private set; } = Vector3.forward;
    /// <summary>How far the back end reaches behind the trigger hand's palm, along the barrel (m).</summary>
    internal float StockBehindGrip => Vector3.Dot(RightPalm - Butt, Barrel);
    /// <summary>How far ahead of the trigger hand's palm the support hand's is, along the barrel (m).</summary>
    internal float GripsApart => Vector3.Dot(LeftPalm - RightPalm, Barrel);
    /// <summary>The pose holds the gun in both hands.</summary>
    internal bool SupportOnGun
    {
        get
        {
            Vector3 apart = LeftPalm - RightPalm;
            return (apart - Barrel * Vector3.Dot(apart, Barrel)).sqrMagnitude <= OnGun * OnGun;
        }
    }

    // Each gun's handles, read once a session; null for a gun that couldn't be read (tried once).
    private static readonly Dictionary<WeaponData, GunHandles> known = new();

    /// <summary>Every gun is read again when next asked for (a new play session; the editor's hold check starting over).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Forget() => known.Clear();

    private GunHandles() { }

    /// <summary>
    /// <paramref name="entry"/>'s gun as a thing to hold. <paramref name="gun"/> is a copy of its model as the body wears it
    /// (its own space is the gun's frame), <paramref name="aimPoint"/> its sight's, <paramref name="armsModel"/> the arms its
    /// clips are made for when the entry names none, and <paramref name="gunArms"/> keeps the hidden copies of those arms.
    /// Null when the gun's pose couldn't be read on them.
    /// </summary>
    internal static GunHandles Of(in FPSBodyAssets.Gun entry, GameObject armsModel, Quaternion frame, Transform gun, Transform aimPoint,
        Dictionary<GameObject, GunArms> gunArms)
    {
        if (entry.weapon == null) return null;
        if (!known.TryGetValue(entry.weapon, out var handles)) known[entry.weapon] = handles = Read(entry, armsModel, frame, gun, aimPoint, gunArms);
        return handles;
    }

    private static GunHandles Read(in FPSBodyAssets.Gun entry, GameObject armsModel, Quaternion frame, Transform gun, Transform aimPoint,
        Dictionary<GameObject, GunArms> gunArms)
    {
        var prefab = entry.ArmsOr(armsModel);
        if (prefab == null || entry.sourcePose == null || gun == null || gunArms == null) return null;
        if (!gunArms.TryGetValue(prefab, out var arms)) gunArms[prefab] = arms = GunArms.Make(prefab);
        if (arms == null) return null;
        var handles = new GunHandles { Frame = frame };
        if (!handles.Hand(arms, entry.sourcePose, false)) return null;
        var supportPose = entry.sourcePoseNoGrip != null ? entry.sourcePoseNoGrip : entry.sourcePose;
        if (!handles.Hand(arms, supportPose, true) && !handles.Hand(arms, entry.sourcePose, true)) return null;
        GunMuzzle.Back(gun, aimPoint, ButtDepth, out Vector3 butt, out Vector3 barrel);
        handles.Butt = butt;
        if (barrel.sqrMagnitude > .5f) handles.Barrel = barrel.normalized;
        return handles;
    }

    // One hand of KINEMATION's arms in a pose clip: its palm and its fingers' way in the gun's frame. How straight that arm
    // is on its own rig is not read: that rig's shoulders are where first-person arms look right, and its numbers mean
    // nothing on a body's shoulder joint (the arms were once fitted to them: 0.9.91 found it).
    private bool Hand(GunArms arms, AnimationClip pose, bool isLeft)
    {
        if (pose == null || !arms.Sample(pose, 0f)) return false;
        arms.Gun(Frame, out Vector3 at, out Quaternion turn);
        if (!arms.Palm(isLeft, at, Quaternion.Inverse(turn), out Vector3 palm, out Vector3 fingers, out _)) return false;
        if (isLeft) { LeftPalm = palm; LeftFingers = fingers; }
        else { RightPalm = palm; RightFingers = fingers; }
        return true;
    }
}
