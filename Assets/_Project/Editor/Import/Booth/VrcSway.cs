using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Turns a BOOTH prefab's VRChat PhysBones into rows of Cloth.csv, the game's own sway (Magica Cloth with the body's
/// colliders, the arms pressing skirts, and the cloth shared over the network), so each avatar and outfit sways the way
/// its maker tuned it without VRChat. The user asked for exactly that: compatible, but done our way.
/// A PhysBone keeps its root in place and swings the chains under it; a row here lists that root and lets it turn with
/// them (rootTurn 1), which is the same motion. Where turning the root would be wrong (a bone the body shares, another
/// PhysBone starting under it) the chains' first bones are listed instead. Skirt chains (named like one) hanging from
/// one bone become one ring, ordered round the body, so the legs and arms push the skirt as a whole; makers often give
/// each skirt chain a PhysBone of its own. Breasts, hips and thighs become springs. Chains with the same settings share
/// a row. Bones an outfit shares with the character are left out: they follow the character's own bones, which sway
/// already. VRChat's gimmicks (the AFK animals, a grabbable tongue or cheeks, a runny nose) are left out.
/// Each row names the meshes that really hang from its bones (by skin weight: BOOTH meshes list the whole skeleton), so
/// it rests while none of them shows.
/// Numbers, from VRChat's PhysBone documentation to Magica's: pull (pull back to the rest pose) → stiffness; spring
/// (wobble) → less damping; gravity (share of g) → gravity ×10, as the presets use 2 for hair and 5 for skirts;
/// gravity falloff as is; immobile (motion of the world ignored) → 1 - world inertia; the angle limit as is; the radius
/// (metres) → thickness as a share of the chain's length; colliders → the body parts they hang from.
/// </summary>
internal static class VrcSway
{
    private static readonly string[] SkirtWords = { "skirt", "スカート", "frill", "hem", "すそ", "裾" };
    private static readonly string[] SpringWords = { "breast", "bust", "oppai", "胸", "おっぱい", "butt", "hip", "thigh", "belly", "stomach", "jiggle" };
    private static readonly string[] GimmickWords = { "tongue", "舌", "cheek", "nose", "afk" };
    // Each mesh's bones that carry any of its skin, by bone slot, read once.
    private static readonly Dictionary<Mesh, HashSet<int>> weighted = new Dictionary<Mesh, HashSet<int>>();

    /// <param name="prefab">The prefab as read from its file.</param>
    /// <param name="root">The prefab's loaded root, whose meshes the rows name.</param>
    /// <param name="owner">Cloth.csv's skin column: the character's id, or the outfit model's.</param>
    /// <param name="shared">Bone names the chains may not start at: the character's humanoid bones, or for an outfit every bone of the character.</param>
    /// <param name="partOf">The body part ("legs", "head") a collider stands on, or null.</param>
    internal static List<Dictionary<string, string>> Rows(VrcPrefab prefab, GameObject root, string owner, ISet<string> shared,
        Func<Transform, string> partOf)
    {
        var bones = prefab.PhysBones.Where(bone => !Gimmick(bone.Root)).ToList();
        var roots = new HashSet<Transform>(bones.Select(bone => bone.Root));
        var meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(mesh => mesh.sharedMesh != null && !Gimmick(mesh.transform)).ToList();
        var rows = new List<Dictionary<string, string>>();
        var done = new HashSet<Transform>();

        // Skirt chains hanging side by side from one bone: one ring.
        foreach (var hanging in bones.Where(bone => bone.Root.parent != null && Named(bone.Root, SkirtWords) && !shared.Contains(bone.Root.name))
                     .GroupBy(bone => bone.Root.parent))
        {
            var chains = hanging.Select(bone => bone.Root).Distinct().ToList();
            if (chains.Count < 4) continue;
            chains = RoundTheBody(hanging.Key, chains);
            var row = Settings(hanging.First(), true, false, chains.Max(Reach), chains.Any(chain => chain.childCount == 0));
            row["shapes"] = "legs|arms";
            if (!Finish(row, owner, hanging.Key.name + "-skirt", chains, meshes)) continue;
            rows.Add(row);
            done.UnionWith(chains);
        }

        var lines = new Dictionary<string, Dictionary<string, string>>();
        foreach (var bone in bones)
        {
            // One PhysBone per root, and none inside another's chains but right at their start: VRChat moves a bone once.
            if (!done.Add(bone.Root)) continue;
            if (roots.Any(other => other != bone.Root && bone.Root.IsChildOf(other) && bone.Root.parent != other)) continue;
            var starts = new List<Transform>();
            foreach (Transform child in bone.Root)
                if (!bone.Ignore.Contains(child) && !shared.Contains(child.name) && !roots.Contains(child) && child.GetComponent<Renderer>() == null)
                    starts.Add(child);
            bool skirt = Named(bone.Root, SkirtWords);
            bool spring = !skirt && Named(bone.Root, SpringWords);
            bool ring = skirt && starts.Count >= 4;
            // The root itself is listed and turns with its chains (Magica's root rotation), unless it is a bone the body
            // shares (turning it would turn the whole outfit, or the chest), another PhysBone starts under it, or it is a
            // ring, which needs one root per chain: then the chains' first bones are listed instead.
            bool nested = roots.Any(other => other != bone.Root && other.IsChildOf(bone.Root));
            List<Transform> chains;
            if (!ring && !nested && !shared.Contains(bone.Root.name) && (bone.Root.childCount > 0 || bone.Endpoint != Vector3.zero))
                chains = new List<Transform> { bone.Root };
            else chains = ring ? RoundTheBody(bone.Root, starts) : starts;
            if (chains.Count == 0) continue;
            var row = Settings(bone, ring, spring, chains.Max(Reach), chains.Any(chain => chain.childCount == 0));
            row["shapes"] = Shapes(bone, ring, skirt, partOf);
            string key = owner + "|" + string.Join(",", row.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
            if (!ring && lines.TryGetValue(key, out var same))
            {
                var moved = MeshesMovedBy(chains, meshes);
                if (moved.Count == 0) continue;
                same["roots"] += "|" + string.Join("|", chains.Select(chain => chain.name));
                same["parts"] = string.Join("|", same["parts"].Split('|').Concat(moved).Distinct());
                continue;
            }
            if (!Finish(row, owner, bone.Root.name, chains, meshes)) continue;
            rows.Add(row);
            if (!ring) lines[key] = row;
        }
        return rows;
    }

    // Names the row, its roots and the meshes it moves; false when it moves no mesh at all.
    private static bool Finish(Dictionary<string, string> row, string owner, string name, List<Transform> chains, List<SkinnedMeshRenderer> meshes)
    {
        var moved = MeshesMovedBy(chains, meshes);
        if (moved.Count == 0) return false;
        row["skin"] = owner;
        row["id"] = owner + "-" + Slug(name);
        row["roots"] = string.Join("|", chains.Select(chain => chain.name));
        row["parts"] = string.Join("|", moved);
        row["vrc"] = "true";
        return true;
    }

    // The meshes with skin on these chains' bones.
    private static List<string> MeshesMovedBy(List<Transform> chains, List<SkinnedMeshRenderer> meshes)
    {
        var moved = new HashSet<Transform>();
        foreach (var chain in chains) moved.UnionWith(chain.GetComponentsInChildren<Transform>(true));
        var names = new List<string>();
        foreach (var mesh in meshes)
        {
            var bones = mesh.bones;
            foreach (int slot in Weighted(mesh.sharedMesh))
                if (slot < bones.Length && bones[slot] != null && moved.Contains(bones[slot]))
                {
                    if (!names.Contains(mesh.name)) names.Add(mesh.name);
                    break;
                }
        }
        return names;
    }

    private static HashSet<int> Weighted(Mesh mesh)
    {
        if (weighted.TryGetValue(mesh, out var known)) return known;
        var used = new HashSet<int>();
        var all = mesh.GetAllBoneWeights();
        for (int i = 0; i < all.Length; i++)
            if (all[i].weight > 0f) used.Add(all[i].boneIndex);
        // A mesh whose skin can't be read is taken as hanging from every bone it lists.
        if (all.Length == 0)
            for (int i = 0; i < mesh.bindposes.Length; i++) used.Add(i);
        weighted[mesh] = used;
        return used;
    }

    private static Dictionary<string, string> Settings(VrcPrefab.PhysBone bone, bool ring, bool spring, float length, bool tip)
    {
        float stiffness = Mathf.Clamp(.05f + .35f * Mathf.Clamp01(bone.Pull) + (bone.Integration == 1 ? .3f * Mathf.Clamp01(bone.Stiffness) : 0f), .02f, .8f);
        float angle = bone.LimitType == 0 ? 90f : bone.LimitType == 1 ? bone.MaxAngleX : Mathf.Max(bone.MaxAngleX, bone.MaxAngleZ);
        var row = new Dictionary<string, string>
        {
            ["ring"] = ring ? "true" : "false",
            ["spring"] = spring ? "true" : "false",
            ["preset"] = spring ? "MiddleSpring" : ring ? "Skirt" : "ShortHair",
            ["rootTurn"] = "1",
            ["gravity"] = Text(spring ? 0f : Mathf.Clamp(bone.Gravity * 10f, 0f, 10f)),
            ["gravityFalloff"] = Text(Mathf.Clamp01(bone.GravityFalloff)),
            ["damping"] = Text(spring ? .3f : Mathf.Lerp(.25f, .03f, Mathf.Clamp01(bone.Spring))),
            ["worldInertia"] = Text(Mathf.Clamp01(1f - bone.Immobile)),
            ["localInertia"] = "1",
            ["speedLimit"] = "5",
            ["centrifugal"] = ring ? "0.1" : "0.3",
            ["windPower"] = ring ? "0.2" : "0.5",
            ["movingWind"] = ring ? "0" : "1",
            ["windSync"] = ring ? "0.7" : "0",
            ["stiffness"] = Text(spring ? Mathf.Max(stiffness, .3f) : stiffness),
            ["angleLimit"] = Text(Mathf.Clamp(spring ? Mathf.Min(angle, 20f) : angle, 5f, 180f)),
            ["friction"] = "0.05",
            ["teleportDistance"] = "2",
        };
        if (bone.Radius > 0f && length > 0f) row["thickness"] = Text(Mathf.Clamp(bone.Radius / length, 0f, .5f));
        if (tip)
        {
            float parent = bone.Root.parent != null ? Vector3.Distance(bone.Root.position, bone.Root.parent.position) : 0f;
            row["tipLength"] = Text(bone.Endpoint != Vector3.zero && parent > 0f
                ? Mathf.Clamp(bone.Endpoint.magnitude * bone.Root.lossyScale.x / parent, .3f, 2f) : .7f);
            row["tipSpread"] = "0.5";
        }
        if (ring)
        {
            row["reach"] = "0.8";
            row["backstop"] = "0.1";
        }
        return row;
    }

    // Rings meet the legs and the arms, as the game's skirts do, and any skirt chain meets the legs; other parts meet
    // the body parts their colliders stand on.
    private static string Shapes(VrcPrefab.PhysBone bone, bool ring, bool skirt, Func<Transform, string> partOf)
    {
        if (ring) return "legs|arms";
        var parts = new SortedSet<string>(StringComparer.Ordinal);
        if (skirt) parts.Add("legs");
        if (bone.Collides)
            foreach (var collider in bone.Colliders)
                if (partOf(collider) is string part) parts.Add(part);
        return string.Join("|", parts);
    }

    // A VRChat gimmick, or a part of one: nothing the game should swing.
    private static bool Gimmick(Transform bone)
    {
        for (var at = bone; at != null; at = at.parent)
            foreach (string word in GimmickWords)
                if (at.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static bool Named(Transform bone, string[] words) =>
        words.Any(word => bone.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);

    // A ring is joined in the order its roots are listed, so they go round the body.
    private static List<Transform> RoundTheBody(Transform middle, List<Transform> chains) =>
        chains.OrderBy(chain =>
        {
            Vector3 away = chain.position - middle.position;
            return Mathf.Atan2(away.x, away.z);
        }).ToList();

    // How far a chain reaches from its first bone, joint by joint, at its longest.
    private static float Reach(Transform start)
    {
        float longest = 0f;
        foreach (Transform child in start)
            longest = Mathf.Max(longest, Vector3.Distance(start.position, child.position) + Reach(child));
        return longest;
    }

    private static string Text(float value) => Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);

    internal static string Slug(string name)
    {
        var slug = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
            slug.Append(char.IsLetterOrDigit(c) ? c : '-');
        return slug.ToString().Trim('-');
    }
}
