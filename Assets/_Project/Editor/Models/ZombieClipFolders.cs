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
/// Each one-off clip there (all but the loops) is also measured for its travel, how far its root goes as it plays
/// (ZombieLooks.travels), and the zombie moves by that while it plays (ZombieBody.Travel): the user, 2026-11-14, on work
/// made in Blender that was lost on its way into the game, here a lunge or a stagger's steps that played on the spot.
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
    // The travels are measured anew from every one-off clip there: they follow each clip's own import (its list's inPlace).
    private static void Refresh(HashSet<string> pruned, bool report)
    {
        var looks = AssetDatabase.LoadAssetAtPath<ZombieLooks>(LooksPath);
        if (looks == null) return;
        int added = 0, dropped = 0;
        var travels = new List<ZombieLooks.ClipTravel>();
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
            foreach (string file in files)
                if (!Loops(file) && MeasureTravel(ClipIn(file), out var travel)) travels.Add(travel);
        }
        bool moved = !SameTravels(looks.travels, travels);
        if (moved) looks.travels = travels.ToArray();
        if (added + dropped > 0 || moved)
        {
            EditorUtility.SetDirty(looks);
            AssetDatabase.SaveAssetIfDirty(looks);
        }
        if (report || added + dropped > 0 || moved)
            Debug.Log($"[ZombieClipFolders] ZombieLooks: {added} clip(s) added, {dropped} missing entr(ies) dropped, " +
                $"{travels.Count} one-off clip(s) that move the zombie by their own travel.", looks);
    }

    // Unity's humanoid root goes where the body's centre of mass goes across the floor (RootT x and z, in the avatar's own
    // units); its turn is RootQ. Less than StillTravel from its start at every mark (a tenth of a millimetre on a body of
    // Unity's size), a clip doesn't travel: that is only its curves' rounding.
    private const float StillTravel = 1e-4f;

    /// <summary>
    /// How far a one-off clip's root goes as it plays, as Unity takes it out of the pose when the clip's horizontal root
    /// motion isn't baked into it (lockRootPositionXZ off: the zombie workspace's inPlace 1, a Mixamo drop): the body's way
    /// across the floor, mark by mark at the clip's own frame rate, in the root's frame (its rotation offset, a mirror, and
    /// its turn when that isn't baked into the pose either). None when the travel stays in the pose (inPlace 0: the body
    /// shows it itself, so moving the zombie too would count it twice) or when the clip doesn't move.
    /// </summary>
    private static bool MeasureTravel(AnimationClip clip, out ZombieLooks.ClipTravel travel)
    {
        travel = default;
        if (clip == null || clip.length <= 0f) return false;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopBlendPositionXZ) return false;
        var x = Curve(clip, "RootT.x");
        var z = Curve(clip, "RootT.z");
        if (x == null || z == null) return false;
        // The root turns with the body while its turn isn't baked into the pose; based on the body's facing rather than the
        // clip's own (Original), it starts turned as the body does.
        bool turns = !settings.loopBlendOrientation, facing = !settings.keepOriginalOrientation;
        AnimationCurve[] turn = null;
        if (turns || facing)
        {
            turn = new[] { Curve(clip, "RootQ.x"), Curve(clip, "RootQ.y"), Curve(clip, "RootQ.z"), Curve(clip, "RootQ.w") };
            if (turn.Any(c => c == null)) turn = null;
        }
        float mirror = settings.mirror ? -1f : 1f;
        float start = turn != null ? Yaw(turn, 0f) * mirror : 0f;
        float rate = clip.frameRate > 0f ? clip.frameRate : 30f;
        int marks = Mathf.Max(2, Mathf.CeilToInt(clip.length * rate - 1e-3f) + 1);
        float step = clip.length / (marks - 1);
        var along = new Vector2[marks];
        Vector3 at = Body(x, z, 0f, mirror), gone = Vector3.zero;
        float far = 0f;
        for (int i = 1; i < marks; i++)
        {
            float from = (i - 1) * step;
            Vector3 next = Body(x, z, Mathf.Min(clip.length, i * step), mirror);
            float yaw = settings.orientationOffsetY;
            if (turn != null && facing) yaw += start;
            if (turn != null && turns) yaw += Mathf.DeltaAngle(start, Yaw(turn, from) * mirror);
            gone += Quaternion.Euler(0f, -yaw, 0f) * (next - at);
            at = next;
            along[i] = new Vector2(gone.x, gone.z);
            far = Mathf.Max(far, along[i].magnitude);
        }
        if (far < StillTravel) return false;
        travel = new ZombieLooks.ClipTravel { clip = clip, step = step, along = along };
        return true;
    }

    private static AnimationCurve Curve(AnimationClip clip, string property)
        => AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), property));

    private static Vector3 Body(AnimationCurve x, AnimationCurve z, float time, float mirror)
        => new Vector3(x.Evaluate(time) * mirror, 0f, z.Evaluate(time));

    // Which way the body faces about up (degrees), from its turn at that moment.
    private static float Yaw(AnimationCurve[] turn, float time)
    {
        var q = new Quaternion(turn[0].Evaluate(time), turn[1].Evaluate(time), turn[2].Evaluate(time), turn[3].Evaluate(time));
        if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 1e-8f) return 0f;
        Vector3 ahead = Quaternion.Normalize(q) * Vector3.forward;
        return Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
    }

    // Whether the travels measured now are the ones the looks hold already (the asset is only written when they change).
    private static bool SameTravels(ZombieLooks.ClipTravel[] held, List<ZombieLooks.ClipTravel> made)
    {
        if (held == null) return made.Count == 0;
        if (held.Length != made.Count) return false;
        for (int i = 0; i < held.Length; i++)
        {
            var a = held[i].along;
            var b = made[i].along;
            if (held[i].clip != made[i].clip || held[i].step != made[i].step || a == null || a.Length != b.Length) return false;
            for (int k = 0; k < a.Length; k++)
                if (a[k] != b[k]) return false;
        }
        return true;
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
