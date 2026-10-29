using UnityEngine;

/// <summary>
/// Bends a two-bone limb (upper arm and forearm, thigh and shin) so its end joint lands on a target. Rotates only the
/// two bones; the end bone keeps its rotation relative to them, so callers set the hand's or foot's world rotation
/// afterwards.
/// </summary>
internal static class TwoBoneIK
{
    /// <summary>Bends in the limb's current plane, then turns the middle joint (elbow) round toward a hint.</summary>
    public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint)
    {
        Vector3 a = upper.position, bc = end.position - lower.position;
        Vector3 hinge = Vector3.Cross(lower.position - a, bc);
        if (hinge.sqrMagnitude < 1e-8f) hinge = Vector3.Cross(hint - a, bc);
        if (hinge.sqrMagnitude < 1e-8f) hinge = Vector3.Cross(target - a, Vector3.up);
        Reach(upper, lower, end, target, hinge);

        // Swing the middle joint round the root-to-end line toward the hint.
        Vector3 line = (end.position - a).normalized;
        Vector3 middle = Vector3.ProjectOnPlane(lower.position - a, line), toward = Vector3.ProjectOnPlane(hint - a, line);
        if (middle.sqrMagnitude > 1e-6f && toward.sqrMagnitude > 1e-6f)
            upper.rotation = Quaternion.FromToRotation(middle, toward) * upper.rotation;
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

    // The angle (radians) between sides b and c of a triangle whose third side is a.
    private static float TriangleAngle(float a, float b, float c)
        => Mathf.Acos(Mathf.Clamp((b * b + c * c - a * a) / Mathf.Max(1e-6f, 2f * b * c), -1f, 1f));
}
