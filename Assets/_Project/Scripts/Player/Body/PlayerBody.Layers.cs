using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Layers over the movement clips, so the hips and legs carry on under them: the fingers make fists while fists are
/// up, and a punch's chest and arms come from its clip. The hands' IK lets go of the punching arm while the clip has
/// it (PlayerBodyHands); the other hand stays on its fist target, which keeps the view framed the way FistsView set it.
/// Without a clip FistsView's targets alone throw the punch. A reload kick, the steps of a turn on the spot and a step
/// dodge with a gun out take only the legs (PlayerBody.Kick, PlayerBody.Locomotion, PlayerBody.Dodge).
/// Names and numbers come from Player.csv, row Body.
/// </summary>
public sealed partial class PlayerBody
{
    private const int MovesLayer = 0, ClenchLayer = 1, PunchLayer = 2, TurnLayer = 3, KickLayer = 4, DodgeLayer = 5;

    [Header("Fists")]
    [SerializeField, Tooltip("Looped while fists are up. Only its fingers play, so the hands make fists.")]
    private string fistClench = "Punch_M3_Idle";
    [SerializeField] private string punchLeft = "Punch_M3_Left_Jab", punchRight = "Punch_M3_Right_Cross";
    [SerializeField, Tooltip("Punches released with at least punchHardCharge of a full charge.")]
    private string punchLeftHard = "Punch_M3_Left_Hook", punchRightHard = "Punch_M3_Right_Uppercut";
    [SerializeField, Range(0f, 1f)] private float punchHardCharge = .5f;
    [SerializeField, Min(.1f), Tooltip("Playback speed of the punch clips.")]
    private float punchClipSpeed = 1.6f;
    [SerializeField, Min(.01f), Tooltip("Time for a punch clip to take over and to give back the arm (s).")]
    private float punchBlendIn = .04f, punchBlendOut = .12f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of the way the camera steadies off the eyes during a punch, because its chest carries the head forward.")]
    private float punchEyeSteady = .5f;

    private readonly List<Channel> punchChannels = new List<Channel>();
    private readonly Dictionary<string, Channel> punchByName = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
    private AnimationLayerMixerPlayable layers;
    private AnimationMixerPlayable punchMixer;
    private AvatarMask fingerMask, upperMask;
    private PlayerBodyHands hands;
    private Channel punch;
    private bool punchIsLeft;
    private float punchTimeLeft, punchWeight, leftArmShare, rightArmShare;

    private AnimationLayerMixerPlayable BuildLayers()
    {
        layers = AnimationLayerMixerPlayable.Create(graph, DodgeLayer + 1);
        graph.Connect(mixer, 0, layers, MovesLayer);
        layers.SetInputWeight(MovesLayer, 1f);

        fingerMask = Mask(AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);
        layers.SetLayerMaskFromAvatarMask(ClenchLayer, fingerMask);
        var clench = profile.Find(fistClench);
        if (clench != null) graph.Connect(ClipPlayable(clench), 0, layers, ClenchLayer);
        else Report($"clip '{fistClench}' is not in the PlayerBody profile.");

        upperMask = Mask(AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
            AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);
        layers.SetLayerMaskFromAvatarMask(PunchLayer, upperMask);
        punchMixer = AnimationMixerPlayable.Create(graph, 0);
        graph.Connect(punchMixer, 0, layers, PunchLayer);
        foreach (string clip in new[] { punchLeft, punchRight, punchLeftHard, punchRightHard }) AddPunch(clip);
        BuildKick();
        BuildTurn();
        BuildDodge();
        return layers;
    }

    private void TeardownLayers()
    {
        punchChannels.Clear();
        punchByName.Clear();
        punch = null;
        punchTimeLeft = punchWeight = leftArmShare = rightArmShare = 0f;
        TeardownDodge();
        TeardownTurn();
        TeardownKick();
        if (fingerMask != null) Destroy(fingerMask);
        if (upperMask != null) Destroy(upperMask);
    }

    // Hips stay out of every mask (the Root part), so the layers never turn or lift the legs under them.
    private static AvatarMask Mask(params AvatarMaskBodyPart[] parts)
    {
        var mask = new AvatarMask();
        for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            mask.SetHumanoidBodyPartActive(part, Array.IndexOf(parts, part) >= 0);
        return mask;
    }

    private AnimationClipPlayable ClipPlayable(AnimationClip clip)
    {
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(false);
        return playable;
    }

    private void AddPunch(string clipName)
    {
        if (string.IsNullOrEmpty(clipName) || punchByName.ContainsKey(clipName)) return;
        var clip = profile.Find(clipName);
        if (clip == null)
        {
            Report($"clip '{clipName}' is not in the PlayerBody profile.");
            return;
        }
        var playable = ClipPlayable(clip);
        int input = punchMixer.GetInputCount();
        punchMixer.SetInputCount(input + 1);
        graph.Connect(playable, 0, punchMixer, input);
        var channel = new Channel { Playable = playable, Input = input, Length = Mathf.Max(.01f, clip.length) };
        punchChannels.Add(channel);
        punchByName[clipName] = channel;
    }

    /// <summary>A punch just left (FistsView.Thrown): the chest and that arm take its clip.</summary>
    internal void ThrowPunch(bool right, float charge)
    {
        bool hard = charge >= punchHardCharge;
        string clip = right ? (hard ? punchRightHard : punchRight) : (hard ? punchLeftHard : punchLeft);
        if (string.IsNullOrEmpty(clip) || !punchByName.TryGetValue(clip, out var channel)) return;
        channel.Playable.SetTime(0);
        channel.Playable.SetSpeed(punchClipSpeed);
        channel.Playable.SetDone(false);
        punch = channel;
        punchIsLeft = !right;
        punchTimeLeft = channel.Length / punchClipSpeed;
    }

    /// <summary>How much of this arm the punch clip has; the hand's fist target keeps the rest.</summary>
    internal float PunchArmShare(bool leftArm) => leftArm ? leftArmShare : rightArmShare;

    private void BlendLayers(float dt)
    {
        layers.SetInputWeight(ClenchLayer, hands != null ? hands.FistsWeight : 0f);
        BlendTurn(dt);
        BlendKick(dt);
        BlendDodge(dt);
        // A guard or a climb takes the arm back at once: the parry's sweep and the hold need the hand on its target.
        var melee = player.MeleeCombat;
        bool guarding = melee != null && (melee.IsGuarding || melee.IsParryActive);
        punchTimeLeft = dead || guarding || !PlayerBodyHands.HoldsWeapons(state) ? 0f : Mathf.Max(0f, punchTimeLeft - dt);
        bool throwing = punch != null && punchTimeLeft > punchBlendOut;
        punchWeight = Ease(punchWeight, throwing);
        leftArmShare = Ease(leftArmShare, throwing && punchIsLeft);
        rightArmShare = Ease(rightArmShare, throwing && !punchIsLeft);
        layers.SetInputWeight(PunchLayer, punchWeight);

        float total = 0f;
        foreach (var channel in punchChannels)
        {
            channel.Weight = Mathf.MoveTowards(channel.Weight, channel == punch ? 1f : 0f, dt / punchBlendIn);
            total += channel.Weight;
        }
        if (total <= .0001f) return;
        foreach (var channel in punchChannels) punchMixer.SetInputWeight(channel.Input, channel.Weight / total);

        float Ease(float value, bool on) => Mathf.MoveTowards(value, on ? 1f : 0f, dt / (on ? punchBlendIn : punchBlendOut));
    }
}
