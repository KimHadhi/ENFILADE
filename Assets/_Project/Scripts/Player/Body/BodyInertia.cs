using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The body's clips switching over without a seam: dead blending (Daniel Holden, 2023, a simpler kin of David Bollo's
/// inertialization). When the clips switch (a landing, a roll, a slide's end, a dodge, a jump, a crouch), the mixer goes to the
/// new ones at once (PlayerBody.Blend), and for the switch's time the bones go on from where they were with the motion they
/// had, that motion dying away, while the new clips' pose takes over along a smooth curve. Nothing in between is an average of
/// two poses, so no pose shows that is neither move, and the body's motion carries through the switch: with the camera on the
/// eyes, the view goes on as the head did. Until 0.9.379 the clips were crossfaded, both played and averaged over the blend's
/// time: the pose went soft between them and half the motion went out of it, and the user felt every move as cut off from the
/// next (2026-11-24: "모든 동작이 전반적으로 … 애니메이션이 이어지지 않는 느낌"). Which foot a run takes over with is the
/// clips' own (PlayerBody.StepFrom); this carries only the motion.
/// It works on the bones as the animation pass left them, first in PlayerBody's late pose work, so everything after it (the look
/// bend, the feet, the hands) works from the eased pose. While FPS Animation Framework holds the upper body with a gun it eases
/// only the hips and legs, so the gun and the sight on the head never lag behind the aim.
/// </summary>
internal sealed class BodyInertia
{
    private static readonly HumanBodyBones[] Legs =
    {
        HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes
    };
    private static readonly HumanBodyBones[] Above =
    {
        HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand
    };
    // The motion carried from before a switch halves in this share of the switch's time.
    private const float CarryHalfLife = .25f;
    // Faster than these the bones did not move but jumped (a pose set at once): that is not carried (degrees, m a second).
    private const float FastestTurn = 1440f, FastestHips = 8f;

    private readonly List<Transform> bones = new List<Transform>();
    private readonly List<bool> above = new List<bool>();
    // Each bone's local turn as this left it on the last frame and the one before; where the switch under way carries it on
    // from, and how fast it turns there (its parent's frame, radians a second).
    private Quaternion[] last = new Quaternion[0], before = new Quaternion[0], from = new Quaternion[0];
    private Vector3[] spin = new Vector3[0];
    private Transform hips;
    private Vector3 lastHips, beforeHips, fromHips, hipsSpeed;
    // Frames seen since it was bound (0, 1, 2 and on), the last one's length, the switch's time and how far into it.
    private int seen;
    private float lastDt, length, played;
    private bool blending, asked;
    private float askedLength;

    internal void Bind(Animator animator)
    {
        bones.Clear();
        above.Clear();
        hips = null;
        seen = 0;
        blending = asked = false;
        if (animator == null || !animator.isHuman) return;
        foreach (var bone in Legs) Add(animator.GetBoneTransform(bone), false);
        foreach (var bone in Above) Add(animator.GetBoneTransform(bone), true);
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        int count = bones.Count;
        last = new Quaternion[count];
        before = new Quaternion[count];
        from = new Quaternion[count];
        spin = new Vector3[count];
    }

    private void Add(Transform bone, bool upper)
    {
        if (bone == null || bones.Contains(bone)) return;
        bones.Add(bone);
        above.Add(upper);
    }

    /// <summary>The clips switched this frame: ease over <paramref name="time"/> (s).</summary>
    internal void Begin(float time)
    {
        asked = true;
        askedLength = Mathf.Max(.02f, time);
    }

    /// <summary>Right after the animation pass: the bones eased from where they were if a switch is under way, and kept for
    /// the next. <paramref name="aboveHeld"/>: FPSAF has the upper body (a gun), which is left as it is.</summary>
    internal void Apply(float dt, bool aboveHeld)
    {
        int count = bones.Count;
        if (count == 0 || dt <= 0f) return;
        if (asked)
        {
            asked = false;
            if (seen >= 1) Start();
        }
        if (blending)
        {
            played += dt;
            float share = Mathf.Clamp01(played / length), ease = share * share * (3f - 2f * share);
            float keep = Mathf.Pow(.5f, dt / Mathf.Max(.005f, CarryHalfLife * length));
            for (int i = 0; i < count; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;
                Vector3 turn = spin[i] * dt;
                float angle = turn.magnitude;
                if (angle > 1e-6f) from[i] = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, turn / angle) * from[i];
                spin[i] *= keep;
                if (aboveHeld && above[i]) continue;
                bone.localRotation = Quaternion.Slerp(from[i], bone.localRotation, ease);
            }
            if (hips != null)
            {
                fromHips += hipsSpeed * dt;
                hipsSpeed *= keep;
                hips.localPosition = Vector3.Lerp(fromHips, hips.localPosition, ease);
            }
            if (share >= 1f) blending = false;
        }
        for (int i = 0; i < count; i++)
        {
            before[i] = last[i];
            if (bones[i] != null) last[i] = bones[i].localRotation;
        }
        beforeHips = lastHips;
        if (hips != null) lastHips = hips.localPosition;
        lastDt = dt;
        if (seen < 2) seen++;
    }

    // From where the bones were last shown, moving as they were then.
    private void Start()
    {
        int count = bones.Count;
        bool moving = seen >= 2 && lastDt > 0f;
        for (int i = 0; i < count; i++)
        {
            from[i] = last[i];
            spin[i] = moving ? Spin(before[i], last[i], lastDt) : Vector3.zero;
        }
        fromHips = lastHips;
        hipsSpeed = moving ? Vector3.ClampMagnitude((lastHips - beforeHips) / lastDt, FastestHips) : Vector3.zero;
        length = askedLength;
        played = 0f;
        blending = true;
    }

    // How fast a bone turned from one pose to the next (its parent's frame, radians a second); none for a jump.
    private static Vector3 Spin(Quaternion was, Quaternion now, float dt)
    {
        (now * Quaternion.Inverse(was)).ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        float speed = angle / dt;
        if (Mathf.Abs(speed) > FastestTurn || !float.IsFinite(axis.x)) return Vector3.zero;
        return axis * (speed * Mathf.Deg2Rad);
    }
}
