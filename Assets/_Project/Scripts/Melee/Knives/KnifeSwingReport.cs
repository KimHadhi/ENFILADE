using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Every class knife's swings as text, so what the tables say can be checked without playing (the user's standing demand:
/// what was built is checked by whoever built it): each knife's stats (Tables/Melee/ClassKnives.csv), each of its attack
/// clips' phases (KnifeSwing: how long, when the blade is live, when the next action may begin) with what is wrong with
/// them, and what a script of presses turns into on that knife's own timeline (SwingTimeline, the class the game itself
/// steps): a tap, a hold, a mash; and where each clip puts the blade (KnifeSeat, from the knife model's own markers):
/// how far ahead of the eyes it gets, how far it travels through its marks, and what a swing at a body standing at a
/// few distances does (PlayerMeleeCombat.Approach: the step in, the arm's reach, how deep the blade then passes). The
/// editor writes it to Logs/knife-swings.txt after every compile and whenever a table is saved (MeleeReportFiles), so
/// the file is always what the game would do.
/// </summary>
public static class KnifeSwingReport
{
    // The classes whose knives are listed: every row of the table, the default first.
    private const float Step = 1f / 240f;
    // Bodies a swing is tried on, by how far their capsule's surface is from the eyes (m): touching, a step off, where a
    // zombie stops to strike (1.6 m between the two middles, its capsule 0.28 m thick), and too far.
    private static readonly float[] Stands = { .32f, .8f, 1.32f, 2f };

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
        text.AppendLine("== The blade: what each clip reaches, by the knife's own model (no number says how far a knife reaches) ==");
        float bite = GameTables.Number(PlayerMovement.TableName, "Fists", "bite", .25f), reachIn = GameTables.Number(PlayerMovement.TableName, "Fists", "reachIn", .15f);
        text.AppendLine("Worked out for a hand " + N(KnifeSeat.NominalHand) + " m long (the game measures the body's own). 'ahead' is how far ahead of the eyes the");
        text.AppendLine("blade gets while it is live; a body is tried at " + string.Join(", ", System.Array.ConvertAll(Stands, N)) + " m from the eyes (its capsule's surface): the body steps in");
        text.AppendLine("(lunge), the arm reaches (up to " + N(reachIn) + "), and the blade passes that deep into the capsule (aimed for " + N(bite) + "; a zombie's flesh starts about 0.15 inside it).");
        foreach (var knife in knives)
        {
            var prefab = UnityEngine.Resources.Load<UnityEngine.GameObject>(knife.Model);
            var shape = KnifeSeat.Measure(prefab != null ? prefab.transform : null, knife.Scale);
            text.Append(knife.Id.PadRight(12)).Append(' ').Append(Model(knife).PadRight(13)).Append(" blade ").Append(N(shape.Length))
                .Append(" m from ").Append(shape.Base.sqrMagnitude > 1e-8f ? "its BladeBase marker" : "the grip").Append(" to its tip")
                .Append("  lunge ").Append(N(knife.Lunge)).Append("  cut ").Append(N(knife.BladeRadius * 2f)).AppendLine(" m thick");
            if (prefab == null) problems.Add($"{knife.Id}: no model at Resources/{knife.Model}: nothing to hold, the fist's own length cuts.");
            else if (!shape.Marked) problems.Add($"{knife.Id}: the model {Model(knife)} has no Tip marker: its blade is taken as 0.2 m along its -Z.");
            Blade(text, problems, knife, shape, "light", knife.SlashClip, bite, reachIn);
            if (knife.BackslashClip != knife.SlashClip) Blade(text, problems, knife, shape, "back", knife.BackslashClip, bite, reachIn);
            if (knife.StabClip != knife.SlashClip) Blade(text, problems, knife, shape, "heavy", knife.StabClip, bite, reachIn);
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

    // One clip's blade: how far ahead it gets, how far its tip travels through its marks and how fast at most (so how many
    // steps the cut takes between two frames at 60 a second), and a swing of it at bodies at the set distances.
    private static void Blade(StringBuilder text, List<string> problems, ClassKnives.Knife knife, in KnifeSeat.Shape shape, string kind, string table,
        float bite, float reachIn)
    {
        var clip = KnifeSwing.Of(table);
        if (clip == null || !clip.Cuts) return;
        float ahead = KnifeSeat.Reach(clip, knife, KnifeSeat.NominalHand, shape);
        float travel = 0f, fastest = 0f;
        UnityEngine.Vector3 last = default;
        bool has = false;
        for (float time = clip.ActiveFrom; time <= clip.ActiveTo + 1e-5f; time += Step)
        {
            if (!clip.Sample(time, out var key) || !KnifeSeat.Blade(key, knife, KnifeSeat.NominalHand, shape, out _, out var tip)) continue;
            if (has)
            {
                float moved = UnityEngine.Vector3.Distance(last, tip);
                travel += moved;
                fastest = System.Math.Max(fastest, moved / Step * knife.AttackSpeed);
            }
            last = tip;
            has = true;
        }
        text.Append("    ").Append(kind.PadRight(6)).Append((table ?? "").PadRight(31)).Append("ahead ").Append(N(ahead).PadRight(6))
            .Append("tip travels ").Append(N(travel)).Append(" m, at most ").Append(N(fastest)).Append(" m/s (")
            .Append(System.Math.Max(1, (int)System.Math.Ceiling(fastest / 60f / .05f))).Append(" steps a frame at 60)  |");
        foreach (float stand in Stands)
        {
            float deep = PlayerMeleeCombat.Approach(stand, stand, ahead, knife.Lunge, bite, reachIn, out float step, out float arm);
            bool seen = stand <= ahead + knife.Lunge + reachIn;
            text.Append("  at ").Append(N(stand)).Append(": ");
            if (!seen) text.Append("too far");
            else text.Append("step ").Append(N(step)).Append(" arm ").Append(N(arm)).Append(deep >= 0f ? " in " : " SHORT ").Append(N(System.Math.Abs(deep)));
            if (seen && deep < .15f && stand <= 1.4f)
                problems.Add($"{knife.Id}: the {kind} clip '{table}' gets only {N(deep)} m into a body {N(stand)} m off (its blade gets {N(ahead)} m ahead): a zombie's flesh starts about 0.15 m inside its capsule.");
        }
        text.AppendLine();
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
