using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Measures, from a move's own clip on a body, how its hands carry the body over an obstacle, for WarpedTraversal. The clip
/// is sampled on the body itself (its pose put back afterwards) in the Animator's space, x right, y up, z ahead, and read
/// like this:
/// - A hand plants where it reaches furthest ahead of the hips, below its shoulder, early in the clip: putting a hand down
///   on something is reaching ahead, and afterwards the body goes over it and it falls behind. It counts only if it
///   reaches far enough ahead (handAhead, a share of the arm's length) and the body then goes over it.
/// - It leaves once the hips have gone a share of an arm past it (handPass) or it has lifted that share off its lowest
///   (handLift), whichever comes first.
/// The support is while every planting hand is down. Nothing here is written for one clip or one character, so a clip
/// swapped in later, on any humanoid, measures itself; a clip that puts no hand down has no support and is left to the
/// straight move. One clip on one model is measured once and kept.
/// </summary>
internal static class ObstacleClipContacts
{
    /// <summary>Samples a second.</summary>
    private const float SampleRate = 60f;

    internal readonly struct Rules
    {
        public readonly float HandAhead, HandPass, HandLift, PlantWindow;

        public Rules(float handAhead, float handPass, float handLift, float plantWindow)
        {
            HandAhead = handAhead; HandPass = handPass; HandLift = handLift; PlantWindow = plantWindow;
        }
    }

    /// <summary>A clip's hand support on one model.</summary>
    internal sealed class Contacts
    {
        public float Length, PlantTime, ReleaseTime;
        public bool Left, Right;
        public Vector3[] LeftHand, RightHand;
        public float Step;

        /// <summary>The middle of the planting hands at <paramref name="time"/>, in the Animator's space.</summary>
        public Vector3 HandsAt(float time)
        {
            if (Left && Right) return (Sample(LeftHand, time) + Sample(RightHand, time)) * .5f;
            return Sample(Left ? LeftHand : RightHand, time);
        }

        private Vector3 Sample(Vector3[] samples, float time)
        {
            float at = Mathf.Clamp(time / Step, 0f, samples.Length - 1);
            int i = Mathf.Min((int)at, samples.Length - 2);
            return Vector3.Lerp(samples[i], samples[i + 1], at - i);
        }
    }

    private static readonly Dictionary<(Object model, AnimationClip clip), Contacts> measured = new();

    /// <summary>
    /// <paramref name="clip"/>'s hand support on <paramref name="animator"/>'s body, made from <paramref name="model"/>;
    /// null when it puts no hand down.
    /// </summary>
    internal static Contacts For(Object model, AnimationClip clip, Animator animator, in Rules rules)
    {
        if (clip == null || animator == null || !animator.isHuman) return null;
        if (measured.TryGetValue((model, clip), out var known)) return known;
        var made = Measure(clip, animator, rules);
        measured[(model, clip)] = made;
        return made;
    }

    private static Contacts Measure(AnimationClip clip, Animator animator, in Rules rules)
    {
        Transform root = animator.transform;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var leftElbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        if (hips == null || leftHand == null || rightHand == null || leftShoulder == null || rightShoulder == null || leftElbow == null)
            return null;

        int count = Mathf.Max(2, Mathf.CeilToInt(clip.length * SampleRate) + 1);
        float step = clip.length / (count - 1);
        var leftSamples = new Vector3[count];
        var rightSamples = new Vector3[count];
        var hipSamples = new Vector3[count];
        var leftShoulders = new Vector3[count];
        var rightShoulders = new Vector3[count];

        // The body is sampled where it stands and put back as it was: its own graph poses it again before it is drawn.
        var bones = root.GetComponentsInChildren<Transform>();
        var poses = new (Vector3 position, Quaternion rotation, Vector3 scale)[bones.Length];
        for (int i = 0; i < bones.Length; i++) poses[i] = (bones[i].localPosition, bones[i].localRotation, bones[i].localScale);
        float arm;
        try
        {
            clip.SampleAnimation(animator.gameObject, 0f);
            arm = (Vector3.Distance(leftShoulder.position, leftElbow.position) + Vector3.Distance(leftElbow.position, leftHand.position))
                / Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.y));
            for (int i = 0; i < count; i++)
            {
                clip.SampleAnimation(animator.gameObject, i * step);
                leftSamples[i] = root.InverseTransformPoint(leftHand.position);
                rightSamples[i] = root.InverseTransformPoint(rightHand.position);
                hipSamples[i] = root.InverseTransformPoint(hips.position);
                leftShoulders[i] = root.InverseTransformPoint(leftShoulder.position);
                rightShoulders[i] = root.InverseTransformPoint(rightShoulder.position);
            }
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(poses[i].position, poses[i].rotation);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].localScale = poses[i].scale;
        }
        if (arm <= 1e-3f) return null;

        bool left = Plants(leftSamples, leftShoulders, hipSamples, arm, rules, out int leftPlant, out int leftRelease);
        bool right = Plants(rightSamples, rightShoulders, hipSamples, arm, rules, out int rightPlant, out int rightRelease);
        if (!left && !right)
        {
            // Not reached down ahead: a clip that grabs high and pushes up over its hands, as the climb off a ladder's top
            // does (Beatrun's: up to 1.5 m over the feet by 0.3 s, then held while sinking to 0.8 m by about 1.1 s).
            left = PushesUp(leftSamples, arm, step, out leftPlant, out leftRelease);
            right = PushesUp(rightSamples, arm, step, out rightPlant, out rightRelease);
        }
        if (!left && !right) return null;
        int plant = Mathf.Max(left ? leftPlant : 0, right ? rightPlant : 0);
        int release = Mathf.Min(left ? leftRelease : count - 1, right ? rightRelease : count - 1);
        if (release <= plant) return null;
        return new Contacts
        {
            Length = clip.length, PlantTime = plant * step, ReleaseTime = release * step, Step = step,
            Left = left, Right = right, LeftHand = leftSamples, RightHand = rightSamples
        };
    }

    // A hand that grabs high and pushes the body up over it: it plants at its highest early on (the first 60% of the
    // clip), and leaves once, having sunk at least PushDrop of an arm toward the feet, it stops sinking (slower than
    // PushStill). False when it never sinks that far: the body never climbed over it.
    private const float PushWindow = .6f, PushDrop = .25f, PushStill = .1f;
    private static bool PushesUp(Vector3[] hand, float arm, float step, out int plant, out int release)
    {
        plant = release = -1;
        int last = hand.Length - 1, window = Mathf.Clamp(Mathf.FloorToInt(PushWindow * last), 1, last);
        float highest = float.NegativeInfinity;
        for (int i = 0; i <= window; i++)
            if (hand[i].y > highest) { highest = hand[i].y; plant = i; }
        if (plant < 0) return false;
        for (int i = plant + 1; i <= last; i++)
        {
            float sinking = (hand[i - 1].y - hand[i].y) / Mathf.Max(1e-4f, step);
            if (highest - hand[i].y >= PushDrop * arm && sinking < PushStill) { release = i; break; }
        }
        return release > plant + 1;
    }

    // Where one hand plants (reaching furthest ahead of the hips, below its shoulder, early on) and leaves (the hips gone a
    // share of an arm past it, or lifted that share off its lowest). False when it never reaches ahead enough or the body
    // never goes over it.
    private static bool Plants(Vector3[] hand, Vector3[] shoulder, Vector3[] hips, float arm, in Rules rules, out int plant, out int release)
    {
        plant = release = -1;
        int last = hand.Length - 1, window = Mathf.Clamp(Mathf.FloorToInt(rules.PlantWindow * last), 1, last);
        float best = float.NegativeInfinity;
        for (int i = 0; i <= window; i++)
        {
            if (hand[i].y >= shoulder[i].y) continue;
            float ahead = hand[i].z - hips[i].z;
            if (ahead > best) { best = ahead; plant = i; }
        }
        if (plant < 0 || best < rules.HandAhead * arm) return false;
        float lowest = float.PositiveInfinity;
        for (int i = plant + 1; i <= last; i++)
        {
            float height = hand[i].y - hips[i].y;
            lowest = Mathf.Min(lowest, height);
            bool passed = hand[i].z - hips[i].z <= best - rules.HandPass * arm;
            bool lifted = height - lowest >= rules.HandLift * arm;
            if (passed || lifted) { release = i; break; }
        }
        // The clip ends with the hand still down: the body never went over it.
        return release > plant + 1;
    }
}
