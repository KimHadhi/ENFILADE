using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What sway rows need to know of a character (VrcSway): her humanoid bones, which her own chains may not start at;
/// every bone of hers, which an outfit made for her shares and so leaves to her own rows; which part of the body a bone
/// is (a PhysBone collider on "Collider_UpperLeg.L" stands on the legs); and which of an outfit's bones only share a
/// name with one of hers (OutfitSkeleton: a mirrored side lock hanging from "R_Side"), so stay the outfit's own.
/// The editor's BOOTH import reads it from each character's prefab when it writes Cloth.csv, and the game reads it the
/// same way for a character or an outfit a mod brings (ModAvatars, ModOutfits), so all of them get the same rows.
/// </summary>
public sealed class VrcWearer
{
    /// <summary>Her humanoid bones' names.</summary>
    public readonly HashSet<string> Humanoid = new HashSet<string>(StringComparer.Ordinal);
    /// <summary>The names of her hips and every bone under them.</summary>
    public readonly HashSet<string> Bones = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> parts = new Dictionary<string, string>(StringComparer.Ordinal);
    // Her bones as an outfit's file is joined to them when worn (OutfitDresser.Merge reads them the same way).
    private readonly OutfitSkeleton skeleton;
    private static readonly HashSet<string> NoBones = new HashSet<string>(StringComparer.Ordinal);

    /// <param name="prefab">The character's model as its file has it, standing as it was made.</param>
    public VrcWearer(GameObject prefab)
    {
        var animator = prefab.GetComponentInChildren<Animator>(true);
        var avatar = animator != null ? animator.avatar : null;
        string hipsName = null;
        if (avatar != null && avatar.isHuman)
            foreach (var human in avatar.humanDescription.human)
            {
                Humanoid.Add(human.boneName);
                if (human.humanName == "Hips") hipsName = human.boneName;
                string part = VrcSway.BodyPart(human.humanName);
                if (part != null) parts[human.boneName] = part;
            }
        var all = prefab.GetComponentsInChildren<Transform>(true);
        Transform hips = null;
        var humanoid = new HashSet<Transform>();
        foreach (var bone in all)
        {
            if (hips == null && hipsName != null && bone.name == hipsName) hips = bone;
            if (Humanoid.Contains(bone.name)) humanoid.Add(bone);
        }
        var top = hips != null ? hips : prefab.transform;
        foreach (var bone in top.GetComponentsInChildren<Transform>(true)) Bones.Add(bone.name);
        var drawn = new HashSet<Transform>();
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true)) drawn.Add(renderer.transform);
        skeleton = new OutfitSkeleton(top, prefab.transform, drawn, humanoid);
    }

    /// <summary>
    /// The names of an outfit's bones that only share a name with one of the character's, as dressing sorts them
    /// (OutfitSkeleton): not a humanoid bone, standing elsewhere, and not hanging from her bone's parent as hers does.
    /// Worn, such a bone stays the outfit's own and is renamed after the model, so its chain sways on the outfit's own
    /// rows, under that name.
    /// </summary>
    public HashSet<string> Namesakes(GameObject outfit)
    {
        var namesakes = new List<Transform>();
        skeleton.Sort(outfit.transform, new Dictionary<Transform, Transform>(), namesakes);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bone in namesakes) names.Add(bone.name);
        return names;
    }

    /// <summary>
    /// The body part a collider stands on: its own bone, or the nearest humanoid bone above it (colliders often hang
    /// from bones of their own, "Collider_UpperLeg.L").
    /// </summary>
    public string PartOf(Transform bone)
    {
        for (var at = bone; at != null; at = at.parent)
            if (parts.TryGetValue(at.name, out var part)) return part;
        return null;
    }

    /// <summary>The character's own sway rows, under her id.</summary>
    public List<Dictionary<string, string>> OwnRows(IReadOnlyList<VrcPhysBone> bones, GameObject prefab, string skin) =>
        VrcSway.Rows(bones, prefab, skin, Humanoid, PartOf);

    /// <summary>
    /// The sway rows of an outfit made for this character, under "model@skin" (the outfit made for each character has
    /// bones of its own). The bones it shares with her follow hers, which sway already. One that only shares a name
    /// stands elsewhere and is the outfit's own (<see cref="Namesakes"/>): Chocolate rice's mirrored side locks hang
    /// from "R_Side" or "L_Side", the names of Mayo's other locks, and every bone of theirs carried one of her names,
    /// so they got no rows and hung still (2026-11-12). Such a bone sways on the outfit's row, written under the name
    /// the game gives it once worn ("R_Side [mayo-side-hair-1]").
    /// </summary>
    public List<Dictionary<string, string>> OutfitRows(IReadOnlyList<VrcPhysBone> bones, GameObject outfit, string model, string skin)
    {
        var namesakes = Namesakes(outfit);
        var shared = new HashSet<string>(Bones, StringComparer.Ordinal);
        shared.ExceptWith(namesakes);
        var rows = VrcSway.Rows(bones, outfit, model + OutfitLook.Separator + skin, shared, PartOf);
        if (namesakes.Count == 0) return rows;
        foreach (var row in rows)
        {
            if (!row.TryGetValue("roots", out string roots)) continue;
            string[] names = roots.Split('|');
            for (int i = 0; i < names.Length; i++)
                if (namesakes.Contains(names[i])) names[i] = OutfitSkeleton.Mark(names[i], model);
            row["roots"] = string.Join("|", names);
        }
        return rows;
    }

    /// <summary>
    /// The sway rows of a model made for anyone (an accessory sold for any avatar): it shares no bone with any
    /// character, so its rows are the model's own (under its id) and swing on whoever wears it.
    /// </summary>
    public static List<Dictionary<string, string>> AnyoneRows(IReadOnlyList<VrcPhysBone> bones, GameObject outfit, string model) =>
        VrcSway.Rows(bones, outfit, model, NoBones, _ => null);
}
