using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The support (left) hand lent away from the gun for a while. Something else of the player's takes it and poses it on its
/// own after the hands (FrameOrder.GunFramework + 6): the hold-F point (PointArm), the G off-hand throw and its hold pose.
/// Lent, the left hand's gun contact (GunContact), its hold on a grip the gun wears (GunGrip), its gun fingers (GunFingers)
/// and its place on the gun through a vault or slide (Place) ease out over Hands.blendTime (0.15 s), so the borrower starts
/// from a hand that nothing else holds; given back, they take the gun again over the same time. Nothing else changes:
/// FPS Animation Framework keeps the upper body and the right hand keeps the gun. A one-handed vault (leftOffGun) is not
/// this: it hands the upper body to the move's clip.
/// The manager's decision (2026-11-12): one shared way for every off-hand use, instead of overrides stacked in LateUpdate.
/// </summary>
public sealed partial class PlayerBodyHands
{
    // Who has the left hand lent and how much of it (0-1, above 0); the largest share holds.
    private readonly List<(Object who, float share)> offHandLends = new();
    private float offHandLent;

    /// <summary>
    /// Lends <paramref name="share"/> (0-1, clamped) of the left hand away from the gun to <paramref name="who"/>, the caller
    /// itself (its own component, <c>this</c>), until the same caller lends again with another share, or gives it back with 0.
    /// Calling again every frame with the same share costs nothing. With several borrowers the largest share holds. A
    /// borrower that is destroyed, or is a component switched off, or a game object made inactive, gives its share back by
    /// itself on the next frame. The hand eases over Hands.blendTime both ways (OffHandLent).
    /// </summary>
    internal void LendOffHand(Object who, float share)
    {
        if (who == null) return;
        share = Mathf.Clamp01(share);
        for (int i = 0; i < offHandLends.Count; i++)
        {
            if (!ReferenceEquals(offHandLends[i].who, who)) continue;
            if (share <= 0f) offHandLends.RemoveAt(i);
            else offHandLends[i] = (who, share);
            return;
        }
        if (share > 0f) offHandLends.Add((who, share));
    }

    /// <summary>
    /// How far the left hand is lent away from the gun now (0-1), eased over Hands.blendTime: a borrower may blend its own
    /// pose in by it, so the hand leaves the gun as the pose comes in.
    /// </summary>
    internal float OffHandLent => offHandLent;

    // How much of the left hand the gun keeps (1 with nothing lent).
    private float OffHandKept => 1f - offHandLent;

    // Each frame before the hands are placed: borrowers gone or switched off give their share back, and the lent share
    // eases toward the largest one left.
    private void EaseOffHand(float dt)
    {
        float wanted = 0f;
        for (int i = offHandLends.Count - 1; i >= 0; i--)
        {
            var lend = offHandLends[i];
            if (!Lending(lend.who))
            {
                offHandLends.RemoveAt(i);
                continue;
            }
            wanted = Mathf.Max(wanted, lend.share);
        }
        offHandLent = Mathf.MoveTowards(offHandLent, wanted, dt / Mathf.Max(.01f, blendTime));
    }

    // Still there and switched on.
    private static bool Lending(Object who)
    {
        if (who == null) return false;
        if (who is Behaviour behaviour) return behaviour.isActiveAndEnabled;
        if (who is GameObject owner) return owner.activeInHierarchy;
        return true;
    }
}
