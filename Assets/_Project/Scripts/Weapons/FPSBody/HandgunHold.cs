using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

/// <summary>
/// A gun without a stock: nothing of it rests on the body, so it is held out before the eyes where its clip has it in the
/// view (the animators' framing, kept from the eyes), turned in the hand to point where the body looks, and no nearer or
/// further than the body's own arms hold it: a trigger arm that would be folded tighter than Rules.BendLeast holds it
/// further out along its barrel, as far as the support arm (when the pose has it on the gun) stays within Rules.ReachMost;
/// an arm that would be straighter than that holds it nearer. A longer-armed body holds a pistol further out, as it would.
/// Aimed, its sight is on the eyes and the same holds along the sight line (EyeRelief): KINEMATION's own eye point for the
/// R08's dot sight is 11 cm from the eyes, where arms made for a body are folded flat (0.27 and 0.20 on the reference body).
/// It only ever goes further out than the sight's own eye point, never nearer.
/// The gun turns in the trigger hand as the barrel goes onto the crosshair's point (Pivot).
/// </summary>
internal sealed class HandgunHold : GunHold
{
    internal HandgunHold(GunHandles gun, in Rules rules) : base(gun, rules) { }

    internal override string Name => "handgun";

    internal override Vector3 Pivot => gun.RightPalm;

    internal override Pose On(HoldBody body, KTransform clip)
    {
        float scale = body.Scale;
        // The trigger hand where the clip has it; the gun turned about it to point where the body looks.
        Quaternion clipAxes = clip.rotation * gun.Frame, axes = Levelled(clipAxes);
        Vector3 at = clip.position + clipAxes * (gun.RightPalm / scale) - axes * (gun.RightPalm / scale);
        Vector3 back = -(axes * gun.Barrel);
        Wrists(body, at, axes, out Vector3 right, out Vector3 left);
        float fit = ForArms(body, right, left, back);
        return new Pose
        {
            Gun = Bone(at + back * fit, axes),
            Fit = fit * scale,
            Right = body.Right.Straight(right + back * fit),
            Left = body.Left.Straight(left + back * fit)
        };
    }

    internal override float EyeRelief(HoldBody body, Vector3 sightAt, Quaternion sightTurn)
    {
        // The gun with its sight's eye point on the eyes, looking where the body looks.
        Quaternion axes = Quaternion.LookRotation(HoldBody.Ahead, HoldBody.Up) * Quaternion.Inverse(sightTurn);
        Vector3 at = body.Eyes - axes * (sightAt / body.Scale);
        Wrists(body, at, axes, out Vector3 right, out Vector3 left);
        return Mathf.Max(0f, -ForArms(body, right, left, -(axes * gun.Barrel))) * body.Scale;
    }

    // How far the gun goes along `back` (model units) for the arms: toward the body for an arm too straight, away from it
    // (under zero) for a trigger arm folded too tight, as far as a support arm on the gun has room to straighten.
    private float ForArms(HoldBody body, Vector3 right, Vector3 left, Vector3 back)
    {
        bool both = gun.SupportOnGun;
        float nearer = body.Right.ToBend(right, back, rules.ReachMost);
        if (both) nearer = Mathf.Max(nearer, body.Left.ToBend(left, back, rules.ReachMost));
        if (nearer > 0f) return nearer;
        float further = body.Right.ToUnfold(right, back, rules.BendLeast);
        if (further > 0f && both) further = Mathf.Min(further, body.Left.RoomToStraighten(left, back, rules.ReachMost));
        return -further;
    }
}
