using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// What this PC's player has done, kept for the title screen's 기록 page: totals (kills, headshots, going down, dying,
/// friends got up, team wipes, games, play time); each campaign chapter's line (escapes with the fastest time, when it
/// was set and the fewest deaths, and every game played on it: how many, how long, kills, headshots, going down, deaths,
/// the most kills in one, when first escaped and last played) for the chapter's own page; and the last games. A game is
/// one map from loading it to leaving it; it counts as escaped once its chapter's ending shows (CampaignRun). Only this
/// player's own doings count, in a room too. Saved as records.json in the game's save folder when a game ends or the
/// game closes. Never shown during play.
/// </summary>
public static class PlayerRecords
{
    private const string FileName = "records.json";
    // More games are kept than the page lists (8), so a chapter's page can pick out its own last few.
    private const int RecentKept = 40;
    // A game left this quickly with nothing done is a look round, not a game.
    private const float ShortestKept = 20f;
    // 1 (0.8.34): a chapter's line counts every game on it, not only escapes; older files are brought up once (Upgrade).
    private const int Version = 1;

    [Serializable]
    public sealed class Chapter
    {
        public string map;
        public int escapes;
        /// <summary>Fastest escape (s); 0 until one.</summary>
        public float best;
        /// <summary>Fewest deaths in an escape; -1 until one.</summary>
        public int fewestDeaths = -1;
        /// <summary>Games counted on it, escaped or not, and this player's kills, headshots, going down and deaths in them.</summary>
        public int games, kills, headshots, downs, deaths;
        /// <summary>The most kills in one game on it.</summary>
        public int mostKills;
        /// <summary>Time spent in it (s), short looks round too.</summary>
        public double seconds;
        /// <summary>When the fastest escape was, when it was first escaped and when it was last played ("yyyy-MM-dd HH:mm"; empty if not known).</summary>
        public string bestWhen, firstEscape, last;
    }

    [Serializable]
    public sealed class Game
    {
        public string map;
        public bool escaped;
        public float seconds;
        public int kills, headshots, downs, deaths;
        /// <summary>When it ended, "yyyy-MM-dd HH:mm".</summary>
        public string when;
    }

    [Serializable]
    public sealed class Data
    {
        /// <summary>The file's shape (Version); 0 in files from before 0.8.34.</summary>
        public int version;
        public long kills, headshots, downs, deaths, revives, wipes, games, escapes;
        public double seconds;
        public List<Chapter> chapters = new List<Chapter>();
        public List<Game> recent = new List<Game>();
    }

    private static Data data;
    private static Game current;
    private static float startedAt;

    /// <summary>Something new was kept.</summary>
    public static event Action Changed;

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    public static Data Current
    {
        get
        {
            if (data == null) Load();
            return data;
        }
    }

    /// <summary>The chapter's line, or null if it was never played.</summary>
    public static Chapter ChapterOf(string map)
    {
        foreach (var chapter in Current.chapters) if (chapter.map == map) return chapter;
        return null;
    }

    // Only the campaign's chapters (Tables/CampaignChapters) get a line; the practice maps count in the totals alone.
    private static bool IsChapter(string map)
        => !string.IsNullOrEmpty(map) && GameTables.TryGetRow(CampaignChapter.TableName, map, out _);

    // The chapter's line in these records, made the first time.
    private static Chapter LineOf(Data kept, string map)
    {
        foreach (var chapter in kept.chapters) if (chapter.map == map) return chapter;
        var made = new Chapter { map = map };
        kept.chapters.Add(made);
        return made;
    }

    private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        data = null;
        current = null;
        Changed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Listen()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.quitting -= EndGame;
        Application.quitting += EndGame;
        CombatDamageEvents.Damaged -= OnDamaged;
        CombatDamageEvents.Damaged += OnDamaged;
    }

    // A map from Tables/Maps starts a game; any other whole-scene load (the title screen) only ends one.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        EndGame();
        if (Array.IndexOf(MapTable.All(), scene.name) < 0) return;
        current = new Game { map = scene.name };
        startedAt = Time.realtimeSinceStartup;
    }

    private static void EndGame()
    {
        if (current == null) return;
        var game = current;
        current = null;
        var kept = Current;
        float played = Time.realtimeSinceStartup - startedAt;
        kept.seconds += played;
        if (!game.escaped) game.seconds = played;
        var line = IsChapter(game.map) ? LineOf(kept, game.map) : null;
        if (line != null) line.seconds += played;
        if (game.escaped || played >= ShortestKept || game.kills > 0)
        {
            kept.games++;
            game.when = Stamp();
            kept.recent.Insert(0, game);
            if (kept.recent.Count > RecentKept) kept.recent.RemoveRange(RecentKept, kept.recent.Count - RecentKept);
            if (line != null) Tally(line, game);
        }
        Save();
        Changed?.Invoke();
    }

    // One more game on a chapter's line (its time is added apart: a look round counts there but is no game).
    private static void Tally(Chapter line, Game game)
    {
        line.games++;
        line.kills += game.kills;
        line.headshots += game.headshots;
        line.downs += game.downs;
        line.deaths += game.deaths;
        line.mostKills = Math.Max(line.mostKills, game.kills);
        // The stamps sort as text, so the newest is kept whatever order the games come in.
        if (!string.IsNullOrEmpty(game.when) && (string.IsNullOrEmpty(line.last) || string.CompareOrdinal(game.when, line.last) > 0))
            line.last = game.when;
    }

    /// <summary>The chapter's ending is showing: the game is an escape, timed to now.</summary>
    public static void ChapterEscaped(string map)
    {
        if (current == null || current.escaped || current.map != map) return;
        current.escaped = true;
        current.seconds = Time.realtimeSinceStartup - startedAt;
        var kept = Current;
        kept.escapes++;
        var chapter = LineOf(kept, map);
        string now = Stamp();
        chapter.escapes++;
        // Only a true first: an older file's line may have escapes from before first escapes were kept.
        if (chapter.escapes == 1) chapter.firstEscape = now;
        if (chapter.best <= 0f || current.seconds < chapter.best)
        {
            chapter.best = current.seconds;
            chapter.bestWhen = now;
        }
        if (chapter.fewestDeaths < 0 || current.deaths < chapter.fewestDeaths) chapter.fewestDeaths = current.deaths;
        Save();
        Changed?.Invoke();
    }

    /// <summary>This player went down.</summary>
    public static void WentDown()
    {
        if (current == null) return;
        current.downs++;
        Current.downs++;
    }

    /// <summary>This player died.</summary>
    public static void Died()
    {
        if (current == null) return;
        current.deaths++;
        Current.deaths++;
    }

    /// <summary>This player got a downed friend up.</summary>
    public static void Revived()
    {
        if (current == null) return;
        Current.revives++;
    }

    /// <summary>The team was wiped.</summary>
    public static void Wiped()
    {
        if (current == null) return;
        Current.wipes++;
    }

    // This player's kills of real enemies (not practice targets), and the headshots among them (a bullet's critical,
    // as the kill feed counts them).
    private static void OnDamaged(CombatDamageReport report)
    {
        if (current == null || !report.IsKillingBlow || !(report.Target is EnemyHealth)) return;
        var hit = report.Hit;
        if (hit.Kind == DamageKind.Fall || hit.Source == null || report.ByOtherPlayer || hit.Source.GetComponentInParent<PlayerMovement>() == null)
            return;
        var kept = Current;
        current.kills++;
        kept.kills++;
        if (!report.IsCritical || hit.Method != DamageMethod.Bullet) return;
        current.headshots++;
        kept.headshots++;
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
            Debug.LogWarning("[기록] 기록 파일을 읽지 못했어요: " + problem.Message);
        }
        if (data == null) data = new Data();
        if (data.chapters == null) data.chapters = new List<Chapter>();
        if (data.recent == null) data.recent = new List<Game>();
        Upgrade(data);
    }

    // Files from before 0.8.34 kept only escapes per chapter. The games still in the recent list are counted into their
    // chapters once, and escapes older than those count as games too, so a chapter's page starts from what is known
    // rather than from nothing (kills in games already gone from the list are lost). Done again from the file until the
    // next save writes the new version, so it never counts twice.
    private static void Upgrade(Data loaded)
    {
        if (loaded.version >= Version) return;
        var escapedSeen = new Dictionary<string, int>();
        foreach (var game in loaded.recent)
        {
            if (game == null || !IsChapter(game.map)) continue;
            var line = LineOf(loaded, game.map);
            Tally(line, game);
            line.seconds += game.seconds;
            if (!game.escaped) continue;
            escapedSeen[game.map] = escapedSeen.TryGetValue(game.map, out int seen) ? seen + 1 : 1;
            // The list is newest first, so a best matched twice keeps the newer day; close enough for a note.
            if (line.best > 0f && string.IsNullOrEmpty(line.bestWhen) && Mathf.Abs(game.seconds - line.best) < .5f) line.bestWhen = game.when;
        }
        foreach (var line in loaded.chapters)
        {
            if (line == null) continue;
            int seen = escapedSeen.TryGetValue(line.map ?? "", out int count) ? count : 0;
            line.games += Math.Max(0, line.escapes - seen);
            // The first escape is known only when every escape is still in the list: the oldest of them.
            if (line.escapes <= 0 || line.escapes != seen || !string.IsNullOrEmpty(line.firstEscape)) continue;
            foreach (var game in loaded.recent)
                if (game != null && game.escaped && game.map == line.map) line.firstEscape = game.when;
        }
        loaded.version = Version;
    }

    private static void Save()
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true)); }
        catch (Exception problem) { Debug.LogWarning("[기록] 기록을 저장하지 못했어요: " + problem.Message); }
    }
}
