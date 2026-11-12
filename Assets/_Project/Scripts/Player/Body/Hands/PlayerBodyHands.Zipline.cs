using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Backlog 142 (hands, the cable part; Docs/Records/AddonPortNotes "사다리·줄의 손 IK"): riding a zipline, PlayerBody.Grip only
/// moved the body so the clip's two hands met the cable on average, so each hand hung near the cable, open, at whatever
/// height its clip and the character's arms put it. Now each hand closes round the cable: it stays where the clip has it
/// along the cable (the clip's lead and trail), but goes onto the cable itself, fingers over the top from its own side and
/// curled round the cable's real thickness, with each character's own palm size (ArmIK's grip measures). Hands the clip
/// holds side by side are spread along the cable so they don't sit in one another.
/// </summary>
public sealed partial class PlayerBodyHands
{
    [Header("Zipline grip")]
    [SerializeField, Min(0f), Tooltip("Room left between the two hands along the cable when the clip holds them side by side (m).")]
    private float ziplineHandGap = .015f;

    // Where each hand is along the cable from ZiplineFrom (m), fixed once a frame before either arm is solved, so the
    // second arm doesn't read the first one's solved hand.
    private readonly float[] ziplineAlong = new float[2];
    private bool ziplineReady;

    private bool OnZipline(MovementState state)
        => state == MovementState.Zipline && player.Abilities.Fixtures.ZiplineDirection.sqrMagnitude > .5f;

    // Also while the hold eases out after letting go, so the hands leave the cable instead of jumping off it.
    private void PrepareZipline()
    {
        ziplineReady = false;
        var fixtures = player.Abilities.Fixtures;
        if (weights[(int)Hold.Zipline] <= 0f || fixtures.ZiplineDirection.sqrMagnitude < .5f || left == null || right == null) return;
        Vector3 from = fixtures.ZiplineFrom, along = fixtures.ZiplineDirection.normalized;
        float l = Vector3.Dot(left.PalmPosition - from, along), r = Vector3.Dot(right.PalmPosition - from, along);
        float gap = left.HalfPalmWidth + right.HalfPalmWidth + ziplineHandGap;
        if (Mathf.Abs(r - l) < gap)
        {
            // The hand already a little ahead leads; held level, the right hand does.
            float middle = (l + r) * .5f, lead = r >= l ? 1f : -1f;
            r = middle + lead * gap * .5f;
            l = middle - lead * gap * .5f;
        }
        ziplineAlong[0] = l;
        ziplineAlong[1] = r;
        ziplineReady = true;
    }

    private bool ZiplineTarget(ArmIK arm, bool isLeft, out Vector3 position, out Quaternion rotation, out Vector3 hint)
    {
        position = hint = Vector3.zero;
        rotation = Quaternion.identity;
        if (!ziplineReady) return false;
        var fixtures = player.Abilities.Fixtures;
        Vector3 along = fixtures.ZiplineDirection.normalized;
        Vector3 contact = fixtures.ZiplineFrom + along * ziplineAlong[isLeft ? 0 : 1];
        // Fingers up over the cable; the palm faces it from the hand's own side of the body (the rider faces along it).
        Vector3 fingers = Vector3.ProjectOnPlane(Vector3.up, along).normalized;
        Vector3 bodyRight = Vector3.Cross(fingers, along).normalized;
        if (Vector3.Dot(bodyRight, transform.right) < 0f) bodyRight = -bodyRight;
        Vector3 palmFacing = isLeft ? bodyRight : -bodyRight;
        rotation = arm.HandRotation(fingers, palmFacing);
        Vector3 palm = contact - palmFacing * (Zipline.CableThickness * .5f + arm.PalmDepth + ladderPalmClearance);
        position = arm.WristAtPalm(palm, rotation);
        // The elbows out to the sides, a little in front, as on a bar.
        hint = arm.Upper.position - palmFacing * .3f + transform.forward * .05f + Vector3.up * .1f;
        return true;
    }

    private void CurlZiplineFingers(ArmIK arm)
    {
        float weight = weights[(int)Hold.Zipline];
        if (weight <= 0f || !ziplineReady) return;
        arm.GripBar(Zipline.CableThickness * .5f, ladderPalmClearance, fingerSurfaceClearance, weight, 1f);
    }
}
