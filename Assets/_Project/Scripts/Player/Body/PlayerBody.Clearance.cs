using System.Collections.Generic;
using UnityEngine;

public sealed partial class PlayerBody
{
    private readonly CameraClearance clearance = new CameraClearance();
    private Transform[] lookBones;
    private Quaternion[] unbentRotations;
    private CharacterController bodyController;
    private float spineClearanceScale = 1f;

    private void SetUpClearance()
    {
        bodyController = GetComponent<CharacterController>();
        var bones = new List<Transform>(spine);
        if (neck != null && !bones.Contains(neck)) bones.Add(neck);
        if (head != null && !bones.Contains(head)) bones.Add(head);
        lookBones = bones.ToArray();
        unbentRotations = new Quaternion[lookBones.Length];
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

    private float CameraRadius()
    {
        if (viewCamera == null) return .05f;
        // Protect the near plane's corners as well as the optical centre, including a wide field of view.
        float halfHeight = Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
        return Mathf.Max(.035f, viewCamera.nearClipPlane
            * Mathf.Sqrt(1f + halfHeight * halfHeight * (1f + viewCamera.aspect * viewCamera.aspect)));
    }

    private bool LookPoseIsClear(Vector3 anchor, float radius)
    {
        Vector3 lean = cameraEffects != null ? cameraEffects.LeanDisplacement : transform.right * player.LeanOffset;
        return clearance.IsClear(anchor, head.position, Mathf.Max(.09f, radius), player.groundLayers, transform)
            && clearance.IsClear(anchor, EyePosition + lean, radius, player.groundLayers, transform);
    }

    // Keep the requested gaze, but use less torso lean when that would push the face into geometry. The neck and
    // head take the remaining pitch. Solve the pose before moving the camera instead of moving it behind the face.
    private void BendWithClearance(float pitch, float twist)
    {
        for (int i = 0; i < lookBones.Length; i++) unbentRotations[i] = lookBones[i].localRotation;
        Vector3 anchor = SafeCameraAnchor();
        float radius = CameraRadius();
        spineClearanceScale = 1f;
        BendAndTwist(pitch, twist);
        if (Mathf.Abs(pitch) < .01f || LookPoseIsClear(anchor, radius)) return;

        RestoreLookPose();
        BendAndTwist(pitch, twist, 0f);
        spineClearanceScale = 0f;
        if (!LookPoseIsClear(anchor, radius)) return;

        float safe = 0f, blocked = 1f;
        for (int i = 0; i < 7; i++)
        {
            float scale = (safe + blocked) * .5f;
            RestoreLookPose();
            BendAndTwist(pitch, twist, scale);
            if (LookPoseIsClear(anchor, radius)) safe = scale;
            else blocked = scale;
        }
        RestoreLookPose();
        spineClearanceScale = safe;
        BendAndTwist(pitch, twist, safe);
    }

    private void RestoreLookPose()
    {
        for (int i = 0; i < lookBones.Length; i++) lookBones[i].localRotation = unbentRotations[i];
    }
}
