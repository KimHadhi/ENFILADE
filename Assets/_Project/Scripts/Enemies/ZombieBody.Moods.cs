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
/// only from their first calm: a zombie calmed again after a search stands, as no clip sits it back down. A pose is busy
/// with something (CalmPose kind, the 2026-11-11 memo's item 47): an eater kneels or lies over a body laid in front of it
/// (ZombieMeal) and a dozer nods off, and while they hold it ZombieMind sees and hears less (PoseKind).
/// A hard rule over all of it (user, 2026-11-12, twice: "calm한 좀비한테 들켰을 때 좀비가 여전히 원래 포즈 그대로 와. 누워서
/// 오거나 앉은 채로 부동 자세로 나한테 와"): the pose's share of the body is worked out here every frame from the weights
/// themselves, not from which mood woke it, and while any of it shows the zombie goes nowhere (ZombieMind.GettingUp, which
/// every mover waits on). If it moves all the same (pushed, carried, or on another PC following its host's zombie that
/// stood a moment sooner), the pose gives way within yieldTime instead of riding along.
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
    // A share of the body below this no longer counts as its pose showing.
    private const float PoseGone = .02f;

    [SerializeField, Range(0f, 1f), Tooltip("Share of zombies placed calm that sit, eat or crouch (ZombieLooks calmPoses) instead of standing.")]
    private float poseShare = .5f;
    [SerializeField, Min(.2f), Tooltip("Woken straight out of such a pose, the longest its getting up may take (s); a longer clip plays faster.")]
    private float getUpRoused = 1f;
    [SerializeField, Min(.05f), Tooltip("A pose with no getting-up clip of its own eases into standing this fast, nothing on top (s).")]
    private float quickStand = .35f;
    [SerializeField, Min(.1f), Tooltip("Moving faster than this (m/s) while any of its pose shows (pushed, or on another PC following " +
        "its host's zombie that stood sooner), the pose gives way at once rather than slide along.")]
    private float yieldSpeed = .75f;
    [SerializeField, Min(.05f), Tooltip("Giving way, what is left of its getting up is played within this (s).")]
    private float yieldTime = .25f;

    private enum CueKind : byte { Notice, Rouse, GetUp }
    // A calm pose: None (this zombie has none), Pending (dressed, waiting for its first calm), Held, Done (got up).
    private enum PoseState : byte { None, Pending, Held, Done }

    private ZombieMind mind;
    private AnimationMixerPlayable idles, walks;
    private AnimationClipPlayable calmIdle, alertIdle, searchWalk, notice, rouse, poseLoop, getUp;
    private float calmIdleLength, alertIdleLength, searchWalkLength, noticeLength, rouseLength, searchWalkSpeed;
    private float poseLength, getUpLength, poseTime, poseClock, cueRate = 1f;
    private float calmShare, alertShare, searchShare, calmIdleTime, alertIdleTime, cueTime, cueWeight;
    private bool cueing, mealLaid;
    private CueKind cueKind;
    private PoseState pose;
    // What its pose is busy with (eating, dozing) and how far ahead the body it eats lies (ZombieLooks.CalmPose).
    private CalmPoseKind poseKind;
    private float mealAhead;
    private ZombieMood shownMood = ZombieMood.Awake;

    /// <summary>
    /// Set right after it is made, before it dresses (ConsolePoseTest, /poses): it takes ZombieLooks.calmPoses[index]
    /// rather than its seeded pick (-1). On this PC only: other PCs pick by the network id as ever.
    /// </summary>
    internal int ForcedPose { get; set; } = -1;

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
        if (ChoosePose(looks, out var chosen))
        {
            poseLoop = Clip(chosen.loop, idles, 3, out poseLength);
            // Without its own getting up it eases into standing (quickStand) with nothing on top: the notice clip used to
            // stand in, which snapped a lying body upright in a tenth of a second (a modder's CalmPose X with no X_getup).
            if (chosen.getUp != null) getUp = Clip(chosen.getUp, top, GetUpInput, out getUpLength);
            else getUpLength = quickStand;
            pose = PoseState.Pending;
            poseTime = ZombieLooks.Fraction(clipSeed, PoseSlot + 2u) * poseLength;
            poseKind = chosen.kind;
            mealAhead = chosen.mealAhead;
        }
        calmIdleTime = ZombieLooks.Fraction(clipSeed, CalmSlot) * calmIdleLength;
        alertIdleTime = ZombieLooks.Fraction(clipSeed, AlertSlot) * alertIdleLength;
        idles.SetInputWeight(0, 1f);
        walks.SetInputWeight(0, 1f);
    }

    // Its pose: the one forced on it (ForcedPose), else its seeded pick (poseShare of calm zombies lie low).
    private bool ChoosePose(ZombieLooks looks, out ZombieLooks.CalmPose chosen)
    {
        var poses = looks.calmPoses;
        if (ForcedPose >= 0 && poses != null && ForcedPose < poses.Length && poses[ForcedPose].loop != null)
        {
            chosen = poses[ForcedPose];
            return true;
        }
        return ZombieLooks.PickPose(poses, clipSeed, PoseSlot, poseShare, out chosen);
    }

    private void Moods(float dt)
    {
        var mood = mind != null ? mind.Mood : ZombieMood.Awake;
        bool leavesPose = WatchPose(mood, dt);
        if (mood != shownMood)
        {
            bool roused = mood == ZombieMood.Rousing || mood == ZombieMood.Rising;
            bool gettingUp = cueing && cueKind == CueKind.GetUp;
            // Out of a pose it gets up first, whatever woke it; knocked down out of it, the doll and its rise do instead.
            // Only an alert (it stands looking) takes the whole clip: woken to chase, sent to search or shot awake, it is
            // up within getUpRoused, and it goes nowhere until it is (ZombieMind.GettingUp).
            if (leavesPose) { if (!down) Cue(CueKind.GetUp, mood != ZombieMood.Alert); }
            // Still getting up, it finishes that, quicker now, rather than snapping to its feet for a rouse.
            else if (gettingUp) { if (mood != ZombieMood.Alert) cueRate = Mathf.Max(cueRate, HurriedGetUp); }
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
        bool holding = pose == PoseState.Pending || pose == PoseState.Held;
        // Moving while its pose shows, it doesn't wait for a mood to say so: the pose gives way (speed is last frame's).
        if (speed > yieldSpeed && !Pinned && (holding ? calm > PoseGone : cueing && cueKind == CueKind.GetUp))
        {
            Yield();
            holding = false;
        }
        bool inGetUp = cueing && cueKind == CueKind.GetUp;
        // The pose's share of the legs: the calm share while it holds the pose, falling to nothing as it gets up (the
        // getting up covers the body meanwhile) and none once it stands, whatever its mood; the calm idle has the rest.
        float progress = inGetUp ? Mathf.Clamp01(cueTime / Mathf.Max(.01f, getUpLength)) : 1f;
        float poseWeight = holding ? calm : inGetUp ? calm * (1f - progress) : 0f;
        idles.SetInputWeight(0, Mathf.Max(0f, 1f - alertShare - calm));
        idles.SetInputWeight(1, Mathf.Max(0f, calm - poseWeight));
        idles.SetInputWeight(2, alertShare);
        if (pose != PoseState.None)
        {
            idles.SetInputWeight(3, poseWeight);
            poseTime = Mathf.Repeat(poseTime + dt, poseLength);
            poseLoop.SetTime(poseTime);
        }
        calmIdleTime = Mathf.Repeat(calmIdleTime + dt, calmIdleLength);
        alertIdleTime = Mathf.Repeat(alertIdleTime + dt, alertIdleLength);
        calmIdle.SetTime(calmIdleTime);
        alertIdle.SetTime(alertIdleTime);
        walks.SetInputWeight(0, 1f - searchShare);
        walks.SetInputWeight(1, searchShare);

        bool posed = poseWeight > PoseGone;
        if (!cueing) { HoldStill(posed); return; }
        cueTime += dt * cueRate;
        float length = cueKind == CueKind.GetUp ? getUpLength : cueKind == CueKind.Rouse ? rouseLength : noticeLength;
        var playing = cueKind == CueKind.GetUp ? getUp : cueKind == CueKind.Rouse ? rouse : notice;
        // A pose with no getting up of its own (quickStand) has no clip here: nothing eases over the body, and it is over
        // when its time is.
        bool shows = playing.IsValid();
        bool over = shows ? cueTime >= length - CueOut * cueRate : cueTime >= length;
        cueWeight = Mathf.MoveTowards(cueWeight, over || !shows ? 0f : 1f, dt / (over || !shows ? CueOut : CueIn));
        if (shows) playing.SetTime(Mathf.Min(cueTime, length));
        if (over && cueWeight <= 0f) cueing = false;
        // It goes nowhere while any of its pose shows, nor till its getting up is all but done (the legs take over as the
        // clip eases out). The getting up runs on the body's own clock, never slower than the game's, so it always ends:
        // nothing holds it there for good. (A cap on wall-clock time from the get-up's start used to let a zombie that had
        // stared a while go the moment it was hurried.)
        HoldStill(posed || (cueKind == CueKind.GetUp && !over));
    }

    // The getting up's pace when it hurries (woken to chase, sent to search, shot awake): up within getUpRoused.
    private float HurriedGetUp => Mathf.Max(1f, getUpLength / Mathf.Max(.2f, getUpRoused));

    // Down (its doll moves it), held (a takedown, a grab) or lying: what moves it then isn't a walk of its own.
    private bool Pinned => down || (target != null && (target.IsHeld || target.IsLying));

    // Its pose gives way within yieldTime: the getting up begun if it hadn't, and hurried through what is left of it.
    private void Yield()
    {
        if (pose == PoseState.Pending || pose == PoseState.Held) pose = PoseState.Done;
        if (!cueing || cueKind != CueKind.GetUp) Cue(CueKind.GetUp, false);
        cueRate = Mathf.Max(cueRate, (getUpLength - cueTime) / yieldTime);
    }

    // Tells the mind (the host's moves the body) whether its pose still shows or it is still getting up; set on every
    // PC alike, read where it thinks.
    private void HoldStill(bool on)
    {
        if (mind != null && mind.GettingUp != on) mind.GettingUp = on;
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
            if (mood == ZombieMood.Calm) { pose = PoseState.Held; LayMeal(); }
            else if (poseClock > PoseWait) pose = PoseState.Done;
        }
        else if (pose == PoseState.Held && mood != ZombieMood.Calm)
        {
            pose = PoseState.Done;
            leaves = true;
        }
        if (mind != null)
        {
            mind.HoldsPose = pose == PoseState.Held;
            // Busy eating or nodding off, it sees and hears less (ZombieMind), on every PC alike for the stealth view.
            mind.PoseKind = pose == PoseState.Held ? poseKind : CalmPoseKind.Rest;
        }
        return leaves;
    }

    // An eater lies over a body: one of the pack's characters baked lying still in front of it, the same on every PC
    // (picked from its network id, laid by its feet and the way it faces as it first takes the pose). It stays when the
    // eater gets up: it is a body (ZombieMeal keeps the newest few).
    private void LayMeal()
    {
        if (poseKind != CalmPoseKind.Eat || mealLaid) return;
        mealLaid = true;
        Vector3 feet = transform.position + Vector3.up * feetHeight;
        Vector3 ahead = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        float yaw = Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg + 90f + (ZombieLooks.Fraction(clipSeed, PoseSlot + 3u) - .5f) * 40f;
        ZombieMeal.Lay(ZombieLooks.Instance, clipSeed, feet + ahead * Mathf.Max(.2f, mealAhead), yaw);
    }

    private void Cue(CueKind kind, bool hurried)
    {
        cueing = true;
        cueKind = kind;
        cueTime = 0f;
        // Roused straight out of a pose it is up within getUpRoused seconds, so it isn't still sitting as it charges.
        cueRate = kind == CueKind.GetUp && hurried ? HurriedGetUp : 1f;
    }

    // The one-off clip's share of the body (ZombieBody.Weigh): notice, rouse or getting up out of a pose.
    private void WeighCue(float cue)
    {
        top.SetInputWeight(6, cueKind == CueKind.Notice ? cue : 0f);
        top.SetInputWeight(7, cueKind == CueKind.Rouse ? cue : 0f);
        top.SetInputWeight(GetUpInput, cueKind == CueKind.GetUp && getUp.IsValid() ? cue : 0f);
    }
}
