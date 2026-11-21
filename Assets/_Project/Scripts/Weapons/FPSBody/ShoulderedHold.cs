using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

/// <summary>
/// A gun with a stock: its butt rests on the front of the body's own right shoulder, in the pocket (HoldBody.Pocket: where
/// the body itself ends there, measured from its meshes), and its barrel points where the body looks. That is the whole
/// place of the gun, on any body: one with lower, wider or deeper shoulders holds it lower, wider or further out, as it
/// would.
/// Then the body takes the gun as it is built:
/// - a gun whose grip sits so high over its butt that the trigger hand would be held above the shoulder (a traditional
///   stock: the Herrington) has its butt lower on the chest, straight down from the pocket, as far as brings the trigger
///   wrist Rules.WristUnderShoulder under its shoulder joint. Held ready, a hand is not kept up by the face (the user,
///   2026-11-12: "헤링턴은 여전히 높게 잡고 있어"); down the sights the gun comes up to the eye from there;
/// - an arm that would be straighter than Rules.ReachMost has the gun held firmly in: the butt presses into the shoulder,
///   as far as the flesh and what is worn there give (Rules.StockPress) and only while the trigger arm stays unfolded
///   (Rules.BendLeast);
/// - a support arm still too straight holds the gun further back, where it reaches, down to Rules.SupportNearest ahead of
///   the trigger hand, as a shorter-armed shooter holds a long gun.
/// The gun turns in the shoulder: the butt stays put while the barrel goes onto the crosshair's point (Pivot). Turned about
/// its muzzle instead, as it was, the butt swung out of the shoulder with every near target, and a clip that idles with
/// the muzzle up (the Herrington, 14 degrees) was levelled at the height of its muzzle, across the middle of the view (the
/// user, 2026-11-14: "나 샷건을 왜 이따구로 듦?").
/// The pocket being this body's own is what makes the hold right on every body.
/// </summary>
internal sealed class ShoulderedHold : GunHold
{
    internal ShoulderedHold(GunHandles gun, in Rules rules) : base(gun, rules) { }

    internal override string Name => "shouldered";

    internal override Vector3 Pivot => gun.Butt;

    internal override Pose On(HoldBody body, KTransform clip)
    {
        float scale = body.Scale;
        Quaternion axes = Levelled(clip.rotation * gun.Frame);
        Vector3 back = -(axes * gun.Barrel), fromButt = -(axes * (gun.Butt / scale));

        // The butt in the pocket; lower on the chest for a gun that would hold the trigger hand above the shoulder there.
        Vector3 at = body.Pocket + fromButt;
        Wrists(body, at, axes, out Vector3 right, out Vector3 left);
        float under = Mathf.Max(0f, rules.WristUnderShoulder) / scale;
        float lowered = Mathf.Max(0f, Vector3.Dot(right - body.Right.Shoulder, HoldBody.Up) + under);
        if (lowered > 0f)
        {
            at -= HoldBody.Up * lowered;
            Wrists(body, at, axes, out right, out left);
        }

        // The butt pressed into the shoulder, the least that bends an arm too straight, as far as the trigger arm has room.
        float forTrigger = body.Right.ToBend(right, back, rules.ReachMost);
        float fit = Mathf.Max(forTrigger, body.Left.ToBend(left, back, rules.ReachMost));
        if (fit > 0f)
            fit = Mathf.Min(Mathf.Min(fit, Mathf.Max(forTrigger, body.Right.RoomToFold(right, back, rules.BendLeast))), Mathf.Max(0f, rules.StockPress) / scale);
        // Then the support hand where its arm reaches.
        float room = Mathf.Max(0f, gun.GripsApart - rules.SupportNearest) / scale;
        float slide = Mathf.Min(body.Left.ToBend(left + back * fit, back, rules.ReachMost), room);

        return new Pose
        {
            Gun = Bone(at + back * fit, axes),
            Slide = slide * scale,
            Fit = fit * scale,
            Lowered = lowered * scale,
            Right = body.Right.Straight(right + back * fit),
            Left = body.Left.Straight(left + back * (fit + slide))
        };
    }
}
