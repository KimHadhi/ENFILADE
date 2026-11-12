using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Fills ZombieLooks' lists from the slot folders under Art/Animations/Zombies (2026-11-11, the zombie animation
/// pipeline): a clip in Walk/ joins walks, one in Attack/ joins attacks, and so on, and each zombie then picks one per
/// slot (ZombieLooks.Pick). CalmPose/ holds poses as pairs, X.fbx held while calm and X_getup.fbx getting up from it.
/// It runs whenever an FBX in those folders is imported, moved or deleted, and from ENFILADE → 좀비 → 클립 폴더 다시 읽기
/// Folders. Entries it didn't add (the bought packs' clips) are left alone; a deleted file's entry is dropped. The
/// Blender zombie workspace (Tools/Blender/Zombies) exports straight into these folders, and a Mixamo download (with
/// skin) can be dropped in as it is (ModelImportRules gives it a loop by its folder).
/// </summary>
internal sealed class ZombieClipFolders : AssetPostprocessor
{
    public const string Root = ModelImportRules.AnimationsFolder + "Zombies/";
    public const string GetUpSuffix = "_getup";
    private const string LooksPath = ProjectFolders.DataByName + "Enemies/ZombieLooks.asset";
    private const string PoseFolder = "CalmPose";

    /// <summary>Each slot folder, the ZombieLooks list it fills, and whether its clips loop.</summary>
    public static readonly (string folder, string list, bool loop)[] Slots =
    {
        ("Idle", nameof(ZombieLooks.idles), true),
        ("Walk", nameof(ZombieLooks.walks), true),
        ("Run", nameof(ZombieLooks.runs), true),
        ("Attack", nameof(ZombieLooks.attacks), false),
        ("Flinch", nameof(ZombieLooks.flinches), false),
        ("Stagger", nameof(ZombieLooks.staggers), false),
        ("CalmIdle", nameof(ZombieLooks.calmIdles), true),
        ("AlertIdle", nameof(ZombieLooks.alertIdles), true),
        ("SearchWalk", nameof(ZombieLooks.searchWalks), true),
        ("Notice", nameof(ZombieLooks.notices), false),
        ("Rouse", nameof(ZombieLooks.rouses), false),
        ("CrawlAttack", nameof(ZombieLooks.crawlAttacks), false),
        (PoseFolder, nameof(ZombieLooks.calmPoses), true),
    };

    /// <summary>The slot folder a file sits in (the first folder under Zombies/), or null outside them.</summary>
    public static string SlotOf(string path)
    {
        if (path == null || !path.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) return null;
        string rest = path.Substring(Root.Length);
        int slash = rest.IndexOf('/');
        if (slash <= 0) return null;
        string folder = rest.Substring(0, slash);
        foreach (var slot in Slots)
            if (string.Equals(slot.folder, folder, StringComparison.OrdinalIgnoreCase)) return slot.folder;
        return null;
    }

    /// <summary>Whether a clip file in a slot folder loops; a pose's getting up (X_getup) plays once.</summary>
    public static bool Loops(string path)
    {
        string slot = SlotOf(path);
        if (slot == null) return false;
        if (slot == PoseFolder) return !IsGetUp(path);
        return Slots.First(s => s.folder == slot).loop;
    }

    private static bool IsGetUp(string path)
        => Path.GetFileNameWithoutExtension(path).EndsWith(GetUpSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsClipFile(string path) => SlotOf(path) != null && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var gone = new HashSet<string>(deleted.Concat(movedFrom).Where(IsClipFile).Select(SlotOf));
        if (gone.Count > 0 || imported.Concat(moved).Any(IsClipFile)) Refresh(gone, false);
    }

    [MenuItem("ENFILADE/좀비/클립 폴더 다시 읽기")]
    private static void RefreshFromMenu() => Refresh(new HashSet<string>(Slots.Select(s => s.folder)), true);

    // Adds every clip in the slot folders that its list lacks. Lists in `pruned` (folders a file just left) also lose
    // their missing entries: only there, since a pack clip can read as missing for a moment while the Library rebuilds.
    private static void Refresh(HashSet<string> pruned, bool report)
    {
        var looks = AssetDatabase.LoadAssetAtPath<ZombieLooks>(LooksPath);
        if (looks == null) return;
        int added = 0, dropped = 0;
        foreach (var slot in Slots)
        {
            string folder = Root + slot.folder;
            var files = AssetDatabase.IsValidFolder(folder)
                ? AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)).Distinct()
                    .OrderBy(p => p, StringComparer.Ordinal).ToList()
                : new List<string>();
            bool prune = pruned.Contains(slot.folder);
            if (slot.folder == PoseFolder) RefreshPoses(looks, files, prune, ref added, ref dropped);
            else RefreshList(looks, slot.list, files, prune, ref added, ref dropped);
        }
        if (added + dropped > 0)
        {
            EditorUtility.SetDirty(looks);
            AssetDatabase.SaveAssetIfDirty(looks);
        }
        if (report || added + dropped > 0)
            Debug.Log($"[ZombieClipFolders] ZombieLooks: {added} clip(s) added, {dropped} missing entr(ies) dropped.", looks);
    }

    private static void RefreshList(ZombieLooks looks, string listName, List<string> files, bool prune, ref int added, ref int dropped)
    {
        var field = typeof(ZombieLooks).GetField(listName);
        var list = new List<AnimationClip>((AnimationClip[])field.GetValue(looks) ?? new AnimationClip[0]);
        if (prune) dropped += list.RemoveAll(clip => clip == null);
        foreach (string file in files)
        {
            var clip = ClipIn(file);
            if (clip == null || list.Contains(clip)) continue;
            list.Add(clip);
            added++;
        }
        field.SetValue(looks, list.ToArray());
    }

    private static void RefreshPoses(ZombieLooks looks, List<string> files, bool prune, ref int added, ref int dropped)
    {
        var poses = new List<ZombieLooks.CalmPose>(looks.calmPoses ?? new ZombieLooks.CalmPose[0]);
        if (prune) dropped += poses.RemoveAll(pose => pose.loop == null);
        foreach (string file in files.Where(f => !IsGetUp(f)))
        {
            var loop = ClipIn(file);
            if (loop == null) continue;
            string getUpFile = files.FirstOrDefault(f => IsGetUp(f) && string.Equals(
                Path.GetFileNameWithoutExtension(f), Path.GetFileNameWithoutExtension(file) + GetUpSuffix,
                StringComparison.OrdinalIgnoreCase));
            var getUp = getUpFile != null ? ClipIn(getUpFile) : null;
            int at = poses.FindIndex(pose => pose.loop == loop);
            if (at < 0)
            {
                poses.Add(new ZombieLooks.CalmPose { loop = loop, getUp = getUp });
                added++;
            }
            else if (poses[at].getUp == null && getUp != null)
            {
                // Its getting up was exported after the pose itself.
                poses[at] = new ZombieLooks.CalmPose { loop = loop, getUp = getUp };
                added++;
            }
        }
        looks.calmPoses = poses.ToArray();
    }

    // The file's own clip (an FBX holds Unity's "__preview__" copies too).
    private static AnimationClip ClipIn(string path)
        => AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
}
