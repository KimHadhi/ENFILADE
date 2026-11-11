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
/// PhysBone starting under it) the chains' first bones are listed instead. A skirt (four or more chains under a root
/// named like one) becomes a ring, its chains ordered round the body, so the legs and arms push it as a whole; a breast
/// becomes a spring. Chains with the same settings share a row. Bones an outfit shares with the character are left out:
/// they follow the character's own bones, which sway already. Each row names the meshes it moves, so it rests while none
/// of them shows.
/// Numbers, from VRChat's PhysBone documentation to Magica's: pull (pull back to the rest pose) → stiffness; spring
/// (wobble) → less damping; gravity (share of g) → gravity ×10, as the presets use 2 for hair and 5 for skirts;
/// gravity falloff as is; immobile (motion of the world ignored) → 1 - world inertia; the angle limit as is; the radius
/// (metres) → thickness as a share of the chain's length.
/// </summary>
internal static class VrcSway
{
    private static readonly string[] SkirtWords = { "skirt", "スカート", "frill", "hem", "すそ", "裾" };
    private static readonly string[] BreastWords = { "breast", "bust", "oppai", "胸", "おっぱい" };

    /// <param name="prefab">The prefab as read, before VRChat's parts come off.</param>
    /// <param name="owner">Cloth.csv's skin column: the character's id, or the outfit model's.</param>
    /// <param name="shared">Bone names the chains may not start at: the character's humanoid bones, or for an outfit every bone of the character.</param>
    /// <param name="partOf">The body part ("legs", "head") a collider's bone stands for, or null.</param>
    internal static List<Dictionary<string, string>> Rows(VrcPrefab prefab, GameObject root, string owner, ISet<string> shared,
        Func<string, string> partOf)
    {
        var roots = new HashSet<Transform>(prefab.PhysBones.Select(bone => bone.Root));
        var rows = new List<Dictionary<string, string>>();
        var lines = new Dictionary<string, Dictionary<string, string>>();
        var seen = new HashSet<Transform>();
        foreach (var bone in prefab.PhysBones)
        {
            // One PhysBone per root, and none inside another's chains but right at their start: VRChat moves a bone once.
            if (!seen.Add(bone.Root)) continue;
            if (roots.Any(other => other != bone.Root && bone.Root.IsChildOf(other) && bone.Root.parent != other)) continue;
            var starts = new List<Transform>();
            foreach (Transform child in bone.Root)
                if (!bone.Ignore.Contains(child) && !shared.Contains(child.name) && !roots.Contains(child) && child.GetComponent<Renderer>() == null)
                    starts.Add(child);
            bool spring = Named(bone.Root, starts, BreastWords);
            bool ring = !spring && starts.Count >= 4 && Named(bone.Root, starts, SkirtWords);
            // The root itself is listed and turns with its chains (Magica's root rotation), unless it is a bone the body
            // shares (turning it would turn the whole outfit, or the chest), another PhysBone starts under it, or it is a
            // ring, which needs one root per chain: then the chains' first bones are listed instead.
            bool nested = roots.Any(other => other != bone.Root && other.IsChildOf(bone.Root));
            List<Transform> chains;
            if (!ring && !nested && !shared.Contains(bone.Root.name) && (bone.Root.childCount > 0 || bone.Endpoint != Vector3.zero))
                chains = new List<Transform> { bone.Root };
            else chains = ring ? RoundTheBody(bone.Root, starts) : starts;
            if (chains.Count == 0) continue;
            // A bone with nothing under it swings toward a tip put under it, as VRChat's end point does.
            bool tip = chains.Any(chain => chain.childCount == 0);
            float length = chains.Max(Reach);
            starts = chains;
            var row = Settings(bone, ring, spring, length, tip);
            row["shapes"] = Shapes(bone, ring, partOf);
            row["skin"] = owner;
            var moved = new HashSet<Transform>();
            foreach (var start in starts) moved.UnionWith(start.GetComponentsInChildren<Transform>(true));
            var meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(mesh => mesh.bones.Any(moved.Contains)).Select(mesh => mesh.name).Distinct().ToList();
            if (meshes.Count == 0) continue;

            string key = string.Join(",", row.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
            if (!ring && lines.TryGetValue(key, out var shared2))
            {
                shared2["roots"] += "|" + string.Join("|", starts.Select(s => s.name));
                shared2["parts"] = string.Join("|", Split(shared2["parts"]).Concat(meshes).Distinct());
                continue;
            }
            row["id"] = owner + "-" + Slug(bone.Root.name);
            row["roots"] = string.Join("|", starts.Select(s => s.name));
            row["parts"] = string.Join("|", meshes);
            rows.Add(row);
            if (!ring) lines[key] = row;
        }
        return rows;
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

    // Rings always meet the legs and the arms, as the game's skirts do; other parts meet what their colliders stood for.
    private static string Shapes(VrcPrefab.PhysBone bone, bool ring, Func<string, string> partOf)
    {
        if (ring) return "legs|arms";
        if (!bone.Collides) return "";
        var parts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var collider in bone.Colliders)
            if (partOf(collider.name) is string part) parts.Add(part);
        return string.Join("|", parts);
    }

    private static bool Named(Transform root, List<Transform> starts, string[] words) =>
        words.Any(word => root.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
            || starts.Any(start => start.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0));

    // A ring is joined in the order its roots are listed, so they go round the body.
    private static List<Transform> RoundTheBody(Transform root, List<Transform> starts) =>
        starts.OrderBy(start =>
        {
            Vector3 away = start.position - root.position;
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

    private static IEnumerable<string> Split(string list) => list.Split('|').Where(name => name.Length > 0);

    private static string Text(float value) => Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);

    internal static string Slug(string name)
    {
        var slug = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
            slug.Append(char.IsLetterOrDigit(c) ? c : '-');
        return slug.ToString().Trim('-');
    }
}
