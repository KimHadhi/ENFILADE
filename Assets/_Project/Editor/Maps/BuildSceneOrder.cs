using System.IO;
using System.Linq;
using UnityEditor;

/// <summary>Keeps the main menu first in the build's scene list, so a built game opens on it.</summary>
public static class BuildSceneOrder
{
    private const string MenuScene = "Assets/_Project/Scenes/MainMenu.unity";

    [InitializeOnLoadMethod]
    private static void Schedule() => EditorApplication.delayCall += Keep;

    private static void Keep()
    {
        var scenes = EditorBuildSettings.scenes;
        if (!File.Exists(MenuScene) || (scenes.Length > 0 && scenes[0].path == MenuScene)) return;
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuScene, true) }
            .Concat(scenes.Where(scene => scene.path != MenuScene)).ToArray();
    }
}
