using UnityEngine;

/// <summary>
/// Keeps the face and the camera out of walls. KINEMATION's Look (BodyCas) bends the spine whatever is in front of the
/// face, so near a wall a downward look gives back as much of the spine's bend as it takes for the face to clear: the
/// head keeps looking where it did, and the neck takes up the difference. Found by halving, before the camera is placed.
/// The camera's sweep from inside the capsule is the last guard (CameraClearance).
/// </summary>
public sealed partial class PlayerBody
{
    private readonly CameraClearance clearance = new CameraClearance();
    private CharacterController bodyController;
    private BodyCas bodyCas;
    private Quaternion[] facePose = new Quaternion[0];

    private void SetUpClearance()
    {
        bodyController = GetComponent<CharacterController>();
    }

    // A stable point inside the movement capsule stays on the same side of a wall as the player. In particular,
    // never change the sweep's origin between an overlapping head and last frame's already corrected camera.
    private Vector3 SafeCameraAnchor()
    {
        Vector3 anchor = cameraEffects != null ? cameraEffects.CameraAnchorPosition
            : view != null && view.parent != null ? view.parent.TransformPoint(viewHome) : transform.position;
        if (bodyController == null) return anchor;
        Vector3 local = transform.InverseTransformPoint(anchor);
        float half = Mathf.Max(0f, bodyController.height * .5f - bodyController.radius);
        local.x = bodyController.center.x;
        local.z = bodyController.center.z;
        local.y = Mathf.Clamp(local.y, bodyController.center.y - half, bodyController.center.y + half);
        return transform.TransformPoint(local);
    }

    internal float CameraRadius()
    {
        if (viewCamera == null) return .05f;
        // Protect the near plane's corners as well as the optical centre, including a wide field of view.
        float halfHeight = Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
        return Mathf.Max(.035f, viewCamera.nearClipPlane
            * Mathf.Sqrt(1f + halfHeight * halfHeight * (1f + viewCamera.aspect * viewCamera.aspect)));
    }

    // Walls keep the camera out, enemies don't: sliding past or under one would otherwise pull the camera back to its
    // anchor inside one's own body, and a slide passes right through them.
    private LayerMask CameraBlockers => player.groundLayers & ~LayerMask.GetMask("Enemy");

    private bool LookPoseIsClear(Vector3 anchor, float radius)
        => clearance.IsClear(anchor, head.position, Mathf.Max(.09f, radius), CameraBlockers, transform)
            && clearance.IsClear(anchor, EyePosition, radius, CameraBlockers, transform);

    // How much of CAS's spine bend the face can keep without going into what is in front of it.
    private void ClearFace()
    {
        if (bodyCas == null || head == null || CasLookWeight <= 0f || Mathf.Abs(CasLookPitch) < .01f) return;
        var bends = bodyCas.SpineBend;
        int count = bends.Count;
        if (count == 0) return;
        Vector3 anchor = SafeCameraAnchor();
        float radius = CameraRadius();
        if (LookPoseIsClear(anchor, radius)) return;

        if (facePose.Length != count + 1) facePose = new Quaternion[count + 1];
        for (int i = 0; i < count; i++) facePose[i] = bends[i].bone.rotation;
        facePose[count] = head.rotation;
        // CAS turned each spine bone about the view's right axis, as the hips see the view, by its share of the pitch.
        float turn = Mathf.DeltaAngle(body.eulerAngles.y, CasLookYaw) * CasLookWeight;
        Vector3 axis = body.rotation * Quaternion.Euler(0f, turn, 0f) * Vector3.right;
        float fraction = CasLookPitch / 90f * CasLookWeight;

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

        // The spine as CAS bent it, with only `keep` of its bend; undone from the top down, as CAS bent it from the hips up.
        void Keep(float keep)
        {
            for (int i = 0; i < count; i++) bends[i].bone.rotation = facePose[i];
            for (int i = count - 1; i >= 0; i--)
            {
                float angle = (CasLookPitch > 0f ? bends[i].down : bends[i].up) * fraction * (1f - keep);
                bends[i].bone.rotation = Quaternion.AngleAxis(-angle, axis) * bends[i].bone.rotation;
            }
            head.rotation = facePose[count];
        }
    }
}
