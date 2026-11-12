using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// What the animation probe reads of this body (ZombieAnimProbe), for the bug the user reported three times since
/// 2026-11-12: woken calm zombies "앉은 채, 누운 채로 와 ... 모션이 굳은 채로", "부동 자세로 나한테 와", and on 11-13 "좀비
/// 애니메이션이 여전히 안 나와". Its mood and pose, every weight that decides what shows and the clip behind it, the graph's
/// clock and the Animator's state, and whether its bones moved at all since the last look while it moved over the ground
/// in plain view (frozen), or its pose still showed while it walked. Read only: nothing here changes the body, but for
/// the notes GuardGraph leaves when it had to put the graph back. Every dressed body is listed here, on every PC.
/// </summary>
public sealed partial class ZombieBody
{
    // Names of the top mixer's inputs, as Weigh fills them.
    private static readonly string[] TopNames =
    {
        "legs", "swing", "flinch", "stagger", "riseFD", "riseUP", "notice", "rouse", "crawl", "crawlSwing", "crawlFlinch",
        "toCrawl", "armSwing", "bite", "grab", "grabHold", "feedStart", "feed", "leap", "land", "getUp", "executed"
    };
    // A share of a weight below this isn't worth a word.
    private const float ProbeShown = .01f;

    private static readonly List<ZombieBody> probed = new List<ZombieBody>();
    private static readonly Queue<string> probeEvents = new Queue<string>();

    /// <summary>Every dressed zombie body on this PC.</summary>
    internal static IReadOnlyList<ZombieBody> Probed => probed;
    /// <summary>What happened to bodies since the probe last looked (a mood changed, a pose left, the graph put back).</summary>
    internal static Queue<string> ProbeEvents => probeEvents;

    // The bones as last looked at, how long it has moved in view with them standing still, how long its pose has shown
    // while it moved, and what the last look saw, to tell changes.
    private Quaternion probeHips, probeLeg, probeChest;
    private Vector3 probeHipsAt;
    private bool probeLooked, probeCueing;
    private float probeStill, probePoseMoving, probeTurned;
    private ZombieMood probeMood = (ZombieMood)255;
    private PoseState probePose;
    private Renderer probeSkin;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearProbed()
    {
        probed.Clear();
        probeEvents.Clear();
    }

    /// <summary>How often its graph was found stopped or off the game's clock and put back (GuardGraph).</summary>
    internal int ProbeHeals { get; private set; }

    /// <summary>A short name for the record: its network id (else its instance id), its kind and the look it wears.</summary>
    internal string ProbeName
    {
        get
        {
            ulong id = network != null && network.IsSpawned ? network.NetworkObjectId : (ulong)Mathf.Abs(GetInstanceID());
            if (kinds == null) TryGetComponent(out kinds);
            return "#" + id + " " + (kinds != null ? kinds.Kind.ToString() : "?") + " " + (Model != null ? Model.name : "-");
        }
    }

    private void ProbeEvent(string what)
    {
        if (probeEvents.Count < 1024) probeEvents.Enqueue(Time.time.ToString("0.00") + " " + ProbeName + " " + what);
    }

    // From GuardGraph: the graph had to be put back.
    private void ProbeHeal(string what)
    {
        ProbeHeals++;
        ProbeEvent("HEAL " + what);
    }

    // From Start once dressed: listed, with the clips it picked.
    private void ProbeDressed()
    {
        if (!probed.Contains(this)) probed.Add(this);
        ProbeEvent("dressed: walk " + NameOf(walk) + ", run " + NameOf(run) + ", idle " + NameOf(idle)
            + (pose != PoseState.None ? ", pose " + NameOf(poseLoop) + " (" + poseKind + ") getUp " + NameOf(getUp) : ", no pose")
            + ", animator cull " + (animator != null ? animator.cullingMode.ToString() : "-"));
    }

    // From Fall and OnDestroy: off the list.
    private void ProbeGone(string why)
    {
        if (!probed.Remove(this)) return;
        ProbeEvent("gone: " + why);
    }

    // The getting up's, the rouse's or the notice's length (s): whichever one-off clip plays.
    private float CueLength => cueKind == CueKind.GetUp ? getUpLength : cueKind == CueKind.Rouse ? rouseLength : noticeLength;

    // How much of the body shows its calm pose loop now: the legs' share, the idle's share of the legs, the pose's of the idle.
    private float PoseShowing => pose == PoseState.None || !top.IsValid() || !moves.IsValid() || !idles.IsValid() ? 0f
        : top.GetInputWeight(0) * moves.GetInputWeight(0) * idles.GetInputWeight(3);

    // On screen for any camera (the Animator stops posing a body no camera sees: CullUpdateTransforms).
    private bool ProbeSeen
    {
        get
        {
            if (probeSkin == null && Model != null) probeSkin = Model.GetComponentInChildren<SkinnedMeshRenderer>();
            return probeSkin != null && probeSkin.isVisible;
        }
    }

    /// <summary>
    /// Looks at the body now, <paramref name="since"/> seconds after the last look: changes of mood, pose and the one-off
    /// clips go into ProbeEvents, and it says whether its bones have stood still for a second while it moved over the
    /// ground in view (frozen), or its pose (or the first half of its getting up) has shown for half a second while it moved.
    /// </summary>
    internal void ProbeLook(float since, out bool frozen, out bool poseMoving)
    {
        frozen = poseMoving = false;
        var mood = mind != null ? mind.Mood : ZombieMood.Awake;
        if (mood != probeMood)
        {
            if ((int)probeMood != 255) ProbeEvent("mood " + probeMood + " -> " + mood);
            probeMood = mood;
        }
        if (pose != probePose)
        {
            ProbeEvent("pose " + probePose + " -> " + pose + " (" + poseKind + ")");
            probePose = pose;
        }
        if (cueing != probeCueing)
        {
            ProbeEvent(cueing ? "cue " + cueKind + " starts: " + CueLength.ToString("0.00") + " s at x" + cueRate.ToString("0.0")
                : "cue " + cueKind + " ends");
            probeCueing = cueing;
        }
        var hips = human != null ? human[(int)HumanBodyBones.Hips] : null;
        var leg = human != null ? human[(int)HumanBodyBones.LeftUpperLeg] : null;
        var chest = human != null ? human[(int)HumanBodyBones.Chest] : null;
        if (chest == null && human != null) chest = human[(int)HumanBodyBones.Spine];
        float shifted = 0f;
        probeTurned = 0f;
        bool hasBones = hips != null && leg != null && chest != null;
        if (hasBones)
        {
            if (probeLooked)
            {
                probeTurned = Quaternion.Angle(probeHips, hips.localRotation) + Quaternion.Angle(probeLeg, leg.localRotation)
                    + Quaternion.Angle(probeChest, chest.localRotation);
                shifted = (hips.localPosition - probeHipsAt).magnitude;
            }
            probeHips = hips.localRotation;
            probeLeg = leg.localRotation;
            probeChest = chest.localRotation;
            probeHipsAt = hips.localPosition;
        }
        bool walking = speed > .5f && !down && !Pinned && !IsExecuted && Model != null;
        if (probeLooked && hasBones && walking && ProbeSeen && probeTurned < .05f && shifted < .0005f) probeStill += since;
        else probeStill = 0f;
        probeLooked = hasBones;
        frozen = probeStill >= 1f;
        bool poseShows = PoseShowing > .05f
            || (cueing && cueKind == CueKind.GetUp && cueWeight > .5f && cueTime < CueLength * .5f);
        probePoseMoving = walking && poseShows ? probePoseMoving + since : 0f;
        poseMoving = probePoseMoving >= .5f;
    }

    /// <summary>One line of everything that decides what this body shows now (the record, ZombieAnimProbe).</summary>
    internal void ProbeLine(StringBuilder line, Vector3 from)
    {
        var mood = mind != null ? mind.Mood : ZombieMood.Awake;
        line.Append(ProbeName).Append(" d=").Append(Vector3.Distance(from, transform.position).ToString("0.0"));
        line.Append(" | mood ").Append(mood).Append(" ready=").Append(mind != null && mind.Ready ? 1 : 0)
            .Append(" gettingUp=").Append(mind != null && mind.GettingUp ? 1 : 0);
        line.Append(" | pose ").Append(pose);
        if (pose != PoseState.None) line.Append('(').Append(poseKind).Append(')');
        line.Append(" shows=").Append(PoseShowing.ToString("0.00")).Append(" calm=").Append(calmShare.ToString("0.00"))
            .Append(" alert=").Append(alertShare.ToString("0.00"));
        if (cueing)
            line.Append(" | cue ").Append(cueKind).Append(' ').Append(cueTime.ToString("0.00")).Append('/').Append(CueLength.ToString("0.00"))
                .Append(" x").Append(cueRate.ToString("0.0")).Append(" w=").Append(cueWeight.ToString("0.00"));
        line.Append(" | speed ").Append(speed.ToString("0.00"));
        if (TryGetComponent(out NavMeshAgent agent) && agent.isActiveAndEnabled)
        {
            line.Append(" agent ").Append(agent.velocity.magnitude.ToString("0.00"));
            if (agent.isOnNavMesh && agent.isStopped) line.Append(" stopped");
        }
        line.Append(" | moves idle ").Append(Weight(moves, 0)).Append(" walk ").Append(Weight(moves, 1)).Append(" run ").Append(Weight(moves, 2));
        line.Append(" | idles ").Append(Weight(idles, 0)).Append(' ').Append(Weight(idles, 1)).Append(' ').Append(Weight(idles, 2))
            .Append(" pose ").Append(Weight(idles, 3));
        line.Append(" | top");
        if (top.IsValid())
            for (int i = 0; i < top.GetInputCount(); i++)
            {
                float weight = top.GetInputWeight(i);
                if (weight < ProbeShown) continue;
                line.Append(' ').Append(i < TopNames.Length ? TopNames[i] : i.ToString()).Append('=').Append(weight.ToString("0.00"));
                string clip = ClipName(top.GetInput(i));
                if (clip != null) line.Append('(').Append(clip).Append(')');
            }
        line.Append(" | clips walk ").Append(NameOf(walk)).Append(" run ").Append(NameOf(run));
        if (pose != PoseState.None) line.Append(" pose ").Append(NameOf(poseLoop)).Append(" getUp ").Append(NameOf(getUp));
        line.Append(" | graph ").Append(!graph.IsValid() ? "INVALID" : (graph.IsPlaying() ? "playing " : "STOPPED ") + graph.GetTimeUpdateMode())
            .Append(" heals=").Append(ProbeHeals);
        if (animator != null)
            line.Append(" | animator ").Append(animator.isActiveAndEnabled ? "on" : "OFF").Append(" cull=").Append(animator.cullingMode)
                .Append(" bound=").Append(animator.hasBoundPlayables ? 1 : 0);
        line.Append(" seen=").Append(ProbeSeen ? 1 : 0);
        line.Append(" | down=").Append(down ? 1 : 0).Append(" rise=").Append(riseWeight.ToString("0.00"))
            .Append(" exec=").Append(executedWeight.ToString("0.00")).Append(" hold=").Append(holdWeight.ToString("0.00"))
            .Append(" swing=").Append(attackWeight.ToString("0.00")).Append(" crawl=").Append(crawlShare.ToString("0.00"));
        line.Append(" | bones turned ").Append(probeTurned.ToString("0.00")).Append(" deg, still ").Append(probeStill.ToString("0.0")).Append(" s");
    }

    /// <summary>Three short lines over its head (/zanim): its mood and pose, what plays most, and whether its bones move.</summary>
    internal string ProbeLabel(bool frozen, bool poseMoving)
    {
        var mood = mind != null ? mind.Mood : ZombieMood.Awake;
        var label = new StringBuilder(160);
        label.Append(ProbeName).Append('\n').Append(mood).Append(mind != null && mind.GettingUp ? " (getting up)" : "")
            .Append("  pose ").Append(pose).Append(' ').Append(PoseShowing.ToString("0.00"))
            .Append("  speed ").Append(speed.ToString("0.0")).Append('\n');
        float most = 0f;
        int shown = -1;
        if (top.IsValid())
            for (int i = 0; i < top.GetInputCount(); i++)
                if (top.GetInputWeight(i) > most) { most = top.GetInputWeight(i); shown = i; }
        label.Append(shown >= 0 && shown < TopNames.Length ? TopNames[shown] : "-").Append(' ').Append(most.ToString("0.00"));
        if (shown == 0 && moves.IsValid())
            label.Append(" (idle ").Append(Weight(moves, 0)).Append(" walk ").Append(Weight(moves, 1)).Append(" run ").Append(Weight(moves, 2)).Append(')');
        label.Append("  graph ").Append(!graph.IsValid() ? "INVALID" : graph.IsPlaying() ? graph.GetTimeUpdateMode().ToString() : "STOPPED");
        label.Append("  bones ").Append(probeTurned.ToString("0.0")).Append(" deg");
        if (frozen) label.Append("  << FROZEN");
        if (poseMoving) label.Append("  << POSE+MOVE");
        return label.ToString();
    }

    private static string Weight(AnimationMixerPlayable mixer, int input)
        => mixer.IsValid() && input < mixer.GetInputCount() ? mixer.GetInputWeight(input).ToString("0.00") : "-";

    private static string NameOf(AnimationClipPlayable playable)
    {
        if (!playable.IsValid()) return "-";
        var clip = playable.GetAnimationClip();
        return clip != null ? clip.name : "-";
    }

    // The clip on this input, if a clip sits on it directly (the legs are a mixer).
    private static string ClipName(Playable input)
    {
        if (!input.IsValid() || !input.IsPlayableOfType<AnimationClipPlayable>()) return null;
        var clip = ((AnimationClipPlayable)input).GetAnimationClip();
        return clip != null ? clip.name : null;
    }
}
