using System;
using System.Linq;
using UnityEditor;

/// <summary>
/// Takes in a gun move Blender's shove workspace has just sent ("게임에 보내기" writes Resources/Combat/GunMoves/*.csv), even
/// while the game runs: GunMoves forgets what it read on GameTables.Reloaded, so the next V plays the new move.
/// </summary>
internal sealed class GunMovesWatcher : AssetPostprocessor
{
    // GunMoves.Folder under Resources; written out because the game's types are internal to its own assembly.
    private const string FolderPath = "Assets/_Project/Resources/Combat/GunMoves/";

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (!imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsGunMove)) return;
        GameTables.Reload();
    }

    private static bool IsGunMove(string path) => path.StartsWith(FolderPath, StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
}
