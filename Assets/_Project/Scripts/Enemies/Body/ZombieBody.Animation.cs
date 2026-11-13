using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// ZombieBody: Animation. One part of the class; the rest is in ZombieBody.cs and the files beside it.
public sealed partial class ZombieBody
{
    private void BuildGraph(ZombieLooks looks)
    {
        var rest = Or(looks.idle, looks.walk);
        if (rest == null) return;
        // The same on every PC in a room (the network id), so everyone sees this zombie walk and swing alike.
        clipSeed = network != null && network.IsSpawned
            ? unchecked((uint)network.NetworkObjectId * 2654435761u ^ (uint)(network.NetworkObjectId >> 32))
            : (uint)Random.Range(int.MinValue, int.MaxValue);
        graph = PlayableGraph.Create(name + " (Zombie)");
        // The Animator poses it as usual, but every clip stands still (speed 0) and its time is set here each frame,
        // so slow motion, the windup and the feet follow the game rather than the clock.
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Body", animator);
        // Idle and walk (inputs 0 and 1) are mood mixers (ZombieBody.Moods.cs); run is plain.
        moves = AnimationMixerPlayable.Create(graph, 3);
        runClip = Pick(looks.run, looks.runs, RunSlot);
        run = Clip(Or(runClip, Or(looks.walk, rest)), moves, 2, out runLength);
        top = AnimationMixerPlayable.Create(graph, TopInputs);
        graph.Connect(moves, 0, top, 0);
        // A kind with a swing of its own in EliteLooks swings it (the reaper's axe chop); the rest pick one of the pack's.
        attackClip = Or(TryGetComponent(out ZombieType ofKind) ? EliteLooks.For(ofKind.Kind).swing : null,
            Or(Pick(looks.attack, looks.attacks, AttackSlot), rest));
        attack = Clip(attackClip, top, 1, out float attackLength);
        var flinchClip = Or(Pick(looks.flinch, looks.flinches, FlinchSlot), rest);
        flinch = Clip(flinchClip, top, 2, out flinchLength);
        stagger = Clip(Or(Pick(looks.stagger, looks.staggers, StaggerSlot), flinchClip), top, 3, out staggerLength);
        // Without a getting-up clip it eases from the doll straight into standing.
        riseDown = Clip(Or(looks.riseFaceDown, rest), top, 4, out riseDownLength);
        riseUp = Clip(Or(looks.riseFaceUp, Or(looks.riseFaceDown, rest)), top, 5, out riseUpLength);
        BuildMoods(looks, rest);
        BuildCrawl(looks, rest);
        BuildBite(looks, rest);
        BuildLeap(looks, rest);
        output.SetSourcePlayable(top);
        // A bigger body's stride covers more ground (a brute).
        walkSpeed = RootSpeed(walkClip, WalkFallback) * grown;
        runSpeed = Mathf.Max(walkSpeed + .5f, RootSpeed(runClip, RunFallback) * grown);
        searchWalkSpeed = Mathf.Min(walkSpeed, RootSpeed(searchWalkClip, walkSpeed / grown) * grown);
        attackHit = HitTime(attackClip, attackLength);
        // The pack's swings don't mark their strike: it is measured on this body at the first swing (Act).
        attackMeasured = HasHitEvent(attackClip);
        // Each starts somewhere else in its loops, so zombies spawned together don't step and sway in time.
        phase = ZombieLooks.Fraction(clipSeed, PhaseSlot);
        idleTime = phase * idleLength;
        graph.Play();
        riseDownShape = RiseShape(looks.riseFaceDown, 4);
        riseUpShape = RiseShape(Or(looks.riseFaceUp, looks.riseFaceDown), 5);
        riseCrawlShape = RiseShape(looks.toCrawl, 11);
        Weigh();
    }

    private static AnimationClip Or(AnimationClip clip, AnimationClip fallback) => clip != null ? clip : fallback;

    /// <summary>This zombie's clip for a slot: the slot's own or one of its list's (ZombieLooks.Pick), null if neither.</summary>
    private AnimationClip Pick(AnimationClip main, AnimationClip[] more, uint slot) => ZombieLooks.Pick(main, more, clipSeed, slot);

    private static bool HasHitEvent(AnimationClip clip)
    {
        if (clip != null)
            foreach (var cue in clip.events)
                if (cue.functionName != null && cue.functionName.EndsWith("AttackHit")) return true;
        return false;
    }

    private AnimationClipPlayable Clip(AnimationClip clip, AnimationMixerPlayable mixer, int input, out float length)
    {
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(true);
        playable.SetSpeed(0d);
        graph.Connect(playable, 0, mixer, input);
        length = Mathf.Max(.05f, clip.length);
        return playable;
    }

    /// <summary>How fast the clip's own root travels (m/s): the pace at which its feet don't slide.</summary>
    private float RootSpeed(AnimationClip clip, float fallback)
    {
        if (clip == null) return fallback;
        Vector3 average = clip.averageSpeed;
        float pace = new Vector2(average.x, average.z).magnitude * animator.humanScale;
        return pace > .3f && pace < 9f ? pace : fallback;
    }

    /// <summary>The strike frame: the clip's own AnimationEvent_AttackHit, else a third of the way in.</summary>
    private static float HitTime(AnimationClip clip, float length)
    {
        if (clip != null)
            foreach (var cue in clip.events)
                if (cue.functionName != null && cue.functionName.EndsWith("AttackHit")) return Mathf.Clamp(cue.time, .05f, length);
        return length * .35f;
    }

    private void OnInterrupted()
    {
        flinching = true;
        flinchTime = 0f;
    }

    private void Update()
    {
        if (!graph.IsValid()) return;
        float real = Time.deltaTime;
        if (real <= 0f) return;
        float pace = network != null ? network.Pace : 1f;
        float dt = real * pace;
        WatchDown();
        SettleArms();
        Moods(dt);
        Move(real, pace, dt);
        Act(dt);
        ArmsTick();
        LeapTick(dt);
        EyesTick();
        CrawlTick(dt);
        TickRise(dt);
        // A paired execution's clip at the pair's own clock (ZombieBody.Executed).
        ExecutedTick(real);
        Weigh();
        GuardGraph();
        // From the frame after dressing, once the pack's spare meshes are gone, so a doll's copy of the body is light.
        // Half a second apart for each zombie (it looked through the dolls every frame for every zombie): dolls are made
        // one a frame at most anyway.
        if (Time.frameCount > dressedFrame && (stockLeft -= real) <= 0f)
        {
            stockLeft = .5f;
            ZombieCorpse.Stock(animator);
        }
    }

    private float stockLeft;

    /// <summary>
    /// The graph always runs on the game's clock, and the Animator poses the body from it every frame it is seen. 0.8.93
    /// (ZombieLod animEvery) put a far zombie on screen on a manual clock, posed every third frame by Evaluate here, and
    /// back on the game's clock once near or busy. That froze them for good: Unity schedules a graph only when it is
    /// played, and one taken off Manual while playing is not scheduled again, though IsPlaying() still says true (Unity
    /// UUM-32824, closed Won't Fix: stop it before the clock changes, then play it). So every zombie first seen on screen
    /// beyond 30 m kept its last pose from the moment it came within 30 m or did anything at all (woke, got up, noticed,
    /// swung, flinched): calm ones came on sitting or lying, the rest slid in on one frame of their walk, and only far off
    /// did they move. Those are the user's reports from 2026-11-12 ("앉은 채, 누운 채로 와 ... 모션이 굳은 채로", "부동
    /// 자세로 나한테 와") and 11-13 ("좀비 애니메이션이 여전히 안 나와", all zombies: Chapter 1's are first seen from afar).
    /// The switch is gone (0.8.317). Should anything still stop the graph or take it off the game's clock, it is put back
    /// here at once, in the order Unity schedules it again, and the probe records it (HEAL in Logs/zombie-anim.txt).
    /// </summary>
    private void GuardGraph()
    {
        var clock = graph.GetTimeUpdateMode();
        if (clock != DirectorUpdateMode.GameTime)
        {
            // Stopped first: only Play schedules a graph, and one switched off Manual while playing stays unscheduled
            // (UUM-32824). Every clip here stands still and has its time set each frame, so stopping loses nothing.
            graph.Stop();
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            graph.Play();
            ProbeHeal("clock was " + clock);
        }
        else if (!graph.IsPlaying())
        {
            graph.Play();
            ProbeHeal("graph was stopped");
        }
    }

    /// <summary>Idle, walk and run by the body's real speed, the walk and run on one shared step so they blend.</summary>
    private void Move(float real, float pace, float dt)
    {
        Vector3 step = transform.position - lastPosition;
        lastPosition = transform.position;
        // In slow motion it covers less ground; the legs show the same gait, slowed (dt), not a walk.
        float measured = new Vector2(step.x, step.z).magnitude / real / Mathf.Max(.05f, pace);
        if (measured > 20f) measured = 0f; // a placement (spawn, getting up), not a stride
        // What a corpse carries on with if it dies now.
        moving = measured > 0f ? step / real : Vector3.zero;
        speed = Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-SpeedSharpness * real));
        // Searching, the slower search walk sets the pace.
        float stepSpeed = StepSpeed, walking, running;
        // Only a kind that runs (ZombieKinds runs: a runner, the big elites) breaks into its run clip; the rest hurry in
        // their own walk, played faster up to hurryRate (user, 2026-11-11: "쉠블러는 뛰는 모션 안 했으면 좋겠어 러너랑
        // 헷갈리니까"). Eased, so a kind the host names late doesn't snap from one gait to the other.
        if (kinds == null) TryGetComponent(out kinds);
        float gait = kinds == null || kinds.RunsWhenFast ? 1f : 0f;
        runShare = gaitKnown ? Mathf.MoveTowards(runShare, gait, real * 2f) : gait;
        gaitKnown = true;
        if (speed <= stepSpeed) { walking = Mathf.SmoothStep(0f, 1f, speed / stepSpeed); running = 0f; }
        else { running = runShare * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(stepSpeed, runSpeed, speed)); walking = 1f - running; }
        moves.SetInputWeight(0, 1f - walking - running);
        moves.SetInputWeight(1, walking);
        moves.SetInputWeight(2, running);
        float rate = Mathf.Lerp(speed > stepSpeed ? Mathf.Min(speed / Mathf.Max(.1f, stepSpeed), Mathf.Max(1f, hurryRate)) : 1f,
            speed > runSpeed ? Mathf.Min(speed / runSpeed, MaxRate) : 1f, runShare);
        phase = Mathf.Repeat(phase + dt * rate / Mathf.Lerp(StepLength, runLength, running), 1f);
        walk.SetTime(phase * walkLength);
        searchWalk.SetTime(phase * searchWalkLength);
        run.SetTime(phase * runLength);
        idleTime = Mathf.Repeat(idleTime + dt, idleLength);
        idle.SetTime(idleTime);
    }

    private void Act(float dt)
    {
        // The swing follows the windup: its strike frame lands when the windup hits, however long the windup is.
        swingPreparing = combat != null && combat.IsPreparingAttack;
        // A swing whose clip doesn't mark its strike: the moment its hand reaches furthest is found once, standing up,
        // as the first windup begins (shared by every zombie with that clip).
        if (swingPreparing && !attackMeasured && !down && animator != null && animator.enabled)
        {
            attackMeasured = true;
            attackHit = Hand(attackClip).hit;
        }
        if (swingPreparing)
        {
            swingWindup = combat.Windup;
            swingElapsed = Mathf.Min(combat.WindupElapsed, swingWindup);
            sinceStrike = 0f;
            striking = true;
            attackWeight = Mathf.MoveTowards(attackWeight, 1f, dt / AttackIn);
        }
        else if (striking)
        {
            // Follow through a moment, then hand back to the legs.
            sinceStrike += dt;
            if (sinceStrike > AttackHold) attackWeight = Mathf.MoveTowards(attackWeight, 0f, dt / AttackOut);
            if (attackWeight <= 0f) striking = false;
        }
        attack.SetTime(SwingTime(attackHit));

        if (flinching)
        {
            flinchTime += dt;
            bool over = flinchTime >= flinchLength - FlinchOut;
            flinchWeight = Mathf.MoveTowards(flinchWeight, over ? 0f : 1f, dt / (over ? FlinchOut : FlinchIn));
            if (over && flinchWeight <= 0f) flinching = false;
        }
        flinch.SetTime(Mathf.Min(flinchTime, flinchLength));

        bool reeling = target != null && target.IsStaggered && !target.IsKnockedDown;
        if (reeling && staggerWeight <= 0f) staggerTime = 0f;
        if (reeling) staggerTime += dt;
        staggerWeight = Mathf.MoveTowards(staggerWeight, reeling ? 1f : 0f, dt / (reeling ? StaggerIn : StaggerOut));
        stagger.SetTime(Mathf.Min(staggerTime, staggerLength));
    }

    // Where in the swing clip it is: before the strike, its strike frame lands when the windup does; after, it follows through.
    private float SwingTime(float hitAt)
    {
        if (!swingPreparing) return hitAt + sinceStrike;
        return swingWindup <= hitAt ? hitAt - swingWindup + swingElapsed : hitAt * swingElapsed / Mathf.Max(.01f, swingWindup);
    }

    /// <summary>
    /// Getting up over reel over flinch over a mood's one-off clip (notice, rouse) over swing over the legs, always adding
    /// up to one (less would sag toward the rest pose). Crawling, the legs, swing and flinch are the crawl's own, and it
    /// neither reels nor shows its moods.
    /// </summary>
    private void Weigh()
    {
        // Holding a player (G&B-1), its hold covers all but getting up.
        float rise = Mathf.Clamp01(riseWeight), hold = holdWeight * (1f - rise), rest = 1f - rise - hold, crawl = crawlShare, stand = 1f - crawl;
        // A runner's leap and its landing (ZombieBody.Leap.cs) cover the rest of the standing body.
        float jumping = Mathf.Clamp01(leapWeight + landWeight) * stand * rest;
        rest -= jumping;
        float leapShare = leapWeight + landWeight > 0f ? leapWeight / (leapWeight + landWeight) : 1f;
        top.SetInputWeight(LeapInput, jumping * leapShare);
        top.SetInputWeight(LandInput, jumping * (1f - leapShare));
        float reel = staggerWeight * stand;
        float flinched = flinchWeight * (1f - reel);
        float cueShown = cueWeight * stand;
        float cue = cueShown * (1f - reel) * (1f - flinchWeight);
        float swing = attackWeight * (1f - reel) * (1f - flinchWeight) * (1f - cueShown);
        float legs = Mathf.Max(0f, 1f - reel - flinched - cue - swing);
        top.SetInputWeight(0, legs * stand * rest);
        top.SetInputWeight(8, legs * crawl * rest);
        // With an arm gone its swing is the one for the arm it kept (ZombieBody.Arms.cs, input 12); a bite is its own (13).
        float standSwing = swing * stand * rest;
        bool special = swingIsBite || swingIsGrab || swingIsFeed;
        top.SetInputWeight(1, armSwingOn || special ? 0f : standSwing);
        top.SetInputWeight(12, armSwingOn && !special ? standSwing : 0f);
        top.SetInputWeight(13, swingIsBite ? standSwing : 0f);
        float grabbing = swingIsGrab && !swingIsBite ? standSwing : 0f;
        float kneeling = swingIsFeed && !swingIsBite && !swingIsGrab ? standSwing : 0f;
        top.SetInputWeight(14, grabbing);
        top.SetInputWeight(16, kneeling);
        top.SetInputWeight(15, hold * (1f - feedShare));
        top.SetInputWeight(17, hold * feedShare);
        FaceGrabs(grabbing, hold * (1f - feedShare), kneeling, hold * feedShare);
        top.SetInputWeight(9, swing * crawl * rest);
        top.SetInputWeight(2, flinched * stand * rest);
        top.SetInputWeight(10, flinched * crawl * rest);
        top.SetInputWeight(3, reel * rest);
        WeighCue(cue * rest);
        top.SetInputWeight(4, !crawling && riseFaceDown ? rise : 0f);
        top.SetInputWeight(5, !crawling && !riseFaceDown ? rise : 0f);
        top.SetInputWeight(11, crawling ? rise : 0f);
        // A paired execution's clip over all of it (ZombieBody.Executed).
        WeighExecuted();
    }
}
