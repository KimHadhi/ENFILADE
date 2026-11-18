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
        if (words.Length > 0 && words[0] == "lanes") return active != null ? active.DescribeLanes() : "타워 안에서만 된다"; // dev-text
        if (words.Length == 0 || words[0] != "rooms")
            return "rooms [날짜 20261118] — 그날 방마다 나올 좀비와 엘리트 수 · lanes — 지금 레인마다 상태·층·탄 사람과 그 자리(방장)"; // dev-text
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

    // /tower lanes: where the run stands, lane by lane: its state and floor, who boarded it (the host's list) and where
    // each of them is now, and which of its doors stand open on this PC.
    private string DescribeLanes()
    {
        var text = new System.Text.StringBuilder("phase " + phase + " · stretch " + segment + "\n");
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            text.Append((char)('A' + k)).Append(' ').Append(states[k]).Append(" floor ").Append(TowerLayout.FloorNumber(segment, floors[k]))
                .Append(" left ").Append(lefts[k]).Append(" · members");
            if (!IsHost) text.Append(" (the host knows)");
            foreach (ulong id in members[k])
            {
                var who = PlayerById(id);
                text.Append(' ').Append(id).Append(who == null ? " gone" : who.IsDead ? " dead"
                    : " at " + who.transform.position.ToString("0.0") + (TowerLayout.LaneAt(who.transform.position, segment) == k ? "" : " (not in the lane)"));
            }
            text.Append(" · open doors");
            foreach (var door in doors)
                if (door != null && door.Lane == k && door.Open) text.Append(' ').Append(door.Kind).Append('@').Append(door.Level);
            text.Append('\n');
        }
        Debug.Log("[Tower] lanes\n" + text);
        return text.ToString();
    }
}
