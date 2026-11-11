using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Multiplayer plan stage 6E: each class's growth on this PC, shown only as a rank insignia (the user's pick: no
/// experience or level numbers anywhere, the insignia climbing only when a run ends). A game is one map from loading it
/// to leaving it, as PlayerRecords counts it; what it adds is the growth earned meanwhile (Wallet.Experience: every
/// point, plus a share of the money kept). The chapter's ending settles it and shows the climb (RankClimb, from
/// CampaignRun); a game left without one still adds it, unseen. Ranks are Tables/ClassRanks rows (24, the LAYERLAB Space
/// Rank insignia in Resources/UI/Ranks); at prestigeAt (Tables/ClassGrowth) the class starts again from the first rank
/// with one more pink diamond, cosmetic only. Classes are Tables/Classes rows: one stand-in until the user's class design
/// fills them (stage 7). Saved as classes.json in the game's save folder.
/// </summary>
public static class ClassGrowth
{
    public const string ClassTable = "Classes", RankTable = "ClassRanks", GrowthTable = "ClassGrowth", GrowthRow = "Default";
    private const string FileName = "classes.json", RankFolder = "UI/Ranks/";

    [Serializable]
    public sealed class Entry
    {
        public string id;
        public long points;
        public int prestige;
    }

    [Serializable]
    public sealed class Data
    {
        /// <summary>The class this player plays; empty for the table's first.</summary>
        public string current;
        public List<Entry> classes = new List<Entry>();
    }

    /// <summary>What one game did to the class it was played as: the insignia before and after (RankClimb).</summary>
    public sealed class Climb
    {
        public string ClassId, ClassTitle;
        public int RankBefore, RankAfter, PrestigeBefore, PrestigeAfter;
        /// <summary>It went up a rank, or round to a new prestige.</summary>
        public bool Rose => PrestigeAfter > PrestigeBefore || RankAfter > RankBefore;
    }

    private struct Rank
    {
        public long Points;
        public string Icon;
    }

    private static Data data;
    private static List<Rank> ranks;
    private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
    private static bool playing, settled;
    private static long baseline;

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        data = null;
        ranks = null;
        icons.Clear();
        playing = settled = false;
        baseline = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Listen()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.quitting -= EndGame;
        Application.quitting += EndGame;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() => ranks = null;

    private static Data Current
    {
        get
        {
            if (data == null) Load();
            return data;
        }
    }

    // A map from Tables/Maps starts a game; any other whole-scene load (the title screen) only ends one.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        EndGame();
        if (Array.IndexOf(MapTable.All(), scene.name) < 0) return;
        playing = true;
        settled = false;
        baseline = Wallet.Experience;
    }

    /// <summary>
    /// The game is being left for the title screen (MainMenuScreen, just before it sets the score back to 0): what it
    /// earned counts now, while the score still holds it. Waiting for the scene's load, as before, read it after the
    /// reset and added next to nothing.
    /// </summary>
    public static void Leave() => EndGame();

    // Left without an ending: what it earned still counts, unseen.
    private static void EndGame()
    {
        if (!playing) return;
        playing = false;
        if (!settled) Award(Earned());
    }

    private static long Earned() => Math.Max(0L, Wallet.Experience - baseline);

    /// <summary>
    /// The chapter's ending (CampaignRun): this game's growth goes to the class now, once; null if it was settled
    /// already or no game is under way.
    /// </summary>
    public static Climb Settle()
    {
        if (!playing || settled) return null;
        settled = true;
        return Award(Earned());
    }

    private static Climb Award(long amount)
    {
        string id = CurrentId;
        var entry = EntryOf(id);
        var climb = new Climb
        {
            ClassId = id, ClassTitle = TitleOf(id), RankBefore = RankOf(entry.points), PrestigeBefore = entry.prestige
        };
        entry.points += Math.Max(0L, amount);
        long top = PrestigeAt;
        while (top > 0 && entry.points >= top)
        {
            entry.points -= top;
            entry.prestige++;
        }
        climb.RankAfter = RankOf(entry.points);
        climb.PrestigeAfter = entry.prestige;
        Save();
        return climb;
    }

    /// <summary>The class this player plays: their pick, or the table's first.</summary>
    public static string CurrentId
    {
        get
        {
            string picked = Current.current;
            if (!string.IsNullOrEmpty(picked) && Known(picked)) return picked;
            // Not picked yet: the first class whose abilities are built (Classes.csv ready), else the first.
            string first = null;
            foreach (string id in GameTables.RowIds(ClassTable))
            {
                first ??= id;
                if (GameTables.TryGetRow(ClassTable, id, out var row) && row.TryGetValue("ready", out string ready) && ready.Trim() == "1") return id;
            }
            return first ?? "Default";
        }
    }

    /// <summary>Plays this class from now on (the class pick, stage 7).</summary>
    public static void Choose(string id)
    {
        if (!Known(id)) return;
        Current.current = id;
        Save();
    }

    public static string TitleOf(string id)
        => GameTables.TryGetRow(ClassTable, id, out var row) && row.TryGetValue("title", out string title) && title.Trim().Length > 0
            ? title.Trim() : id;

    /// <summary>This class's rank now (1 is the first) and its prestige.</summary>
    public static int RankNow(string id) => RankOf(EntryOf(id).points);
    public static int PrestigeNow(string id) => EntryOf(id).prestige;

    /// <summary>How many ranks there are.</summary>
    public static int RankCount => Ranks().Count;

    /// <summary>Pink diamonds drawn under an insignia at most, one a prestige.</summary>
    public static int MostPips => Mathf.Max(0, Mathf.RoundToInt(Number("mostPips", 5f)));

    /// <summary>The insignia of this rank (1 is the first), or null.</summary>
    public static Texture2D Insignia(int rank)
    {
        var all = Ranks();
        if (all.Count == 0) return null;
        string icon = all[Mathf.Clamp(rank, 1, all.Count) - 1].Icon;
        if (string.IsNullOrEmpty(icon)) return null;
        if (!icons.TryGetValue(icon, out var texture) || texture == null)
            icons[icon] = texture = Resources.Load<Texture2D>(RankFolder + icon);
        return texture;
    }

    private static int RankOf(long points)
    {
        var all = Ranks();
        int rank = 1;
        for (int i = 0; i < all.Count; i++) if (points >= all[i].Points) rank = i + 1;
        return rank;
    }

    private static long PrestigeAt => (long)Number("prestigeAt", 1000000f);

    private static List<Rank> Ranks()
    {
        if (ranks != null) return ranks;
        ranks = new List<Rank>();
        foreach (string id in GameTables.RowIds(RankTable))
        {
            if (!GameTables.TryGetRow(RankTable, id, out var row)) continue;
            long points = row.TryGetValue("points", out string text)
                && long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0L;
            ranks.Add(new Rank { Points = points, Icon = row.TryGetValue("icon", out string icon) ? icon.Trim() : "" });
        }
        ranks.Sort((a, b) => a.Points.CompareTo(b.Points));
        return ranks;
    }

    private static float Number(string column, float fallback)
        => GameTables.TryGetRow(GrowthTable, GrowthRow, out var row) && row.TryGetValue(column, out string text)
            && float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    private static bool Known(string id) => GameTables.TryGetRow(ClassTable, id, out _);

    private static Entry EntryOf(string id)
    {
        foreach (var entry in Current.classes) if (entry != null && entry.id == id) return entry;
        var fresh = new Entry { id = id };
        Current.classes.Add(fresh);
        return fresh;
    }

    private static void Load()
    {
        data = null;
        try
        {
            if (File.Exists(FilePath)) data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[클래스] 성장 파일을 읽지 못했어요: " + problem.Message);
        }
        if (data == null) data = new Data();
        if (data.classes == null) data.classes = new List<Entry>();
    }

    private static void Save()
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true)); }
        catch (Exception problem) { Debug.LogWarning("[클래스] 성장을 저장하지 못했어요: " + problem.Message); }
    }
}
