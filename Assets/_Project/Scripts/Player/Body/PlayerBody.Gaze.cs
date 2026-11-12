using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// The animation-led gaze's numbers (Player.csv, row Body) and which clips lead it; the gaze itself is BodyGaze, which
/// PlayerBody.Look runs: Update first, FaceHead after the face has cleared walls, its Rotation and Input for the camera.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Animation-led gaze")]
    [SerializeField, Range(0f, 1f), Tooltip("How much selected parkour/action clips lead the gaze. Mouse input stays immediate.")]
    private float animationViewStrength = .65f;
    [SerializeField, Tooltip("Maximum animation view offset in degrees: pitch, yaw, roll. A roll never spins the horizon all the way round.")]
    private Vector3 animationViewLimits = new Vector3(18f, 12f, 10f);
    [SerializeField, Min(.01f), Tooltip("Response time of animation rotation only (s); mouse look is never smoothed.")]
    private float animationViewTime = .1f;

    private readonly BodyGaze bodyGaze = new BodyGaze();
    private readonly HashSet<Channel> animationViewChannels = new HashSet<Channel>();

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

    // How much of the pose the gaze-leading clips have: the actual crossfade, an action playing out after its movement
    // state ends included.
    private float GazeClipWeight()
    {
        float weight = 0f;
        if (mixer.IsValid())
            foreach (var channel in animationViewChannels) weight += mixer.GetInputWeight(channel.Input);
        // A paired execution's clip leads it as a committed action does (PlayerBody.Execution).
        return weight + pairedWeight;
    }

    private void TuneGaze() => bodyGaze.Tune(animationViewStrength, animationViewLimits, animationViewTime);
}
