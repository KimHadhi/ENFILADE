using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The step dodge with a gun out plays on the legs alone (DodgeLayer, the kick's leg mask). The standing clip keeps the
/// hips, spine, arms and head, under FPS Animation Framework's gun hold. With the whole clip, the dodge's own upper body
/// and the hands holding the gun fought over the same bones (user, 2026-11-09: "총 들었을 때 회피 모션은 상체 안
/// 건들였으면"). With nothing in hand, or the fists, the whole clip still plays in the moves mixer.
/// </summary>
public sealed partial class PlayerBody
{
    private AnimationMixerPlayable dodgeMixer;
    // Every dodge's legs-only copy by its clip's name, and the input it is on.
    private readonly System.Collections.Generic.Dictionary<string, (AnimationClipPlayable playable, int input)> dodgeLegs =
        new System.Collections.Generic.Dictionary<string, (AnimationClipPlayable, int)>();
    private float dodgeLayerWeight, dodgeLayerTarget;

    // From BuildLayers, after BuildKick has made the leg mask: the side dodges and every dodge of the ways (PlayerBody.Ways).
    private void BuildDodge()
    {
        layers.SetLayerMaskFromAvatarMask(DodgeLayer, legMask);
        var names = new System.Collections.Generic.List<string> { dodgeLeft, dodgeRight };
        foreach (string set in new[] { dodgeWays, dodgeRunWays, dodgeSprintWays })
            foreach (var (clip, _) in WaysOf(set))
                if (!names.Contains(clip)) names.Add(clip);
        dodgeMixer = AnimationMixerPlayable.Create(graph, names.Count);
        graph.Connect(dodgeMixer, 0, layers, DodgeLayer);
        dodgeLegs.Clear();
        for (int i = 0; i < names.Count; i++)
        {
            var playable = DodgeLegsClip(names[i], i);
            if (playable.IsValid()) dodgeLegs[names[i]] = (playable, i);
        }
        dodgeLayerWeight = dodgeLayerTarget = 0f;
        layers.SetInputWeight(DodgeLayer, 0f);
    }

    private AnimationClipPlayable DodgeLegsClip(string clipName, int input)
    {
        // A missing clip is reported once by the moves mixer's own channel (AddChannel).
        var clip = string.IsNullOrEmpty(clipName) ? null : profile.Find(clipName);
        if (clip == null) return default;
        var playable = ClipPlayable(clip);
        playable.SetSpeed(0);
        graph.Connect(playable, 0, dodgeMixer, input);
        return playable;
    }

    private void TeardownDodge()
    {
        dodgeMixer = default;
        dodgeLegs.Clear();
        dodgeLayerWeight = dodgeLayerTarget = 0f;
    }

    // From Enter: the legs' copy of the dodge starts with the whole clip, so either can take over this frame.
    private void StartDodgeLegs(string clip)
    {
        if (!dodgeMixer.IsValid()) return;
        foreach (var entry in dodgeLegs) dodgeMixer.SetInputWeight(entry.Value.input, entry.Key == clip ? 1f : 0f);
        if (!dodgeLegs.TryGetValue(clip ?? "", out var legs)) return;
        legs.playable.SetTime(0);
        legs.playable.SetSpeed(1);
        legs.playable.SetDone(false);
    }

    // From ChoosePose. With a gun the body stands as it does still and the legs dodge; otherwise the whole clip plays.
    private void DodgePose()
    {
        // From a crouch: the crouch held low as the body slides along, a crouched slide (user, 2026-11-12: "앉아서 회피할
        // 때는 앉아서 슬라이딩하는 회피 모션"); the standing clip hopped up out of the crouch and its feet ran.
        if (player.IsCrouchStance) { Want(crouchIdle); return; }
        if (HoldsGun && dodgeLegs.ContainsKey(dodgeClip ?? ""))
        {
            Want(idle);
            dodgeLayerTarget = 1f;
        }
        else Want(dodgeClip);
    }

    // From BlendLayers, after ChoosePose has said whether the legs dodge this frame.
    private void BlendDodge(float dt)
    {
        if (!dodgeMixer.IsValid()) return;
        float target = dead ? 0f : dodgeLayerTarget;
        dodgeLayerTarget = 0f;
        dodgeLayerWeight = Mathf.MoveTowards(dodgeLayerWeight, target, dt / blendTime);
        layers.SetInputWeight(DodgeLayer, dodgeLayerWeight);
    }
}
