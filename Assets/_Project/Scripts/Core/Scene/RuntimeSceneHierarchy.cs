using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-local runtime folders. Categories and groups have no gameplay components.</summary>
public static class RuntimeSceneHierarchy
{
    public static Transform GetGroup(Scene scene, string categoryName, string groupName)
    {
        Transform category = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (!MatchesCategory(root.name, categoryName)) continue;
            category = root.transform;
            break;
        }
        if (category == null)
        {
            var host = new GameObject("◤━━━━━━━━━━━━━━━━ " + categoryName + " ━━━━━━━━━━━━━◥");
            SceneManager.MoveGameObjectToScene(host, scene);
            category = host.transform;
        }
        var group = category.Find(groupName);
        if (group != null) return group;
        var folder = new GameObject(groupName);
        SceneManager.MoveGameObjectToScene(folder, scene);
        folder.transform.SetParent(category, true);
        return folder.transform;
    }

    public static void Group(GameObject host, string categoryName, string groupName, GameObject owner = null)
    {
        var scene = owner != null ? owner.scene : host.scene;
        var parent = GetGroup(scene, categoryName, groupName);
        if (host.transform.parent == parent) return;
        if (host.scene != scene)
        {
            host.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(host, scene);
        }
        host.transform.SetParent(parent, true);
    }

    private static bool MatchesCategory(string name, string categoryName)
    {
        // Ignore decorative dividers, but do not mistake "Console Enemies" for "ENEMIES".
        int start = 0, end = name.Length - 1;
        while (start <= end && !char.IsLetterOrDigit(name[start])) start++;
        while (end >= start && !char.IsLetterOrDigit(name[end])) end--;
        return end - start + 1 == categoryName.Length
            && string.Compare(name, start, categoryName, 0, categoryName.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
