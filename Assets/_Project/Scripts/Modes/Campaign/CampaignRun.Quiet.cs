using UnityEngine;

// CampaignRun: the quiet start, calm zombies ahead until someone is seen and the alarm goes. One part of the class; the rest is in CampaignRun.cs and the files beside it.
public sealed partial class CampaignRun
{
    // A quiet stretch begins (the chapter, or the way on from a rest stop): no horde, and calm zombies owed to the way ahead.
    private void BeginQuiet()
    {
        quiet = wasQuiet = calmDue = quietStart;
        lull = false;
        unseen = quietStart;
        alarm = wasAlarm = false;
        alarmSince = -1f;
        calmMade = 0;
    }

    // One of them has noticed someone. Quiet, the alarm is theirs and is watched from now (Alarm); with alarmSeconds 0 the
    // horde is on its way at once, as it used to be.
    private void OnWoke(ZombieMind mind, PlayerMovement who)
    {
        if (!IsHost || finished) return;
        if (quiet && alarmSeconds > 0f) { if (alarmSince < 0f) alarmSince = Time.time; return; }
        quiet = lull = unseen = false;
        noticedAt = Time.time;
    }

    // The host, quiet: a local alarm lasting alarmSeconds ends the quiet (the horde comes, the stretch is no longer unseen);
    // one put out sooner (no zombie woken by stealth still after anyone: the witnesses dead, or lost and gone searching)
    // leaves it quiet.
    private void Alarm()
    {
        if (!quiet || alarmSeconds <= 0f) { alarm = false; alarmSince = -1f; return; }
        bool up = ZombieMind.AlarmUp;
        if (up && alarmSince < 0f) alarmSince = Time.time;
        if (up && Time.time - alarmSince >= alarmSeconds)
        {
            quiet = lull = unseen = alarm = false;
            alarmSince = -1f;
            noticedAt = Time.time;
            return;
        }
        if (!up) alarmSince = -1f;
        alarm = up;
    }

    // The way ahead of a quiet stretch, up to the next hold, escape or rest stop: each step's share of calm zombies between
    // the place before it and its own.
    private void PlaceCalmAhead()
    {
        Vector3 from = players[0].transform.position;
        for (int i = step; i < steps.Count; i++)
        {
            var ahead = steps[i];
            if (ahead.Kind == CampaignStepKind.Hold || ahead.Kind == CampaignStepKind.Escape) break;
            var spot = SpotOf(ahead.Spot);
            if (spot == null) continue;
            calmMade += horde.PlaceCalm(Mathf.RoundToInt(ahead.Horde * calmShare), from, spot.position, players, calmNear);
            from = spot.position;
            if (ahead.Kind == CampaignStepKind.Checkpoint) break;
        }
    }

    // Every PC: the quiet ended because they were noticed. A hold or the escape beginning says so itself.
    private void WatchQuiet()
    {
        if (quiet == wasQuiet) return;
        var now = Current;
        bool noticed = wasQuiet && !finished && now != null && now.Kind != CampaignStepKind.Hold && now.Kind != CampaignStepKind.Escape;
        wasQuiet = quiet;
        if (noticed) AlertHUD.Danger(Texts.Get("campaign.alert.noticed"));
        else if (quiet && lull && !finished) AlertHUD.Self(Texts.Get("campaign.alert.lull"));
    }

    // Every PC: a quiet stretch's alarm put out in time, the witnesses dead or lost; the stretch stays quiet.
    private void WatchAlarm()
    {
        if (alarm == wasAlarm) return;
        bool cleared = wasAlarm && quiet && !finished;
        wasAlarm = alarm;
        if (cleared) AlertHUD.Self(Texts.Get("stealth.clear"));
    }
}
