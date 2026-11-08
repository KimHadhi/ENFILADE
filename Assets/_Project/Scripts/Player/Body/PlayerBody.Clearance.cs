using UnityEngine;

/// <summary>
/// Keeps the face and the camera out of walls. The look bend (SpineLook) bends the spine whatever is in front of the
/// face, so near a wall a downward look gives back as much of the spine's bend as it takes for the face to clear: the
/// head keeps looking where it did, and the neck takes up the difference. Found by halving, before the camera is placed.
/// The camera's sweep from inside the capsule is the last guard (CameraClearance).
/// </summary>
public sealed partial class PlayerBody
{
    private Quaternion[] facePose = new Quaternion[0];

    /// <summary>The camera's size for its wall sweep (BodyCamera.Radius); the ragdoll's view uses it too.</summary>
    internal float CameraRadius() => bodyCamera.Radius();

    private bool LookPoseIsClear(Vector3 anchor, float radius)
        => bodyCamera.IsClear(anchor, head.position, Mathf.Max(.09f, radius))
            && bodyCamera.IsClear(anchor, EyePosition, radius);

    // How much of the look's spine bend the face can keep without going into what is in front of it.
    private void ClearFace()
    {
        if (head == null || spineLookWeight <= 0f || Mathf.Abs(spineLookPitch) < .01f) return;
        var bones = spineLook.Spine;
        int count = bones.Count;
        if (count == 0) return;
        Vector3 anchor = bodyCamera.SafeAnchor();
        float radius = bodyCamera.Radius();
        if (LookPoseIsClear(anchor, radius)) return;

        if (facePose.Length != count + 1) facePose = new Quaternion[count + 1];
        for (int i = 0; i < count; i++) facePose[i] = bones[i].rotation;
        facePose[count] = head.rotation;
        // SpineLook turned each spine bone about the view's right axis, as the hips see the view, by its share of the pitch.
        Vector3 axis = spineLook.PitchAxis;

        Keep(0f);
        if (!LookPoseIsClear(anchor, radius)) return;
        float safe = 0f, blocked = 1f;
        for (int i = 0; i < 7; i++)
        {
            float keep = (safe + blocked) * .5f;
            Keep(keep);
            if (LookPoseIsClear(anchor, radius)) safe = keep;
            else blocked = keep;
        }
        Keep(safe);

        // The spine as the look bent it, with only `keep` of its bend; undone from the top down, as it was bent from the
        // hips up.
        void Keep(float keep)
        {
            for (int i = 0; i < count; i++) bones[i].rotation = facePose[i];
            for (int i = count - 1; i >= 0; i--)
            {
                float angle = spineLook.SpinePitch(i) * (1f - keep);
                bones[i].rotation = Quaternion.AngleAxis(-angle, axis) * bones[i].rotation;
            }
            head.rotation = facePose[count];
        }
    }
}
