using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Which side of the squad the horde may come from, on the mission's flow (Tables/SpawnRules `from`).</summary>
public enum SpawnSide : byte
{
    /// <summary>Only from places farther along the mission's route than the squad's front: the squad is travelling.</summary>
    Ahead,
    /// <summary>From every side: the squad is working one place (a hold, an escape, a search for fuel cans).</summary>
    Around
}

/// <summary>What cleared ground is to the horde (Tables/SpawnRules `cleared`).</summary>
public enum ClearedGround : byte
{
    /// <summary>Nothing comes out of it again (user, 2026-11-18: "한 방을 클리어했다면 양자역학이 아닌 이상 거기서 다시
    /// 좀비가 출몰해 나오는 게 이상하죠").</summary>
    Shut,
    /// <summary>They come in through it from beyond: the finale draws the whole neighbourhood (user: "피날레 구간을 말하는
    /// 것이라면 소탕한 곳에서 나와도 될 것 같습니다").</summary>
    Through
}

/// <summary>What happens when no place within reach will do (Tables/SpawnRules `otherwise`).</summary>
public enum SpawnOtherwise : byte
{
    /// <summary>Nobody comes for now.</summary>
    Wait,
    /// <summary>The nearest place that will do farther off.</summary>
    Farther
}

/// <summary>What a spawn point is (Tables/SpawnPoints `role`).</summary>
public enum SpawnRole : byte
{
    /// <summary>A place on open ground the squad walks over.</summary>
    Open,
    /// <summary>A room or passage made for them to come out of, from beyond where the squad can go: it cannot be
    /// cleared, and it goes before open ground.</summary>
    Room
}

/// <summary>
/// Where the horde may come from during one step of a chapter: one rule, read from Tables/SpawnRules for that step
/// (SpawnRules.For). What each value means in the world is said where SpawnPick weighs it.
/// </summary>
internal sealed class SpawnRule
{
    /// <summary>The rows it was read from, the most particular first (for the record).</summary>
    public string Rows = "";
    public SpawnSide From = SpawnSide.Ahead;
    public ClearedGround Cleared = ClearedGround.Shut;
    public SpawnOtherwise Otherwise = SpawnOtherwise.Farther;
    /// <summary>No nearer than this to anyone, in a straight line (m).</summary>
    public float Nearest = 15f;
    /// <summary>No farther than this from the player they head for, by walking (m).</summary>
    public float Farthest = 60f;
    /// <summary>Behind the back of the player they would go for, no nearer than this by walking (m): nobody gets one
    /// that appears behind them and is on them in a moment (user: "뒤는 안 됩니다").</summary>
    public float Behind = 30f;
    /// <summary>Nearer than this (m), a place inside someone's view (ViewAngle, degrees across) is never used, hidden or not.</summary>
    public float ViewNear = 20f, ViewAngle = 110f;
}

/// <summary>
/// The spawn rules as the tables give them (user, 2026-11-18: "적 스폰 위치 및 조건 전면 개편 … 전용 스폰 룸/통로나 아직
/// 정리하지 않은 미개척 구역에서 몰려오도록"). Tables/SpawnRules: the row Default is the rule of every step; a row named
/// after a kind of step (Hold, Collect …) changes it for that kind, the row Finale for the chapter's finale (its
/// escape, and the hold right before it), one named chapter.Kind or chapter.Finale (Ch1_Rooftops.Hold) for one chapter,
/// and one named after a step (Ch1.06) for that step alone. A cell left empty is the next row's, so a row changes only
/// what it writes. The Default row also carries the model's own values (grain, lookEvery, perFrame, reach, clearWithin,
/// relayEvery: SpawnGround, SpawnRoute). Tables/SpawnPoints says what a map's points are and adds points of its own:
/// a row names a point of the scene (by chapter and the point's name) and gives it a role, and a row whose name no
/// point of the scene has, with a place (x, y, z), is a new point there. So a spawn room is marked or added without a
/// rebuild or a line of code, by the user (user, 2026-11-18: "맵별로 센스 있게. 근데 뭐 방법만 알려 주면 알아
/// 하겠습니다") and by a mod as well.
/// </summary>
internal static class SpawnRules
{
    public const string TableName = "SpawnRules", PointsTable = "SpawnPoints", DefaultRow = "Default", FinaleRow = "Finale";

    private static readonly Dictionary<string, SpawnRule> rules = new Dictionary<string, SpawnRule>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IReadOnlyDictionary<string, string>> chain = new List<IReadOnlyDictionary<string, string>>();
    private static readonly HashSet<string> reported = new HashSet<string>();
    private static Dictionary<string, SpawnRole> roles;
    private static Dictionary<string, List<TablePoint>> added;

    /// <summary>A point Tables/SpawnPoints adds to a chapter: its name, what it is and where it stands (feet).</summary>
    public struct TablePoint
    {
        public string Name;
        public SpawnRole Role;
        public Vector3 At;
    }

    /// <summary>Goes up each time the tables are read again (a new Play, a table saved while playing): whoever built
    /// something from them builds it again when this is no longer what it was.</summary>
    public static int Version { get; private set; }

    // The model's own values (the Default row), read once a Play and again when the table is saved.
    private static bool modelRead;
    private static float grain, lookEvery, reach, clearWithin, relayEvery;
    private static int perFrame;

    /// <summary>How far apart the places along the mission's route are that everything is measured against (m).</summary>
    public static float Grain { get { ReadModel(); return grain; } }

    /// <summary>A place is cleared when someone sees it from within this far with nothing alive within this far of it (m).</summary>
    public static float ClearWithin { get { ReadModel(); return clearWithin; } }

    /// <summary>How often the route is laid again though nothing told of a change (s): a door broken or a gate opened
    /// joins the walk mesh, and nothing says so.</summary>
    public static float RelayEvery { get { ReadModel(); return relayEvery; } }

    /// <summary>How often each player's place on the route, and what is cleared, is looked at again (s).</summary>
    public static float LookEvery { get { ReadModel(); return lookEvery; } }

    /// <summary>How many spawn points find their place on a newly laid route in one frame.</summary>
    public static int PerFrame { get { ReadModel(); return perFrame; } }

    /// <summary>A spawn point looks for its walk to the route among the route's places within this far of it in a straight line (m).</summary>
    public static float Reach { get { ReadModel(); return reach; } }

    private static void ReadModel()
    {
        if (modelRead) return;
        modelRead = true;
        grain = Mathf.Max(.5f, GameTables.Number(TableName, DefaultRow, "grain", 2f));
        clearWithin = Mathf.Max(0f, GameTables.Number(TableName, DefaultRow, "clearWithin", 15f));
        relayEvery = Mathf.Max(1f, GameTables.Number(TableName, DefaultRow, "relayEvery", 5f));
        lookEvery = Mathf.Max(.1f, GameTables.Number(TableName, DefaultRow, "lookEvery", .5f));
        perFrame = Mathf.Max(1, Mathf.RoundToInt(GameTables.Number(TableName, DefaultRow, "perFrame", 1f)));
        reach = Mathf.Max(1f, GameTables.Number(TableName, DefaultRow, "reach", 80f));
    }

    /// <summary>
    /// The rule of this step of this chapter (the scene's name); the Default row's for no step. <paramref name="finale"/>:
    /// the step belongs to the chapter's finale (IsFinale), whose row goes before its kind's.
    /// </summary>
    public static SpawnRule For(string chapter, CampaignStep step, bool finale)
    {
        string stepId = step != null ? step.Id ?? "" : "", key = (chapter ?? "") + "\n" + stepId + (finale ? "\nfinale" : "");
        if (rules.TryGetValue(key, out var known)) return known;
        chain.Clear();
        var rule = new SpawnRule();
        if (step != null)
        {
            Take(stepId, rule);
            if (finale) Take((chapter ?? "") + "." + FinaleRow, rule);
            Take((chapter ?? "") + "." + step.Kind, rule);
            if (finale) Take(FinaleRow, rule);
            Take(step.Kind.ToString(), rule);
        }
        Take(DefaultRow, rule);
        if (chain.Count == 0) Report($"Tables/{TableName}에 '{DefaultRow}' 줄이 없어서 코드에 적힌 기본 규칙을 씁니다.");
        rule.From = Word("from", rule.From);
        rule.Cleared = Word("cleared", rule.Cleared);
        rule.Otherwise = Word("otherwise", rule.Otherwise);
        rule.Nearest = Mathf.Max(0f, Number("nearest", rule.Nearest));
        rule.Farthest = Mathf.Max(rule.Nearest, Number("farthest", rule.Farthest));
        rule.Behind = Mathf.Max(0f, Number("behind", rule.Behind));
        rule.ViewNear = Mathf.Max(0f, Number("viewNear", rule.ViewNear));
        rule.ViewAngle = Mathf.Clamp(Number("viewAngle", rule.ViewAngle), 0f, 360f);
        return rules[key] = rule;
    }

    /// <summary>
    /// The step at this place of a chapter's steps belongs to its finale: the escape, and a hold right before it. The
    /// finale is the one time the whole neighbourhood comes, through cleared ground too.
    /// </summary>
    public static bool IsFinale(IReadOnlyList<CampaignStep> steps, int index)
    {
        if (steps == null || index < 0 || index >= steps.Count) return false;
        var kind = steps[index].Kind;
        return kind == CampaignStepKind.Escape
            || kind == CampaignStepKind.Hold && index + 1 < steps.Count && steps[index + 1].Kind == CampaignStepKind.Escape;
    }

    /// <summary>What the point of this name is in this chapter: a room if Tables/SpawnPoints says so, else open ground.</summary>
    public static SpawnRole RoleOf(string chapter, string point)
    {
        ReadPoints();
        return roles.TryGetValue((chapter ?? "") + "\n" + (point ?? ""), out var found) ? found : SpawnRole.Open;
    }

    /// <summary>The rows of Tables/SpawnPoints for this chapter that carry a place: points to add where the scene has
    /// none of that name (CampaignHorde).</summary>
    public static IReadOnlyList<TablePoint> PlacedIn(string chapter)
    {
        ReadPoints();
        return added.TryGetValue(chapter ?? "", out var list) ? list : (IReadOnlyList<TablePoint>)Array.Empty<TablePoint>();
    }

    /// <summary>
    /// A row of Tables/SpawnPoints for a point of this name at this place, as the table writes it (id, chapter, point,
    /// role, x, y, z): what the console's /spawns here hands the user to paste in.
    /// </summary>
    public static string RowFor(string chapter, string point, SpawnRole role, Vector3 at)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return string.Join(",", chapter + "." + point, chapter, point, role.ToString().ToLowerInvariant(),
            at.x.ToString("0.##", culture), at.y.ToString("0.##", culture), at.z.ToString("0.##", culture));
    }

    // Tables/SpawnPoints, once: every row's role by chapter and point, and the rows that carry a place.
    private static void ReadPoints()
    {
        if (roles != null) return;
        roles = new Dictionary<string, SpawnRole>(StringComparer.OrdinalIgnoreCase);
        added = new Dictionary<string, List<TablePoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in GameTables.RowIds(PointsTable))
        {
            if (!GameTables.TryGetRow(PointsTable, id, out var row)) continue;
            string chapter = row.Text("chapter"), point = row.Text("point"), text = row.Text("role", "room");
            if (chapter.Length == 0 || point.Length == 0) { Report($"Tables/{PointsTable} [{id}]: chapter와 point가 있어야 합니다."); continue; }
            if (!Enum.TryParse(text, true, out SpawnRole role) || !Enum.IsDefined(typeof(SpawnRole), role))
            {
                Report($"Tables/{PointsTable} [{id}] role '{text}': open 또는 room이어야 합니다.");
                continue;
            }
            roles[chapter + "\n" + point] = role;
            // A place wants all three numbers; none of them is a row for a point the scene has.
            bool x = TableCells.TryNumber(row.Text("x"), out float px), y = TableCells.TryNumber(row.Text("y"), out float py),
                z = TableCells.TryNumber(row.Text("z"), out float pz);
            if (!x && !y && !z) continue;
            if (!x || !y || !z) { Report($"Tables/{PointsTable} [{id}]: 자리는 x, y, z 세 칸이 모두 숫자여야 합니다."); continue; }
            if (!added.TryGetValue(chapter, out var list)) added[chapter] = list = new List<TablePoint>();
            list.Add(new TablePoint { Name = point, Role = role, At = new Vector3(px, py, pz) });
        }
    }

    private static void Take(string id, SpawnRule rule)
    {
        if (string.IsNullOrEmpty(id) || !GameTables.TryGetRow(TableName, id, out var row)) return;
        chain.Add(row);
        rule.Rows += (rule.Rows.Length > 0 ? " > " : "") + id;
    }

    // The first row of the chain that writes this cell says it.
    private static bool Cell(string column, out string text)
    {
        foreach (var row in chain)
        {
            text = row.Text(column);
            if (text.Length > 0) return true;
        }
        text = "";
        return false;
    }

    private static float Number(string column, float fallback)
    {
        if (!Cell(column, out string text)) return fallback;
        if (TableCells.TryNumber(text, out float value)) return value;
        Report($"Tables/{TableName} '{column}' = '{text}': 숫자가 아닙니다.");
        return fallback;
    }

    private static T Word<T>(string column, T fallback) where T : struct
    {
        if (!Cell(column, out string text)) return fallback;
        if (Enum.TryParse(text, true, out T value) && Enum.IsDefined(typeof(T), value)) return value;
        Report($"Tables/{TableName} '{column}' = '{text}': {string.Join(" / ", Enum.GetNames(typeof(T))).ToLowerInvariant()} 중 하나여야 합니다.");
        return fallback;
    }

    private static void Report(string message)
    {
        if (reported.Add(message)) Debug.LogWarning("[Spawns] " + message);
    }

    // A new Play and a table saved while playing read the rules again.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Forget();
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget()
    {
        rules.Clear();
        reported.Clear();
        roles = null;
        added = null;
        modelRead = false;
        Version++;
    }
}
