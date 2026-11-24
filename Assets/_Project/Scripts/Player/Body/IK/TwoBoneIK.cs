using UnityEngine;

/// <summary>
/// Bends a two-bone limb (upper arm and forearm, thigh and shin) so its end joint lands on a target. Rotates only the
/// two bones; the end bone keeps its rotation relative to them, so callers set the hand's or foot's world rotation
/// afterwards.
/// </summary>
internal static class TwoBoneIK
{
    /// <summary>Bends in the limb's current plane, then turns the middle joint (elbow) round toward a hint, <paramref name="swing"/> of the way.</summary>
    public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint, float swing = 1f)
    {
        Vector3 a = upper.position, ab = lower.position - a, bc = end.position - lower.position;
        // The side the middle joint sticks out to: the limb's own (away from where its lower bone turns), nudged toward the
        // hint by HintBend's worth of bend, so a limb all but straight bends to the hint's side and not to whichever side its
        // last degree of bend went. Until 0.9.402 it bent that way, and the swing toward the hint then turned the whole arm
        // 160 to 180 degrees about its length in a frame whenever that degree changed sides (hanging from a ledge and moving
        // along it: the pops record of 2026-11-24 20:3x). A limb clearly bent keeps its own side.
        Vector3 side = -Vector3.ProjectOnPlane(bc, ab), toHint = Vector3.ProjectOnPlane(hint - a, ab);
        if (toHint.sqrMagnitude > 1e-8f) side += toHint.normalized * (bc.magnitude * HintBend);
        Vector3 hinge = Vector3.Cross(side, ab);
        if (hinge.sqrMagnitude < 1e-8f) hinge = Vector3.Cross(target - a, Vector3.up);
        Reach(upper, lower, end, target, hinge);

        // Swing the middle joint round the root-to-end line toward the hint.
        Vector3 line = (end.position - a).normalized;
        Vector3 middle = Vector3.ProjectOnPlane(lower.position - a, line), toward = Vector3.ProjectOnPlane(hint - a, line);
        if (middle.sqrMagnitude > 1e-6f && toward.sqrMagnitude > 1e-6f)
            upper.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(middle, toward, line) * swing, line) * upper.rotation;
    }

    /// <summary>
    /// Bends the middle joint about <paramref name="hinge"/> (a knee's sideways axis) until the limb is as long as the
    /// distance to the target, then turns the limb the least way to point at it. Nothing turns about the limb's own
    /// length, so a knee stays where the clip points it and the foot never twists against the shin.
    /// </summary>
    public static void Reach(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hinge)
    {
        Vector3 a = upper.position, ab = lower.position - a, bc = end.position - lower.position;
        float abLength = ab.magnitude, bcLength = bc.magnitude;
        float atLength = Mathf.Clamp((target - a).magnitude, .001f, (abLength + bcLength) * .9999f);
        Vector3 axis = Vector3.ProjectOnPlane(hinge, ab);
        if (axis.sqrMagnitude > 1e-8f && abLength > 1e-4f)
        {
            // Turning the upper bone's direction about the hinge by the outer angle gives the lower bone's direction.
            float bend = 180f - TriangleAngle(atLength, abLength, bcLength) * Mathf.Rad2Deg;
            Vector3 lowerDirection = Quaternion.AngleAxis(bend, axis.normalized) * (ab / abLength);
            lower.rotation = Quaternion.FromToRotation(bc, lowerDirection) * lower.rotation;
        }
        upper.rotation = Quaternion.FromToRotation(end.position - a, target - a) * upper.rotation;
    }

    // How far a limb's own bend must go before its side, not the hint's, says which way it bends: the sine of 10 degrees.
    private const float HintBend = .17f;

    // The angle (radians) between sides b and c of a triangle whose third side is a.
    private static float TriangleAngle(float a, float b, float c)
        => Mathf.Acos(Mathf.Clamp((b * b + c * c - a * a) / Mathf.Max(1e-6f, 2f * b * c), -1f, 1f));
}
