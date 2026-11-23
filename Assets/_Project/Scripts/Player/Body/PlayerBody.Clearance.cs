using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the face, the eyes and the shoulders off the player's own legs, and the face and the camera out of walls, by how far
/// the spine may bend down (SpineRoom), worked out every frame before the bend goes on (the user, 2026-11-23: "앉아서 맨
/// 아래 보고 걸을 때 무릎 뚫리는 현상", "다양한 상황을 가정해도 안정성 있는 방법"). One limit for every way the spine is bent:
/// with nothing in hand the look's own bend (SpineLook.Bend) goes no further, with a gun FPS Animation Framework's Look is
/// bent back to it (SpineRoomLayer; FPSBody hands it SpineMost, and the body works the next one out from the spine as Look
/// bent it, TakeFrameworkSpine). The legs are the body's own shape (OwnLegs), the one the pointing arm keeps off too.
/// The camera's sweep from inside the capsule is the last guard (CameraClearance).
/// </summary>
public sealed partial class PlayerBody
{
    // Balls kept clear (m): the head about its bone, the shoulders' joints, and the eyes no smaller than this.
    private const float HeadBall = .09f, ShoulderBall = .06f, EyeBallLeast = .05f;
    // The legs are read off the mesh once something comes this near them (m).
    private const float LegsReadWithin = .2f;

    private readonly SpineRoom spineRoom = new SpineRoom();
    private readonly OwnLegs ownLegs = new OwnLegs();
    private Func<Vector3, float, bool> roomClear;
    private Vector3 roomAnchor;

    // What FPSBody last said FPSAF's Look does to the spine (TakeFrameworkSpine), and in which frame.
    private IReadOnlyList<(Transform bone, float angle)> frameworkSpine;
    private float frameworkLook, frameworkWeight, frameworkMost;
    private Vector3 frameworkAxis;
    private int frameworkFrame = -1;

    /// <summary>The camera's size for its wall sweep (BodyCamera.Radius); the ragdoll's view uses it too.</summary>
    internal float CameraRadius() => bodyCamera.Radius();

    /// <summary>The player's own legs (OwnLegs): what the bent spine and the pointing arm keep off.</summary>
    internal OwnLegs Legs => ownLegs;

    /// <summary>How far the spine may bend down now (degrees, all its bones together); FPSBody gives it to FPSAF's Look.</summary>
    internal float SpineMost => spineRoom.Most;

    /// <summary>
    /// FPSAF's Look this frame, from FPSBody: <paramref name="spine"/> are the bones it bends with the spine, from the hips up,
    /// each with its angle at a 90° look down; <paramref name="lookDown"/> is how far down the view looks (degrees),
    /// <paramref name="weight"/> Look's weight, <paramref name="most"/> the limit it was given and <paramref name="axis"/> the
    /// axis it bends about (world).
    /// </summary>
    internal void TakeFrameworkSpine(IReadOnlyList<(Transform bone, float angle)> spine, float lookDown, float weight, float most,
        Vector3 axis)
    {
        frameworkSpine = spine;
        frameworkLook = lookDown;
        frameworkWeight = Mathf.Clamp01(weight);
        frameworkMost = most;
        frameworkAxis = axis;
        frameworkFrame = Time.frameCount;
    }

    private void SetUpRoom()
    {
        ownLegs.Bind(animator);
        spineRoom.Reset();
        roomClear ??= (to, radius) => bodyCamera.IsClear(roomAnchor, to, radius);
    }

    // After the look's turn and before its bend: how far the spine may bend down this frame. With a gun, from the spine as
    // FPSAF's Look bent it this frame (the limit then goes to it next frame); with nothing in hand, from the spine as the clip
    // has it, before the look's bend.
    private void MeasureSpineRoom(float dt)
    {
        if (head == null || dt <= 0f) return;
        float want;
        // Where the eyes will be from the head, once the head looks where it will.
        Vector3 eyes = EyePosition - head.position;
        var chain = frameworkSpine;
        if (UpperBodyFromFramework && frameworkFrame == Time.frameCount && chain != null && chain.Count > 0)
        {
            float sum = 0f;
            foreach (var (_, angle) in chain) sum += angle;
            want = sum * Mathf.Max(0f, frameworkLook) / 90f * frameworkWeight;
            spineRoom.Begin(frameworkAxis, SpineRoom.Soft(want, frameworkMost));
            foreach (var (bone, angle) in chain)
                if (bone != null && sum > 0f) spineRoom.Joint(bone.position, angle / sum);
            // Look keeps the head turned as it had it whatever the spine gives up (SpineRoomLayer): the eyes stay as they are.
        }
        else
        {
            want = spineLook.SpineBendFor(spineLookWeight, spineLookPitch);
            spineRoom.Begin(spineLook.PitchAxis, 0f);
            var bones = spineLook.Spine;
            foreach (var bone in bones) spineRoom.Joint(bone.position, 1f / bones.Count);
            // The head is yet to look down with the view (the bend, then BodyGaze.FaceHead), whatever the spine takes of it.
            eyes = Quaternion.AngleAxis(Mathf.Max(0f, spineLookPitch) * Mathf.Clamp01(spineLookWeight), spineLook.PitchAxis) * eyes;
        }
        float eyeBall = Mathf.Max(EyeBallLeast, bodyCamera.Radius());
        // The head and the eyes stay off the legs and out of walls; the shoulders off the legs.
        spineRoom.Point(head.position, Vector3.zero, Mathf.Max(HeadBall, eyeBall), true);
        spineRoom.Point(head.position, eyes, eyeBall, true);
        if (leftUpperArm != null) spineRoom.Point(leftUpperArm.position, Vector3.zero, ShoulderBall, false);
        if (rightUpperArm != null) spineRoom.Point(rightUpperArm.position, Vector3.zero, ShoulderBall, false);
        roomAnchor = bodyCamera.SafeAnchor();
        spineRoom.Measure(want, dt, ownLegs, roomClear);
        if (spineRoom.Nearest < LegsReadWithin) ownLegs.Read(player);
    }
}
