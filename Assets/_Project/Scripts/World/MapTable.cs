using System.Linq;
using UnityEngine;

/// <summary>
/// The maps players can choose, from Resources/Tables/Maps.csv. Only scenes in the build are offered. Each map's
/// picture for the title screen is Resources/UI/Maps/&lt;id&gt; (taken by Tools/World/맵 사진 찍기, or any image put
/// there by hand); a "picture" column can name another image in that folder.
/// The hub (HubFlow) has its row for its name and picture, but it is no map to choose and no game to count: Playable and
/// All leave it out, so the title screen's picker, the records, class growth and field pickups never see it.
/// </summary>
public static class MapTable
{
    private const string Table = "Maps", PictureFolder = "UI/Maps/";

    public static string[] Playable() => GameTables.RowIds(Table).Where(id => id != HubFlow.SceneName && Application.CanStreamedLevelBeLoaded(id)).ToArray();

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
