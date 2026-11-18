using System.IO;
using UnityEngine;

// The console's /tower: what the tower would do, said without climbing it.
public sealed partial class TowerRun
{
    /// <summary>
    /// /tower rooms [day]: every room of today's program (or of a day given as 20261118), lane by lane, plain and after
    /// a wire doubled it, as TowerPlan.RoomOf makes it: how many come out, how many of them are ranked elite, and each
    /// kind with its toughness (a champion marked *). Written to Logs/tower-rooms.txt in the editor. The check for "an
    /// ordinary zombie never comes out an elite": elites are at most one a room, and only the champion.
    /// </summary>
    public static string Command(string args)
    {
        string[] words = (args ?? "").Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words[0] != "rooms") return "rooms [날짜 20261118] — 그날 방마다 나올 좀비와 엘리트 수"; // dev-text
        int seed = words.Length > 1 && int.TryParse(words[1], out int day) ? day : TodaySeed();
        // The plan's rows are bound to an owner; this one lives only for the dump.
        var holder = new GameObject("TowerPlan (dump)");
        try
        {
            var plan = new TowerPlan(seed, holder);
            float crowd = GameTables.Number(TableName, TableRow, "crowdPerPlayer", .5f);
            float champion = GameTables.Number(TableName, TableRow, "championPerPlayer", .6f);
            string text = "seed " + seed + " · stealth lane " + (char)('A' + plan.StealthLane) + "\n" + plan.Describe(crowd, champion);
            int wrong = 0;
            for (int floor = 1; floor <= TowerLayout.Floors; floor++)
                for (int lane = 0; lane < TowerLayout.Lanes; lane++)
                    for (int doubled = 0; doubled < 2; doubled++)
                        foreach (var spawn in plan.RoomOf(floor, lane, 1, crowd, champion, doubled == 1, plan.Quiet(floor, lane)))
                            if (spawn.Rank != EnemyRank.Normal && !spawn.Champion) wrong++;
            text += "ranked without being the room's champion: " + wrong + "\n";
#if UNITY_EDITOR
            string path = Path.Combine(Application.dataPath, "..", "Logs", "tower-rooms.txt");
            try { File.WriteAllText(path, text); }
            catch (System.Exception problem) { Debug.LogWarning("[Tower] Logs/tower-rooms.txt: " + problem.Message); }
#endif
            Debug.Log("[Tower] rooms of " + seed + "\n" + text);
            return "방 " + TowerLayout.Floors * TowerLayout.Lanes + "개 · 챔피언이 아닌데 엘리트인 좀비 " + wrong + " · Logs/tower-rooms.txt"; // dev-text
        }
        finally
        {
            Destroy(holder);
        }
    }
}
