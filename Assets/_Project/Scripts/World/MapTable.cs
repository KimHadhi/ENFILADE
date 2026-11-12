using System.Linq;
using UnityEngine;

/// <summary>
/// The maps players can choose, from Resources/Tables/Maps/Maps.csv. Only scenes in the build are offered. Each map's
/// picture for the title screen is Resources/UI/Maps/&lt;id&gt; (taken by Tools/World/맵 사진 찍기, or any image put
/// there by hand); a "picture" column can name another image in that folder.
/// The hub (HubFlow) has its row for its name and picture, but it is no map to choose and no game to count: Playable and
/// All leave it out, so the title screen's picker, the records, class growth and field pickups never see it.
/// A row whose "list" cell is 0 is a testing room (the user, 2026-11-13: "전투 시험장이랑 이동 시험장은 이제 필요 없으니
/// 콘솔로만 접근 가능한 테스팅 룸으로. 메인 메뉴에서 안 떴으면 좋겠어."): still a map in the build, with its name and
/// picture for the loading screen, but never offered where a map is picked; the console's /testroom goes there
/// (TestRooms). An empty cell offers it, so a map a mod adds with no such column shows as before.
/// </summary>
public static class MapTable
{
    private const string Table = "Maps", PictureFolder = "UI/Maps/";

    /// <summary>The maps players can choose (the title screen's map card, the hub's map table): in the build, offered
    /// (<see cref="IsListed"/>), and not the hub.</summary>
    public static string[] Playable() => GameTables.RowIds(Table)
        .Where(id => id != HubFlow.SceneName && IsListed(id) && Application.CanStreamedLevelBeLoaded(id)).ToArray();

    /// <summary>Offered where a map is picked: its row's "list" cell is anything but 0 (empty included). A map not in the
    /// table is not offered.</summary>
    public static bool IsListed(string id)
        => !string.IsNullOrEmpty(id) && GameTables.TryGetRow(Table, id, out var row) && !Hidden(row);

    /// <summary>A testing room: a map of the table with list 0 (not the hub), reached only by the console's /testroom.
    /// A scene the table doesn't name is none.</summary>
    public static bool IsTestingRoom(string id)
        => !string.IsNullOrEmpty(id) && id != HubFlow.SceneName && GameTables.TryGetRow(Table, id, out var row) && Hidden(row);

    /// <summary>The testing rooms in the build, in the table's order (TestRooms).</summary>
    public static string[] TestingRooms() => GameTables.RowIds(Table)
        .Where(id => IsTestingRoom(id) && Application.CanStreamedLevelBeLoaded(id)).ToArray();

    private static bool Hidden(System.Collections.Generic.IReadOnlyDictionary<string, string> row)
        => row.TryGetValue("list", out string cell) && cell != null && cell.Trim() == "0";

    /// <summary>Every map in the table, playable or not (the editor's picture tool goes through these).</summary>
    public static string[] All() => GameTables.RowIds(Table).Where(id => id != HubFlow.SceneName).ToArray();

    /// <summary>The map's name: its displayName cell's words (a Texts id, map.&lt;id&gt;.name, or plain words), else the id.</summary>
    public static string DisplayName(string id) =>
        GameTables.TryGetRow(Table, id, out var row) && row.TryGetValue("displayName", out string name) ? Texts.Cell(name) : id;

    /// <summary>The map's picture, or null when it has none yet.</summary>
    public static Texture2D Picture(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        string file = GameTables.TryGetRow(Table, id, out var row) && row.TryGetValue("picture", out string named) && !string.IsNullOrWhiteSpace(named)
            ? named.Trim() : id;
        return Resources.Load<Texture2D>(PictureFolder + file);
    }

    /// <summary>The Resources path pictures are saved under (for the editor's picture tool).</summary>
    public static string PictureResource(string id) => PictureFolder + id;
}
