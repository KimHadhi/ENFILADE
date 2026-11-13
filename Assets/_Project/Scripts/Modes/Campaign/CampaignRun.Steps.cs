using UnityEngine;

// CampaignRun: walking the chapter's steps, who is where, moving on, checkpoints and the music. One part of the class; the rest is in CampaignRun.cs and the files beside it.
public sealed partial class CampaignRun
{
    // Who counts: the players of this room (this PC's and the others'), not test dummies, and not the dead.
    private void GatherPlayers()
    {
        players.Clear();
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.IsInitialized && !someone.IsDead && (someone.IsLocal || someone.IsNetworkDriven))
                players.Add(someone);
    }

    // Every PC sees its own wipe (PlayerLife); the host sends the run back to after the last rest stop at once, so
    // everyone comes back there when the wipe ends.
    private void WatchForWipe()
    {
        var mine = PlayerRoster.Local;
        bool wiped = mine != null && mine.TryGetComponent(out PlayerLife life) && life.IsWiped;
        if (wiped && !wasWiped && IsHost) BackToCheckpoint();
        wasWiped = wiped;
    }

    private void Run(float dt)
    {
        if (finished)
        {
            if (!movedOn && Time.time - finishedAt >= outroSeconds) MoveOn();
            return;
        }
        var now = Current;
        if (now == null) return;
        bool scripted = now.Kind == CampaignStepKind.Hold || now.Kind == CampaignStepKind.Escape;
        // A hold or the escape is a crescendo whatever was noticed. Quiet, nobody is sent, and the pacing starts afresh
        // when it ends, as after a rest stop.
        if (scripted) quiet = calmDue = lull = false;
        Alarm();
        // The chase dies down: found once, but no zombie has had anyone in sight for lullSeconds (they shut themselves in a
        // rest stop, or slipped away), so the director stops and the zombies go looking, then calm (ZombieMind.LoseAll).
        // Found was found for good before (user, 2026-11-11 playtest).
        if (IsHost && !quiet && !scripted && lullSeconds > 0f && Time.time - noticedAt >= lullSeconds
            && Time.time - ZombieMind.LastNoticed >= lullSeconds)
        {
            quiet = lull = true;
            ZombieMind.LoseAll();
        }
        if (calmDue && players.Count > 0) { calmDue = false; PlaceCalmAhead(); }
        pacer.Pace(dt, players, scripted, quiet || Resting(), now.Horde, now.Rate, out int most, out float rate);
        pacer.Spawned(horde.Tick(dt, most, rate, players, minSpawnDistance, maxSpawnDistance, seenMinDistance));
        switch (now.Kind)
        {
            case CampaignStepKind.Reach:
                if (AnyoneAt(now)) Advance();
                break;
            case CampaignStepKind.Checkpoint:
                if (EveryoneAt(now) && DoorsShut(now.Spot)) Advance();
                break;
            case CampaignStepKind.Gather:
                if (EveryoneAt(now)) Advance();
                break;
            case CampaignStepKind.Escape:
                if (CampaignMusic.TryFinale(now.Music, out float holdBefore, out float board))
                {
                    // The ride waits out the song and leaves as it ends, with whoever stands on it.
                    held = SongTime(now, dt, holdBefore);
                    if (held >= board) Depart(now);
                    else ShowHeld(now);
                }
                else if (EveryoneAt(now)) Advance();
                break;
            case CampaignStepKind.Hold:
                // A manned hold (chapter 4's tug) counts only while someone stands at its spot, and pauses otherwise.
                held = now.Manned ? held + (AnyoneAt(now) ? dt : 0f) : SongTime(now, dt, 0f);
                if (held >= TotalOf(now)) Advance();
                else ShowHeld(now);
                break;
            case CampaignStepKind.Collect:
                if (fuel.Poured >= Mathf.Max(1, now.Count)) Advance();
                break;
        }
        if (!finished && Current == now) SkipAhead();
    }

    // How long a hold lasts, or a finale's escape waits: under a finale's song, by the song (CampaignMusic.TryFinale);
    // otherwise the hold's own seconds, and no time at all for an escape (0).
    private float TotalOf(CampaignStep at)
    {
        bool finale = CampaignMusic.TryFinale(at.Music, out float hold, out float board);
        return at.Kind == CampaignStepKind.Escape ? (finale ? board : 0f) : finale ? hold : at.Seconds;
    }

    // How far into its time a step is. Under a finale's song that this PC is playing, the song's own clock says, less
    // what of the song came before the step: the song runs in real time, and a slowed moment of the game must not
    // leave the ride waiting after its last note. Otherwise the game's clock.
    private float SongTime(CampaignStep now, float dt, float before)
    {
        bool bySong = !string.IsNullOrEmpty(now.Music) && string.Equals(music.Playing, now.Music, System.StringComparison.OrdinalIgnoreCase)
            && CampaignMusic.TryFinale(now.Music, out _, out _);
        return bySong ? Mathf.Max(held, music.Clock - before) : held + dt;
    }

    // The song is over and the ride goes. Whoever stands on it leaves with it and the chapter is done (ShowFinish names
    // the rest); with nobody aboard, the run goes back to the last rest stop.
    private void Depart(CampaignStep now)
    {
        foreach (var someone in players)
            if (Within(someone, now)) { Advance(); return; }
        MissedRide();
        BackToCheckpoint();
    }

    private static void MissedRide() => AlertHUD.Danger(Texts.Get("campaign.alert.ride"));

    // Through unseen: the points, the words and three bells.
    private static void ShowUnseen()
    {
        CombatFeedHUD.Award(Texts.Get("feed.score.unseen"), StealthScore.Unseen);
        AlertHUD.Self(Texts.Get("campaign.alert.unseen"));
        KitSound.Play(KitSound.Cue.Unseen, .8f);
    }

    // Friend feedback C4 ("일부러 지나친 곳 다시 가기 싫어"): a later step met first finishes every step before it, as long
    // as each step on the way is only getting somewhere, pressing something or a rest stop. A hold, a collect and the
    // escape always play out, and nothing is skipped past them. A rest stop passed on the way still becomes the place
    // the dead come back. Pressing a later button goes through Prompt.
    private void SkipAhead()
    {
        for (int later = step + 1; later < steps.Count; later++)
        {
            if (!Passable(steps[later - 1].Kind)) return;
            var ahead = steps[later];
            if (SpotOf(ahead.Spot) == null) continue;
            bool met = ahead.Kind == CampaignStepKind.Reach ? AnyoneAt(ahead)
                : ahead.Kind == CampaignStepKind.Gather ? EveryoneAt(ahead)
                : ahead.Kind == CampaignStepKind.Checkpoint && EveryoneAt(ahead) && DoorsShut(ahead.Spot);
            if (met) { AdvanceTo(later + 1); return; }
        }
    }

    private static bool Passable(CampaignStepKind kind) => kind == CampaignStepKind.Reach || kind == CampaignStepKind.Press
        || kind == CampaignStepKind.Gather || kind == CampaignStepKind.Checkpoint;

    // Every step from the current one up to this one can be passed by (SkipAhead).
    private bool PassableUpTo(int index)
    {
        if (index < step || index >= steps.Count) return false;
        for (int i = step; i < index; i++) if (!Passable(steps[i].Kind)) return false;
        return true;
    }

    // The steps passed by go into the log one after another; only the step landed on is announced.
    private void AdvanceTo(int next)
    {
        while (!finished && step < next) Advance(step + 1 >= next);
    }

    private void Advance(bool announce = true)
    {
        var done = Current;
        if (done == null) return;
        if (done.Kind == CampaignStepKind.Checkpoint) checkpoint = step;
        step++;
        held = 0f;
        songlessSent = false;
        // A whole quiet stretch got through without one zombie finding anyone: into the rest stop, or up to where a hold
        // or the escape begins. Every player is paid for it, each PC on the host's word (Status.Unseen).
        var next = Current;
        bool through = done.Kind == CampaignStepKind.Checkpoint
            || next != null && (next.Kind == CampaignStepKind.Hold || next.Kind == CampaignStepKind.Escape);
        if (IsHost && through && unseen && quiet && !lull && calmMade > 0)
        {
            unseen = false;
            unseenCount++;
            ShowUnseen();
        }
        // The way on from a rest stop starts quiet again.
        if (done.Kind == CampaignStepKind.Checkpoint && step < steps.Count) BeginQuiet();
        if (done.Kind == CampaignStepKind.Escape || step >= steps.Count) Finish();
        else ShowObjective(announce);
    }

    private void Finish()
    {
        finished = true;
        finishedAt = Time.time;
        horde.Clear();
        ShowFinish();
    }

    // C1/C2: the step's song on every PC (a finished chapter's plays on under its closing card); on the host, the
    // song's highlight sends the step's brutes. A spot for them is looked for again every half second until one does.
    private void PlayMusic()
    {
        var now = Current;
        string wanted = finished ? music.Playing : now != null ? now.Music : "";
        // Joined partway: a hold's song from as far in as the hold is, a finale's escape from past the whole hold.
        float from = now == null ? 0f : now.Kind == CampaignStepKind.Hold ? held
            : now.Kind == CampaignStepKind.Escape && CampaignMusic.TryFinale(now.Music, out float holdBefore, out _) ? holdBefore + held : 0f;
        if (music.Want(wanted, from)) brutesDue = IsHost && now != null ? now.PeakBosses : 0;
        music.Tick();
        // A hold with no song (the first chapter's shutter, whose siren is its music) sends its brutes halfway through.
        bool songless = now != null && now.Kind == CampaignStepKind.Hold && string.IsNullOrEmpty(now.Music);
        if (IsHost && songless && !songlessSent && now.PeakBosses > 0 && held >= now.Seconds * .5f)
        {
            songlessSent = true;
            brutesDue = now.PeakBosses;
        }
        if (!IsHost || finished || brutesDue <= 0 || !(music.PastPeak || songless) || Time.time < nextBruteTry) return;
        nextBruteTry = Time.time + .5f;
        brutesDue -= horde.SpawnBrutes(brutesDue, players, minSpawnDistance, maxSpawnDistance, seenMinDistance);
    }

    private void BackToCheckpoint()
    {
        if (finished) return;
        horde.Clear();
        music.Stop();
        brutesDue = 0;
        fuel.Reset();
        step = checkpoint + 1;
        held = 0f;
        songlessSent = false;
        BeginQuiet();
        // Back in the rest stop with its doors shut, not standing open on the horde (user, 2026-11-11 playtest: "죽고 나서
        // 다시 시작할 때 문이 열려 있어서 매번 식겁함"). Status shares the doors with the room.
        if (checkpoint >= 0 && checkpoint < steps.Count)
            foreach (var door in doors) if (door != null && door.Spot == steps[checkpoint].Spot) door.Closed = true;
        // The goals done since the rest stop are undone: the log starts over rather than counting them twice.
        if (tracker != null) tracker.ClearObjective();
        ShowObjective(true);
    }

    // Back to the hub for everyone, the results with them and its map table offering the next chapter (memo 46,
    // HubFlow). With no hub built, the next chapter as before; after the last one, or with the next not built yet, the
    // run just stays done.
    private void MoveOn()
    {
        movedOn = true;
        string next = chapter != null ? chapter.Next : null;
        if (HubFlow.Return(next)) return;
        if (string.IsNullOrEmpty(next) || !Application.CanStreamedLevelBeLoaded(next)) return;
        if (GameSession.IsOnline) GameSession.ChangeMap(next);
        else SceneLoad.Go(next);
    }
}
