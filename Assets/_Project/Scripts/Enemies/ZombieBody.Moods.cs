using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Stage 4C: the body shows what the zombie knows (ZombieMind.Mood, the host's on every PC). Standing it idles calm,
/// alert or awake; searching it walks a slower walk; noticing something it starts (notice) and woken from calm it rouses
/// (rouse) before it charges. Idle and walk are each a small mixer under the legs, so the feet keep pace as before.
/// Clips: ZombieLooks calmIdle, alertIdle, searchWalk, notice, rouse.
/// </summary>
public sealed partial class ZombieBody
{
    // How long a change of mood takes to show in the idle and walk (s), and a one-off clip's ease in and out (s).
    private const float MoodBlend = .3f, CueIn = .1f, CueOut = .2f;

    private ZombieMind mind;
    private AnimationMixerPlayable idles, walks;
    private AnimationClipPlayable calmIdle, alertIdle, searchWalk, notice, rouse;
    private float calmIdleLength, alertIdleLength, searchWalkLength, noticeLength, rouseLength, searchWalkSpeed;
    private float calmShare, alertShare, searchShare, calmIdleTime, alertIdleTime, cueTime, cueWeight;
    private bool cueing, cueRouse;
    private ZombieMood shownMood = ZombieMood.Awake;

    /// <summary>The walking pace and step length now: the awake walk's, or the search walk's while searching.</summary>
    private float StepSpeed => Mathf.Lerp(walkSpeed, searchWalkSpeed, searchShare);
    private float StepLength => Mathf.Lerp(walkLength, searchWalkLength, searchShare);

    // Idle and walk under the legs (moves inputs 0 and 1), and the two one-off clips over everything (top 6 and 7).
    private void BuildMoods(ZombieLooks looks, AnimationClip rest)
    {
        idles = AnimationMixerPlayable.Create(graph, 3);
        idle = Clip(rest, idles, 0, out idleLength);
        calmIdle = Clip(Or(looks.calmIdle, rest), idles, 1, out calmIdleLength);
        alertIdle = Clip(Or(looks.alertIdle, Or(looks.calmIdle, rest)), idles, 2, out alertIdleLength);
        graph.Connect(idles, 0, moves, 0);
        walks = AnimationMixerPlayable.Create(graph, 2);
        walk = Clip(Or(looks.walk, rest), walks, 0, out walkLength);
        searchWalk = Clip(Or(looks.searchWalk, Or(looks.walk, rest)), walks, 1, out searchWalkLength);
        graph.Connect(walks, 0, moves, 1);
        notice = Clip(Or(looks.notice, rest), top, 6, out noticeLength);
        rouse = Clip(Or(looks.rouse, rest), top, 7, out rouseLength);
        idles.SetInputWeight(0, 1f);
        walks.SetInputWeight(0, 1f);
    }

    private void Moods(float dt)
    {
        var mood = mind != null ? mind.Mood : ZombieMood.Awake;
        if (mood != shownMood)
        {
            if (mood == ZombieMood.Rousing || mood == ZombieMood.Rising) Cue(true);
            else if (mood == ZombieMood.Alert && shownMood == ZombieMood.Calm) Cue(false);
            shownMood = mood;
        }
        float step = dt / MoodBlend;
        bool looking = mood == ZombieMood.Alert || mood == ZombieMood.Searching || mood == ZombieMood.Rousing
            || mood == ZombieMood.Rising;
        calmShare = Mathf.MoveTowards(calmShare, mood == ZombieMood.Calm ? 1f : 0f, step);
        alertShare = Mathf.MoveTowards(alertShare, looking ? 1f : 0f, step);
        searchShare = Mathf.MoveTowards(searchShare, mood == ZombieMood.Searching ? 1f : 0f, step);
        float calm = calmShare * (1f - alertShare);
        idles.SetInputWeight(0, Mathf.Max(0f, 1f - alertShare - calm));
        idles.SetInputWeight(1, calm);
        idles.SetInputWeight(2, alertShare);
        calmIdleTime = Mathf.Repeat(calmIdleTime + dt, calmIdleLength);
        alertIdleTime = Mathf.Repeat(alertIdleTime + dt, alertIdleLength);
        calmIdle.SetTime(calmIdleTime);
        alertIdle.SetTime(alertIdleTime);
        walks.SetInputWeight(0, 1f - searchShare);
        walks.SetInputWeight(1, searchShare);

        if (!cueing) return;
        cueTime += dt;
        float length = cueRouse ? rouseLength : noticeLength;
        bool over = cueTime >= length - CueOut;
        cueWeight = Mathf.MoveTowards(cueWeight, over ? 0f : 1f, dt / (over ? CueOut : CueIn));
        (cueRouse ? rouse : notice).SetTime(Mathf.Min(cueTime, length));
        if (over && cueWeight <= 0f) cueing = false;
    }

    private void Cue(bool roused)
    {
        cueing = true;
        cueRouse = roused;
        cueTime = 0f;
    }
}
