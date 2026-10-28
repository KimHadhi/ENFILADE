using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps each Resources/Audio/&lt;bank&gt; in step with its sound folder: every subfolder that holds clips becomes a key
/// ("Run/Concrete"). Adding, moving or deleting a clip there rebuilds the bank. Art/Audio/Library is never read.
/// </summary>
internal sealed class SoundBankBuilder : AssetPostprocessor
{
    private const string OutputFolder = "Assets/_Project/Resources/" + SoundBank.ResourceFolder;
    // Bank id → its root folder. The voice bank points at the current character's voice folder.
    private static readonly (string Id, string Folder)[] Banks =
    {
        ("Movement", "Assets/_Project/Art/Audio/Movement"),
        ("Voice", "Assets/_Project/Art/Audio/Voice/Hiro"),
    };
    private static bool queued;

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var paths = imported.Concat(deleted).Concat(moved).Concat(movedFrom);
        if (queued || !paths.Any(p => Banks.Any(b => p.StartsWith(b.Folder + "/", StringComparison.OrdinalIgnoreCase)))) return;
        queued = true;
        EditorApplication.delayCall += Refresh;
    }

    private static void Refresh()
    {
        queued = false;
        foreach (var (id, folder) in Banks) Rebuild(id, folder);
    }

    private static void Rebuild(string id, string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return;
        var groups = new SortedDictionary<string, List<AudioClip>>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            int slash = path.LastIndexOf('/');
            string key = slash > folder.Length ? path.Substring(folder.Length + 1, slash - folder.Length - 1) : "";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (key.Length == 0 || clip == null) continue;
            if (!groups.TryGetValue(key, out var clips)) groups[key] = clips = new List<AudioClip>();
            clips.Add(clip);
        }
        var entries = groups.Select(g => new SoundBank.Entry { key = g.Key, clips = g.Value.ToArray() }).ToList();

        string assetPath = OutputFolder + "/" + id + ".asset";
        var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(assetPath);
        if (bank == null)
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder)) AssetDatabase.CreateFolder("Assets/_Project/Resources", SoundBank.ResourceFolder);
            bank = ScriptableObject.CreateInstance<SoundBank>();
            bank.EditorRefresh(id, entries);
            AssetDatabase.CreateAsset(bank, assetPath);
            AssetDatabase.SaveAssets();
            return;
        }
        if (!bank.EditorRefresh(id, entries)) return;
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssetIfDirty(bank);
    }
}
