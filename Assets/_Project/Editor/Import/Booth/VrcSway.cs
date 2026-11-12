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
/// each skirt chain a PhysBone of its own. Breasts, hips, bellies and thighs become springs of flesh (Flesh). Chains with
/// the same settings share a row. Bones an outfit shares with the character are left out: they follow the character's own bones, which sway
/// already. VRChat's gimmicks (the AFK animals, a grabbable tongue or cheeks, a runny nose) are left out.
/// Each row names the meshes that really hang from its bones (by skin weight: BOOTH meshes list the whole skeleton), so
/// it rests while none of them shows.
/// Numbers, from VRChat's PhysBone documentation to Magica's: pull (pull back to the rest pose) → stiffness; spring
/// (wobble) → less damping; gravity (share of g) → gravity ×10, as the presets use 2 for hair and 5 for skirts;
/// immobile (the body's motion ignored) → 1 - world inertia (half of it for a chain that hangs: ImmobileShare); the
/// angle limit as is; the radius (metres) → thickness as a share of the chain's length; colliders → the body parts
/// they hang from.
/// A chain that hangs (hair, tails, ribbons, ties: not a ring, not a spring) carries its maker's numbers down its whole
/// length, curves included, as Magica's own curves (ClothPart), and Magica's hair preset no longer bends them: its
/// curves let a bone near the root turn a fifth of the angle set (Mayo's back hair 9° of 45) and rest twice as stiff
/// as at the end. VRChat keeps a chain off the body with its limit cones, turned (limitRotation) so a strand rests on
/// the cone's edge and can swing out but not in; Magica's cones can't turn, so such a chain meets the whole body here
/// instead (PlayerBodyCloth.Touches) and rests where its maker drew it while the character stands (gravity falloff 1:
/// Magica's falloff is the whole body's lean, not each bone's, and gravity pulled Mayo's side locks off her chest and
/// into it). PhysBones slide along what they touch, so there is no friction: Magica's static friction held a strand
/// that slowly met the body where it was, and her hair looked painted on (the user, 2026-11-12: "마요는 머리카락이 안
/// 움직여. 어쩐지 내려온 옆머리가 가슴 쪽을 뚫더라"). Rings keep the preset they were tuned with.
/// A spring of the body's flesh springs as soft, heavy flesh rather than as cloth (ClothPart.Flesh, FleshSpring): its maker's
/// pull and spring become how many times a second it swings and how much of each swing it keeps, its limit cone holds
/// it, and the bones its PhysBone ignores stay still, as in VRChat (the user, 2026-11-12: "가슴 물리가 살덩이처럼 좀
/// 탱탱하고 질량이 느껴졌으면 좋겠어. 머리처럼 너무 흩날리는 느낌").
/// </summary>
internal static class VrcSway
{
    /// <summary>
    /// Raised whenever the rows this writes change, so the rows an older one wrote are written again (BoothImport).
    /// 2: hanging chains carry their curves, hold their drawn shape, slide without friction and take half of immobile;
    /// switched-off PhysBones are skipped (2026-11-12).
    /// 3: skirt chains that each hang from a bone of their own join one ring (Sera Yura's pleats, 2026-11-12).
    /// 4: springs of flesh carry firmness, bounce and the speed they feel, their makers' limit cones with their curves, and
    /// the bones their PhysBones ignore (2026-11-12).
    /// 5: flesh by kind (breasts softer than a bottom), the limit at the swinging bone's depth, and a slide for a lump whose
    /// bone stands upright (2026-11-12).
    /// </summary>
    internal const int Version = 5;
    // Depths down a chain where its curves are read: root, quarters, far end.
    private const int Samples = 5;
    // How much of a PhysBone's immobile carries over to a chain that hangs. VRChat makers set it high to calm what a
    // VR body does and a game body doesn't: a tracked head jerking every frame and thumbstick walking (Ichigo's hair
    // 0.54-0.86). Taken whole, it left her hair 14-46% of the body's moves, so turning, running or being spun in the
    // wardrobe hardly stirred it; the game's own fast turns are already capped by the presets' speed limits (720°/s
    // for the body, 360°/s felt by the cloth). Half keeps a calm maker's hair calmer than a free one's: Ichigo's back
    // hair takes 0.57 of the body's moves, near the 0.7 Kazusa's hand-tuned hair swings with.
    private const float ImmobileShare = .5f;
    // How much VRChat's advanced stiffness (a bone keeping its turn) firms flesh beside pull: the weight Stiffness() gives
    // it against pull's (.3 to .35).
    private const float AdvancedHold = .86f;
    // The table holding how makers' numbers become flesh, a row per kind and Default for the rest (FleshKinds).
    private const string FleshTable = "ClothFlesh", FleshRow = "Default";
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
        var flesh = FleshKinds.Read();

        // Skirt chains hanging side by side from one bone: one ring. A chain may hang from a bone of its own holding
        // nothing else: Sera Yura hangs each of its twenty pleats from a Sera_Yura_Skirt_Dummy under Sera_Yura_Skirt_Root,
        // and grouped by their parents they made twenty loose strands, not a skirt (2026-11-12).
        foreach (var hanging in bones.Where(bone => bone.Root.parent != null && Named(bone.Root, SkirtWords) && !shared.Contains(bone.Root.name))
                     .GroupBy(bone => HungFrom(bone.Root, shared)))
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
            if (spring) Flesh(row, bone, chains, flesh.For(bone.Root.name));
            string key = owner + "|" + string.Join(",", row.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
            // Named after their own side (Breast.1_L.001, Breast.1_R.001), so they join a shared row after the key.
            string still = spring ? string.Join("|", Still(bone, chains)) : "";
            if (!ring && lines.TryGetValue(key, out var same))
            {
                var moved = MeshesMovedBy(chains, meshes);
                if (moved.Count == 0) continue;
                same["roots"] += "|" + string.Join("|", chains.Select(chain => chain.name));
                same["parts"] = string.Join("|", same["parts"].Split('|').Concat(moved).Distinct());
                if (still.Length > 0) same["still"] = same.TryGetValue("still", out var held) && held.Length > 0 ? held + "|" + still : still;
                continue;
            }
            if (still.Length > 0) row["still"] = still;
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
        float stiffness = Stiffness(bone, -1f);
        float angle = bone.LimitType == 0 ? 90f : bone.LimitType == 1 ? bone.MaxAngleX : Mathf.Max(bone.MaxAngleX, bone.MaxAngleZ);
        var row = new Dictionary<string, string>
        {
            ["ring"] = ring ? "true" : "false",
            ["spring"] = spring ? "true" : "false",
            ["preset"] = spring ? "MiddleSpring" : ring ? "Skirt" : "ShortHair",
            ["rootTurn"] = "1",
            ["gravity"] = Text(spring ? 0f : Mathf.Clamp(bone.Gravity * 10f, 0f, 10f)),
            ["gravityFalloff"] = Text(Mathf.Clamp01(bone.GravityFalloff)),
            ["damping"] = Text(Mathf.Lerp(.25f, .03f, Mathf.Clamp01(bone.Spring))),
            ["worldInertia"] = Text(Mathf.Clamp01(1f - bone.Immobile)),
            ["localInertia"] = "1",
            ["speedLimit"] = "5",
            ["centrifugal"] = ring ? "0.1" : "0.3",
            ["windPower"] = ring ? "0.2" : "0.5",
            ["movingWind"] = ring ? "0" : "1",
            ["windSync"] = ring ? "0.7" : "0",
            ["stiffness"] = Text(stiffness),
            ["angleLimit"] = Text(Mathf.Clamp(angle, 5f, 180f)),
            ["friction"] = "0.05",
            ["teleportDistance"] = "2",
        };
        if (!ring && !spring) Hanging(row, bone, length);
        else if (bone.Radius > 0f && length > 0f) row["thickness"] = Text(Mathf.Clamp(bone.Radius / length, 0f, .5f));
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

    // A chain that hangs, as its maker set it down its length. Each number is read at five depths, curve applied, and
    // written as the row's largest with a curve of shares (ClothPart). Immobile (its share that carries over) becomes
    // Magica's two inertias: what the far end feels of the body's motion is the world inertia, and how much more of it
    // the root takes with it is the depth inertia (Magica's grows toward the root as 1 - depth², as VRChat makers draw
    // immobile falling off toward the end: Mayo's side lock 0.7 at the root, 0.29 at its end). Gravity is held off while
    // she stands and friction is none (see the class).
    private static void Hanging(Dictionary<string, string> row, VrcPrefab.PhysBone bone, float length)
    {
        var stiff = new float[Samples];
        var damp = new float[Samples];
        var limit = new float[Samples];
        var radius = new float[Samples];
        var still = new float[Samples];
        float gravity = 0f;
        for (int i = 0; i < Samples; i++)
        {
            float depth = i / (Samples - 1f);
            stiff[i] = Stiffness(bone, depth);
            damp[i] = Damping(Mathf.Clamp01(VrcPrefab.PhysBone.At(bone.Spring, bone.SpringCurve, depth)));
            limit[i] = Mathf.Clamp(Limit(bone, depth), 5f, 180f);
            radius[i] = Mathf.Max(0f, VrcPrefab.PhysBone.At(bone.Radius, bone.RadiusCurve, depth));
            still[i] = Mathf.Clamp01(VrcPrefab.PhysBone.At(bone.Immobile, bone.ImmobileCurve, depth)) * ImmobileShare;
            gravity += VrcPrefab.PhysBone.At(bone.Gravity, bone.GravityCurve, depth) / Samples;
        }
        Down(row, "stiffness", "stiffnessCurve", stiff);
        Down(row, "damping", "dampingCurve", damp);
        Down(row, "angleLimit", "angleCurve", limit);
        float widest = radius.Max();
        if (widest > 0f && length > 0f)
        {
            row["thickness"] = Text(Mathf.Clamp(widest / length, 0f, .5f));
            row["thicknessCurve"] = Shares(radius);
        }
        float root = still[0], end = still[Samples - 1];
        bool fading = root > end && end < 1f;
        row["worldInertia"] = Text(1f - (fading ? end : (root + end) * .5f));
        row["depthInertia"] = Text(fading ? (root - end) / (1f - end) : 0f);
        row["gravity"] = Text(Mathf.Clamp(gravity * 10f, 0f, 10f));
        row["gravityFalloff"] = "1";
        row["friction"] = "0";
    }

    // A spring of the body's flesh (breasts, a bottom, a belly, thighs), soft and heavy rather than cloth (ClothPart.Flesh,
    // FleshSpring), by the kind its name says (ClothFlesh.csv: breasts swing softer than a bottom, a belly or thighs, which
    // muscle holds). How hard its maker pulls it back (pull; in VRChat's advanced mode its stiffness too) sets how many
    // times a second it swings, and how much it wobbles on the way (spring) how much of each swing it keeps, both read down
    // the whole chain and spread over the kind's bands: Mayo's breasts (pull .9 rising from half, spring .95) swing 3.2
    // times a second keeping .17, Ichigo's (pull .38 and spring .85, both falling to half) 2.6 and .2. Only the bones on
    // the root swing (one lump: ClothPart.HoldStill), so the maker's limit is read at their depth down the chain, as VRChat
    // reads it for them (Mayo's 24.7°, Ichigo's 16.7°; read at the far end it cut Ichigo's to 12.5°). A lump whose bone
    // stands upright can't show its bounce by turning (Mayo's bottom, 32° off upright): its root slides that kind's share
    // of the part's length, times how upright the bone stands. Stiffness and damping are worked out from these
    // (ClothPart.Apply), and it takes no wind: wind and Magica's damping, which is air drag, are what hair streams back in.
    private static void Flesh(Dictionary<string, string> row, VrcPrefab.PhysBone bone, List<Transform> chains, FleshKind kind)
    {
        float pull = 0f, wobble = 0f;
        for (int i = 0; i < Samples; i++)
        {
            float depth = i / (Samples - 1f);
            float hold = bone.Integration == 1 ? VrcPrefab.PhysBone.At(bone.Stiffness, bone.StiffnessCurve, depth) * AdvancedHold : 0f;
            pull += Mathf.Clamp01(VrcPrefab.PhysBone.At(bone.Pull, bone.PullCurve, depth) + hold) / Samples;
            wobble += Mathf.Clamp01(VrcPrefab.PhysBone.At(bone.Spring, bone.SpringCurve, depth)) / Samples;
        }
        // The bone that swings on the part's first root, and how far down the chain it ends. A root with nothing under it
        // swings a tip hung straight down from it (ClothPart.AddTips).
        var root = chains[0];
        Transform swinging = null;
        foreach (Transform child in root)
            if (!bone.Ignore.Contains(child) && child.GetComponent<Renderer>() == null)
            {
                swinging = child;
                break;
            }
        Vector3 along = swinging != null ? swinging.position - root.position : Vector3.down;
        float reach = Reach(root);
        float swingDepth = swinging != null && reach > 0f ? Mathf.Clamp01(along.magnitude / reach) : 1f;
        float upright = along.sqrMagnitude > 1e-12f ? Mathf.Abs(Vector3.Dot(along.normalized, Vector3.up)) : 0f;
        row["firmness"] = Text(Mathf.Lerp(kind.SoftHz, kind.FirmHz, pull));
        row["bounce"] = Text(Mathf.Lerp(kind.DeadBounce, kind.LiveBounce, wobble));
        row["feltSpeed"] = Text(kind.FeltSpeed);
        row["slide"] = Text(kind.Slide * upright);
        row["angleLimit"] = Text(Mathf.Clamp(Limit(bone, swingDepth), 5f, 180f));
        row["angleCurve"] = "1";
        row["stiffness"] = "";
        row["damping"] = "";
        row["windPower"] = "0";
        row["movingWind"] = "0";
    }

    // The bones a PhysBone leaves out (its ignore list) inside these chains. VRChat holds them, and all under them, still
    // against their parent; so does the row (ClothPart.still), where Magica would swing them as particles of their own.
    // Bones listed from outside the chains are none of the row's (Mayo's breasts list a switched-off small-breast setup
    // beside the armature, Breast_PB_small). Only springs for now: a chain that hangs is fitted and laid down bone by bone
    // (ClothBones), which would have to leave them out as well.
    private static IEnumerable<string> Still(VrcPrefab.PhysBone bone, List<Transform> chains) =>
        bone.Ignore.Where(ignored => ignored != null && chains.Any(chain => ignored != chain && ignored.IsChildOf(chain)))
            .Select(ignored => ignored.name).Distinct();

    // How makers' numbers become one kind of flesh (a row of Tables/ClothFlesh): pull 0..1 spread from softHz to firmHz,
    // spring 0..1 from deadBounce to liveBounce, the speed of the body it feels, and how far it slides where its bone
    // stands upright. The defaults are the table's Default row, for a missing table.
    private sealed class FleshKind
    {
        public string[] Words = Array.Empty<string>();
        public float SoftHz = 2.8f, FirmHz = 4.4f, DeadBounce = .3f, LiveBounce = .18f, FeltSpeed = 3.5f, Slide = .15f;
    }

    // The kinds of flesh in the table's order, the first whose words a root's name holds taking it, Default the rest.
    private sealed class FleshKinds
    {
        private readonly List<FleshKind> kinds = new List<FleshKind>();
        private FleshKind rest = new FleshKind();

        public static FleshKinds Read()
        {
            var read = new FleshKinds();
            foreach (string id in GameTables.RowIds(FleshTable))
            {
                if (id.StartsWith("#", StringComparison.Ordinal) || !GameTables.TryGetRow(FleshTable, id, out var row)) continue;
                var kind = new FleshKind
                {
                    Words = row.TryGetValue("words", out var words)
                        ? words.Split('|').Select(word => word.Trim()).Where(word => word.Length > 0).ToArray() : Array.Empty<string>(),
                };
                kind.SoftHz = Number(row, "softHz", kind.SoftHz);
                kind.FirmHz = Number(row, "firmHz", kind.FirmHz);
                kind.DeadBounce = Number(row, "deadBounce", kind.DeadBounce);
                kind.LiveBounce = Number(row, "liveBounce", kind.LiveBounce);
                kind.FeltSpeed = Number(row, "feltSpeed", kind.FeltSpeed);
                kind.Slide = Number(row, "slide", kind.Slide);
                if (string.Equals(id, FleshRow, StringComparison.OrdinalIgnoreCase)) read.rest = kind;
                else read.kinds.Add(kind);
            }
            if (read.kinds.Count == 0 && !GameTables.TryGetRow(FleshTable, FleshRow, out _))
                Debug.LogWarning($"[BOOTH] Resources/Tables/{FleshTable}.csv를 읽지 못해 살 출렁임을 기본값으로 씁니다.");
            return read;
        }

        public FleshKind For(string boneName)
        {
            foreach (var kind in kinds)
                foreach (string word in kind.Words)
                    if (boneName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return kind;
            return rest;
        }

        private static float Number(IReadOnlyDictionary<string, string> row, string column, float fallback) =>
            row.TryGetValue(column, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : fallback;
    }

    // Pull (and in VRChat's advanced mode stiffness) as Magica's angle restoration, at a depth; below zero, the values
    // as set, curves left out.
    private static float Stiffness(VrcPrefab.PhysBone bone, float depth)
    {
        float pull = depth < 0f ? bone.Pull : VrcPrefab.PhysBone.At(bone.Pull, bone.PullCurve, depth);
        float stiff = depth < 0f ? bone.Stiffness : VrcPrefab.PhysBone.At(bone.Stiffness, bone.StiffnessCurve, depth);
        return Mathf.Clamp(.05f + .35f * Mathf.Clamp01(pull) + (bone.Integration == 1 ? .3f * Mathf.Clamp01(stiff) : 0f), .02f, .8f);
    }

    // VRChat's spring (how much a bone wobbles on its way back) as Magica's damping, which takes that share of a bone's
    // speed every step. VRChat's default, 0.2, lands on the 0.1 of Magica's hair preset, the damping the game's own hair
    // swings with; the old straight line put it at 0.21, which takes speed twice as fast, and Mayo's hair stopped before
    // it got going. No wobble at all damps 0.2, full wobble 0.02, the least that still settles.
    private static float Damping(float spring) =>
        spring <= .2f ? Mathf.Lerp(.2f, .1f, spring / .2f) : Mathf.Lerp(.1f, .02f, (spring - .2f) / .8f);

    // The angle a bone may turn from its rest at a depth: an angle cone's, a hinge's, or the wider of a polar limit's two.
    private static float Limit(VrcPrefab.PhysBone bone, float depth) => bone.LimitType switch
    {
        0 => 90f,
        1 or 2 => VrcPrefab.PhysBone.At(bone.MaxAngleX, bone.MaxAngleXCurve, depth),
        _ => Mathf.Max(VrcPrefab.PhysBone.At(bone.MaxAngleX, bone.MaxAngleXCurve, depth), VrcPrefab.PhysBone.At(bone.MaxAngleZ, bone.MaxAngleZCurve, depth)),
    };

    // A number read down the chain: its largest, and the curve of shares that gives the rest.
    private static void Down(Dictionary<string, string> row, string value, string curve, float[] samples)
    {
        row[value] = Text(samples.Max());
        row[curve] = Shares(samples);
    }

    // "depth:share|…" of the largest, or "1" where it stays the same all the way.
    private static string Shares(float[] samples)
    {
        float top = samples.Max(), least = samples.Min();
        if (top <= 0f || top - least <= top * .01f) return "1";
        return string.Join("|", samples.Select((sample, i) => Text(i / (Samples - 1f)) + ":" + Math.Round(sample / top, 2).ToString(CultureInfo.InvariantCulture)));
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

    // The bone a chain hangs from beside its neighbours: its parent, or, past parents that hold nothing but it (a pleat's
    // own dummy bone), the first one above that holds more. A bone the body shares ends the climb.
    private static Transform HungFrom(Transform root, ISet<string> shared)
    {
        var at = root.parent;
        while (at.childCount == 1 && at.parent != null && !shared.Contains(at.name)) at = at.parent;
        return at;
    }

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
