using UnityEngine;

/// <summary>
/// The Esc menu stops time, and everything that holds the hands in place after the clips (IK in LateUpdate) stops with
/// it, while the Animator still plays the bare clip pose every frame. So a pause keeps the body exactly as it stood:
/// every humanoid bone's pose is taken as time stops and put back after each frame's animation until play goes on.
/// The Animator stays on throughout, because KINEMATION's frameworks run in its graph.
/// </summary>
public sealed partial class PlayerBody
{
    private bool frozen;
    private readonly System.Collections.Generic.List<Transform> frozenBones = new System.Collections.Generic.List<Transform>();
    private Vector3[] frozenPositions = new Vector3[0];
    private Quaternion[] frozenRotations = new Quaternion[0];

    /// <summary>True while paused. Takes the body's pose on the way in.</summary>
    private bool HoldsForPause()
    {
        bool paused = GamePause.IsPaused;
        if (paused == frozen) return paused;
        frozen = paused;
        // A dead body lies as physics left it, with its Animator off.
        if (paused && !limp) TakeFrozenPose();
        return paused;
    }

    // Before this frame's animation, the bones still hold the pose last shown.
    private void TakeFrozenPose()
    {
        frozenBones.Clear();
        if (animator == null || !animator.isHuman) return;
        for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            if (animator.GetBoneTransform(bone) is Transform found) frozenBones.Add(found);
        if (frozenPositions.Length != frozenBones.Count)
        {
            frozenPositions = new Vector3[frozenBones.Count];
            frozenRotations = new Quaternion[frozenBones.Count];
        }
        for (int i = 0; i < frozenBones.Count; i++) frozenBones[i].GetLocalPositionAndRotation(out frozenPositions[i], out frozenRotations[i]);
    }

    private void HoldFrozenPose()
    {
        if (!frozen || limp) return;
        for (int i = 0; i < frozenBones.Count; i++)
            if (frozenBones[i] != null) frozenBones[i].SetLocalPositionAndRotation(frozenPositions[i], frozenRotations[i]);
    }
}
