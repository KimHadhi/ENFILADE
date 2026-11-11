using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Kicks on the legs. When a kick leaves (ShoveMoves.Kicked: at the press on this player's own PC, through
/// PlayerNetworkCombat on everyone else's copy), the clip for the pose it left from plays once on both legs: standing,
/// crouched, looking down, or a dropkick in the air (Beatrun's own air kicks, the moving one and the nearly still one). A
/// pose whose clip is missing kicks with the standing one. The hips, the spine and the arms carry on as they were, so the
/// hands stay on the gun, the reload or the fists, and the eyes, which carry the camera, stay where they are. The foot IK
/// keeps the clip's own lift (PlayerBody.Feet), so the kicking foot rises while the other stays planted. Standing still,
/// legs left facing away from the view turn to it with the kick, so the foot goes out where the eyes look, which is where
/// ShoveMoves sends it. Names and numbers come from Player.csv, row Body.
/// </summary>
public sealed partial class PlayerBody
{
    private const int KickKinds = (int)KickKind.DropStill + 1;

    [Header("Kicks")]
    [SerializeField, Tooltip("Played once on the legs when a kick leaves standing (ShoveMoves.Kicked).")]
    private string kick = "kick";
    [SerializeField, Tooltip("The same, crouched. Missing: the standing one.")]
    private string kickCrouch = "kick";
    [SerializeField, Tooltip("On the ground looking down past Shove.kickDownAngle. Missing: the standing one.")]
    private string kickDown = "kick";
    [SerializeField, Tooltip("In the air and moving: the dropkick (Beatrun's own, meleeair). Missing: the standing kick.")]
    private string dropkick = "meleeair";
    [SerializeField, Tooltip("In the air and nearly still (Beatrun's meleeairstill). Missing: the dropkick.")]
    private string dropkickStill = "meleeairstill";
    [SerializeField, Min(.1f), Tooltip("Playback speed of the kick clips.")]
    private float kickClipSpeed = 1f;
    [SerializeField, Min(.01f), Tooltip("Time for the kick clip to take the legs, and to give them back before it ends (s).")]
    private float kickBlendIn = .06f, kickBlendOut = .2f;
    [SerializeField, Min(0f), Tooltip("Standing still, how fast legs left facing away from the view turn to it with a kick (degrees/s).")]
    private float kickTurnSpeed = 360f;

    private AnimationMixerPlayable kickMixer;
    private readonly List<AnimationClipPlayable> kickClips = new List<AnimationClipPlayable>();
    private readonly List<float> kickLengths = new List<float>();
    // The kick mixer's input each pose plays, -1 for none; and the one playing now.
    private readonly int[] kickInputs = new int[KickKinds];
    private int kickInput = -1;
    private AvatarMask legMask;
    private ShoveMoves kicks;
    private float kickWeight;
    private bool kicking;

    // From BuildLayers, once the layer mixer exists.
    private void BuildKick()
    {
        legMask = Mask(AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg);
        layers.SetLayerMaskFromAvatarMask(KickLayer, legMask);
        kickMixer = AnimationMixerPlayable.Create(graph, 0);
        graph.Connect(kickMixer, 0, layers, KickLayer);
        layers.SetInputWeight(KickLayer, 0f);
        kickWeight = 0f;
        kickInput = -1;
        // One playable per clip, however many poses share it; a pose without a clip of its own takes the one it stands
        // in for (KickKind's order puts that one first).
        var made = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < KickKinds; i++)
        {
            int input = KickInput(KickClipName((KickKind)i), made);
            kickInputs[i] = input >= 0 || i == (int)KickKind.Stand ? input : kickInputs[(int)KickFallback((KickKind)i)];
        }
        kicks = ShoveMoves.For(player);
        if (kicks != null) kicks.Kicked += OnKicked;
    }

    private string KickClipName(KickKind pose)
    {
        switch (pose)
        {
            case KickKind.Crouch: return kickCrouch;
            case KickKind.Down: return kickDown;
            case KickKind.Drop: return dropkick;
            case KickKind.DropStill: return dropkickStill;
            default: return kick;
        }
    }

    // What a pose without a clip of its own kicks with.
    private static KickKind KickFallback(KickKind pose) => pose == KickKind.DropStill ? KickKind.Drop : KickKind.Stand;

    // The mixer input playing this clip, made the first time; -1 when there is no such clip.
    private int KickInput(string clipName, Dictionary<string, int> made)
    {
        if (string.IsNullOrEmpty(clipName)) return -1;
        if (made.TryGetValue(clipName, out int known)) return known;
        var clip = profile.Find(clipName);
        if (clip == null)
        {
            Report($"clip '{clipName}' is not in the PlayerBody profile.");
            return made[clipName] = -1;
        }
        var playable = ClipPlayable(clip);
        // Held at its start until a kick leaves.
        playable.SetSpeed(0);
        int input = kickMixer.GetInputCount();
        kickMixer.SetInputCount(input + 1);
        graph.Connect(playable, 0, kickMixer, input);
        kickMixer.SetInputWeight(input, 0f);
        kickClips.Add(playable);
        kickLengths.Add(Mathf.Max(.01f, clip.length));
        return made[clipName] = input;
    }

    private void TeardownKick()
    {
        if (kicks != null) kicks.Kicked -= OnKicked;
        kicks = null;
        kickMixer = default;
        kickClips.Clear();
        kickLengths.Clear();
        kickInput = -1;
        kickWeight = 0f;
        kicking = false;
        if (legMask != null) Destroy(legMask);
    }

    private void OnKicked(KickKind pose)
    {
        int input = (int)pose < KickKinds ? kickInputs[(int)pose] : -1;
        if (!kickMixer.IsValid() || input < 0 || input >= kickClips.Count) return;
        kickInput = input;
        for (int i = 0; i < kickClips.Count; i++) kickMixer.SetInputWeight(i, i == input ? 1f : 0f);
        var clip = kickClips[input];
        clip.SetTime(0);
        clip.SetSpeed(kickClipSpeed);
        clip.SetDone(false);
        kicking = true;
    }

    // From BlendLayers, after ChoosePose has set where the legs face.
    private void BlendKick(float dt)
    {
        if (!kickMixer.IsValid() || kickInput < 0) return;
        // Counted on the clip's own clock, so a slowed copy (SetPace) kicks as slowly as its legs play.
        float left = (kickLengths[kickInput] - (float)kickClips[kickInput].GetTime()) / kickClipSpeed;
        // Any other move takes the legs back: a vault, a slide or a climb has legs of its own. Standing and crouching
        // include their air, where the dropkick plays.
        if (dead || left <= 0f || (state != MovementState.Standing && state != MovementState.Crouching)) kicking = false;
        bool on = kicking && left > kickBlendOut;
        kickWeight = Mathf.MoveTowards(kickWeight, on ? 1f : 0f, dt / (on ? kickBlendIn : kickBlendOut));
        layers.SetInputWeight(KickLayer, kickWeight);
        // Standing still the legs may face up to turnStartAngle away from the view (TurnOnTheSpot).
        if (!kicking || !legsPlanted || state != MovementState.Standing) return;
        float look = transform.eulerAngles.y;
        float offset = Mathf.MoveTowards(Mathf.DeltaAngle(look, legYaw), 0f, kickTurnSpeed * dt);
        legYaw = look + offset;
        targetYaw = offset;
    }
}
