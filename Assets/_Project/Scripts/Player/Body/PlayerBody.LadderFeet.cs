using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Backlog 142 (feet): on a ladder the feet stand on its rungs. The foot IK (FootPlanting) only works on the ground, so
/// on a ladder the clip's feet hung in front of the rungs or between them (user, 2026-11-08). Each foot goes onto the
/// rung nearest its sole, the ball of the foot on it: fully while the clip holds the foot near that rung, letting go as
/// the clip lifts it for the next step, so the steps still show. In and out with the ladder over blendTime; not while
/// sliding down, when the feet are off the rungs.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Feet on ladder rungs")]
    [SerializeField, Min(.01f), Tooltip("A foot stands fully on a rung while its sole is within this of the rung's top; it lets go by twice that (m).")]
    private float rungFootSnap = .12f;
    [SerializeField, Tooltip("Where the ankle sits from the rung's front face, out toward the climber (m): the ball of the foot is on the rung.")]
    private float rungAnkleOut = .07f;
    private float ladderFeetWeight;

    private void PlantLadderFeet(float dt)
    {
        var fixtures = player.Abilities.Fixtures;
        var ladder = fixtures.Ladder;
        bool on = !dead && state == MovementState.Ladder && ladder != null && ladder.isActiveAndEnabled && !fixtures.LadderSliding;
        ladderFeetWeight = Mathf.MoveTowards(ladderFeetWeight, on ? 1f : 0f, dt / blendTime);
        if (ladderFeetWeight <= 0f || ladder == null || leftLeg == null || rightLeg == null) return;
        Vector3 hips = rightLeg.Upper.position - leftLeg.Upper.position;
        OnRung(leftLeg);
        OnRung(rightLeg);

        void OnRung(FootPlanting.Leg leg)
        {
            Vector3 foot = leg.Foot.position, up = ladder.transform.up;
            Vector3 sole = foot - up * leg.Sole;
            int rung = ladder.NearestRung(sole);
            float across = Vector3.Dot(foot - ladder.transform.position, ladder.transform.right);
            Vector3 top = ladder.RungPoint(rung, across) + up * (Ladder.BarThickness * .5f);
            float off = Mathf.Abs(Vector3.Dot(sole - top, up));
            float weight = ladderFeetWeight * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rungFootSnap, rungFootSnap * 2f, off)));
            if (weight <= 0f) return;
            Vector3 target = top + up * leg.Sole + ladder.Outward * (Ladder.BarThickness * .5f + rungAnkleOut);
            Quaternion rotation = leg.Foot.rotation;
            // The knee bends about its own axis from the clip, as FootPlanting does; a straight leg about the hips' line.
            Vector3 thigh = leg.Lower.position - leg.Upper.position, shin = foot - leg.Lower.position;
            Vector3 own = Vector3.Cross(thigh, shin), straight = Vector3.ProjectOnPlane(hips, thigh).normalized;
            float bent = Mathf.Clamp01(own.magnitude / Mathf.Max(1e-6f, thigh.magnitude * shin.magnitude) / .26f);
            if (Vector3.Dot(own, straight) < 0f && bent < 1f) bent = 0f;
            TwoBoneIK.Reach(leg.Upper, leg.Lower, leg.Foot, Vector3.Lerp(foot, target, weight), Vector3.Lerp(straight, own.normalized, bent));
            leg.Foot.rotation = rotation;
        }
    }
}
