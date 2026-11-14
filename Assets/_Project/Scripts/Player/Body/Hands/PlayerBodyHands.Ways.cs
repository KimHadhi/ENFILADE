using UnityEngine;

/// <summary>
/// A hand posed by a clip made in Blender gets its fingers here, through this body's own hand (FingerWays,
/// HandGrip.WearWays): the left hand's throw and repair tool (GrenadeArm, RepairArm), and either hand of whatever else
/// poses an arm from a Blender workspace's table. Whoever poses the arm after the hands (FrameOrder.GunFramework + n) asks
/// here, so every such clip's fingers go onto the hand HandGrip measured at rest, the one the gun's clip fingers use too,
/// by the one working, never a copy of it.
/// </summary>
public sealed partial class PlayerBodyHands
{
    /// <summary>
    /// Puts a clip's fingers on this body's left or right hand, <paramref name="weight"/> of the way from what they have now.
    /// False, with nothing done, when <paramref name="model"/> is not the body these hands are bound to (yet), that hand is
    /// missing, or the clip has no ways (a table from before 0.9.61): the caller closes the fingers its own way then.
    /// </summary>
    internal bool WearFingerWays(Animator model, bool isLeft, FingerWays ways, float weight)
    {
        if (ways == null || !ways.Has || model == null || model != boundAnimator) return false;
        var arm = isLeft ? left : right;
        if (arm == null) return false;
        arm.WearWays(ways, weight);
        return true;
    }
}
