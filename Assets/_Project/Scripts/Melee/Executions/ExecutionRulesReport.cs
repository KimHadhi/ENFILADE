using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// The rules of takedowns and executions as text, so what the table says can be checked without playing (the user's
/// standing demand: a result is checked by whoever built it, not found by them while playing): every rule of
/// Tables/ExecutionRules.csv with what is wrong with it (a pair nobody made, a rule nothing can reach), the pairs no
/// rule plays, and for every posture of a zombie that is unaware or broken, which rule a player gets standing at each
/// place round it (ExecutionRules.Pick, the function the game itself asks). The editor writes it to
/// Logs/execution-rules.txt after every compile and whenever a table is saved (ExecutionRulesReportFile), so the file is
/// always what the game would do.
/// </summary>
public static class ExecutionRulesReport
{
    /// <summary>The postures a zombie's body names (ZombieBody.ExecutionPosture).</summary>
    public static readonly string[] Postures =
        { ExecutionRules.Stand, ExecutionRules.Sit, ExecutionRules.Feed, ExecutionRules.Crouch, ExecutionRules.Prone, ExecutionRules.Supine };

    // The player's places the grid asks about: eyes to its body (m), and round it from straight before it (degrees).
    private static readonly float[] Distances = { .5f, 1.2f, 1.3f, 2.1f, 2.2f, 4f, 4.1f };
    private const int BearingStep = 20;

    public static string Text()
    {
        var rules = ExecutionRules.All;
        var text = new StringBuilder();
        text.AppendLine("Takedown and execution rules (Tables/Melee/ExecutionRules.csv), as the game picks them (ExecutionRules.Pick).");
        text.AppendLine("bearing: where the player stands round the zombie, 0 = straight before it, 180 = straight behind, + = its right.");
        text.AppendLine("distance: the player's eyes to the nearest of its body (m). F = the interact key, L = the attack button with the melee slot out.");
        text.AppendLine();
        text.AppendLine("== Rules ==");
        var problems = new List<string>();
        var used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var pair = rule.Pair.Length > 0 ? ExecutionPairs.Find(rule.Pair) : null;
            if (rule.Pair.Length > 0) used.Add(rule.Pair);
            text.Append(Letter(i)).Append("  ").Append(rule.Id.PadRight(14))
                .Append(" state ").Append(rule.State.PadRight(7))
                .Append(" victim ").Append((rule.Unaware && rule.Broken ? "any" : rule.Unaware ? "unaware" : rule.Broken ? "broken" : "nobody").PadRight(8))
                .Append(" side ").Append(N(rule.Side).PadLeft(4)).Append(" +-").Append(N(rule.Cone).PadRight(4))
                .Append(" reach F ").Append(N(rule.Reach).PadRight(4)).Append(" L ").Append(N(rule.LungeReach).PadRight(4))
                .Append(" pair ").Append(rule.Pair.Length > 0 ? rule.Pair : "(none: the knife's own stab)");
            if (pair != null) text.Append("  [clips ").Append(N(pair.Length)).Append(" s, kill at ").Append(N(pair.Kill)).Append(" s]");
            text.AppendLine();
            if (rule.Pair.Length > 0 && pair == null) problems.Add($"rule '{rule.Id}' names the pair '{rule.Pair}', which Executions.csv does not have (or has with no length): it plays the knife's own stab.");
            if (!rule.Unaware && !rule.Broken) problems.Add($"rule '{rule.Id}' is for nobody (victim).");
            if (!rule.AnyState && System.Array.IndexOf(Postures, rule.State.ToLowerInvariant()) < 0)
                problems.Add($"rule '{rule.Id}' is for the posture '{rule.State}', which no zombie body names ({string.Join(", ", Postures)}).");
        }
        text.AppendLine();
        text.AppendLine("== Problems ==");
        foreach (string id in GameTables.RowIds(ExecutionPairs.TableName))
            if (ExecutionPairs.Find(id) != null && !used.Contains(id)) problems.Add($"pair '{id}' is played by no rule.");
        // A broken enemy must always be executable, from anywhere within some rule's reach.
        foreach (string posture in Postures)
            for (int bearing = -180; bearing < 180; bearing += 5)
                if (ExecutionRules.Pick(rules, posture, false, true, bearing, .5f, false) == null)
                {
                    problems.Add($"a broken zombie ({posture}) cannot be executed from bearing {bearing} even at 0.5 m.");
                    break;
                }
        if (problems.Count == 0) text.AppendLine("none");
        foreach (string problem in problems) text.AppendLine("- " + problem);
        text.AppendLine();
        text.AppendLine("== Picks: which rule a player gets at each place (letters above; . = nothing can be done there) ==");
        foreach (string posture in Postures)
            foreach (bool unaware in new[] { true, false })
                Grid(text, rules, posture, unaware);
        return text.ToString();
    }

    // One zombie (a posture, unaware or broken): rows of distance, columns of bearing, F and L for each.
    private static void Grid(StringBuilder text, IReadOnlyList<ExecutionRule> rules, string posture, bool unaware)
    {
        text.AppendLine();
        text.Append(posture).Append(unaware ? ", unaware" : ", broken").AppendLine();
        // Only one half is printed when the other mirrors it (every rule about 0 or 180).
        bool mirrored = true;
        foreach (var rule in rules)
            if (System.Math.Abs(rule.Side) > .01f && System.Math.Abs(System.Math.Abs(rule.Side) - 180f) > .01f) mirrored = false;
        int from = mirrored ? 0 : -180 + BearingStep;
        text.Append("  dist  key  ");
        for (int bearing = from; bearing <= 180; bearing += BearingStep) text.Append(bearing.ToString(CultureInfo.InvariantCulture).PadLeft(5));
        text.AppendLine(mirrored ? "   (bearing; the left side is the same)" : "   (bearing)");
        foreach (float distance in Distances)
            for (int key = 0; key < 2; key++)
            {
                text.Append("  ").Append(N(distance).PadRight(5)).Append(key == 0 ? " F    " : " L    ");
                for (int bearing = from; bearing <= 180; bearing += BearingStep)
                {
                    var rule = ExecutionRules.Pick(rules, posture, unaware, !unaware, bearing, distance, key == 1);
                    text.Append("    ").Append(rule != null ? Letter(IndexOf(rules, rule)) : '.');
                }
                text.AppendLine();
            }
    }

    private static int IndexOf(IReadOnlyList<ExecutionRule> rules, ExecutionRule rule)
    {
        for (int i = 0; i < rules.Count; i++)
            if (ReferenceEquals(rules[i], rule)) return i;
        return -1;
    }

    private static char Letter(int index) => index < 0 ? '?' : index < 26 ? (char)('a' + index) : (char)('A' + (index - 26) % 26);

    private static string N(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
