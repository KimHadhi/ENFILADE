using UnityEngine;

/// <summary>
/// Holds an ankle to what a real one can do. A clip retargeted from a bad reference pose, a blend or leg IK can leave a
/// foot turned about the shin or rolled onto its side; this measures the foot against the shin from the character's
/// rest pose and trims the turn and the roll, leaving the foot free to point up and down.
/// </summary>
internal sealed class AnkleLimit
{
    private readonly Transform foot;
    private readonly Quaternion rest;
    // In the shin bone's space: down the shin (the turn axis) and forward across it (the roll axis).
    private readonly Vector3 shin, side;

    private AnkleLimit(Transform foot, Quaternion rest, Vector3 shin, Vector3 side)
    {
        this.foot = foot; this.rest = rest; this.shin = shin; this.side = side;
    }

    /// <summary>Measured in the rest pose; <paramref name="hipsRight"/> runs from the left hip joint to the right one.</summary>
    public static AnkleLimit Create(Transform foot, Vector3 hipsRight)
    {
        Transform calf = foot != null ? foot.parent : null;
        if (calf == null || foot.localPosition.sqrMagnitude < 1e-8f) return null;
        Vector3 shin = foot.localPosition.normalized;
        Vector3 hinge = Vector3.ProjectOnPlane(calf.InverseTransformDirection(hipsRight), shin);
        if (hinge.sqrMagnitude < 1e-8f) return null;
        return new AnkleLimit(foot, foot.localRotation, shin, Vector3.Cross(shin, hinge.normalized));
    }

    /// <summary>Trims the foot's turn about the shin and its roll to either side (degrees, each way).</summary>
    public void Apply(float maxTurn, float maxRoll)
    {
        // The rotation away from the rest pose in the shin's space, split into a turn about the shin and a swing across it.
        Quaternion delta = Shortest(foot.localRotation * Quaternion.Inverse(rest));
        float along = delta.x * shin.x + delta.y * shin.y + delta.z * shin.z;
        float size = Mathf.Sqrt(along * along + delta.w * delta.w);
        if (size < 1e-5f) return; // a half-turn swing leaves the turn undefined
        var turn = new Quaternion(shin.x * along / size, shin.y * along / size, shin.z * along / size, delta.w / size);
        float turnAngle = 2f * Mathf.Atan2(along, delta.w) * Mathf.Rad2Deg;
        Quaternion swing = Shortest(delta * Quaternion.Inverse(turn));

        // The swing as axis × angle, so its roll part can be read and trimmed; what is left points the foot up or down.
        var axis = new Vector3(swing.x, swing.y, swing.z);
        float sinHalf = axis.magnitude;
        Vector3 bend = sinHalf > 1e-6f ? axis * (2f * Mathf.Atan2(sinHalf, swing.w) * Mathf.Rad2Deg / sinHalf) : Vector3.zero;
        float roll = Vector3.Dot(bend, side);
        float heldTurn = Mathf.Clamp(turnAngle, -maxTurn, maxTurn), heldRoll = Mathf.Clamp(roll, -maxRoll, maxRoll);
        if (heldTurn == turnAngle && heldRoll == roll) return;
        bend += side * (heldRoll - roll);
        float angle = bend.magnitude;
        swing = angle > 1e-4f ? Quaternion.AngleAxis(angle, bend / angle) : Quaternion.identity;
        foot.localRotation = swing * Quaternion.AngleAxis(heldTurn, shin) * rest;
    }

    private static Quaternion Shortest(Quaternion q) => q.w < 0f ? new Quaternion(-q.x, -q.y, -q.z, -q.w) : q;
}
