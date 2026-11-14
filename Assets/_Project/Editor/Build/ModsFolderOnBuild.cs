using System;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// A build gets its Mods folder: the project's Mods folder (the guide, the example mod and whatever mods are in it) is
/// copied beside the executable, where the game looks for mods (GameMods.Root). The tables /mods export wrote out are
/// left behind: the built game writes its own. The avatar exporter goes in too, as Mods/_SDK (ModSdkPackage).
/// </summary>
internal sealed class ModsFolderOnBuild : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        try
        {
            string source = GameMods.Root;
            string output = report.summary.outputPath;
            string folder = Directory.Exists(output) ? output : Path.GetDirectoryName(output);
            if (string.IsNullOrEmpty(folder)) return;
            if (Directory.Exists(source)) Copy(source, Path.Combine(folder, GameMods.FolderName));
            // The exporter that matches this build, for whoever wants to bring a character of their own (ModSdkPackage).
            ModSdkPackage.Write(Path.Combine(folder, GameMods.FolderName, ModSdkPackage.FolderName));
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("[Mods] 빌드 옆에 Mods 폴더를 복사하지 못했습니다: " + error.Message);
        }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (string folder in Directory.GetDirectories(from))
        {
            string name = Path.GetFileName(folder);
            if (name == GameMods.ExportFolder) continue;
            Copy(folder, Path.Combine(to, name));
        }
    }
}
