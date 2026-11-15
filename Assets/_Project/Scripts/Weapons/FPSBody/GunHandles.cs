using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A gun as a thing to hold: the points of it a hold needs, in the gun's own frame in metres (the gun model's, which the
/// body's IK weapon bone carries: KINEMATION's weapon bone turned by the gun's rotationOffset).
/// - Where each palm goes on it and the way its fingers point: KINEMATION's own pose clip on KINEMATION's arms, as the
///   gun's tracks keep it (GunTrackSet), the support hand's from the no-grip pose where the gun has one, as a bare gun is
///   held.
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
    /// <paramref name="entry"/>'s gun as a thing to hold. <paramref name="tracks"/> are its actions as numbers (GunTracks),
    /// <paramref name="gun"/> a copy of its model as the body wears it (its own space is the gun's frame) and
    /// <paramref name="aimPoint"/> its sight's. Null when the gun has no track of its pose.
    /// </summary>
    internal static GunHandles Of(in FPSBodyAssets.Gun entry, GunTrackSet tracks, Quaternion frame, Transform gun, Transform aimPoint)
    {
        if (entry.weapon == null) return null;
        if (!known.TryGetValue(entry.weapon, out var handles)) known[entry.weapon] = handles = Read(tracks, frame, gun, aimPoint);
        return handles;
    }

    // Each palm and its fingers' way in the gun's frame, as the pose's track has them. How straight KINEMATION's arms are in
    // that pose is not read: that rig's shoulders are where first-person arms look right, and its numbers mean nothing on a
    // body's shoulder joint (the arms were once fitted to them: 0.9.91 found it; 0.9.115 measured where they are).
    private static GunHandles Read(GunTrackSet tracks, Quaternion frame, Transform gun, Transform aimPoint)
    {
        var pose = tracks != null ? tracks.Track(GunTrackKind.Hold) : null;
        if (pose == null || gun == null) return null;
        var bare = tracks.Track(GunTrackKind.HoldNoGrip) ?? pose;
        var handles = new GunHandles
        {
            Frame = frame, RightPalm = pose.right[0].palm, RightFingers = pose.right[0].fingers,
            LeftPalm = bare.left[0].palm, LeftFingers = bare.left[0].fingers
        };
        GunMuzzle.Back(gun, aimPoint, ButtDepth, out Vector3 butt, out Vector3 barrel);
        handles.Butt = butt;
        if (barrel.sqrMagnitude > .5f) handles.Barrel = barrel.normalized;
        return handles;
    }
}
