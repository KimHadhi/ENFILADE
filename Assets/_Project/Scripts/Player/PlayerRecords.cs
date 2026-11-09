using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// What this PC's player has done, kept for the title screen's 기록 page: totals (kills, headshots, going down, dying,
/// friends got up, team wipes, games, play time), each campaign chapter's escapes with the fastest time and the fewest
/// deaths, and the last few games. A game is one map from loading it to leaving it; it counts as escaped once its
/// chapter's ending shows (CampaignRun). Only this player's own doings count, in a room too. Saved as records.json in
/// the game's save folder when a game ends or the game closes. Never shown during play.
/// </summary>
public static class PlayerRecords
{
    private const string FileName = "records.json";
    private const int RecentKept = 8;
    // A game left this quickly with nothing done is a look round, not a game.
    private const float ShortestKept = 20f;

    [Serializable]
    public sealed class Chapter
    {
        public string map;
        public int escapes;
        /// <summary>Fastest escape (s); 0 until one.</summary>
        public float best;
        /// <summary>Fewest deaths in an escape; -1 until one.</summary>
        public int fewestDeaths = -1;
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

    /// <summary>The chapter's line, or null if it was never escaped.</summary>
    public static Chapter ChapterOf(string map)
    {
        foreach (var chapter in Current.chapters) if (chapter.map == map) return chapter;
        return null;
    }

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
        if (game.escaped || played >= ShortestKept || game.kills > 0)
        {
            kept.games++;
            game.when = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            kept.recent.Insert(0, game);
            if (kept.recent.Count > RecentKept) kept.recent.RemoveRange(RecentKept, kept.recent.Count - RecentKept);
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>The chapter's ending is showing: the game is an escape, timed to now.</summary>
    public static void ChapterEscaped(string map)
    {
        if (current == null || current.escaped || current.map != map) return;
        current.escaped = true;
        current.seconds = Time.realtimeSinceStartup - startedAt;
        var kept = Current;
        kept.escapes++;
        var chapter = ChapterOf(map);
        if (chapter == null) kept.chapters.Add(chapter = new Chapter { map = map });
        chapter.escapes++;
        if (chapter.best <= 0f || current.seconds < chapter.best) chapter.best = current.seconds;
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
    }

    private static void Save()
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true)); }
        catch (Exception problem) { Debug.LogWarning("[기록] 기록을 저장하지 못했어요: " + problem.Message); }
    }
}
