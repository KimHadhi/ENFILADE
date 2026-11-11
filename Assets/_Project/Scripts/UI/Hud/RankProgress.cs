using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Where the class played now stands on its insignia ladder while a game is on, read for the HUD (RankStrip under the
/// portrait, the Tab screen): the growth it had when the game began, what this game has added so far (kills, and the
/// share of the money held that counts when it ends), the insignia that adds up to, and the way to the next. The user
/// asked for it during play (2026-11-11: "경험치를 얼마나 받는지 잘 모르겠음 ... 내 현재 랭크가 어딘지, 그리고 지금까지
/// 내가 번 경험치로 나는 내 랭크를 얼마나 성장시킬 수 있는지 게임을 플레이하면서 바로 직관적으로"), which overturns the
/// 11-09 rule that growth shows only on the chapter's ending. It only reads: ClassGrowth still settles the game at its
/// end, with the same rule (a game is a map loaded on its own, counted from Wallet.Experience then), and its ranks
/// (Tables/ClassRanks), prestige (Tables/ClassGrowth) and the classes' saved growth (classes.json) are read as they are.
/// </summary>
public static class RankProgress
{
    /// <summary>One look at the ladder.</summary>
    public struct View
    {
        public string ClassId, Title;
        /// <summary>The insignia held when the game began, and the one this game's growth would make it (1 is the first).</summary>
        public int RankBefore, Rank;
        public int PrestigeBefore, Prestige;
        /// <summary>Growth held when the game began, and this game's: from kills and from the money held now.</summary>
        public long Stored, Kills, Kept;
        /// <summary>Within the insignia it adds up to: where it starts, where the next begins, and how far each part reaches.</summary>
        public long From, To, StoredReach, Reach;
        /// <summary>The last insignia: no next one to go for.</summary>
        public bool Top;
        /// <summary>A game is on (a map), so this game's growth counts.</summary>
        public bool Playing;

        /// <summary>The bar's parts as shares of the way from this insignia to the next: held, then held plus this game's.</summary>
        public float StoredShare => To > From ? Mathf.Clamp01((StoredReach - From) / (float)(To - From)) : 1f;
        public float Share => To > From ? Mathf.Clamp01((Reach - From) / (float)(To - From)) : 1f;
        /// <summary>Insignias this game's growth climbs, counting a prestige as the whole ladder.</summary>
        public int Climb => (Prestige - PrestigeBefore) * Mathf.Max(1, ClassGrowth.RankCount) + Rank - RankBefore;
        public long Earned => Kills + Kept;
        /// <summary>Growth still wanted for the next insignia.</summary>
        public long ToNext => Top ? 0L : Math.Max(0L, To - Reach);
    }

    private static List<long> ranks;
    private static bool playing;
    private static long baseExperience, baseScore;
    private static ClassGrowth.Data saved;
    private static DateTime savedAt;
    private static float lookedAt = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ranks = null;
        playing = false;
        baseExperience = baseScore = 0;
        saved = null;
        savedAt = default;
        lookedAt = float.NegativeInfinity;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Listen()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() => ranks = null;

    // ClassGrowth's own rule: a map loaded on its own starts a game, counted from the experience there is then.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        playing = Array.IndexOf(MapTable.All(), scene.name) >= 0;
        baseExperience = Wallet.Experience;
        baseScore = CombatFeedHUD.TotalScore;
        lookedAt = float.NegativeInfinity;
    }

    /// <summary>The class played now, as it stands.</summary>
    public static View Now()
    {
        string id = ClassGrowth.CurrentId;
        var view = new View { ClassId = id, Title = ClassGrowth.TitleOf(id), Playing = playing };
        view.Stored = StoredOf(id, out view.PrestigeBefore);
        if (playing)
        {
            view.Kills = Math.Max(0L, CombatFeedHUD.TotalScore - baseScore);
            view.Kept = Math.Max(0L, Wallet.Experience - baseExperience - view.Kills);
        }
        var all = Ranks();
        view.RankBefore = RankOf(all, view.Stored);
        long total = view.Stored + view.Earned;
        view.Prestige = view.PrestigeBefore;
        long top = PrestigeAt;
        while (top > 0 && total >= top)
        {
            total -= top;
            view.Prestige++;
        }
        bool wrapped = view.Prestige > view.PrestigeBefore;
        view.Rank = RankOf(all, total);
        view.Reach = total;
        // Once it goes round to a new prestige, what was held is all behind it.
        view.StoredReach = wrapped ? 0L : view.Stored;
        view.From = all.Count > 0 ? all[Mathf.Clamp(view.Rank - 1, 0, all.Count - 1)] : 0L;
        view.Top = view.Rank >= all.Count;
        view.To = view.Top ? (top > 0 ? top : view.From) : all[view.Rank];
        return view;
    }

    private static int RankOf(List<long> all, long points)
    {
        int rank = 1;
        for (int i = 0; i < all.Count; i++) if (points >= all[i]) rank = i + 1;
        return rank;
    }

    // The growth a class had saved (classes.json), read again only when the file has changed: ClassGrowth writes it at a
    // game's end and when a class is picked.
    private static long StoredOf(string id, out int prestige)
    {
        prestige = 0;
        if (Time.unscaledTime - lookedAt > 1f)
        {
            lookedAt = Time.unscaledTime;
            try
            {
                string path = ClassGrowth.FilePath;
                var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
                if (saved == null || stamp != savedAt)
                {
                    savedAt = stamp;
                    saved = stamp != default ? JsonUtility.FromJson<ClassGrowth.Data>(File.ReadAllText(path)) : null;
                }
            }
            catch (Exception)
            {
                saved = null; // unreadable for now: show the start of the ladder rather than break the HUD
            }
        }
        if (saved?.classes == null) return 0L;
        foreach (var entry in saved.classes)
            if (entry != null && entry.id == id)
            {
                prestige = entry.prestige;
                return Math.Max(0L, entry.points);
            }
        return 0L;
    }

    private static List<long> Ranks()
    {
        if (ranks != null) return ranks;
        ranks = new List<long>();
        foreach (string id in GameTables.RowIds(ClassGrowth.RankTable))
            if (GameTables.TryGetRow(ClassGrowth.RankTable, id, out var row) && row.TryGetValue("points", out string text)
                && long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long points))
                ranks.Add(points);
        ranks.Sort();
        return ranks;
    }

    private static long PrestigeAt
        => GameTables.TryGetRow(ClassGrowth.GrowthTable, ClassGrowth.GrowthRow, out var row) && row.TryGetValue("prestigeAt", out string text)
            && long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 1000000L;
}
