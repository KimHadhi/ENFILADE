using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The crouched leg sweep on the body (user, 2026-11-11 #33: Guilty Gear Strive's Ramlethal crouching sweep). No clip
/// in the project sweeps (Beatrun has a standing kick and a slide kick), so the leg is posed by IK over the crouch: when a
/// kick leaves while crouched (ShoveMoves.Kicked, on every PC), the right foot goes out along the floor as far as the leg
/// reaches and round the front from one side to the other, eased out as ShoveMoves times it, then back into the crouch.
/// The knee keeps the bend the crouch gives it, as the ladder feet do, and the foot keeps the clip's turn.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Crouched sweep")]
    [SerializeField, Range(30f, 360f), Tooltip("How far round the foot shows going (degrees); wider than this the leg would cross behind the body.")]
    private float sweepShowArc = 180f;
    [SerializeField, Range(.5f, 1f), Tooltip("How straight the leg goes out (share of its full length).")]
    private float sweepStretch = .93f;
    [SerializeField, Min(.01f), Tooltip("The leg into the sweep and back into the crouch (s).")]
    private float sweepBlendIn = .05f, sweepBlendOut = .12f;
    private float sweepElapsed = -1f;
    private Vector3 sweepFacing;

    // From OnKicked: crouched, the kick is the sweep.
    private void BeginSweep()
    {
        sweepElapsed = 0f;
        sweepFacing = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
    }

    // From LateUpdate with the other leg work, after the feet are planted.
    private void SweepLeg(float dt)
    {
        if (sweepElapsed < 0f) return;
        var leg = rightLeg;
        var moves = player != null ? ShoveMoves.For(player) : null;
        float seconds = moves != null ? moves.SweepSeconds : .32f;
        float arc = Mathf.Min(sweepShowArc, moves != null ? moves.SweepArc : 220f);
        sweepElapsed += dt;
        float t = sweepElapsed / seconds;
        if (dead || leg == null || state != MovementState.Crouching || sweepElapsed > seconds + sweepBlendOut) { sweepElapsed = -1f; return; }
        float weight = Mathf.Min(Mathf.Clamp01(sweepElapsed / sweepBlendIn), sweepElapsed <= seconds ? 1f : 1f - (sweepElapsed - seconds) / sweepBlendOut);
        float eased = 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t));
        Vector3 facing = Quaternion.AngleAxis(Mathf.Lerp(arc * .5f, -arc * .5f, eased), Vector3.up) * sweepFacing;
        // Along the floor from under the hip, as far as the leg reaches at the crouch's hip height.
        Vector3 hip = leg.Upper.position, foot = leg.Foot.position;
        float floor = player.transform.position.y + leg.Sole;
        float length = ((leg.Lower.position - hip).magnitude + (foot - leg.Lower.position).magnitude) * sweepStretch;
        float drop = Mathf.Max(0f, hip.y - floor);
        float along = Mathf.Sqrt(Mathf.Max(.01f, length * length - drop * drop));
        Vector3 target = new Vector3(hip.x, floor, hip.z) + facing * along;
        // The knee bends about its own axis from the crouch, as on a ladder; straight, about the hips' line.
        Vector3 thigh = leg.Lower.position - hip, shin = foot - leg.Lower.position;
        Vector3 own = Vector3.Cross(thigh, shin);
        Vector3 hinge = own.sqrMagnitude > 1e-6f ? own.normalized
            : leftLeg != null ? Vector3.ProjectOnPlane(hip - leftLeg.Upper.position, thigh).normalized : transform.right;
        Quaternion turn = leg.Foot.rotation;
        TwoBoneIK.Reach(leg.Upper, leg.Lower, leg.Foot, Vector3.Lerp(foot, target, weight), hinge);
        leg.Foot.rotation = turn;
    }
}
