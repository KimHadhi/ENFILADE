using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// CampaignRun: the host's status sent to the other PCs and what the screen shows of it. One part of the class; the rest is in CampaignRun.cs and the files beside it.
public sealed partial class CampaignRun
{
    private void Share()
    {
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        var now = new Status
        {
            Step = (byte)Mathf.Clamp(step, 0, 255), Finished = (byte)(finished ? 1 : 0),
            Checkpoint = (byte)Mathf.Clamp(checkpoint + 1, 0, 255), Held = (ushort)Mathf.Clamp(Mathf.FloorToInt(held), 0, ushort.MaxValue),
            Doors = DoorBits(), Quiet = (byte)(quiet ? lull ? 2 : 1 : 0), Unseen = unseenCount, Alarm = (byte)(alarm ? 1 : 0)
        };
        if (now.Equals(sent) && Time.unscaledTime < nextShare) return;
        sent = now;
        nextShare = Time.unscaledTime + ShareEvery;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Status>(), Allocator.Temp);
        writer.WriteValueSafe(now);
        network.CustomMessagingManager.SendNamedMessageToAll(StatusMessage, writer);
    }

    // A guest follows the host's run.
    private void ReceiveStatus(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Status status);
        if (sender != NetworkManager.ServerClientId) return;
        bool back = status.Step < step, moved = status.Step != step;
        // Sent back from a finale's escape while the chapter still runs: the ride left with nobody on it.
        if (back && !finished && Current != null && Current.Kind == CampaignStepKind.Escape && TotalOf(Current) > 0f) MissedRide();
        step = status.Step;
        checkpoint = status.Checkpoint - 1;
        held = status.Held;
        quiet = status.Quiet != 0;
        lull = status.Quiet == 2;
        alarm = status.Alarm != 0;
        // The host's count of stretches got through unseen: each one more pays this player too. The first word only
        // sets the count, so joining late pays for nothing that came before.
        if (!unseenKnown) { unseenKnown = true; unseenCount = status.Unseen; }
        else if (status.Unseen != unseenCount)
        {
            int more = (byte)(status.Unseen - unseenCount);
            unseenCount = status.Unseen;
            if (more > 0 && more < 4) for (int i = 0; i < more; i++) ShowUnseen();
        }
        for (int i = 0; i < doors.Length && i < 8; i++) if (doors[i] != null) doors[i].Closed = (status.Doors & (1 << i)) != 0;
        if (status.Finished != 0)
        {
            if (!finished) { finished = true; ShowFinish(); }
            return;
        }
        finished = false;
        if (back) music.Stop();
        if (back && tracker != null) tracker.ClearObjective();
        if (moved) ShowObjective(true);
        else if (Current != null) ShowHeld(Current);
    }

    private void ShowObjective(bool announce)
    {
        var now = Current;
        if (tracker == null || now == null) return;
        // A hold, and a finale's escape (the ride's wait), show their time passing.
        float total = now.Kind == CampaignStepKind.Hold || now.Kind == CampaignStepKind.Escape ? TotalOf(now) : 0f;
        tracker.SetObjective(total > 0f
            ? new MissionObjective(now.Id, now.Title, now.Description, MissionProgressDisplay.Percent,
                Mathf.FloorToInt(held), Mathf.CeilToInt(total), now.Done)
            : now.Kind == CampaignStepKind.Collect
            ? new MissionObjective(now.Id, now.Title, now.Description, MissionProgressDisplay.Count,
                Mathf.Min(fuel.Poured, now.Count), Mathf.Max(1, now.Count), now.Done)
            : new MissionObjective(now.Id, now.Title, now.Description, doneTitle: now.Done), announce);
        shownPoured = -1;
    }

    private void ShowHeld(CampaignStep now)
    {
        if (tracker == null || (now.Kind != CampaignStepKind.Hold && now.Kind != CampaignStepKind.Escape)) return;
        float total = TotalOf(now);
        if (total > 0f) tracker.SetProgress(now.Id, Mathf.FloorToInt(held), Mathf.CeilToInt(total));
    }

    private void ShowFinish()
    {
        // Under a finale's song the ride left as it ended: whoever wasn't on it is named on the card ("못 타면 걍 아쉬운
        // 거지": no more comes of it than that).
        leftBehind = "";
        var last = step - 1 >= 0 && step - 1 < steps.Count ? steps[step - 1] : null;
        if (last != null && last.Kind == CampaignStepKind.Escape && TotalOf(last) > 0f)
        {
            var names = new List<string>();
            foreach (var someone in players)
                if (!Within(someone, last)) names.Add(someone.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : someone.name);
            leftBehind = string.Join(", ", names);
        }
        PlayerRecords.ChapterEscaped(gameObject.scene.name);
        // The escape has its moment first; the card comes up as the banner lets go (ShowCardWhenDue).
        climb = ClassGrowth.Settle();
        // Everyone's numbers go with this PC to the hub, where the results play over the players (memo 37, MatchResults).
        MatchResults.Capture(gameObject.scene.name, chapter != null ? chapter.Title : "", chapter != null ? chapter.Next : "", climb,
            someone => last == null || last.Kind != CampaignStepKind.Escape || TotalOf(last) <= 0f || Within(someone, last));
        ChapterEndBanner.Play(chapter != null ? chapter.Title : "", leftBehind.Length > 0 ? Texts.Format("campaign.escape.left", leftBehind)
            : GameSession.IsOnline ? Texts.Get("campaign.escape.all") : Texts.Get("campaign.escape.solo"), BannerSeconds);
        cardAt = Time.unscaledTime + BannerSeconds - CardOverBanner;
        if (tracker == null) return;
        string next = chapter != null ? chapter.Next : null;
        string title = string.IsNullOrEmpty(next) ? Texts.Get("campaign.end.done")
            : Application.CanStreamedLevelBeLoaded(next) ? Texts.Get("campaign.end.next") : Texts.Get("campaign.end.unbuilt");
        tracker.SetObjective(new MissionObjective(gameObject.scene.name + ".end", title, ""));
    }

    // The closing card and, beside its results, the class's insignia, climbing if this chapter took it up a rank (6E).
    // With the hub built the card is the story alone: the results and the insignia play in the hub (ResultsShow).
    private void ShowCardWhenDue()
    {
        // The results keep up while the card is up: the last numbers on their way from the others (MatchResults).
        if (finished && Time.unscaledTime >= nextResults) { nextResults = Time.unscaledTime + .5f; MatchResults.Refresh(); }
        if (cardAt < 0f || Time.unscaledTime < cardAt) return;
        cardAt = -1f;
        float seconds = Mathf.Max(4f, outroSeconds - (BannerSeconds - CardOverBanner));
        bool hub = HubFlow.IsBuilt;
        if (chapter != null && screen != null) screen.ShowCard(chapter.Title, chapter.Outro, seconds, hub ? null : Results());
        if (climb != null && !hub) RankClimb.Play(climb, seconds);
        climb = null;
    }

    // The chapter's results under its closing story (stage 6A), two plain numbers: this chapter's kills and the money
    // held (the same one number as in play; no experience anywhere, which confused the user). In a room, each player's.
    private string Results()
    {
        const string Gold = "<color=#FFD36B>", End = "</color>";
        var text = new System.Text.StringBuilder();
        var mine = PlayerRoster.Local;
        int kills = mine != null && mine.TryGetComponent(out PlayerTally own) ? own.Kills : 0;
        text.Append(Texts.Get("campaign.results.kills")).Append("<pos=50%>").Append(kills).Append('\n');
        text.Append(Texts.Get("campaign.results.money")).Append("<pos=50%>").Append(Gold).Append(Wallet.Format(Wallet.Balance)).Append(End).Append('\n');
        if (leftBehind.Length > 0) text.Append(Texts.Get("campaign.results.left")).Append("<pos=50%>").Append(leftBehind).Append('\n');
        if (!GameSession.IsOnline) return text.ToString();
        text.Append("\n<b>").Append(Texts.Get("campaign.results.team")).Append("</b><pos=50%>").Append(Texts.Get("campaign.results.kills"))
            .Append("<pos=72%>").Append(Texts.Get("campaign.results.money")).Append('\n');
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null || !(someone.IsLocal || someone.IsNetworkDriven) || !someone.TryGetComponent(out PlayerTally tally)) continue;
            text.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(tally.Color)).Append('>').Append(tally.DisplayName).Append(End)
                .Append("<pos=50%>").Append(tally.Kills).Append("<pos=72%>").Append(Wallet.Format(tally.Score)).Append('\n');
        }
        return text.ToString();
    }
}
