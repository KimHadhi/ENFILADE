using System;
using UnityEngine;

// PlayerGrenades: A throw an ultimate makes (the Point's nine-bang, PointBreach). One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    // While the off hand throws for an ultimate: at the release this gets where the hand let go, instead of a grenade
    // coming off the belt.
    private Action<Vector3> ultimateRelease;

    /// <summary>An ultimate's throw is in the hand and hasn't left it yet.</summary>
    internal bool UltimatePending => ultimateRelease != null;

    /// <summary>The hand went back to the gun without letting the ultimate's throw go (or never got to it).</summary>
    internal bool UltimateDropped => ultimateRelease != null && (arm == Arm.None || arm == Arm.Lower) && !released;

    /// <summary>The ultimate's throw is taken care of elsewhere (PointBreach's BreachHand threw it without the hand).</summary>
    internal void ForgetUltimateThrow() => ultimateRelease = null;

    /// <summary>
    /// The off hand's quick throw for an ultimate (the Point's nine-bang, PointBreach): <paramref name="look"/>'s tap,
    /// its pin and its whoosh, friends' copies moving their arm as for any tap, and nothing spent from the belt. At the
    /// release <paramref name="release"/> gets where the hand let go. False: the hand is on something already or the body
    /// can't throw now; the caller throws without the hand.
    /// </summary>
    internal bool ThrowForUltimate(Throwable look, Action<Vector3> release)
    {
        if (movement == null || !movement.IsLocal || release == null || arm != Arm.None || Throwables.Of(look) == null || !Able())
            return false;
        ultimateRelease = release;
        StartArm(look);
        pressedAt = Time.time;
        holding = false;
        letGo = true;
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) weapon.InterruptForSurvival();
        Handed?.Invoke(look, HandPhase.Draw);
        Handed?.Invoke(look, HandPhase.TapThrow);
        // As a tapped G: the tap's own clip, or the draw at the tap's speed straight into the throw.
        if (!StartTap()) speed = Motion.TapSpeed;
        return true;
    }

    // From Throw, at the hand's release: the ultimate gets where the hand let go. Nothing comes off the belt, and a
    // flashbang's fuse burning from the pin stops here.
    private void ReleaseForUltimate(ThrowableInfo info)
    {
        var release = ultimateRelease;
        ultimateRelease = null;
        cooking = holding = letGo = false;
        StopAim();
        readyAt = Time.time + Cooldown;
        var eye = Camera.main;
        Vector3 from = HandPoint();
        if (eye != null && info != null) Launch(eye.transform, info, out from, out _);
        Throwables.Play("Throw", from, .8f, 1.5f, 20f);
        Feel();
        release?.Invoke(from);
    }
}
