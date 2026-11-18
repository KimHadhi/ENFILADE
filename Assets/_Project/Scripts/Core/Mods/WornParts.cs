using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// What a character's model, and anything worn on it, may be made of. A look is drawn and moved by its bones; it is
/// not a thing in the world. So of what a model's file brings, only what draws it and what moves it stays
/// (<see cref="Kept"/>): its bones, the humanoid Animator, its meshes and their renderers, a LOD group, and Unity's own
/// constraints. Everything else goes before the model is ever in the world (<see cref="Strip"/>): it would either act
/// on the game's world on its own (colliders, bodies, joints and controllers in its physics, lights, sounds, cameras,
/// particles) or runs only where the model came from (VRChat's scripts and every add-on's). What a character needs of
/// those the game makes itself, after dressing, where it means to: the player's one capsule, its hit shapes, cloth
/// colliders, a corpse's ragdoll.
/// This is one list for every way a model comes in, because each way had its own idea and the game trusted them all:
/// the mod exporter strips with it as it packs (ModAvatarExporter), and the game strips with it at the one place every
/// body and every piece goes through on its way onto a character (OutfitDresser), whoever made the file. Before, the
/// welding mask, a prefab copied from a bought prop pack, came with the pack's solid BoxCollider; worn, it stood in
/// front of the player's own face as part of the world, the crouch's roof probe found it and the stance dropped to
/// prone and could not rise (the user, 2026-11-18: "용접 마스크에 전방 콜라이더가 적용되어 앉기 시 자동 엎드리기(Prone)로
/// 전환되고 기립이 불가능해지는 문제"). Every other question the game asks of "the world" saw it too (the camera's
/// blockers, parkour's walls, the feet's floor), so the collider was never one bug but a kind of bug: a worn thing
/// that became part of the world.
/// This file is shared with the exporter's package (ModSdkPackage), so it uses nothing of the game's: Unity's own
/// types only, and nothing newer than the Unity a VRChat project runs.
/// </summary>
public static class WornParts
{
    /// <summary>Whether a part is one a worn thing keeps: what draws it or moves it.</summary>
    public static bool Kept(Component part) =>
        part is Transform || part is Animator || part is SkinnedMeshRenderer || part is MeshRenderer || part is MeshFilter
        || part is LODGroup || part is UnityEngine.Animations.IConstraint;

    /// <summary>
    /// Every part under <paramref name="root"/> a worn thing does not keep, in an order they can be taken off in
    /// (<see cref="Rank"/>). Nothing is changed: a report asks this, and <see cref="Strip"/> takes these off.
    /// </summary>
    public static List<Component> Foreign(GameObject root)
    {
        var foreign = new List<Component>();
        if (root == null) return foreign;
        foreach (var part in root.GetComponentsInChildren<Component>(true))
            // A script that is missing reads as null: there is nothing to ask and, outside the editor, no way to take it off.
            if (part != null && !Kept(part)) foreign.Add(part);
        foreign.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
        return foreign;
    }

    /// <summary>
    /// Takes off everything under <paramref name="root"/> a worn thing does not keep, at once (nothing of it is left
    /// for a physics query later in the same frame). Returns what went, counted by kind ("BoxCollider 1, AudioSource 3"),
    /// or "" when the model was clean.
    /// </summary>
    public static string Strip(GameObject root)
    {
        var went = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
        // A part another leans on can only go once that one has: in ranked order nothing is refused, and whatever a
        // rank did not foresee goes on a later pass.
        for (int pass = 0; pass < 4; pass++)
        {
            var foreign = Foreign(root);
            if (foreign.Count == 0) break;
            foreach (var part in foreign)
            {
                if (part == null) continue;
                string kind = part.GetType().Name;
                Object.DestroyImmediate(part);
                // Refused (something still leans on it), it is still there and waits for the next pass.
                if (part != null) continue;
                went.TryGetValue(kind, out int count);
                went[kind] = count + 1;
            }
        }
        if (went.Count == 0) return "";
        var text = new StringBuilder();
        foreach (var pair in went)
        {
            if (text.Length > 0) text.Append(", ");
            text.Append(pair.Key).Append(' ').Append(pair.Value);
        }
        return text.ToString();
    }

    /// <summary><see cref="Foreign"/> counted by kind, as <see cref="Strip"/> words it; "" when there is none.</summary>
    public static string Describe(List<Component> foreign)
    {
        if (foreign == null || foreign.Count == 0) return "";
        var counts = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var part in foreign)
        {
            if (part == null) continue;
            string kind = part.GetType().Name;
            counts.TryGetValue(kind, out int count);
            counts[kind] = count + 1;
        }
        var text = new StringBuilder();
        foreach (var pair in counts)
        {
            if (text.Length > 0) text.Append(", ");
            text.Append(pair.Key).Append(' ').Append(pair.Value);
        }
        return text.ToString();
    }

    /// <summary>
    /// Whether a part is one of Unity's physics: a collider, a body, a joint or a character controller, in 3D or 2D.
    /// The parts that make a worn thing solid, which a report names apart from the rest.
    /// </summary>
    public static bool Solid(Component part)
    {
        if (part is Collider || part is Rigidbody || part is Joint || part is CharacterController) return true;
        // 2D physics by name: its types are only there when that module is.
        for (var type = part != null ? part.GetType() : null; type != null && type != typeof(Component); type = type.BaseType)
        {
            string name = type.Name;
            if (name == "Collider2D" || name == "Rigidbody2D" || name == "Joint2D" || name == "Effector2D") return true;
        }
        return false;
    }

    // The order parts come off in, by what leans on what (Unity refuses to take off a part another one needs): scripts
    // lean on anything (their RequireComponent); then the small parts that lean on a host (joints and constant forces
    // on a body, Unity's cloth on its skinned mesh, sound filters on a source or a listener, a flare layer on a
    // camera); then a particle system, which holds its own renderer; then the hosts and everything else.
    private static int Rank(Component part)
    {
        if (part is MonoBehaviour) return 0;
        if (part is Joint || part is Cloth) return 1;
        if (part is Behaviour && !(part is Camera || part is Light || part is AudioSource || part is AudioListener || part is Canvas)) return 1;
        if (part is ParticleSystem) return 2;
        return 3;
    }
}
