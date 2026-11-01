using System.Linq;
using UnityEngine;

/// <summary>The maps players can choose, from Resources/Tables/Maps.csv. Only scenes in the build are offered.</summary>
public static class MapTable
{
    private const string Table = "Maps";

    public static string[] Playable() => GameTables.RowIds(Table).Where(id => Application.CanStreamedLevelBeLoaded(id)).ToArray();

    public static string DisplayName(string id) =>
        GameTables.TryGetRow(Table, id, out var row) && row.TryGetValue("displayName", out string name) ? name : id;
}
