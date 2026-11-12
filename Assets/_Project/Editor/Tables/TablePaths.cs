using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

/// <summary>
/// Where a table's file is, for the editor tools that read and write tables as files (TableFile). Tables are filed in
/// sub-folders of Data/Resources/Tables by subject and the game asks for them by name alone (GameTables), so a tool names a
/// table the same way and never writes a folder down: moving a table to another sub-folder breaks nothing.
/// </summary>
internal static class TablePaths
{
    /// <summary>The folder every table is under, as an asset path.</summary>
    internal const string Folder = ProjectFolders.DataByName + GameTables.ResourcesFolder;

    private static Dictionary<string, string> paths;

    [InitializeOnLoadMethod]
    private static void Watch() => EditorApplication.projectChanged += () => paths = null;

    /// <summary>
    /// The asset path of the table named so ("Cloth" gives Assets/_Project/Data/Data/Resources/Tables/Characters/Cloth.csv).
    /// A table that doesn't exist yet gets a path right under the tables folder, for the tool about to make it.
    /// </summary>
    internal static string Of(string table)
    {
        if (paths == null || !paths.ContainsKey(table)) Scan();
        return paths.TryGetValue(table, out string path) ? path : Folder + "/" + table + ".csv";
    }

    /// <summary>Every table file, as asset paths.</summary>
    internal static List<string> All()
    {
        Scan();
        var all = new List<string>(paths.Values);
        all.Sort(StringComparer.OrdinalIgnoreCase);
        return all;
    }

    private static void Scan()
    {
        paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(Folder)) return;
        foreach (string file in Directory.GetFiles(Folder, "*.csv", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(path);
            if (!paths.ContainsKey(name)) paths[name] = path;
        }
    }
}
