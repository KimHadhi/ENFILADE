using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A character's bones as an outfit's file is joined to them: by OutfitDresser.Merge when the outfit goes on, and by
/// BoothImport when it writes the outfit's sway rows, so both read an outfit's bones the same way. Bones are found by
/// name, exactly first, then loosely, since "Upper_Leg.L" and "UpperLeg_L" are one bone to the people who name them by
/// hand. A bone of the outfit's file is the character's own (its twin) when it carries one of her names and is that bone
/// (<see cref="Same"/>): one of her humanoid bones, or one standing where hers does, or hanging from her bone's parent as
/// hers does. A name alone isn't enough: Chocolate rice's mirrored side locks for Mayo hang from "Side_root" and
/// "R_Side" (or "L_Side"), the names of her own lock bones, yet stand 1.3 to 6 cm from them, along the lock they bring to
/// her other side (2026-11-12). Such a namesake is a bone of the outfit's own: it keeps its place and its own sway, and
/// once worn it is renamed after its model (<see cref="Mark"/>) so nothing that finds bones by name (the cloth rows, the
/// humanoid) takes one for the other.
/// </summary>
public sealed class OutfitSkeleton
{
    /// <summary>
    /// How far apart (metres, as the character model was made) a bone of an outfit's file may stand from the character's
    /// bone of the same name, or hang from their parent apart from it, and still be it. The files of one avatar agree to
    /// 0.05 mm at most (Mayo's and Ichigo's seven outfits, measured from their FBX files on 2026-11-12); the nearest a
    /// different bone of the same name stood was 12.8 mm (the mirrored side lock's first bone).
    /// </summary>
    public const float SamePlace = .005f;

    private readonly Dictionary<string, Transform> exact = new Dictionary<string, Transform>(StringComparer.Ordinal);
    private readonly Dictionary<string, Transform> loose = new Dictionary<string, Transform>(StringComparer.Ordinal);
    private readonly ICollection<Transform> humanoid;
    private readonly Transform frame;
    private readonly float scale;

    /// <param name="top">The character's hips (its root when it has no humanoid): its bones are these and all under them.</param>
    /// <param name="frame">
    /// The character model's root. Places are measured in its frame, so the scale the body is fitted to doesn't change
    /// them, and an outfit's file, standing its root where it stood against the character's, is measured in it too.
    /// </param>
    /// <param name="skip">Transforms under the top that are no bones: the objects of the character's own meshes.</param>
    /// <param name="humanoid">
    /// Its humanoid bones. An outfit's bone of one of their names is always that bone wherever it stands: the body moves
    /// only through them, and the bind poses take in where the outfit's stood apart (OutfitDresser.Bind).
    /// </param>
    public OutfitSkeleton(Transform top, Transform frame, ICollection<Transform> skip, ICollection<Transform> humanoid)
    {
        this.frame = frame;
        this.humanoid = humanoid;
        scale = Mathf.Max(1e-6f, Mathf.Abs(frame.lossyScale.x));
        foreach (var bone in top.GetComponentsInChildren<Transform>(true))
        {
            if (bone == frame || (skip != null && skip.Contains(bone))) continue;
            exact.TryAdd(bone.name, bone);
            loose.TryAdd(Loose(bone.name), bone);
        }
    }

    /// <summary>The character's bone of this name, or null.</summary>
    public Transform Find(string name) =>
        exact.TryGetValue(name, out var bone) || loose.TryGetValue(Loose(name), out bone) ? bone : null;

    /// <summary>
    /// Sorts the bones of an outfit's file (under <paramref name="outfit"/>, its root and its meshes' objects left out)
    /// into the character's own (<paramref name="twins"/>, each to her bone) and those that only share a name with one of
    /// hers (<paramref name="namesakes"/>, may be null). Bones she has no name for are neither.
    /// </summary>
    public void Sort(Transform outfit, Dictionary<Transform, Transform> twins, ICollection<Transform> namesakes)
    {
        // Parents come before their children here, so a bone's parent is sorted when the bone is.
        foreach (var part in outfit.GetComponentsInChildren<Transform>(true))
        {
            if (part == outfit || part.GetComponent<Renderer>() != null) continue;
            var bone = Find(part.name);
            if (bone == null) continue;
            if (Same(part, bone, twins)) twins[part] = bone;
            else namesakes?.Add(part);
        }
    }

    /// <summary>
    /// Whether a bone of an outfit's file, named like <paramref name="bone"/>, is that bone: a humanoid one, one standing
    /// where it does, or one hanging from her bone's parent (its twin, from <paramref name="twins"/>) as far and the same
    /// way as hers. The last is for a file whose pose turns a bone, which carries everything under it away: Ichigo's
    /// prefab sets the turn of seven of her bones that her outfits' files leave as they are.
    /// </summary>
    public bool Same(Transform part, Transform bone, IReadOnlyDictionary<Transform, Transform> twins)
    {
        if (humanoid != null && humanoid.Contains(bone)) return true;
        if (Near(frame.InverseTransformPoint(part.position), frame.InverseTransformPoint(bone.position))) return true;
        return part.parent != null && bone.parent != null && twins != null && twins.TryGetValue(part.parent, out var up)
            && up == bone.parent && Near(Hang(part), Hang(bone));
    }

    /// <summary>
    /// The name a bone of an outfit's file takes once worn when it carries one of the character's names, "R_Side
    /// [mayo-side-hair-1]": a twin left unused in the outfit, or a namesake of the outfit's own, whose sway rows
    /// BoothImport writes under this name.
    /// </summary>
    public static string Mark(string name, string model) => name + " [" + model + "]";

    private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= SamePlace * SamePlace;

    // Where a bone hangs from its parent: its offset in the parent's turn, in metres as the model was made.
    private Vector3 Hang(Transform bone) => Quaternion.Inverse(bone.parent.rotation) * (bone.position - bone.parent.position) / scale;

    private static string Loose(string name)
    {
        var key = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            if (char.IsLetterOrDigit(c)) key.Append(char.ToLowerInvariant(c));
        return key.ToString();
    }
}
