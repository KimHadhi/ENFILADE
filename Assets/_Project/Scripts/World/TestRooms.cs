using System;
using System.Linq;
using System.Text;
using UnityEngine.SceneManagement;

/// <summary>
/// The testing rooms (the user, 2026-11-13: "전투 시험장이랑 이동 시험장은 이제 필요 없으니 콘솔로만 접근 가능한 테스팅
/// 룸으로. 메인 메뉴에서 안 떴으면 좋겠어."): CombatFoundation and MovementAbilityPlayground, the maps of Tables/Maps with
/// list 0 (MapTable.TestingRooms). They stay in the build, but neither the title screen's map card nor the hub's map table
/// offers them. The console's /testroom (the chat's console channel or the ` developer console, on the title screen, in
/// the hub or in a chapter) lists them and goes to one: alone straight there behind the loading screen and its warm-up
/// (SceneLoad), in a room only the host, who takes everyone along (GameSession.ChangeMap, as the hub sets off). The hub's
/// chosen chapter stays as it was, so /hub comes back to the same map table.
/// A room is named by its scene id, by the start of it ("combat", "move"), or by part of its id or name ("전투").
/// </summary>
public static class TestRooms
{
    /// <summary>The console's /testroom: with nothing typed the list, else the room to go to.</summary>
    public static string Command(string args)
    {
        var rooms = MapTable.TestingRooms();
        string typed = (args ?? "").Trim();
        if (typed.Length == 0) return RoomsReport(rooms);
        var found = Find(typed, rooms);
        if (found.Length == 0) return $"그런 테스팅 룸이 없어요: {typed}\n" + RoomsReport(rooms);
        if (found.Length > 1) return $"'{typed}'에 맞는 방이 여럿이에요: {string.Join(", ", found)}. 더 길게 쳐 주세요.";
        string scene = found[0];
        if (SceneManager.GetActiveScene().name == scene) return $"이미 여기예요: {Label(scene)}";
        if (SceneLoad.Busy) return "벌써 다른 곳으로 가는 중이에요.";
        if (GameSession.IsGuest) return "방에서는 방장만 테스팅 룸으로 갈 수 있어요. 방장이 가면 방 전체가 같이 가요.";
        if (GameSession.IsOnline)
            return GameSession.ChangeMap(scene) ? $"방 전체가 가요: {Label(scene)}. 돌아올 땐 /hub." : "지금은 옮길 수 없어요.";
        return SceneLoad.Go(scene) ? $"가요: {Label(scene)}. 돌아올 땐 /hub." : "지금은 옮길 수 없어요.";
    }

    /// <summary>
    /// The rooms <paramref name="typed"/> names: the one whose scene id it is; else every one whose id starts with it
    /// ("combat", "move"); else every one whose id or name holds it.
    /// </summary>
    public static string[] Find(string typed, string[] rooms)
    {
        if (string.IsNullOrWhiteSpace(typed) || rooms == null) return Array.Empty<string>();
        typed = typed.Trim();
        var exact = rooms.Where(id => string.Equals(id, typed, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length > 0) return exact;
        var starts = rooms.Where(id => id.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (starts.Length > 0) return starts;
        return rooms.Where(id => id.IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0
            || MapTable.DisplayName(id).IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
    }

    /// <summary>The short name to type: the scene id's first word in lower case (CombatFoundation: combat).</summary>
    public static string ShortName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        int end = 1;
        while (end < id.Length && !char.IsUpper(id[end]) && id[end] != '_') end++;
        return id.Substring(0, end).ToLowerInvariant();
    }

    // The rooms there are, each with what to type.
    private static string RoomsReport(string[] rooms)
    {
        if (rooms.Length == 0) return "테스팅 룸이 없어요. Maps.csv에서 list가 0이고 빌드 목록에 든 씬이 테스팅 룸이에요.";
        var text = new StringBuilder("테스팅 룸 (메뉴와 허브 지도 탁자에는 안 나와요):");
        foreach (string id in rooms) text.Append("\n  /testroom ").Append(ShortName(id)).Append("  —  ").Append(Label(id));
        text.Append("\n씬 이름 그대로나 앞부분만 쳐도 돼요. 방에서는 방장만 쓰고 방 전체가 같이 가요. 돌아올 땐 /hub.");
        return text.ToString();
    }

    private static string Label(string id) => MapTable.DisplayName(id) + " (" + id + ")";
}
