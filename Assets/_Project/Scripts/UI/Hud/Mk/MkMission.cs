using TMPro;
using UnityEngine;

/// <summary>
/// The objective at the top left in HUD Mk II's words (the draft the user picked, 2026-11-22; the addon itself has no
/// objective): a small spaced label (OBJECTIVE), the scoreboard key and "목표·점수판" at the right of that row, the
/// objective's title under it decoding as it changes (the pickup rows' scramble), its count at the right with a dim
/// leading zero ("02 / 04"), and under them a thin bar in the addon's dark box: a cell a target while they fit (filled
/// white as they are done), one bar for a share. It all makes way for the Tab screen, as the old bar did.
/// MissionTracker says what the objective is (GameHUD's Mission, whose MissionHUD still sounds a new one while HUD Mk II
/// draws it).
/// </summary>
public sealed class MkMission : MkPiece
{
    private readonly MkShapes shapes;
    private readonly TextMeshProUGUI label, tab, title, count;
    private readonly MkNotice notice;
    private MissionTracker tracker;
    private float lookForTracker, changed = -99f;
    private string shownTitle = "";

    public MkMission(MkHud hud, RectTransform canvas, MkNotice notice) : base(hud, canvas, "Mk Mission", new Vector2(0f, 1f))
    {
        this.notice = notice;
        shapes = MkShapes.Make("Shapes", Frame);
        label = MkText.Make("Label", Frame, Look, 0f, 0f);
        tab = MkText.Make("Board key", Frame, Look, 1f, 0f);
        title = MkText.Make("Title", Frame, Look, 0f, 0f);
        count = MkText.Make("Count", Frame, Look, 1f, 0f);
        count.richText = true;
    }

    public override void Dispose() => Track(null);

    private void Track(MissionTracker next)
    {
        if (tracker == next) return;
        if (tracker != null) tracker.Changed -= Changed;
        tracker = next;
        if (tracker != null) tracker.Changed += Changed;
    }

    // A new objective decodes in place and is said at the top of the screen; a count moving is silent.
    private void Changed(MissionObjective objective, bool announce)
    {
        if (objective == null || !announce) return;
        changed = Time.unscaledTime;
        notice.Show(Texts.Get("objective.alert.new"), objective.Title, false);
    }

    public override void Tick(float dt)
    {
        var m = Look.Goal;
        var c = Look.Colour;
        float now = Time.unscaledTime;
        if (tracker == null && now >= lookForTracker)
        {
            lookForTracker = now + 1f;
            Track(Object.FindAnyObjectByType<MissionTracker>());
        }
        Group.alpha = 1f - Look.Hud.tabFade * Mathf.SmoothStep(0f, 1f, ScoreboardHUD.Openness);
        var objective = tracker != null ? tracker.Current : null;

        // The label row: what this is, and the scoreboard's key at its right.
        MkText.Label(label, Look, MkText.Spaced(Texts.Get("hud.mk.objective").ToUpperInvariant()));
        MkText.At(label, m.x, m.y);
        label.color = c.tag;
        MkText.Label(tab, Look, KeyBindings.Name("UI/OpenObjectiveBoard") + "  " + Texts.Get("objective.tab_hint"));
        MkText.At(tab, m.x + m.width, m.y);
        tab.color = c.tag;

        // The title, decoding for a moment after it changes.
        string words = objective != null ? objective.Title : Texts.Get("objective.waiting");
        if (words != shownTitle)
        {
            if (shownTitle.Length > 0) changed = now;
            shownTitle = words;
        }
        float since = now - changed;
        MkText.Size(title, Look, Look.Text.history);
        MkText.Say(title, since < m.decodeWindow ? MkText.Scramble(words, Mathf.Clamp01(since / Mathf.Max(.01f, m.decode)), Look.Taken.chars) : words);
        MkText.At(title, m.x, m.y + m.titleRise);
        title.color = c.text;

        // The count and the bar under it all.
        bool numeric = objective != null && objective.Display != MissionProgressDisplay.None;
        string zero = "<alpha=#" + Hex(m.zero) + ">0</alpha>";
        int cells = 1;
        float share = 0f;
        Color fill = c.text;
        if (numeric && objective.Display == MissionProgressDisplay.Count)
        {
            MkText.Say(count, zero + objective.Current + "<alpha=#" + Hex(m.slash) + "> / </alpha>" + zero + objective.Target);
            cells = objective.Target <= m.mostSegments ? Mathf.Max(1, objective.Target) : 1;
            share = cells > 1 ? objective.Current : objective.Progress;
        }
        else if (numeric)
        {
            MkText.Say(count, objective.ProgressText);
            share = objective.Progress;
        }
        else MkText.Say(count, "");
        MkText.Size(count, Look, Look.Text.history);
        MkText.At(count, m.x + m.width, m.y + m.titleRise);
        count.color = c.text;

        shapes.Clear();
        float pad = Look.Health.pad;
        shapes.Box(m.x - pad, m.barY - pad, m.width + 2f * pad, m.barHeight + 2f * pad, c.background, Look.Health.corner);
        float pitch = m.width / cells, wide = cells > 1 ? pitch * m.segment : m.width;
        for (int i = 0; i < cells; i++)
        {
            float x = m.x + pitch * i;
            shapes.Box(x, m.barY, wide, m.barHeight, MkText.Fade(c.text, m.track));
            float part = cells > 1 ? (i < share ? 1f : 0f) : Mathf.Clamp01(share);
            shapes.Box(x, m.barY, wide * part, m.barHeight, fill);
        }
        shapes.Show();
    }

    private static string Hex(float alpha) => Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f).ToString("X2");
}
