using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Where things made while the game runs stand in the Hierarchy, so a playing scene reads as tidily as a built one.
/// Nothing here has gameplay: categories and groups are empty folders that stay at the origin.
/// <para>A scene's own runtime things go under that scene's category folders (<see cref="Group"/>,
/// <see cref="GetGroup"/>): ENEMIES (Campaign, Waves, Tower, Console), EFFECTS (World, Screen, Audio), WORLD (Bodies,
/// Debris, Pickups), SYSTEMS, UI and PLAYERS. A scene may already hold a category under any decoration and with a note
/// in brackets after it (the builders' "◤━━ 시스템", "◤━━ 지형 (길찾기 굽는 곳)"); it is found by its letters.</para>
/// <para>What outlives scene changes goes under one kept folder (<see cref="Keep"/>) in place of a loose
/// DontDestroyOnLoad object at the top of the Hierarchy.</para>
/// </summary>
public static class RuntimeSceneHierarchy
{
    /// <summary>The categories in use.</summary>
    public const string Systems = "SYSTEMS", Effects = "EFFECTS", World = "WORLD", Enemies = "ENEMIES", Players = "PLAYERS", Ui = "UI";
    // The map builders' own name for a scene's systems folder: runtime systems join it instead of standing beside it.
    private const string BuiltSystems = "시스템"; // dev-text
    private const string Prefix = "◤━━ ", MadeNote = " (실행 중)"; // dev-text
    private const string KeptName = Prefix + "ENFILADE (씬이 바뀌어도 남는 것)"; // dev-text

    private static readonly Dictionary<(int scene, string category, string group), Transform> found =
        new Dictionary<(int scene, string category, string group), Transform>();
    private static Transform kept;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        found.Clear();
        kept = null;
    }

    /// <summary>
    /// The folder of this group in this scene, made on first use. Null while the scene can take none (not valid or not
    /// loaded yet); a caller that parents to it then leaves its object where it is.
    /// </summary>
    public static Transform GetGroup(Scene scene, string categoryName, string groupName)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        var key = (scene.handle, categoryName, groupName);
        if (found.TryGetValue(key, out var cached) && cached != null) return cached;
        Transform category = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (!MatchesCategory(root.name, categoryName) && !(categoryName == Systems && MatchesCategory(root.name, BuiltSystems))) continue;
            category = root.transform;
            break;
        }
        if (category == null)
        {
            var host = new GameObject(Prefix + categoryName + MadeNote);
            SceneManager.MoveGameObjectToScene(host, scene);
            category = host.transform;
        }
        var group = category.Find(groupName);
        if (group == null)
        {
            var folder = new GameObject(groupName);
            SceneManager.MoveGameObjectToScene(folder, scene);
            folder.transform.SetParent(category, true);
            group = folder.transform;
        }
        found[key] = group;
        return group;
    }

    /// <summary>Files a runtime-made object under its scene's group (the owner's scene when one is given), keeping where
    /// it is in the world.</summary>
    public static void Group(GameObject host, string categoryName, string groupName, GameObject owner = null)
    {
        if (host == null) return;
        var scene = owner != null ? owner.scene : host.scene;
        var parent = GetGroup(scene, categoryName, groupName);
        if (parent == null || host.transform.parent == parent) return;
        if (host.scene != scene)
        {
            host.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(host, scene);
        }
        host.transform.SetParent(parent, true);
    }

    /// <summary>
    /// Keeps a runtime-made object across scene changes, in the named group of the one kept folder. In place of
    /// DontDestroyOnLoad(host): the object lives exactly as long (the kept folder is what is marked), and the kept
    /// things stand together in the Hierarchy instead of loose at its top.
    /// </summary>
    public static void Keep(GameObject host, string groupName = "Systems")
    {
        if (host == null) return;
        if (kept == null)
        {
            var root = new GameObject(KeptName);
            Object.DontDestroyOnLoad(root);
            kept = root.transform;
        }
        var group = kept.Find(groupName);
        if (group == null)
        {
            group = new GameObject(groupName).transform;
            group.SetParent(kept, false);
        }
        host.transform.SetParent(group, true);
    }

    private static bool MatchesCategory(string name, string categoryName)
    {
        // Ignore decorative dividers and a note in brackets after the name ("◤━━ 지형 (길찾기 굽는 곳)" is 지형), but do
        // not mistake "Console Enemies" for "ENEMIES": the letters must be the category's and nothing more.
        int start = 0, end = name.Length - 1;
        while (start <= end && !char.IsLetterOrDigit(name[start])) start++;
        int note = name.IndexOf(" (", start, StringComparison.Ordinal);
        if (note >= 0) end = note - 1;
        while (end >= start && !char.IsLetterOrDigit(name[end])) end--;
        return end - start + 1 == categoryName.Length
            && string.Compare(name, start, categoryName, 0, categoryName.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
