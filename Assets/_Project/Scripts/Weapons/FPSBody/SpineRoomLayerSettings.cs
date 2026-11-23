using System;
using System.Collections.Generic;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;

/// <summary>
/// An FPS Animation Framework layer of ours, right after its Look: the spine's share of a look down kept within how far the
/// spine may bend (PlayerBody.SpineMost, SpineRoom), as the look's own bend is with nothing in hand (SpineLook). With a gun,
/// Look bends the hips and the spine 56 of a 90° look down (the rifle's profile), and crouched over a floor that put the head
/// and the camera into the knees (the user, 2026-11-23). What the spine gives up the head takes: it keeps the turn Look gave
/// it, so the view, and the gun hung from the head (FPSBodyRig), look where they did. The gun's hold, its wall pose
/// (GunCoverLayer) and the hands' IK come after it on the body as it is bent; the legs are never walls to the gun.
/// Its weight is Look's own (RoomFor gives it Look's curves), so it takes back only what Look put on. FPSBody gives the
/// bones from Look's (RoomFor) and the limit every frame.
/// </summary>
public sealed class SpineRoomLayerSettings : FPSAnimatorLayerSettings
{
    /// <summary>The bones Look bends with the spine, from the hips up, and each one's angle at a 90° look down.</summary>
    [NonSerialized] public List<KRigElement> spine = new List<KRigElement>();
    [NonSerialized] public List<float> angles = new List<float>();
    /// <summary>The head, which keeps the turn Look gave it.</summary>
    [NonSerialized] public KRigElement head;
    [NonSerialized] public bool hasHead;
    /// <summary>How far the spine may bend down now (degrees, all of it together; FPSBody, every frame).</summary>
    [NonSerialized] public float most = SpineRoom.Widest;

    public override IAnimationLayerJob CreateAnimationJob() => new SpineRoomLayerJob();
}
