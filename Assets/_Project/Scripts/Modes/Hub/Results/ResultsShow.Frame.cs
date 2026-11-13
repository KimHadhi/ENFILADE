using UnityEngine;

// ResultsShow: Every frame of the lineup. One part of the class; the rest is in ResultsShow.cs and the files beside it.
public sealed partial class ResultsShow
{
    // The shot playing at the lineup's time, or null once the whole row is up.
    private Seat ShotOn(float time)
    {
        foreach (var seat in shots)
            if (time >= seat.ShotAt && time < seat.ShotAt + seat.ShotLength) return seat;
        return null;
    }

    private void Pose()
    {
        curtain.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(age / .35f));
        float headerIn = Ease.Out((age - .1f) / .3f);
        header.anchoredPosition = new Vector2(80f - 160f * (1f - headerIn), -40f);
        var now = ShotOn(age);
        // A cut: a white blink, and the player of the new shot strikes their pose.
        float since = age - (now != null ? now.ShotAt : groupAt);
        cut.color = new Color(1f, 1f, 1f, since >= 0f && since < .14f ? .5f * (1f - since / .14f) : 0f);
        foreach (var seat in shots)
        {
            bool live = seat == now;
            float local = age - seat.ShotAt;
            if (live && prevAge < seat.ShotAt && seat.Pose != null) seat.Pose.Strike();
            seat.SpotGroup.alpha = live ? 1f : 0f;
            if (!live) continue;
            // The name slides in from the left; the titles slam on down the left one after another, as the camera settles.
            float nameIn = Ease.Out(local / .22f);
            seat.Name.anchoredPosition = new Vector2(80f - 120f * (1f - nameIn), 118f);
            for (int i = 0; i < seat.Slams.Count; i++)
            {
                var slam = seat.Slams[i];
                float slamAt = seat.ShotAt + HubLobby.Number("showTitlesAt", .9f) + i * HubLobby.Number("showTitleEvery", .16f);
                float after = age - slamAt;
                if (after < 0f) { slam.localScale = Vector3.zero; continue; }
                if (prevAge < slamAt) Slammed();
                // Down from nearly twice its size, a hair under, then home, with a shake as it lands.
                float t = Mathf.Clamp01(after / .2f);
                float size = t < .6f ? Mathf.Lerp(1.8f, .95f, t / .6f) : Mathf.Lerp(.95f, 1f, (t - .6f) / .4f);
                slam.localScale = Vector3.one * size;
                float shake = after > .1f && after < .26f ? (1f - (after - .1f) / .16f) * 6f : 0f;
                var home = new Vector2(80f, -(298f + i * TitleGap));
                slam.anchoredPosition = home + (shake > 0f ? Random.insideUnitCircle * shake : Vector2.zero);
            }
        }
        // The closing row: everyone's name under them.
        bool row = now == null && age >= groupAt && seats.Count > 1;
        if (row && prevAge < groupAt) KitSound.Play(KitSound.Cue.Escape, .5f);
        foreach (var seat in seats)
        {
            float plateIn = row ? Ease.Out((age - groupAt - seat.Index * .06f) / .2f) : 0f;
            seat.PlateGroup.alpha = plateIn;
            if (!row) continue;
            Vector2 feet = OnScreen(seat.Feet);
            seat.Plate.anchoredPosition = feet + new Vector2(0f, -46f - 20f * (1f - plateIn));
        }
        float high = Ease.Out((age - highlightsAt) / .25f);
        highlights.localScale = new Vector3(1f, high, 1f);
        float count = Mathf.Clamp01((age - highlightsAt) / .6f);
        foreach (var (label, value, award) in counters)
            label.text = ResultsAwards.Format(award, award.Unit == "m" ? value * count : System.Math.Round(value * count));
    }

    private void Slammed() => CombatCueAudio.Play(gameObject, CombatCue.ParryBreak, Random.Range(1.1f, 1.3f));

    // A point in the world on this canvas (px from its middle), through the results' camera.
    private Vector2 OnScreen(Vector3 world)
    {
        if (view == null) return Vector2.zero;
        Vector3 screen = view.WorldToScreenPoint(world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 local);
        return local;
    }

    /// <summary>m:ss, or h:mm:ss past an hour.</summary>
    public static string Clock(float seconds)
    {
        int whole = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return whole >= 3600 ? $"{whole / 3600}:{whole / 60 % 60:00}:{whole % 60:00}" : $"{whole / 60}:{whole % 60:00}";
    }

    /// <summary>"3 / 4": how many made the ride of how many there were.</summary>
    public static string Escaped()
    {
        int made = 0;
        foreach (var entry in MatchResults.Entries) if (entry.Escaped) made++;
        return made + " / " + MatchResults.Entries.Count;
    }
}
