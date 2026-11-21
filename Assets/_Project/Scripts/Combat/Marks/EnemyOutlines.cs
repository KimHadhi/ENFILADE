using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Who asks for a foe's outline. Later in the list wins where two would draw, each of OutlineLook's three on its own.</summary>
public enum OutlineSource : byte
{
    /// <summary>A wave's last enemies through walls (EnemyReveal).</summary>
    Wave,
    /// <summary>A Ghost's sight through walls (GhostSight).</summary>
    Ghost,
    /// <summary>One this PC's player can finish now (ExecutableHighlight, through HitFlashRenderer.SetMark).</summary>
    Executable,
    /// <summary>A Schießhund's bounty dye (DyeMarks, through HitFlashRenderer.SetPing).</summary>
    Dye,
    /// <summary>A ping or a bounty mark (PingMarks).</summary>
    Ping,
    /// <summary>A zombie holding a player (HoldReveal).</summary>
    Holder,
}

/// <summary>How one source wants a foe drawn. Each colour's alpha is how strong it is (0: not at all); its colour may be HDR.</summary>
public struct OutlineLook
{
    /// <summary>The band round the body where it is seen.</summary>
    public Color Seen;
    /// <summary>The band round the body where something stands in front of it, or smoke hides it.</summary>
    public Color Hidden;
    /// <summary>Laid over the body where something stands in front of it, or as much as smoke hides it.</summary>
    public Color Fill;

    public OutlineLook(Color seen, Color hidden, Color fill)
    {
        Seen = seen;
        Hidden = hidden;
        Fill = fill;
    }

    public bool Shows => Seen.a > 0f || Hidden.a > 0f || Fill.a > 0f;
}

/// <summary>
/// The one owner of what marks a foe's body on screen: a band of even width round its silhouette, and a flat fill over
/// what is hidden. Pings and bounties, a holder, the executable mark, the bounty dye, a Ghost's sight and a wave's last
/// enemies each say here what they want (Set, every frame or when it changes) and nothing else draws an outline.
/// Drawn the way shipped shooters draw theirs (Left 4 Dead's glow, the enemy outlines of Overwatch and Valorant): the
/// marked bodies are drawn into a mask of their own, and a pass over the screen lays the band where the mask ends
/// (OutlinePass, shaders OutlineMask and OutlineLay). So the band is the body's own shape at any distance, whatever its
/// mesh's normals, submeshes or materials, and nothing is laid on the body's own renderers or copied from them.
/// Until 2026-11-21 each source drew its own: an extra material on every renderer under the enemy whose back faces,
/// pushed out, made the band (HitFlashOverlay's Outline pass), and see-through copies of those renderers
/// (RevealThroughWalls) made by four systems, each with its own list of names not to copy. The user pinged a reaper in
/// the test room as a Ghost and saw a white pill over it: the Ghost's sight had copied the enemy as it came, still in
/// its capsule shell before its body was put on, the ping's extra material then went on that copy too, and a copy
/// writes no depth, so the whole back of the capsule was painted in their colour (OutlineBench, Logs/outlines.txt).
/// The parts drawn are the body's as it stands each frame: the worn character (ZombieBody.Model) once there is one, else
/// the foe's own renderers; switched-off, shadow-only and eye-glow renderers never, and of each only what the eye sees as
/// solid (opaque and cut-out materials, OwnViewMask's rule): a see-through quad riding a body, as a steel target's lead
/// splashes do, came out as a square in the outline. A marked body is kept from the map's
/// occlusion culling while marked, since behind a wall is where it is meant to show, and its animator keeps posing it.
/// Numbers: Tables/Hud/Outlines (the band's width, the dark edge outside it).
/// </summary>
public static class EnemyOutlines
{
    public const string TableName = "Outlines", TableRow = "Default";
    private const string ShaderPath = "Combat/Shaders/OutlineMask";
    private static readonly int SeenId = Shader.PropertyToID("_Seen"), HiddenId = Shader.PropertyToID("_Hidden"), FillId = Shader.PropertyToID("_Fill");
    private static readonly int SourceCount = System.Enum.GetValues(typeof(OutlineSource)).Length;
    // The last queue a material may have and still be drawn as the body: opaque and cut-out (AlphaTest) parts.
    private const int SolidQueue = 2500;

    /// <summary>One draw of the mask: a part of a marked body, one submesh of it, in that body's mask material.</summary>
    internal struct Draw
    {
        public Renderer Renderer;
        public Material Material;
        public int Submesh;
    }

    // One solid submesh of a body's renderer.
    private struct Part
    {
        public Renderer Renderer;
        public int Submesh;
    }

    private sealed class Marked
    {
        public GameObject Foe;
        public readonly OutlineLook[] Looks = new OutlineLook[SourceCount];
        public Material Material;
        public readonly List<Part> Parts = new List<Part>();
    }

    private static readonly Dictionary<GameObject, Marked> marked = new Dictionary<GameObject, Marked>();
    private static readonly List<GameObject> gone = new List<GameObject>();
    private static readonly List<Renderer> found = new List<Renderer>();
    private static readonly List<Material> looks = new List<Material>();
    private static readonly Stack<Material> spare = new Stack<Material>();
    // Renderers kept from occlusion culling while marked, with what they had before.
    private static readonly Dictionary<Renderer, bool> unculled = new Dictionary<Renderer, bool>();
    private static readonly HashSet<Renderer> drawn = new HashSet<Renderer>();
    private static readonly List<Renderer> released = new List<Renderer>();
    private static Shader shader;
    private static bool looked, dirty;
    private static int gatheredFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        marked.Clear();
        spare.Clear();
        unculled.Clear();
        shader = null;
        looked = false;
        gatheredFrame = -1;
    }

    /// <summary>Whether any foe is marked now: OutlinePass draws nothing at all otherwise.</summary>
    public static bool Any => marked.Count > 0;

    /// <summary>What <paramref name="source"/> wants of this foe's body now; a look that shows nothing lets it go.</summary>
    public static void Set(Component foe, OutlineSource source, OutlineLook look)
    {
        if (foe == null) return;
        var key = foe.gameObject;
        if (!marked.TryGetValue(key, out var entry))
        {
            if (!look.Shows) return;
            entry = new Marked { Foe = key };
            marked.Add(key, entry);
        }
        entry.Looks[(int)source] = look;
        dirty = true;
        if (!look.Shows && Empty(entry)) Remove(key, entry);
    }

    /// <summary><paramref name="source"/> no longer marks this foe.</summary>
    public static void Clear(Component foe, OutlineSource source) => Set(foe, source, default);

    /// <summary>The mask's draws for a camera now: every part of every marked body its culling mask takes (OutlinePass).</summary>
    internal static void Collect(Camera camera, List<Draw> draws)
    {
        draws.Clear();
        Gather();
        int layers = camera != null ? camera.cullingMask : ~0;
        foreach (var entry in marked.Values)
        {
            if (entry.Material == null) continue;
            foreach (var part in entry.Parts)
            {
                var renderer = part.Renderer;
                // As it stands at the draw: one switched off since the frame's gathering is not drawn.
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || (layers & (1 << renderer.gameObject.layer)) == 0) continue;
                draws.Add(new Draw { Renderer = renderer, Material = entry.Material, Submesh = part.Submesh });
            }
        }
    }

    // Once a frame (and again after a change in the same frame, as the editor's bench makes): each body's look and parts.
    private static void Gather()
    {
        if (!dirty && gatheredFrame == Time.frameCount) return;
        dirty = false;
        gatheredFrame = Time.frameCount;
        gone.Clear();
        foreach (var pair in marked)
            if (pair.Key == null) gone.Add(pair.Key);
        foreach (var key in gone) Remove(key, marked[key]);
        gone.Clear();
        drawn.Clear();
        foreach (var entry in marked.Values)
        {
            Resolve(entry);
            Parts(entry);
            foreach (var part in entry.Parts) drawn.Add(part.Renderer);
        }
        // Behind a wall is where a marked body shows: kept from the map's occlusion culling while marked.
        foreach (var part in drawn)
        {
            if (unculled.ContainsKey(part)) continue;
            unculled.Add(part, part.allowOcclusionWhenDynamic);
            part.allowOcclusionWhenDynamic = false;
        }
        released.Clear();
        foreach (var pair in unculled)
            if (pair.Key == null || !drawn.Contains(pair.Key)) released.Add(pair.Key);
        foreach (var part in released)
        {
            if (part != null) part.allowOcclusionWhenDynamic = unculled[part];
            unculled.Remove(part);
        }
    }

    // Each of the three from the strongest source that has it.
    private static void Resolve(Marked entry)
    {
        OutlineLook top = default;
        for (int source = SourceCount - 1; source >= 0; source--)
        {
            var look = entry.Looks[source];
            if (top.Seen.a <= 0f && look.Seen.a > 0f) top.Seen = look.Seen;
            if (top.Hidden.a <= 0f && look.Hidden.a > 0f) top.Hidden = look.Hidden;
            if (top.Fill.a <= 0f && look.Fill.a > 0f) top.Fill = look.Fill;
        }
        if (entry.Material == null) entry.Material = Take();
        if (entry.Material == null) return;
        entry.Material.SetColor(SeenId, top.Seen);
        entry.Material.SetColor(HiddenId, top.Hidden);
        entry.Material.SetColor(FillId, top.Fill);
    }

    // The body as it stands now: the worn character once it has one (never the enemy's capsule shell), else the foe's own;
    // of each renderer its solid submeshes (a material past the mesh's last submesh, as the hit flash lays one, is not one).
    private static void Parts(Marked entry)
    {
        entry.Parts.Clear();
        var root = entry.Foe.TryGetComponent(out ZombieBody body) && body.Model != null ? body.Model : entry.Foe;
        root.GetComponentsInChildren(found);
        foreach (var part in found)
        {
            if (!(part is SkinnedMeshRenderer) && !(part is MeshRenderer)) continue;
            if (!part.enabled || !part.gameObject.activeInHierarchy || part.forceRenderingOff) continue;
            if (part.shadowCastingMode == ShadowCastingMode.ShadowsOnly || ZombieBody.IsEye(part)) continue;
            int submeshes = Submeshes(part);
            part.GetSharedMaterials(looks);
            for (int i = 0; i < submeshes && i < looks.Count; i++)
                if (looks[i] != null && looks[i].renderQueue <= SolidQueue) entry.Parts.Add(new Part { Renderer = part, Submesh = i });
        }
        found.Clear();
        looks.Clear();
    }

    private static int Submeshes(Renderer part)
    {
        if (part is SkinnedMeshRenderer skinned) return skinned.sharedMesh != null ? skinned.sharedMesh.subMeshCount : 0;
        return part.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : 0;
    }

    private static bool Empty(Marked entry)
    {
        foreach (var look in entry.Looks) if (look.Shows) return false;
        return true;
    }

    private static void Remove(GameObject key, Marked entry)
    {
        marked.Remove(key);
        if (entry.Material != null) spare.Push(entry.Material);
        entry.Material = null;
        dirty = true;
        // Its parts go back to the map's occlusion culling now: with nothing marked no frame gathers again to do it.
        foreach (var part in entry.Parts)
        {
            var renderer = part.Renderer;
            if (ReferenceEquals(renderer, null) || !unculled.TryGetValue(renderer, out bool before)) continue;
            if (renderer != null) renderer.allowOcclusionWhenDynamic = before;
            unculled.Remove(renderer);
        }
        entry.Parts.Clear();
    }

    // Each body has its own mask material (its colours); kept for the next once it is let go.
    private static Material Take()
    {
        if (spare.Count > 0) return spare.Pop();
        if (!looked)
        {
            looked = true;
            shader = Resources.Load<Shader>(ShaderPath);
        }
        return shader != null ? new Material(shader) { name = "Outline Mask (Runtime)", hideFlags = HideFlags.HideAndDontSave } : null;
    }
}
