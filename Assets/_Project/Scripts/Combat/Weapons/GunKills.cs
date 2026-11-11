using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Every gun counts its kills on this PC, as Counter-Strike's StatTrak and TF2's strange weapons do (user, 2026-11-11:
/// "팀포2의 이상한 무기처럼 킬 카운트 있는 무기 … 모든 총을 스텟트릭처럼 기본적으로 킬 카운트를 하게"; the plan's choice:
/// every gun counts, no separate strange copies). A bullet of this PC's own player that kills an enemy counts for the gun
/// in their hands, and its headshots too; at thresholds the count names a rank (Tables/KillCounter.csv Ranks.kills, the
/// names Texts.csv gunrank.N), worn before the gun's name like a strange weapon's. Kept in gun_kills.json beside the other
/// saves. Shown on the gun itself (GunKillPlate), when it is looked over (Y, AmmoHUD) and in the title screen's weapons tab.
/// </summary>
public static class GunKills
{
    public const string FileName = "gun_kills.json", TableName = "KillCounter";
    // Written at most this often while kills come in (s); a new rank, a scene change and quitting write at once.
    private const float SaveEvery = 5f;
    private static readonly int[] DefaultRanks = { 0, 25, 75, 150, 250, 400, 600, 850, 1200, 1700, 2500, 3500, 5000, 7500, 10000, 15000, 25000 };

    [Serializable]
    private sealed class Entry
    {
        public string gun;
        public int kills, headshots;
    }

    [Serializable]
    private sealed class Data
    {
        public List<Entry> guns = new List<Entry>();
    }

    private static Data data;
    private static int[] ranks;
    private static bool dirty;
    private static float savedAt = float.NegativeInfinity;

    /// <summary>A kill counted for a gun: the gun, its kills now, and whether that took it to a new rank.</summary>
    public static event Action<WeaponData, int, bool> Counted;

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        data = null;
        ranks = null;
        dirty = false;
        savedAt = float.NegativeInfinity;
        Counted = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Listen()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        CombatDamageEvents.Damaged += OnDamaged;
        Application.quitting -= Save;
        Application.quitting += Save;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameTables.Reloaded -= ForgetRanks;
        GameTables.Reloaded += ForgetRanks;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (dirty) Save();
    }

    private static void ForgetRanks() => ranks = null;

    /// <summary>The kills this gun has made on this PC.</summary>
    public static int Kills(WeaponData gun) => gun != null && Find(gun.name) is Entry entry ? entry.kills : 0;

    /// <summary>The headshot kills among them.</summary>
    public static int Headshots(WeaponData gun) => gun != null && Find(gun.name) is Entry entry ? entry.headshots : 0;

    /// <summary>The rank a gun with this many kills has: 0 for the first.</summary>
    public static int RankOf(int kills)
    {
        var at = Ranks;
        int rank = 0;
        for (int i = 0; i < at.Length; i++) if (kills >= at[i]) rank = i;
        return rank;
    }

    /// <summary>How many ranks there are.</summary>
    public static int RankCount => Ranks.Length;

    /// <summary>The kills the rank after this one starts at; -1 past the last.</summary>
    public static int NextAt(int rank) => rank + 1 < Ranks.Length ? Ranks[rank + 1] : -1;

    /// <summary>The kills this rank starts at.</summary>
    public static int RankStart(int rank) => Ranks[Mathf.Clamp(rank, 0, Ranks.Length - 1)];

    /// <summary>The rank's name (Texts.csv gunrank.N).</summary>
    public static string RankName(int rank) => Texts.Get("gunrank." + Mathf.Clamp(rank, 0, Ranks.Length - 1));

    /// <summary>The gun's name with its rank before it, as a strange weapon's: "피 맛을 본 MX16A4".</summary>
    public static string Title(WeaponData gun)
        => gun == null ? "" : Texts.Format("gunkills.title", RankName(RankOf(Kills(gun))), gun.DisplayName);

    // This PC's own player's bullet killed an enemy (as the records count it, PlayerRecords): the gun in their hands
    // made it.
    private static void OnDamaged(CombatDamageReport report)
    {
        if (!report.IsKillingBlow || !(report.Target is EnemyHealth)) return;
        var hit = report.Hit;
        if (hit.Method != DamageMethod.Bullet || hit.Source == null || report.ByOtherPlayer) return;
        var player = hit.Source.GetComponentInParent<PlayerMovement>();
        if (player == null || !player.IsLocal || player.IsNetworkDriven) return;
        var weapons = player.GetComponentInChildren<WeaponController>();
        var gun = weapons != null ? weapons.EquippedData : null;
        if (gun == null || gun.Category == WeaponCategory.Melee) return;
        Count(gun, report.IsCritical);
    }

    private static void Count(WeaponData gun, bool headshot)
    {
        var entry = Find(gun.name);
        if (entry == null) Load().guns.Add(entry = new Entry { gun = gun.name });
        int before = RankOf(entry.kills);
        entry.kills++;
        if (headshot) entry.headshots++;
        bool rankedUp = RankOf(entry.kills) > before;
        dirty = true;
        if (rankedUp || Time.unscaledTime - savedAt > SaveEvery) Save();
        Counted?.Invoke(gun, entry.kills, rankedUp);
    }

    private static Entry Find(string gun)
    {
        foreach (var entry in Load().guns)
            if (entry != null && string.Equals(entry.gun, gun, StringComparison.Ordinal)) return entry;
        return null;
    }

    private static Data Load()
    {
        if (data != null) return data;
        try
        {
            if (File.Exists(FilePath)) data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[킬 카운트] 기록 파일을 읽지 못했어요: " + problem.Message);
        }
        if (data == null) data = new Data();
        if (data.guns == null) data.guns = new List<Entry>();
        return data;
    }

    private static void Save()
    {
        if (data == null) return;
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            dirty = false;
            savedAt = Time.unscaledTime;
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[킬 카운트] 기록 파일을 쓰지 못했어요: " + problem.Message);
        }
    }

    // The thresholds from the table, lowest first, the first always 0.
    private static int[] Ranks
    {
        get
        {
            if (ranks != null) return ranks;
            var list = new List<int>();
            if (GameTables.TryGetRow(TableName, "Ranks", out var row) && row.TryGetValue("kills", out string raw))
                foreach (string part in raw.Split(';'))
                    if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int at) && at >= 0) list.Add(at);
            if (list.Count == 0) list.AddRange(DefaultRanks);
            list.Sort();
            if (list[0] != 0) list.Insert(0, 0);
            return ranks = list.ToArray();
        }
    }
}
