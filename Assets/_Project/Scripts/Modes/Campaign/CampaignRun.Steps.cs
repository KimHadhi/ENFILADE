using System.Collections.Generic;
using UnityEngine;

// CampaignRun: walking the chapter's steps, who is where, moving on, checkpoints, the song and the fight's phase. One part of the class; the rest is in CampaignRun.cs and the files beside it.
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
        // An alarm the whole map hears (GimmickFireAlarm) keeps it loud: found as of now, every frame it rings.
        ringing.RemoveWhere(alarm => alarm == null);
        if (ringing.Count > 0 && !scripted)
        {
            quiet = calmDue = lull = false;
            noticedAt = Time.time;
        }
        Alarm();
        // The chase dies down: found once, but no zombie has had anyone in sight for lullSeconds (they shut themselves in a
        // rest stop, or slipped away), so the horde stops and the zombies go looking, then calm (ZombieMind.LoseAll).
        // Found was found for good before (user, 2026-11-11 playtest).
        if (IsHost && !quiet && !scripted && lullSeconds > 0f && Time.time - noticedAt >= lullSeconds
            && Time.time - ZombieMind.LastNoticed >= lullSeconds)
        {
            quiet = lull = true;
            ZombieMind.LoseAll();
        }
        if (calmDue && players.Count > 0) { calmDue = false; PlaceCalmAhead(); }
        // The phase sets how hard the horde comes (CampaignPhases); nobody while quiet or resting behind a shut door.
        phases.Pace(quiet || Resting(), now.Horde, now.Rate, out int most, out float rate, out float special);
        // Greed (GimmickCargoLoad): what was loaded brings more of them, faster, whenever any come.
        if (most > 0) { most += greedMore; rate *= greedRate; }
        // Where they may come from is the mission's flow and what the squad has cleared (CampaignRun.Spawns, SpawnGround).
        FollowSpawns();
        phases.Spawned(horde.Tick(dt, most, rate, players, special));
        switch (now.Kind)
        {
            case CampaignStepKind.Reach:
                // Within its radius and with nothing of the building in between (CampaignRun.Reach).
                if (Arrived(now)) Advance();
                break;
            case CampaignStepKind.Checkpoint:
                if (EveryoneAt(now) && DoorsShut(now.Spot)) Advance();
                break;
            case CampaignStepKind.Gather:
                if (EveryoneAt(now)) Advance();
                break;
            case CampaignStepKind.Escape:
                if (TryFinale(step, out float holdBefore, out float board))
                {
                    // The ride waits out the song and leaves as it ends, with whoever stands on it.
                    held = SongTime(now, dt, holdBefore);
                    if (held >= board) Depart(now);
                    else ShowHeld(now);
                }
                else if (EveryoneAt(now)) Advance();
                break;
            case CampaignStepKind.Hold:
                // A manned hold (chapter 4's tug) counts only while someone stands at its spot, and pauses otherwise; a burn
                // hold (chapter 2's car pile) only while its fire has fuel.
                held = now.Manned ? held + (AnyoneAt(now) ? dt : 0f) : now.Burn > 0f ? held + (Burn(now, dt) ? dt : 0f) : SongTime(now, dt, 0f);
                if (held >= TotalOf(step)) Advance();
                else ShowHeld(now);
                break;
            case CampaignStepKind.Collect:
                if (fuel.Poured >= Mathf.Max(1, now.Count)) Advance();
                break;
        }
        if (!finished && Current == now) SkipAhead();
    }

    // A burn hold's fire (the map gimmick card "불로만 치우는 바리케이드"): as it begins, the cans already poured on its pile
    // light it, each worth the step's burn seconds; each can poured on it after adds as much again, and it burns down
    // meanwhile. Its poured cans go back to the station (CampaignCans.RespawnPoured), so it can always be fed. True while
    // it burns.
    private bool Burn(CampaignStep now, float dt)
    {
        if (burnStep != step) { burnStep = step; burnLeft = fuel.Poured * now.Burn; poursSeen = fuel.PourCount; }
        if (fuel.PourCount > poursSeen) { burnLeft += (fuel.PourCount - poursSeen) * now.Burn; poursSeen = fuel.PourCount; }
        burning = burnLeft > 0f;
        if (!burning) return false;
        burnLeft -= dt;
        return true;
    }

    // A finale (C-14): a hold whose song plays on into the escape after it, the escape's seconds being how long the ride
    // waits there. The hold lasts the song less that wait (CampaignMusic.TryFinale), and the ride leaves as the song
    // ends. False for any other step, and without the song's file.
    private bool TryFinale(int index, out float hold, out float board)
    {
        hold = board = 0f;
        if (index < 0 || index >= steps.Count) return false;
        var at = steps[index];
        var escape = at.Kind == CampaignStepKind.Escape ? at
            : at.Kind == CampaignStepKind.Hold && index + 1 < steps.Count ? steps[index + 1] : null;
        if (escape == null || escape.Kind != CampaignStepKind.Escape || escape.Seconds <= 0f || !SameSong(at.Music, escape.Music)) return false;
        board = escape.Seconds;
        return CampaignMusic.TryFinale(escape.Music, board, out hold);
    }

    // How long a hold lasts, or a finale's escape waits: under a finale's song, by the song; otherwise the hold's own
    // seconds, and no time at all for an escape (0).
    private float TotalOf(int index)
    {
        if (index < 0 || index >= steps.Count) return 0f;
        var at = steps[index];
        bool finale = TryFinale(index, out float hold, out float board);
        return at.Kind == CampaignStepKind.Escape ? (finale ? board : 0f) : finale ? hold : at.Seconds;
    }

    // How far into its time a step is. Under a finale's song that this PC is playing, the song's own clock says, less
    // where the song came in (CampaignPhases.EntryOf: a song with a far first highlight starts further along) and less
    // what of the song came before the step: the song runs in real time, and a slowed moment of the game must not
    // leave the ride waiting after its last note. Otherwise the game's clock.
    private float SongTime(CampaignStep now, float dt, float before)
    {
        bool bySong = SameSong(music.Playing, now.Music) && TryFinale(step, out _, out _);
        return bySong ? Mathf.Max(held, music.Clock - CampaignPhases.EntryOf(music.Current) - before) : held + dt;
    }

    private static bool SameSong(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

    // The songs of the chapter's loud stretches (CampaignChapters.songs), else every song of Music.csv.
    private IReadOnlyList<string> Songs => chapter != null && chapter.Songs.Length > 0 ? chapter.Songs : CampaignMusic.All;

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
            bool met = ahead.Kind == CampaignStepKind.Reach ? Arrived(ahead)
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
        // The way on from a rest stop starts quiet again; the way on from a hold has its map crowds set as it is reached.
        if (done.Kind == CampaignStepKind.Checkpoint && step < steps.Count) BeginQuiet();
        else if (IsHost && done.Kind == CampaignStepKind.Hold && step < steps.Count) PlaceCrowdsAhead();
        if (done.Kind == CampaignStepKind.Escape || step >= steps.Count) Finish();
        else ShowObjective(announce);
    }

    private void Finish()
    {
        finished = true;
        finishedAt = Time.time;
        horde.Clear();
        phases.End();
        ShowFinish();
    }

    // The song and the phase: the host picks them as the run stands (CampaignPhases), and every PC plays the song (a
    // guest the host's, from the host's place in it: ReceiveStatus). A finished chapter's song plays on under its
    // closing card.
    private void PlayMusic()
    {
        if (IsHost)
        {
            var now = Current;
            if (finished || now == null) phases.End();
            else
            {
                bool songless = (now.Kind == CampaignStepKind.Hold || now.Kind == CampaignStepKind.Escape) && string.IsNullOrEmpty(now.Music);
                phases.Run(!quiet, step, now.Music ?? "", songless, Songs, music);
            }
            phase = phases.Now;
        }
        music.Tick();
        if (IsHost) SendBrutes();
    }

    // C1/C2, the host: a step's brutes (peakBoss) come at its song's peak, or halfway through a hold without a song (the
    // first chapter's shutter, whose siren is its music). A spot for them is looked for again every half second until
    // one does.
    private void SendBrutes()
    {
        var now = Current;
        if (finished || now == null) return;
        if (brutesStep != step) { brutesStep = step; brutesDue = now.PeakBosses; }
        if (brutesDue <= 0 || Time.time < nextBruteTry) return;
        bool due = string.IsNullOrEmpty(now.Music) ? now.Kind == CampaignStepKind.Hold && held >= now.Seconds * .5f
            : SameSong(music.Playing, now.Music) && music.PastPeak;
        if (!due) return;
        nextBruteTry = Time.time + .5f;
        brutesDue -= horde.SpawnBrutes(brutesDue, players);
    }

    private void BackToCheckpoint()
    {
        if (finished) return;
        wipes++;
        horde.Clear();
        // The world after the rest stop is put back as it was, so its stretches are no longer cleared (SpawnGround).
        horde.Wipe(checkpoint);
        music.Stop();
        brutesDue = 0;
        brutesStep = -1;
        fuel.Reset();
        crowdsPlaced.Clear();
        burnStep = -1;
        burning = false;
        step = checkpoint + 1;
        held = 0f;
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
