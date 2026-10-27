using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Reloads GameTables whenever a CSV under Resources/Tables is saved, so balance edits reach a running game.</summary>
internal sealed class GameTablesWatcher : AssetPostprocessor
{
    private const string FolderPath = "Assets/_Project/Resources/" + GameTables.ResourcesFolder;

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        string changed = imported.Concat(deleted).Concat(moved).Concat(movedFrom).FirstOrDefault(IsTable);
        if (changed == null) return;
        GameTables.Reload();
        if (EditorApplication.isPlaying) Debug.Log($"[Tables] {System.IO.Path.GetFileName(changed)} 변경을 실행 중인 게임에 적용했습니다.");
    }

    private static bool IsTable(string path) => path.StartsWith(FolderPath + "/", StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    [MenuItem("Tools/Data Tables/Open Tables Folder")]
    private static void OpenFolder() => EditorUtility.RevealInFinder(FolderPath + "/Player.csv");

    [MenuItem("Tools/Data Tables/Reload Tables")]
    private static void ReloadNow()
    {
        AssetDatabase.ImportAsset(FolderPath, ImportAssetOptions.ImportRecursive);
        GameTables.Reload();
    }
}
