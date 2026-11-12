using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The body's side of catching a ledge, warped as KINEMATION Motion Warping warps a move onto its target (LedgeHangState;
/// WarpedTraversal does the same for the vaults). The catch clip starts on the frame its hands touch the edge
/// (Mixamo.clips.csv), so the movement is told where the capsule has to be for that frame's wrists to rest on the edge
/// (TryLedgeContact) and carries it there in ParkourTraversal.LedgeCatchTime. Meanwhile the body holds the catch clip on that
/// frame (LedgePose), the hands reach for their holds over the edge (PlayerBodyHands.Ledge) and the hands take the body
/// over (Grip), all eased by the one share of the catch (LedgeCatchShare), so nothing snaps; the clip plays on from the
/// moment they touch. Every PC has the catch's time and its own copy's time in the state, so every PC shows the same catch.
/// Before, the speed was cut to nothing at once, the capsule lerped to the hang in 0.14 s, and the body, the hands and the
/// clip each snapped in over 0.15 s with the clip already past the touch (user, 2026-11-13: the catch popped).
/// </summary>
public sealed partial class PlayerBody
{
    // The catch clip's wrists on its first frame, their middle in the Animator's space (x right, y up, z ahead), and the body
    // and clip they were measured on.
    private Vector3 catchWrists;
    private Animator catchMeasuredOn;
    private AnimationClip catchMeasuredClip;

    /// <summary>
    /// Catching a ledge: how far the hands have come to its edge (0-1), the time this body has hung against the warp's time
    /// (ParkourTraversal.LedgeCatchTime). 1 once they hold it, outside a hang, and for a hang begun without a warp.
    /// </summary>
    internal float LedgeCatchShare
    {
        get
        {
            if (state != MovementState.LedgeHang || dead) return 1f;
            float time = player.Abilities.Traversal.LedgeCatchTime;
            return time > 0f ? Mathf.Clamp01(stateTime / time) : 1f;
        }
    }

    /// <summary>The catch's share eased in and out, which the body's grip and the hands follow.</summary>
    internal float LedgeCatchWeight => Mathf.SmoothStep(0f, 1f, LedgeCatchShare);

    /// <summary>How long this body has been in its movement state (s), from when its copy saw the state begin.</summary>
    internal float StateTime => stateTime;

    /// <summary>
    /// Where the capsule's feet have to be for the catch clip's wrists, on the frame its hands touch, to rest where the hands
    /// will hold the ledge just caught (PlayerBodyHands.TryLedgeCatchWrists): the body facing the wall by then, as LedgePose
    /// turns it, and keeping its place in the capsule. False when the clip, the hands or the ledge can't tell; the warp then
    /// goes to the hanging place itself.
    /// </summary>
    public bool TryLedgeContact(out Vector3 feet)
    {
        feet = Vector3.zero;
        if (animator == null || !animator.isHuman || dead || limp || hands == null || body == null) return false;
        var ledge = player.Abilities.Traversal.Ledge;
        if (ledge.Surface == null || !MeasureCatchWrists() || !hands.TryLedgeCatchWrists(out Vector3 wrists)) return false;
        Vector3 inward = Vector3.ProjectOnPlane(-ledge.Surface.transform.TransformDirection(ledge.LocalNormal), Vector3.up);
        Vector3 facing = Vector3.ProjectOnPlane(body.forward, Vector3.up);
        if (inward.sqrMagnitude < 1e-6f || facing.sqrMagnitude < 1e-6f) return false;
        // About the vertical only: the body turns to the wall on its yaw (LedgePose's targetYaw).
        Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(facing, inward, Vector3.up), Vector3.up);
        Transform root = animator.transform;
        Vector3 handsFromFeet = root.position - transform.position + turn * (root.TransformPoint(catchWrists) - root.position);
        feet = wrists - handsFromFeet;
        return true;
    }

    // The catch clip's wrists on its first frame, on this body: sampled on the body itself and the pose put back, as
    // ObstacleClipContacts samples the vault clips, once a body and clip (MeasureObstacleClips does it as the body is made).
    private bool MeasureCatchWrists()
    {
        if (animator == null || !animator.isHuman) return false;
        var channel = Find(ledgeCatch);
        var clip = channel != null && channel.Playable.IsValid() ? channel.Playable.GetAnimationClip() : null;
        if (clip == null) return false;
        if (catchMeasuredOn == animator && catchMeasuredClip == clip) return true;
        var leftWrist = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rightWrist = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (leftWrist == null || rightWrist == null) return false;
        Transform root = animator.transform;
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = new (Vector3 position, Quaternion rotation, Vector3 scale)[bones.Length];
        for (int i = 0; i < bones.Length; i++) poses[i] = (bones[i].localPosition, bones[i].localRotation, bones[i].localScale);
        try
        {
            clip.SampleAnimation(animator.gameObject, 0f);
            catchWrists = (root.InverseTransformPoint(leftWrist.position) + root.InverseTransformPoint(rightWrist.position)) * .5f;
        }
        finally
        {
            // Its own graph poses it again before it is drawn; nothing of the sample is left on it meanwhile.
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].position, poses[i].rotation);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].localScale = poses[i].scale;
        }
        catchMeasuredOn = animator;
        catchMeasuredClip = clip;
        return true;
    }
}
