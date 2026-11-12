using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Reloads GameTables whenever a CSV under Resources/Tables (any sub-folder) is saved, so balance edits reach a running game.</summary>
internal sealed class GameTablesWatcher : AssetPostprocessor
{
    private const string FolderPath = TablePaths.Folder;

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        string changed = imported.Concat(deleted).Concat(moved).Concat(movedFrom).FirstOrDefault(IsTable);
        if (changed == null) return;
        GameTables.Reload();
        if (EditorApplication.isPlaying) Debug.Log($"[Tables] {System.IO.Path.GetFileName(changed)} 변경을 실행 중인 게임에 적용했습니다.");
    }

    private static bool IsTable(string path) => path.StartsWith(FolderPath + "/", StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    [MenuItem("ENFILADE/표/표 폴더 열기")]
    private static void OpenFolder() => EditorUtility.RevealInFinder(TablePaths.Of("Player"));

    [MenuItem("ENFILADE/표/표 다시 읽기")]
    private static void ReloadNow()
    {
        AssetDatabase.ImportAsset(FolderPath, ImportAssetOptions.ImportRecursive);
        GameTables.Reload();
    }
}
