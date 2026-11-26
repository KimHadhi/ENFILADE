using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// A feeling shown from the callout wheel's 감정 page (the user, 2026-11-22: "휠에 '감정' 쪽 추가", and "몸과 표정만, 말
/// 없음"): BIPER Combat Girls' seven CBG_* clips, each a callout line whose gesture cell names its clip (Callouts.csv
/// "emote:CBG_Greeting", CalloutLines.Line.Emote), so the wheel, the face and every other PC come with the callouts
/// (PlayerCallouts.Hear → Emote). The body plays it on two layers over the moves, under an execution: the upper body
/// (spine, head, arms) all through, the hips and legs only while the player stands still, so walking off keeps the walk
/// under a wave. Both hands are the feeling's (HandUses.csv body.emote, asked of HandOwner every frame on every PC): a
/// gun is put away meanwhile (WeaponController) and the fists' guard lowered (PlayerBodyHands). Anything that wants a hand
/// more ends it (an aim, a draw, a reload, a grenade; the trigger or the sights pressed while the gun is away, which the
/// gun asks for as an aim), and so does leaving the feet. Ended early on this PC's own player, the other PCs are told
/// (EmoteCut, PlayerNetworkCombat); its own end each PC sees for itself. The camera keeps to where the eyes were while it
/// plays (BodyCamera.Place's hold) and the head stays on the view (BodyGaze), so the view never sways with the body.
/// </summary>
public sealed partial class PlayerBody
{
    private const int EmoteLayer = 5, EmoteLegsLayer = 6;

    private AnimationMixerPlayable emoteMixer, emoteLegsMixer;
    private AvatarMask hipsLegsMask;
    // Every clip the callouts name for a feeling: its input on both mixers and its two playables (one per layer).
    private readonly Dictionary<string, (int input, AnimationClipPlayable upper, AnimationClipPlayable legs)> emotes =
        new Dictionary<string, (int, AnimationClipPlayable, AnimationClipPlayable)>(StringComparer.OrdinalIgnoreCase);
    private string emote;
    private float emoteLeft, emoteWeight, emoteLegsWeight;
    private HandOwner emoteHands;

    /// <summary>
    /// This PC's own player's feeling ended before its clip did (a hand wanted for something else, off the feet): the
    /// other PCs are told (PlayerNetworkCombat).
    /// </summary>
    internal event Action EmoteCut;

    // How far a feeling has the body now (0-1): the camera holds where the eyes were by as much (PlayerBody.Look).
    private float EmoteHold => emoteWeight;

    private HandOwner EmoteHands => emoteHands != null ? emoteHands : emoteHands = HandOwner.Of(player);

    // Standing only: BIPER has no crouched feelings, and a standing clip's upright spine over crouched legs straightens the
    // body up (as a blow's flinch would: PlayerBody.Flinch).
    private bool OnFeetForEmote => state == MovementState.Standing;

    // From BuildLayers, after the punch's upper-body mask is made.
    private void BuildEmotes()
    {
        hipsLegsMask = Mask(AvatarMaskBodyPart.Root, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg);
        layers.SetLayerMaskFromAvatarMask(EmoteLayer, upperMask);
        layers.SetLayerMaskFromAvatarMask(EmoteLegsLayer, hipsLegsMask);
        emotes.Clear();
        var names = new List<string>();
        foreach (var line in CalloutLines.All)
            if (!string.IsNullOrEmpty(line.Emote) && !names.Exists(n => string.Equals(n, line.Emote, StringComparison.OrdinalIgnoreCase)))
                names.Add(line.Emote);
        emoteMixer = AnimationMixerPlayable.Create(graph, names.Count);
        emoteLegsMixer = AnimationMixerPlayable.Create(graph, names.Count);
        graph.Connect(emoteMixer, 0, layers, EmoteLayer);
        graph.Connect(emoteLegsMixer, 0, layers, EmoteLegsLayer);
        for (int i = 0; i < names.Count; i++)
        {
            var clip = profile.Find(names[i]);
            if (clip == null)
            {
                Report($"clip '{names[i]}' (Callouts.csv, a feeling) is not in the PlayerBody profile.");
                continue;
            }
            var upper = ClipPlayable(clip);
            var legs = ClipPlayable(clip);
            upper.SetSpeed(0);
            legs.SetSpeed(0);
            graph.Connect(upper, 0, emoteMixer, i);
            graph.Connect(legs, 0, emoteLegsMixer, i);
            emotes[names[i]] = (i, upper, legs);
        }
        emote = null;
        emoteLeft = emoteWeight = emoteLegsWeight = 0f;
        layers.SetInputWeight(EmoteLayer, 0f);
        layers.SetInputWeight(EmoteLegsLayer, 0f);
    }

    private void TeardownEmotes()
    {
        if (emote != null) emoteHands.OrNull()?.Give(this);
        emote = null;
        emotes.Clear();
        emoteMixer = emoteLegsMixer = default;
        emoteLeft = emoteWeight = emoteLegsWeight = 0f;
        if (hipsLegsMask != null) Destroy(hipsLegsMask);
    }

    /// <summary>
    /// Whether this body could show the feeling whose clip is <paramref name="clipName"/> now: on its feet, the clip
    /// known, and (this PC's own player) both hands free of anything that keeps them (a reload, a grenade held ready).
    /// The wheel asks before the line is said, so a feeling refused here is never sent to anyone.
    /// </summary>
    internal bool CanEmote(string clipName)
    {
        if (dead || !OnFeetForEmote || string.IsNullOrEmpty(clipName) || !emotes.ContainsKey(clipName)) return false;
        var hands = EmoteHands;
        return hands == null || hands.Could(HandUses.Emote, this, true);
    }

    /// <summary>
    /// Shows the feeling whose clip is <paramref name="clipName"/> now, on whichever PC hears it (a new one replaces one
    /// under way). Returns the seconds it lasts, 0 when it can't be shown here.
    /// </summary>
    internal float Emote(string clipName)
    {
        if (dead || !OnFeetForEmote || string.IsNullOrEmpty(clipName) || !emotes.TryGetValue(clipName, out var entry)) return 0f;
        var hands = EmoteHands;
        if (hands != null && !hands.Take(HandUses.Emote, this)) return 0f;
        foreach (var other in emotes.Values)
        {
            bool shown = other.input == entry.input;
            emoteMixer.SetInputWeight(other.input, shown ? 1f : 0f);
            emoteLegsMixer.SetInputWeight(other.input, shown ? 1f : 0f);
        }
        foreach (var playable in new[] { entry.upper, entry.legs })
        {
            playable.SetTime(0);
            playable.SetSpeed(1);
            playable.SetDone(false);
        }
        emote = clipName;
        emoteLeft = entry.upper.GetAnimationClip().length;
        return emoteLeft;
    }

    /// <summary>On a copy: its PC's player's feeling ended before its clip did (PlayerNetworkCombat).</summary>
    internal void CutEmote() => EndEmote(false);

    private void EndEmote(bool cut)
    {
        if (emote == null) return;
        emote = null;
        emoteLeft = 0f;
        var hands = EmoteHands;
        hands.OrNull()?.Give(this);
        if (cut && hands != null && hands.Decides) EmoteCut?.Invoke();
    }

    // From BlendLayers: the feeling plays out while it keeps both hands and the feet. A hand once lost ends it: asked
    // again, it would take the hand back from a gun that has gone back to its plain hold.
    private void BlendEmote(float dt)
    {
        if (!emoteMixer.IsValid()) return;
        if (emote != null)
        {
            var hands = EmoteHands;
            bool keeps = !dead && OnFeetForEmote && !PlaysPaired
                && (hands == null || (hands.Has(this) && hands.Take(HandUses.Emote, this, false)));
            emoteLeft -= dt;
            if (!keeps) EndEmote(true);
            else if (emoteLeft <= 0f) EndEmote(false);
        }
        bool on = emote != null && emoteLeft > blendTime;
        emoteWeight = Mathf.MoveTowards(emoteWeight, on ? 1f : 0f, dt / blendTime);
        // The hips and legs only while standing still: walking off, the walk keeps the legs under the wave.
        bool still = on && state == MovementState.Standing && keepsPlace;
        emoteLegsWeight = Mathf.MoveTowards(emoteLegsWeight, still ? 1f : 0f, dt / blendTime);
        layers.SetInputWeight(EmoteLayer, emoteWeight);
        layers.SetInputWeight(EmoteLegsLayer, Mathf.Min(emoteWeight, emoteLegsWeight));
    }
}
