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
    private AnimationClipPlayable dodgeLeftLegs, dodgeRightLegs;
    private float dodgeLayerWeight, dodgeLayerTarget;

    // From BuildLayers, after BuildKick has made the leg mask.
    private void BuildDodge()
    {
        layers.SetLayerMaskFromAvatarMask(DodgeLayer, legMask);
        dodgeMixer = AnimationMixerPlayable.Create(graph, 2);
        graph.Connect(dodgeMixer, 0, layers, DodgeLayer);
        dodgeLeftLegs = DodgeLegsClip(dodgeLeft, 0);
        dodgeRightLegs = DodgeLegsClip(dodgeRight, 1);
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
        dodgeLeftLegs = dodgeRightLegs = default;
        dodgeLayerWeight = dodgeLayerTarget = 0f;
    }

    // From Enter: the legs' copy of the dodge starts with the whole clip, so either can take over this frame.
    private void StartDodgeLegs(bool left)
    {
        if (!dodgeMixer.IsValid()) return;
        dodgeMixer.SetInputWeight(0, left ? 1f : 0f);
        dodgeMixer.SetInputWeight(1, left ? 0f : 1f);
        var legs = left ? dodgeLeftLegs : dodgeRightLegs;
        if (!legs.IsValid()) return;
        legs.SetTime(0);
        legs.SetSpeed(1);
        legs.SetDone(false);
    }

    // From ChoosePose. With a gun the body stands as it does still and the legs dodge; otherwise the whole clip plays.
    private void DodgePose()
    {
        var legs = dodgeClip == dodgeLeft ? dodgeLeftLegs : dodgeRightLegs;
        if (HoldsGun && legs.IsValid())
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
