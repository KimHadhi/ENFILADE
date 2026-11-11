using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Stage 4C: the body shows what the zombie knows (ZombieMind.Mood, the host's on every PC). Standing it idles calm,
/// alert or awake; searching it walks a slower walk; noticing something it starts (notice) and woken from calm it rouses
/// (rouse) before it charges. Idle and walk are each a small mixer under the legs, so the feet keep pace as before.
/// Clips: ZombieLooks calmIdle, alertIdle, searchWalk, notice, rouse, each with its list of alternatives (one picked per
/// zombie, ZombieLooks.Pick). Some zombies placed calm lie low instead of standing (ZombieLooks.calmPoses: sitting,
/// eating, crouched; Tables/ZombieClips poseShare) and play the pose's getting up as they leave calm. They hold the pose
/// only from their first calm: a zombie calmed again after a search stands, as no clip sits it back down.
/// </summary>
public sealed partial class ZombieBody
{
    public const string ClipsTable = "ZombieClips", ClipsRow = "Default";
    // How long a change of mood takes to show in the idle and walk (s), and a one-off clip's ease in and out (s).
    private const float MoodBlend = .3f, CueIn = .1f, CueOut = .2f;
    // A zombie not calm this long after dressing (s) is a horde one: it never takes its pose.
    private const float PoseWait = 1.5f;
    // The getting up from a pose sits on its own top input, after the runner's leap and landing (18, 19).
    private const int GetUpInput = 20, TopInputs = 21;

    [SerializeField, Range(0f, 1f), Tooltip("Share of zombies placed calm that sit, eat or crouch (ZombieLooks calmPoses) instead of standing.")]
    private float poseShare = .5f;
    [SerializeField, Min(.2f), Tooltip("Woken straight out of such a pose, the longest its getting up may take (s); a longer clip plays faster.")]
    private float getUpRoused = 1f;

    private enum CueKind : byte { Notice, Rouse, GetUp }
    // A calm pose: None (this zombie has none), Pending (dressed, waiting for its first calm), Held, Done (got up).
    private enum PoseState : byte { None, Pending, Held, Done }

    private ZombieMind mind;
    private AnimationMixerPlayable idles, walks;
    private AnimationClipPlayable calmIdle, alertIdle, searchWalk, notice, rouse, poseLoop, getUp;
    private float calmIdleLength, alertIdleLength, searchWalkLength, noticeLength, rouseLength, searchWalkSpeed;
    private float poseLength, getUpLength, poseTime, poseClock, cueRate = 1f;
    private float calmShare, alertShare, searchShare, calmIdleTime, alertIdleTime, cueTime, cueWeight;
    private bool cueing;
    private CueKind cueKind;
    private PoseState pose;
    private ZombieMood shownMood = ZombieMood.Awake;

    /// <summary>The walking pace and step length now: the awake walk's, or the search walk's while searching.</summary>
    private float StepSpeed => Mathf.Lerp(walkSpeed, searchWalkSpeed, searchShare);
    private float StepLength => Mathf.Lerp(walkLength, searchWalkLength, searchShare);

    // Idle and walk under the legs (moves inputs 0 and 1), and the one-off clips over everything (top 6, 7 and 20).
    private void BuildMoods(ZombieLooks looks, AnimationClip rest)
    {
        idles = AnimationMixerPlayable.Create(graph, 4);
        idle = Clip(Or(Pick(looks.idle, looks.idles, IdleSlot), rest), idles, 0, out idleLength);
        var calmClip = Or(Pick(looks.calmIdle, looks.calmIdles, CalmSlot), rest);
        calmIdle = Clip(calmClip, idles, 1, out calmIdleLength);
        alertIdle = Clip(Or(Pick(looks.alertIdle, looks.alertIdles, AlertSlot), calmClip), idles, 2, out alertIdleLength);
        graph.Connect(idles, 0, moves, 0);
        walks = AnimationMixerPlayable.Create(graph, 2);
        walkClip = Or(Pick(looks.walk, looks.walks, WalkSlot), rest);
        walk = Clip(walkClip, walks, 0, out walkLength);
        searchWalkClip = Or(Pick(looks.searchWalk, looks.searchWalks, SearchSlot), walkClip);
        searchWalk = Clip(searchWalkClip, walks, 1, out searchWalkLength);
        graph.Connect(walks, 0, moves, 1);
        var noticeClip = Or(Pick(looks.notice, looks.notices, NoticeSlot), rest);
        notice = Clip(noticeClip, top, 6, out noticeLength);
        rouse = Clip(Or(Pick(looks.rouse, looks.rouses, RouseSlot), rest), top, 7, out rouseLength);
        if (ZombieLooks.PickPose(looks.calmPoses, clipSeed, PoseSlot, poseShare, out var chosen))
        {
            poseLoop = Clip(chosen.loop, idles, 3, out poseLength);
            getUp = Clip(Or(chosen.getUp, noticeClip), top, GetUpInput, out getUpLength);
            pose = PoseState.Pending;
            poseTime = ZombieLooks.Fraction(clipSeed, PoseSlot + 2u) * poseLength;
        }
        calmIdleTime = ZombieLooks.Fraction(clipSeed, CalmSlot) * calmIdleLength;
        alertIdleTime = ZombieLooks.Fraction(clipSeed, AlertSlot) * alertIdleLength;
        idles.SetInputWeight(0, 1f);
        walks.SetInputWeight(0, 1f);
    }

    private void Moods(float dt)
    {
        var mood = mind != null ? mind.Mood : ZombieMood.Awake;
        bool leavesPose = WatchPose(mood, dt);
        if (mood != shownMood)
        {
            bool roused = mood == ZombieMood.Rousing || mood == ZombieMood.Rising;
            // Out of a pose it gets up first, whatever woke it; knocked down out of it, the doll and its rise do instead.
            if (leavesPose) { if (!down) Cue(CueKind.GetUp, roused); }
            else if (roused) Cue(CueKind.Rouse, false);
            else if (mood == ZombieMood.Alert && shownMood == ZombieMood.Calm) Cue(CueKind.Notice, false);
            shownMood = mood;
        }
        float step = dt / MoodBlend;
        bool looking = mood == ZombieMood.Alert || mood == ZombieMood.Searching || mood == ZombieMood.Rousing
            || mood == ZombieMood.Rising;
        calmShare = Mathf.MoveTowards(calmShare, mood == ZombieMood.Calm ? 1f : 0f, step);
        alertShare = Mathf.MoveTowards(alertShare, looking ? 1f : 0f, step);
        searchShare = Mathf.MoveTowards(searchShare, mood == ZombieMood.Searching ? 1f : 0f, step);
        float calm = calmShare * (1f - alertShare);
        // Its calm share shows the pose until it has got up: the getting up covers the body meanwhile.
        bool posed = pose == PoseState.Pending || pose == PoseState.Held || (cueing && cueKind == CueKind.GetUp);
        idles.SetInputWeight(0, Mathf.Max(0f, 1f - alertShare - calm));
        idles.SetInputWeight(1, posed ? 0f : calm);
        idles.SetInputWeight(2, alertShare);
        if (pose != PoseState.None)
        {
            idles.SetInputWeight(3, posed ? calm : 0f);
            poseTime = Mathf.Repeat(poseTime + dt, poseLength);
            poseLoop.SetTime(poseTime);
        }
        calmIdleTime = Mathf.Repeat(calmIdleTime + dt, calmIdleLength);
        alertIdleTime = Mathf.Repeat(alertIdleTime + dt, alertIdleLength);
        calmIdle.SetTime(calmIdleTime);
        alertIdle.SetTime(alertIdleTime);
        walks.SetInputWeight(0, 1f - searchShare);
        walks.SetInputWeight(1, searchShare);

        if (!cueing) return;
        cueTime += dt * cueRate;
        float length = cueKind == CueKind.GetUp ? getUpLength : cueKind == CueKind.Rouse ? rouseLength : noticeLength;
        bool over = cueTime >= length - CueOut * cueRate;
        cueWeight = Mathf.MoveTowards(cueWeight, over ? 0f : 1f, dt / (over ? CueOut : CueIn));
        var playing = cueKind == CueKind.GetUp ? getUp : cueKind == CueKind.Rouse ? rouse : notice;
        playing.SetTime(Mathf.Min(cueTime, length));
        if (over && cueWeight <= 0f) cueing = false;
    }

    /// <summary>
    /// Takes the pose at its first calm and keeps it while calm, the host's ZombieMind holding still meanwhile; returns
    /// true the moment it leaves calm out of the pose (it then gets up).
    /// </summary>
    private bool WatchPose(ZombieMood mood, float dt)
    {
        bool leaves = false;
        if (pose == PoseState.Pending)
        {
            poseClock += dt;
            if (mood == ZombieMood.Calm) pose = PoseState.Held;
            else if (poseClock > PoseWait) pose = PoseState.Done;
        }
        else if (pose == PoseState.Held && mood != ZombieMood.Calm)
        {
            pose = PoseState.Done;
            leaves = true;
        }
        if (mind != null) mind.HoldsPose = pose == PoseState.Held;
        return leaves;
    }

    private void Cue(CueKind kind, bool hurried)
    {
        cueing = true;
        cueKind = kind;
        cueTime = 0f;
        // Roused straight out of a pose it is up within getUpRoused seconds, so it isn't still sitting as it charges.
        cueRate = kind == CueKind.GetUp && hurried ? Mathf.Max(1f, getUpLength / Mathf.Max(.2f, getUpRoused)) : 1f;
    }

    // The one-off clip's share of the body (ZombieBody.Weigh): notice, rouse or getting up out of a pose.
    private void WeighCue(float cue)
    {
        top.SetInputWeight(6, cueKind == CueKind.Notice ? cue : 0f);
        top.SetInputWeight(7, cueKind == CueKind.Rouse ? cue : 0f);
        top.SetInputWeight(GetUpInput, cueKind == CueKind.GetUp && getUp.IsValid() ? cue : 0f);
    }
}
