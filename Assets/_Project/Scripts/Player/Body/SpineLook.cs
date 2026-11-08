using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The body's own look bend, done right after the animation pass: the view's pitch, the turn back toward it and the Q/E tip
/// are spread down the spine, neck and head. It replaced KINEMATION Character Animation System's Look on the body
/// (2026-11-09). CAS was a third system in the Animator's graph doing a job our own code had done up to 0.7.32, set up through
/// KINEMATION's private fields. The shares, axes and order here are the ones CAS used, so the body moves the same:
/// - roll (Q/E): the spine bones only, an even share each, about the body's forward axis;
/// - yaw (the turn back toward the view): the spine 60% shared evenly, the neck 20%, the head 20% (40% with no neck);
/// - pitch (down positive): the spine its share (PlayerBody's spineBendDown / spineBendUp) shared evenly, the neck 30% and
///   the head 70% of the rest, about the body's right axis turned by the yaw it was just given.
/// Before it bends, it keeps the head as the clips posed it (ClipHeadPosition / ClipHeadRotation). With a gun out, that is
/// FPS Animation Framework's pose, since FPSAF runs in the animation pass.
/// This is a plain class, not a component. PlayerBody runs it as the first step of its late pose work, so its place in the
/// order is written in one spot (PlayerBody.LateUpdate), not set by an execution-order number.
/// </summary>
internal sealed class SpineLook
{
    private const float SpineYawShare = .6f, NeckYawShare = .2f, NeckPitchShare = .3f;

    private readonly List<Transform> spine = new List<Transform>();
    private readonly List<float> spinePitch = new List<float>();
    private Transform root, neck, head;
    private float spineDownShare, spineUpShare, neckDownShare, neckUpShare, headDownShare, headUpShare, headYawShare;
    private Vector3 clipHeadPosition;
    private Quaternion clipHeadRotation = Quaternion.identity;
    private bool hasClipHead;
    private float appliedYaw;

    /// <summary>The spine bones the look bends, from the hips up; each takes an even share of the bend.</summary>
    internal IReadOnlyList<Transform> Spine => spine;

    private bool IsBound => root != null && head != null;

    /// <summary>Takes the bones of the body's humanoid avatar. <paramref name="spineDown"/> / <paramref name="spineUp"/> are the
    /// spine's share of a look down / up; the neck and head take the rest.</summary>
    internal void Bind(Animator animator, float spineDown, float spineUp)
    {
        Clear();
        if (animator == null || !animator.isHuman) return;
        root = animator.transform;
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
            if (animator.GetBoneTransform(bone) is Transform found) { spine.Add(found); spinePitch.Add(0f); }
        int count = Mathf.Max(1, spine.Count);
        spineDownShare = Mathf.Clamp01(spineDown) / count;
        spineUpShare = Mathf.Clamp01(spineUp) / count;
        float neckShare = neck != null ? NeckPitchShare : 0f;
        neckDownShare = (1f - Mathf.Clamp01(spineDown)) * neckShare;
        neckUpShare = (1f - Mathf.Clamp01(spineUp)) * neckShare;
        headDownShare = (1f - Mathf.Clamp01(spineDown)) * (1f - neckShare);
        headUpShare = (1f - Mathf.Clamp01(spineUp)) * (1f - neckShare);
        headYawShare = neck != null ? NeckYawShare : NeckYawShare * 2f;
    }

    internal void Clear()
    {
        spine.Clear();
        spinePitch.Clear();
        root = neck = head = null;
        hasClipHead = false;
        appliedYaw = 0f;
    }

    /// <summary>Where the head was as the clips (and FPSAF) posed it this frame, before the look bent it. It moves with the
    /// body if the body moves later in the frame.</summary>
    internal Vector3 ClipHeadPosition => hasClipHead ? root.TransformPoint(clipHeadPosition) : head != null ? head.position : Vector3.zero;
    internal Quaternion ClipHeadRotation => hasClipHead ? root.rotation * clipHeadRotation : head != null ? head.rotation : Quaternion.identity;

    /// <summary>Copies the head as the clips posed it. Call it after the animation pass and before Apply.</summary>
    internal void KeepClipHead()
    {
        if (!IsBound) return;
        clipHeadPosition = root.InverseTransformPoint(head.position);
        clipHeadRotation = Quaternion.Inverse(root.rotation) * head.rotation;
        hasClipHead = true;
    }

    /// <summary>The axis the spine was pitched about this frame (world).</summary>
    internal Vector3 PitchAxis => root != null ? root.rotation * Quaternion.Euler(0f, appliedYaw, 0f) * Vector3.right : Vector3.right;

    /// <summary>How far (degrees) spine bone <paramref name="index"/> was pitched this frame.</summary>
    internal float SpinePitch(int index) => index >= 0 && index < spinePitch.Count ? spinePitch[index] : 0f;

    /// <summary>
    /// Bends the body by <paramref name="weight"/> of the look: <paramref name="pitch"/> (down positive),
    /// <paramref name="viewYaw"/> (the view's yaw in the world; the body turns back toward it from its own facing) and
    /// <paramref name="roll"/> (positive tips the top to the left), in degrees.
    /// </summary>
    internal void Apply(float weight, float pitch, float viewYaw, float roll)
    {
        appliedYaw = 0f;
        for (int i = 0; i < spinePitch.Count; i++) spinePitch[i] = 0f;
        weight = Mathf.Clamp01(weight);
        if (!IsBound || weight <= 0f) return;
        Quaternion space = root.rotation;
        int count = Mathf.Max(1, spine.Count);

        // Roll: the spine tips sideways from the hips up, about the body's forward axis.
        var tip = Quaternion.Euler(0f, 0f, roll * weight / count);
        foreach (var bone in spine) Turn(bone, space, tip);

        // Yaw: the turn from the body's facing back toward the view, as the body's root sees it.
        float yaw = Mathf.DeltaAngle(0f, (Quaternion.Inverse(space) * Quaternion.Euler(0f, viewYaw, 0f)).eulerAngles.y) * weight;
        appliedYaw = yaw;
        var spineTurn = Quaternion.Euler(0f, yaw * SpineYawShare / count, 0f);
        foreach (var bone in spine) Turn(bone, space, spineTurn);
        if (neck != null) Turn(neck, space, Quaternion.Euler(0f, yaw * NeckYawShare, 0f));
        Turn(head, space, Quaternion.Euler(0f, yaw * headYawShare, 0f));

        // Pitch: about the right axis as the turn just left it.
        Quaternion pitchSpace = space * Quaternion.Euler(0f, yaw, 0f);
        pitch *= weight;
        bool down = pitch > 0f;
        for (int i = 0; i < spine.Count; i++)
        {
            spinePitch[i] = pitch * (down ? spineDownShare : spineUpShare);
            Turn(spine[i], pitchSpace, Quaternion.Euler(spinePitch[i], 0f, 0f));
        }
        if (neck != null) Turn(neck, pitchSpace, Quaternion.Euler(pitch * (down ? neckDownShare : neckUpShare), 0f, 0f));
        Turn(head, pitchSpace, Quaternion.Euler(pitch * (down ? headDownShare : headUpShare), 0f, 0f));
    }

    // Turns a bone by an offset given in another frame (the body's root), carrying the bones under it.
    private static void Turn(Transform bone, Quaternion space, Quaternion offset)
        => bone.rotation = space * offset * (Quaternion.Inverse(space) * bone.rotation);
}
