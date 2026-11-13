using UnityEngine;

// PlayerGrenades: The arm: on this PC's player and on friends' copies. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    // The draw from the gun, at its motion's draw speed.
    private void StartArm(Throwable throwable)
    {
        arm = Arm.Draw;
        armKind = throwable;
        var motion = Motion;
        clip = motion.DrawTable;
        clipTime = 0f;
        speed = motion.DrawSpeed;
        released = pinned = false;
    }

    // The throw clip from its start (it starts where the hold has the hand; the blend covers the rest).
    private void StartThrow()
    {
        var motion = Motion;
        arm = Arm.Throw;
        clip = motion.ThrowTable;
        clipTime = 0f;
        speed = motion.ThrowSpeed;
    }

    // A tap's own clip, where its motion has one (a claymore is set down, never tapped out of the hand). A clip of its own
    // starts as the draw does, from the gun, so it takes over where the draw had got; the throw used as the tap (the
    // knife's) starts from its beginning, as the addon throws at once. False: no tap clip.
    private bool StartTap()
    {
        var info = Throwables.Of(armKind);
        var motion = Motion;
        if (info == null || info.Places || motion.TapClip == null) return false;
        float at = !motion.TapIsThrow && arm == Arm.Draw ? clipTime : 0f;
        arm = Arm.Tap;
        clip = motion.TapTable;
        clipTime = at;
        speed = motion.TapSpeed;
        return true;
    }

    // Seconds from now until the hand lets go: the throw's lead to its release from the hold, or what is left of it (or
    // of a tap's) once under way.
    private float SwingLeft()
    {
        var motion = Motion;
        var throwing = arm == Arm.Tap ? GrenadeArmPath.ClipOf(clip) : motion.ThrowClip;
        if (throwing == null || !throwing.HasRelease) return 0f;
        float at = arm == Arm.Throw || arm == Arm.Tap ? clipTime : 0f;
        return Mathf.Max(0f, throwing.ReleaseAt - at) / Mathf.Max(.05f, arm == Arm.Tap ? speed : motion.ThrowSpeed);
    }

    // Let go at the hold: thrown, or set down (a claymore: on this PC's player's own PC; a friend's copy waits to be told
    // where, ShowMine, and only lowers its hand).
    private void Release()
    {
        var held = Throwables.Of(armKind);
        if (held == null || !held.Places) { StartThrow(); return; }
        if (movement.IsLocal) Put(held);
        else Lower();
    }
}
