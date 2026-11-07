using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

public sealed partial class PlayerBody
{
    [Header("Animation-led gaze")]
    [SerializeField, Range(0f, 1f), Tooltip("How much selected parkour/action clips lead the gaze. Mouse input stays immediate.")]
    private float animationViewStrength = .65f;
    [SerializeField, Tooltip("Maximum animation view offset in degrees: pitch, yaw, roll. A roll never spins the horizon all the way round.")]
    private Vector3 animationViewLimits = new Vector3(18f, 12f, 10f);
    [SerializeField, Min(.01f), Tooltip("Response time of animation rotation only (s); mouse look is never smoothed.")]
    private float animationViewTime = .1f;

    private readonly HashSet<Channel> animationViewChannels = new HashSet<Channel>();
    private Quaternion headToGaze = Quaternion.identity, gazeRotation = Quaternion.identity;
    private Quaternion animationViewRotation = Quaternion.identity, viewRotationHome = Quaternion.identity;
    private float headAnimationWeight;

    private void SetUpGaze()
    {
        // Imported heads have arbitrary bone axes. Measure the anatomical gaze in the rest pose instead.
        headToGaze = Quaternion.Inverse(head.rotation) * Quaternion.LookRotation(restForward, body.up);
        viewRotationHome = view != null ? view.localRotation : Quaternion.identity;
        animationViewRotation = Quaternion.identity;
        headAnimationWeight = 0f;
        gazeRotation = lookPivot != null ? lookPivot.rotation : transform.rotation;
    }

    private void SetUpAnimationViewChannels()
    {
        animationViewChannels.Clear();
        // Walking, idle and turn clips never steer the gaze. Ladder rungs and sustained hangs also stay
        // input-led; climbing over the top, catching a ledge, and other committed actions may lead it.
        foreach (string clip in new[]
        {
            vault, vaultHigh, vaultKong, springboard, mantle, stepUp, mantleLow, mantleMid, ledgeClimbUp, ledgeCatch, ledgeJump,
            wallClimbStart, wallClimb, wallKick, wallKickRight, barSwing, barRelease,
            ladderExitLeft, ladderExitRight, roll, rollLand, landCrouch, heavyLand, heavyLandFaceDown, heavyLandBack,
            diveStart, dive, dodgeLeft, dodgeRight, dashPush
        })
        {
            Add(clip);
            if (!string.IsNullOrEmpty(clip)) { Add(clip + rifleSuffix); Add(clip + pistolSuffix); }
        }
        void Add(string clip)
        {
            if (!string.IsNullOrEmpty(clip) && byName.TryGetValue(clip, out var channel)) animationViewChannels.Add(channel);
        }
    }

    private void UpdateGaze(float dt)
    {
        Quaternion input = lookPivot != null ? lookPivot.rotation : transform.rotation;
        float weight = 0f;
        // Read the actual crossfade, including an action playing out after its movement state ends.
        if (mixer.IsValid())
            foreach (var channel in animationViewChannels) weight += mixer.GetInputWeight(channel.Input);
        headAnimationWeight = Mathf.Clamp01(weight);
        weight = dead ? 0f : headAnimationWeight * Mathf.Clamp01(animationViewStrength) * (1f - AimBlend());

        // Sample this frame's animated head BEFORE the look bent it (CAS keeps that copy, BodyCas.HeadAsClip). Neither last
        // frame's camera nor its effects feed back into the input or the animation. Body yaw belongs to the hips, not aim.
        Quaternion authored = Quaternion.Inverse(body.rotation) * (ClipHead.rotation * headToGaze);
        // Fold inverted motion into small view offsets. Euler decomposition turns a pure somersault past
        // 90 degrees into spurious yaw/roll of 180 degrees, causing an unrelated sideways camera kick.
        Vector3 forward = authored * Vector3.forward, right = authored * Vector3.right, up = authored * Vector3.up;
        Vector3 angles = new Vector3(
            Mathf.Atan2(-forward.y, new Vector2(forward.x, forward.z).magnitude),
            Mathf.Atan2(forward.x, Mathf.Abs(forward.z)),
            Mathf.Atan2(right.y, Mathf.Abs(up.y))) * Mathf.Rad2Deg;
        angles = new Vector3(Limit(angles.x, animationViewLimits.x), Limit(angles.y, animationViewLimits.y),
            Limit(angles.z, animationViewLimits.z));
        Quaternion target = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(angles), weight);
        animationViewRotation = Quaternion.Slerp(animationViewRotation, target,
            1f - Mathf.Exp(-dt / Mathf.Max(.01f, animationViewTime)));
        gazeRotation = input * animationViewRotation;

        static float Limit(float angle, float limit) => Mathf.Clamp(Mathf.DeltaAngle(0f, angle), -Mathf.Abs(limit), Mathf.Abs(limit));
    }

    private void ApplyGazeToCamera()
    {
        if (view == null || view.parent == null) return;
        Quaternion input = lookPivot != null ? lookPivot.rotation : transform.rotation;
        // Effects (lean, impact, recoil presentation) stay additive and are applied once, after gaze.
        // Weapons and hands run later and read this same final camera pose.
        Quaternion effects = Quaternion.Inverse(input) * view.parent.rotation;
        view.rotation = gazeRotation * effects * viewRotationHome;
    }
}
