using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Every class knife's swings as text, so what the tables say can be checked without playing (the user's standing demand:
/// what was built is checked by whoever built it): each knife's stats (Tables/Melee/ClassKnives.csv), each of its attack
/// clips' phases (KnifeSwing: how long, when the blade is live, when the next action may begin) with what is wrong with
/// them, and what a script of presses turns into on that knife's own timeline (SwingTimeline, the class the game itself
/// steps): a tap, a hold, a mash. The editor writes it to Logs/knife-swings.txt after every compile and whenever a table
/// is saved (MeleeReportFiles), so the file is always what the game would do.
/// </summary>
public static class KnifeSwingReport
{
    // The classes whose knives are listed: every row of the table, the default first.
    private const float Step = 1f / 240f;

    /// <summary>One press of a script: the attack button goes down at Down and up at Up (s).</summary>
    public readonly struct Press
    {
        public readonly float Down, Up;
        public Press(float down, float up) { Down = down; Up = up; }
    }

    public static string Text()
    {
        var text = new StringBuilder();
        text.AppendLine("Knife swings (Tables/Melee/ClassKnives.csv and the knives' clips in Tables/Clips), as the game steps them (SwingTimeline).");
        text.AppendLine("Seconds are real seconds at the knife's attackSpeed; a clip's own seconds are in brackets.");
        text.AppendLine();
        var problems = new List<string>();
        var knives = new List<ClassKnives.Knife>();
        foreach (string id in GameTables.RowIds(ClassKnives.Table)) knives.Add(ClassKnives.Of(id));
        float heavyFrom = GameTables.Number(PlayerMovement.TableName, "Fists", "heavyPunchCharge", .6f);
        text.AppendLine("== Knives: stats, and each attack clip's phases ==");
        foreach (var knife in knives)
        {
            text.Append(knife.Id.PadRight(12)).Append(' ').Append(Model(knife).PadRight(13))
                .Append(" wind-up ").Append(N(knife.Windup / knife.AttackSpeed).PadRight(6))
                .Append(" speed x").Append(N(knife.AttackSpeed).PadRight(5))
                .Append(" full charge ").Append(N(knife.ChargeTime / knife.AttackSpeed).PadRight(6))
                .Append(" cuts ").Append(knife.Penetration).Append(" bodies")
                .Append("  damage ").Append(N(knife.Slash)).Append('-').Append(N(knife.Stab)).AppendLine();
            Clip(text, problems, knife, "light", knife.SlashClip);
            if (knife.BackslashClip != knife.SlashClip) Clip(text, problems, knife, "back", knife.BackslashClip);
            Clip(text, problems, knife, knife.StabClip == knife.SlashClip ? "heavy (the light clip again)" : "heavy", knife.StabClip);
        }
        text.AppendLine();
        text.AppendLine("== Problems ==");
        if (problems.Count == 0) text.AppendLine("none");
        foreach (string problem in problems) text.AppendLine("- " + problem);
        text.AppendLine();
        text.AppendLine("== What presses turn into (heavy from a charge of " + N(heavyFrom) + ") ==");
        foreach (var knife in knives)
        {
            text.AppendLine(knife.Id + " (" + Model(knife) + ")");
            text.AppendLine("  a tap               " + Run(knife, heavyFrom, 1.5f, new Press(0f, .05f)));
            text.AppendLine("  held 0.5 s          " + Run(knife, heavyFrom, 2f, new Press(0f, .5f)));
            text.AppendLine("  held 2 s            " + Run(knife, heavyFrom, 3.5f, new Press(0f, 2f)));
            text.AppendLine("  two taps 0.2 apart  " + Run(knife, heavyFrom, 2f, new Press(0f, .05f), new Press(.2f, .25f)));
            text.AppendLine("  mashed for 3 s      " + Count(knife, heavyFrom));
        }
        return text.ToString();
    }

    // One clip's line: its phases in real seconds at the knife's speed, with the clip's own in brackets.
    private static void Clip(StringBuilder text, List<string> problems, ClassKnives.Knife knife, string kind, string table)
    {
        var clip = KnifeSwing.Of(table);
        text.Append("    ").Append(kind.PadRight(29)).Append((table ?? "").PadRight(31));
        if (clip == null)
        {
            text.AppendLine("MISSING");
            problems.Add($"{knife.Id}: the {kind} clip '{table}' is not a table with keys.");
            return;
        }
        float speed = knife.AttackSpeed;
        float press = knife.Windup / speed;
        text.Append("blade ").Append(N(press + clip.ActiveFrom / speed)).Append('-').Append(N(press + clip.ActiveTo / speed))
            .Append("  next ").Append(N(press + clip.Next / speed)).Append("  home ").Append(N(press + clip.Length / speed))
            .Append("  from the press   [clip: live ").Append(N(clip.ActiveFrom)).Append('-').Append(N(clip.ActiveTo))
            .Append(", next ").Append(N(clip.Next)).Append(", length ").Append(N(clip.Length)).AppendLine("]");
        if (!clip.Cuts) problems.Add($"{knife.Id}: the {kind} clip '{table}' marks no rows the blade is live through (hit): it would cut nothing.");
        else if (clip.Next >= clip.Length - 1e-4f) problems.Add($"{knife.Id}: the {kind} clip '{table}' has no 다음 mark (next): the next action waits for the whole clip, {N(clip.Length)} s.");
    }

    /// <summary>
    /// A script of presses stepped on a knife's own timeline: what happened and when (s from the first press), as one
    /// line. The swing that leaves is the knife's heavy clip from <paramref name="heavyFrom"/> of a charge, else its light.
    /// </summary>
    public static string Run(ClassKnives.Knife knife, float heavyFrom, float seconds, params Press[] presses)
    {
        var line = new StringBuilder();
        var timeline = Timeline(knife, heavyFrom, charge =>
        {
            line.Append(charge >= heavyFrom ? " heavy" : " light").Append(" (charge ").Append(N(charge)).Append(')');
        });
        bool wasHeld = false, wasLive = false;
        for (float time = 0f; time <= seconds; time += Step)
        {
            bool held = false;
            foreach (var press in presses)
                if (time >= press.Down && time < press.Up) held = true;
            float at = time + Step;
            var before = line.Length;
            var happened = timeline.Tick(Step, held, held && !wasHeld);
            wasHeld = held;
            string said = line.ToString(before, line.Length - before);
            line.Length = before;
            if ((happened & SwingTimeline.Events.Wound) != 0) line.Append(N(at)).Append(" wind-up; ");
            if ((happened & SwingTimeline.Events.Charging) != 0) line.Append(N(at)).Append(" charging; ");
            if ((happened & SwingTimeline.Events.Left) != 0) line.Append(N(at)).Append(" leaves").Append(said).Append("; ");
            bool live = timeline.Live(out _, out _);
            if (live && !wasLive) line.Append(N(at)).Append(" blade live; ");
            if (!live && wasLive) line.Append(N(at - Step)).Append(" blade done; ");
            wasLive = live;
            if ((happened & SwingTimeline.Events.Free) != 0) line.Append(N(at)).Append(" next may begin; ");
            if ((happened & SwingTimeline.Events.Home) != 0) line.Append(N(at)).Append(" home; ");
        }
        return line.ToString().TrimEnd(' ', ';');
    }

    // How many swings leave in three seconds of pressing as fast as the button can go, and how far apart they are.
    private static string Count(ClassKnives.Knife knife, float heavyFrom)
    {
        var left = new List<float>();
        float now = 0f;
        var timeline = Timeline(knife, heavyFrom, _ => left.Add(now));
        // Down for one step out of every four: 60 presses a second, far faster than a hand.
        int step = 0;
        for (now = 0f; now <= 3f; now += Step, step++)
        {
            bool held = step % 4 == 0;
            timeline.Tick(Step, held, held);
        }
        if (left.Count < 2) return left.Count + " swing";
        return left.Count + " swings, one every " + N((left[left.Count - 1] - left[0]) / (left.Count - 1)) + " s";
    }

    private static SwingTimeline Timeline(ClassKnives.Knife knife, float heavyFrom, System.Action<float> onLeave)
    {
        var light = KnifeSwing.Of(knife.SlashClip);
        var heavy = KnifeSwing.Of(knife.StabClip);
        var timeline = new SwingTimeline
        {
            Windup = knife.Windup, ChargeTime = knife.ChargeTime, Speed = knife.AttackSpeed,
            Buffer = GameTables.Number(PlayerMovement.TableName, "Fists", "attackBuffer", .35f),
        };
        timeline.Leaving = charge =>
        {
            onLeave(charge);
            var clip = charge >= heavyFrom ? heavy : light;
            return clip != null ? clip.Marks : default;
        };
        return timeline;
    }

    private static string Model(ClassKnives.Knife knife) => knife.Model.Substring(knife.Model.LastIndexOf('/') + 1);

    private static string N(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
