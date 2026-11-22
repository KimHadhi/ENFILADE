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
    [SerializeField, Range(0f, 1f), Tooltip("How much of the hard landings' and rolls' head turn over the body's right carries the view.")]
    private float animationViewFollow = 1f;
    [SerializeField, Tooltip("Maximum animation view offset in degrees: pitch, yaw, roll. A roll never spins the horizon all the way round.")]
    private Vector3 animationViewLimits = new Vector3(18f, 12f, 10f);
    [SerializeField, Min(.01f), Tooltip("Response time of animation rotation only (s); mouse look is never smoothed.")]
    private float animationViewTime = .1f;

    private readonly BodyGaze bodyGaze = new BodyGaze();
    private readonly HashSet<Channel> animationViewChannels = new HashSet<Channel>();
    // The clips whose head carries the view over (BodyGaze): the landings and rolls.
    private readonly HashSet<Channel> carriedViewChannels = new HashSet<Channel>();

    private void SetUpAnimationViewChannels()
    {
        animationViewChannels.Clear();
        carriedViewChannels.Clear();
        // Walking, idle and turn clips never steer the gaze. Ladder rungs and sustained hangs also stay
        // input-led; climbing over the top, catching a ledge, and other committed actions may lead it.
        foreach (string clip in new[]
        {
            vault, vaultHigh, vaultKong, springboard, mantle, stepUp, mantleLow, mantleMid, ledgeClimbUp, ledgeCatch, ledgeJump,
            wallClimbStart, wallClimb, wallKick, wallKickRight, barSwing, barRelease, clamberLow, clamberHigh,
            ladderExitLeft, ladderExitRight, heavyLand, heavyLandFaceDown, heavyLandBack, downedFall,
            diveStart, dive, dashPush
        })
            Add(animationViewChannels, clip);
        // A jump and a light landing leave the view to the mouse (the user, 2026-11-22: "점프할 때 카메라 덜 흔들리게"): the
        // jump's head nods 16-35 degrees through its flight, and carried all the way (0.9.286), or a bounded share of it
        // (0.9.288), it moved the aim at every jump ("점프는 너무 멀미 나는데").
        // The hard landings and rolls: their head carries the view (BodyGaze). Until 0.9.286 only the old names led it at all
        // (roll, landCrouch), and none of BIPER's landings or rolls did, so through them the view stayed held ahead under a
        // head that went over (the user, 2026-11-21: "닭처럼 고정된 느낌").
        foreach (string clip in new[]
        {
            land, landCrouch, roll, player.AbilitySettings.diveRoll, fallFromEdge, landRunHard, landRunStumble, landSlam
        })
            Add(carriedViewChannels, clip);
        foreach (string set in new[] { rollWays, rollRunWays })
            foreach (var (clip, _) in ClipWays.Of(set))
                Add(carriedViewChannels, clip);

        void Add(HashSet<Channel> into, string clip)
        {
            if (string.IsNullOrEmpty(clip)) return;
            foreach (string name in new[] { clip, clip + rifleSuffix, clip + pistolSuffix })
                if (byName.TryGetValue(name, out var channel) && !animationViewChannels.Contains(channel)) into.Add(channel);
        }
    }

    // How much of the pose the gaze-leading clips have (led: a bounded share; carried: the head carries the view): the actual
    // crossfade, an action playing out after its movement state ends included. A paired execution's clip leads it as a
    // committed action does (PlayerBody.Execution).
    private float LedClipWeight() => Weight(animationViewChannels) + pairedWeight;
    private float CarriedClipWeight() => Weight(carriedViewChannels);
    private float GazeClipWeight() => LedClipWeight() + CarriedClipWeight();

    private float Weight(HashSet<Channel> channels)
    {
        float weight = 0f;
        if (mixer.IsValid())
            foreach (var channel in channels) weight += mixer.GetInputWeight(channel.Input);
        return weight;
    }

    private void TuneGaze() => bodyGaze.Tune(animationViewStrength, animationViewFollow, animationViewLimits, animationViewTime);
}
