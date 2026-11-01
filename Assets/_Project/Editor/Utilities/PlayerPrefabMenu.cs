using UnityEditor;
using UnityEngine;

/// <summary>
/// Saves the open scene's player as Resources/Characters/Player.prefab, which other players are made from.
/// The scene object stays as it is, so run this again after changing it.
/// </summary>
public static class PlayerPrefabMenu
{
    private const string MenuPath = "Tools/Player/Save Player Prefab";
    private const string PrefabPath = "Assets/_Project/Resources/Characters/Player.prefab";

    [MenuItem(MenuPath, true)]
    private static bool CanSave() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(MenuPath)]
    private static void Save()
    {
        var players = Object.FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (players.Length != 1)
        {
            Debug.LogWarning($"[PlayerPrefabMenu] The open scene needs exactly one player, but it has {players.Length}.");
            return;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(players[0].gameObject, PrefabPath, out bool saved);
        if (saved) EditorGUIUtility.PingObject(prefab);
        else Debug.LogWarning($"[PlayerPrefabMenu] Could not save {PrefabPath}.");
    }
}
