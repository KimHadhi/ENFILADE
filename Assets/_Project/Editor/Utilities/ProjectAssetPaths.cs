using System;
using UnityEditor;

internal static class ProjectAssetPaths
{
    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains(".."))
            throw new ArgumentException("Expected a project Assets folder.", nameof(path));
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, path.Substring(slash + 1))))
            throw new InvalidOperationException("Could not create asset folder: " + path);
    }

    internal static string DevelopmentScene(string fileName)
    {
        const string folder = "Assets/_Project/Scenes/Development";
        EnsureFolder(folder);
        return folder + "/" + fileName;
    }
}
