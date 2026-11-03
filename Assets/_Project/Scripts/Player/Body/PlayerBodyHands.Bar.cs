using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Hands round a swing bar: half the hands' spacing either side of the grip along the bar, overhand, the fingers up the
/// arm and over the top and the palm facing the way the chest does, closed round the bar's real thickness (measured from
/// its collider). PlayerBody.Bar turns the body with the swing, and the grip turns with it.
/// </summary>
public sealed partial class PlayerBodyHands
{
    private SwingBar measuredBar;
    private float barHalfThickness = Ladder.BarThickness * .5f;

    private bool OnBar(MovementState state) => (state == MovementState.BarGrab || state == MovementState.BarSwing)
        && player.Abilities.Traversal.Bar != null;

    // Half the bar's thickness: from its grip line up to the top of its collider, measured once a bar.
    private float BarHalfThickness(SwingBar bar, Vector3 grip)
    {
        if (bar == measuredBar) return barHalfThickness;
        measuredBar = bar;
        barHalfThickness = Ladder.BarThickness * .5f;
        Vector3 up = bar.transform.up;
        foreach (var surface in bar.GetComponentsInChildren<Collider>())
            if (surface.Raycast(new Ray(grip + up, -up), out RaycastHit top, 2f))
                barHalfThickness = Mathf.Max(.005f, Vector3.Dot(top.point - grip, up));
        return barHalfThickness;
    }

    private bool BarTarget(ArmIK arm, bool isLeft, out Vector3 position, out Quaternion rotation, out Vector3 hint)
    {
        position = hint = Vector3.zero;
        rotation = Quaternion.identity;
        var traversal = player.Abilities.Traversal;
        var bar = traversal.Bar;
        if (bar == null) return false;
        Vector3 grip = bar.transform.TransformPoint(traversal.LocalGrip);
        Vector3 up = body.BarUp, forward = body.BarForward;
        // The body's right, along the bar.
        Vector3 side = bar.transform.right * (Mathf.Sign(Vector3.Dot(bar.transform.right, Vector3.Cross(up, forward))) * (isLeft ? -1f : 1f));
        float across = ledgeHandSpacing * shoulderSpan * (gripSkin?.LedgeHandSpacingScale ?? 1f) * .5f;
        Vector3 contact = bar.ClosestGrip(grip + side * across);
        rotation = arm.HandRotation(up, forward);
        Vector3 palm = contact - forward * (BarHalfThickness(bar, grip) + arm.PalmDepth + ladderPalmClearance);
        position = arm.WristAtPalm(palm, rotation);
        // The elbows go out to the sides, over the shoulders.
        hint = arm.Upper.position + side * .3f + up * .1f;
        return true;
    }

    /// <summary>For the body's hang from the bar (PlayerBody.Grip): where the wrists go, and where the clip holds them.</summary>
    internal bool TryBarWrists(out Vector3 centre, out Vector3 held)
    {
        centre = held = Vector3.zero;
        if (left == null || right == null || !BarTarget(left, true, out Vector3 l, out _, out _)
            || !BarTarget(right, false, out Vector3 r, out _, out _)) return false;
        centre = (l + r) * .5f;
        held = (left.Hand.position + right.Hand.position) * .5f;
        return true;
    }

    private void CurlBarFingers(ArmIK arm)
    {
        float weight = weights[(int)Hold.Bar];
        if (weight <= 0f || measuredBar == null) return;
        arm.GripBar(barHalfThickness, ladderPalmClearance, fingerSurfaceClearance, weight, 1f);
    }
}
