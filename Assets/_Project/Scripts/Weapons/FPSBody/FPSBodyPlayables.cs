using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// FPSAF's playables on our body, with the base layer of its master mixer held by name: the body's own clips (PlayerBody's
/// layers), which everything FPSAF plays for a gun goes over.
/// KINEMATION's (FPSPlayablesController) takes as its base layer whatever plays on the Animator's first output at the
/// moment it sets up, and again whenever the Animator comes back on (InitializeController, RebuildPlayables). That is the
/// body's clips only if they happen to be on that output at that moment, and nothing said so when they weren't: the
/// user's build of 2026-11-15 stood the player in the pose of no clip at all, hips at the feet ("빌드 버전 보니까 나는 땅
/// 속에 박혀서 머리만 바닥 위에 나와 있어"), and its Player.log had only PlayerBody's line that FPSAF had not taken the
/// body's clips. Read from that line and the code, not watched in a build: the Animator puts its own controller back on
/// its first output when it sets itself up again, which it had done there before FPSAF set up (the editor never wrote the
/// line).
/// Here the base layer is the body's clips whatever the first output plays: put in right after KINEMATION's setup and
/// rebuild, and looked at every frame (one read), so nothing that changes the graph can leave the body without its clips.
/// What was found in their place is said once, for the log. PlayerBody keeps its side of it (PlayClipsInOnePlace): its own
/// output lets go of the clips while they are here.
/// It also says what the mixer is playing of a gun's own actions (Slot: each clip, how far into it the graph is, its weight
/// in the mix), which is the one clock and the one blend the gun and the hands follow an action by (FPSBody.FeedAction), and
/// it answers FPSAF's Pose Sampler who carries the gun, the body or the right hand (GunOnBody).
/// </summary>
[DisallowMultipleComponent]
public sealed class FPSBodyPlayables : FPSPlayablesController, IPlayablesController
{
    private PlayerBody body;
    private bool told;

    /// <summary>Whose clips the base layer is. Before FPSAF sets up (FPSBody.Install).</summary>
    internal void Bind(PlayerBody of) => body = of;

    /// <summary>
    /// How much the body carries the held gun (1) and not the right hand of the clip playing (0). FPSAF's Pose Sampler holds
    /// the gun on its weapon bone, which is where this body holds the gun, by a curve of the animation playing
    /// (WeaponBoneWeight), and in the right hand without one; none of our clips has that curve, so it is said here. FPSBody
    /// sets it every frame: through an action whose track moves the gun, the gun is on the body (FPSBody.FeedAction).
    /// </summary>
    internal float GunOnBody;

    public override float GetCurveValue(string curveName, bool isAnimator = false)
        => !isAnimator && curveName == FPSANames.Curve_WeaponBoneWeight ? GunOnBody : base.GetCurveValue(curveName, isAnimator);

    /// <summary>How many clips the slot mixer plays at once at most: a new action's clip comes in over the one before it.</summary>
    internal int Slots => _slotMixer.mixer.IsValid() ? _slotMixer.mixer.GetInputCount() - 1 : 0;

    /// <summary>
    /// One of the clips the slot mixer is playing, from the oldest (0) to the newest: the clip, how far into it the graph
    /// will be once it has moved on this frame (s into the clip, at the clip's own pace), and its weight in the mix as the
    /// mixer set it this frame (each newer clip lies over the older ones by its own weight). False for a slot nothing plays in.
    /// </summary>
    internal bool Slot(int slot, out AnimationClip clip, out float time, out float weight)
    {
        clip = null;
        time = weight = 0f;
        var mixer = _slotMixer.mixer;
        // The mixer's first input is the pose under the actions.
        int input = slot + 1;
        if (!mixer.IsValid() || slot < 0 || input >= mixer.GetInputCount()) return false;
        var playing = mixer.GetInput(input);
        if (!playing.IsValid() || !playing.IsPlayableOfType<AnimationClipPlayable>()) return false;
        clip = ((AnimationClipPlayable)playing).GetAnimationClip();
        if (clip == null) return false;
        weight = mixer.GetInputWeight(input);
        time = (float)(playing.GetTime() + Time.deltaTime * playing.GetSpeed());
        return true;
    }

    public override bool InitializeController()
    {
        bool made = base.InitializeController();
        if (made) TakeClips("when FPS Animation Framework set up");
        return made;
    }

    /// <summary>
    /// KINEMATION's rebuild (the Animator back on: FPSAnimator), then the body's clips as the base layer again. Its own is
    /// not virtual, so the interface FPSAF calls it through (IPlayablesController) is taken here anew.
    /// </summary>
    public new void RebuildPlayables()
    {
        base.RebuildPlayables();
        TakeClips("when FPS Animation Framework rebuilt");
    }

    protected override void Update()
    {
        TakeClips("in play");
        base.Update();
    }

    // The body's clips under the master mixer as its base layer, if they aren't. They hang under one parent, so they come
    // off any other first; clips in another graph than FPSAF's (a body being made again) are left alone.
    private void TakeClips(string when)
    {
        if (body == null || !_masterMixer.IsValid()) return;
        Playable clips = body.Clips;
        if (!clips.IsValid()) return;
        var under = _masterMixer.GetInput(0);
        if (under.IsValid() && under.Equals(clips)) return;
        if (!clips.GetGraph().Equals(_masterMixer.GetGraph())) return;
        string found = under.IsValid() ? under.GetPlayableType().Name : "nothing";
        if (under.IsValid()) _masterMixer.DisconnectInput(0);
        for (int i = 0; i < clips.GetOutputCount(); i++)
        {
            var parent = clips.GetOutput(i);
            if (!parent.IsValid()) continue;
            for (int k = 0; k < parent.GetInputCount(); k++)
                if (parent.GetInput(k).Equals(clips)) parent.DisconnectInput(k);
        }
        _masterMixer.ConnectInput(0, clips, 0, 1f);
        if (told) return;
        told = true;
        Debug.Log($"[FPSBody] FPS Animation Framework's base layer had {found} in place of {name}'s clips {when}; the clips were put in.", this);
    }
}
