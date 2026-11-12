using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A body baked as it stands this moment and drawn as itself, see-through, with the dodge's afterimage shader (Resources/
/// Combat/Shaders/DodgeGhost). It covers every skinned mesh of the body that shows, up to a vertex budget, each sub-mesh
/// with its own texture, tiling and colour, so any outfit or modded character needs nothing of its own.
/// Two effects use it: the Knight's shadow double (BodyDouble) and the cover step's afterimages (CoverStreak). The dodge
/// keeps its own copy of these steps (DodgeDash); this is the same bake, made reusable for them.
/// </summary>
public sealed class BodyBake
{
    private const string GhostShader = "Combat/Shaders/DodgeGhost";
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex"), MainTexStId = Shader.PropertyToID("_MainTex_ST"),
        ColorId = Shader.PropertyToID("_Color"), CutId = Shader.PropertyToID("_Cut"), AlphaId = Shader.PropertyToID("_Alpha"),
        WarmId = Shader.PropertyToID("_Warm");

    private struct Look { public Texture texture; public Vector4 st; public Color color; public float cut; }
    private struct Main { public int texture, color; public bool hasTexture, hasColor; }
    private static readonly Dictionary<Shader, Main> mains = new Dictionary<Shader, Main>();
    private static Material material;

    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Matrix4x4> places = new List<Matrix4x4>();
    private readonly List<int> firstLook = new List<int>();
    private readonly List<Look> looks = new List<Look>();
    private readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
    private readonly List<Material> materials = new List<Material>();
    private MaterialPropertyBlock block;
    private int used;

    /// <summary>Where the body's root stood when it was baked (to draw it somewhere else, offset from there).</summary>
    public Vector3 BakedAt { get; private set; }
    /// <summary>Whether anything was baked.</summary>
    public bool Ready => used > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { mains.Clear(); material = null; }

    /// <summary>
    /// Bakes every skinned mesh under <paramref name="root"/> that shows (and every one hidden for the first person's
    /// eyes, which cast only shadows, so a copy of one's own body has its head), up to <paramref name="vertexBudget"/>.
    /// </summary>
    public bool Bake(Transform root, int vertexBudget = 150000)
    {
        used = 0;
        firstLook.Clear(); looks.Clear();
        if (root == null || Material() == null) return false;
        BakedAt = root.position;
        skins.Clear();
        root.GetComponentsInChildren(false, skins);
        int budget = vertexBudget;
        foreach (var skin in skins)
        {
            var shared = skin.sharedMesh;
            if (!skin.enabled || shared == null || !skin.gameObject.activeInHierarchy || skin.forceRenderingOff) continue;
            int vertices = shared.vertexCount;
            if (vertices > budget) continue;
            budget -= vertices;
            if (used == meshes.Count)
            {
                var made = new Mesh { name = "Body bake", hideFlags = HideFlags.HideAndDontSave };
                made.MarkDynamic();
                meshes.Add(made); places.Add(Matrix4x4.identity);
            }
            var mesh = meshes[used];
            // Scaled as the renderer is, so it is drawn without its scale again.
            skin.BakeMesh(mesh, true);
            Transform place = skin.transform;
            places[used] = Matrix4x4.TRS(place.position, place.rotation, Vector3.one);
            firstLook.Add(looks.Count);
            materials.Clear();
            skin.GetSharedMaterials(materials);
            for (int part = 0; part < mesh.subMeshCount; part++)
                looks.Add(LookOf(materials.Count > 0 ? materials[Mathf.Min(part, materials.Count - 1)] : null));
            used++;
        }
        return used > 0;
    }

    /// <summary>
    /// Draws the bake this frame, moved by <paramref name="move"/> (a world matrix applied over where it was baked),
    /// <paramref name="alpha"/> see-through, its colours pulled toward <paramref name="tint"/> by its alpha (0 keeps them).
    /// </summary>
    public void Draw(Matrix4x4 move, float alpha, Color tint)
    {
        var shared = Material();
        if (used == 0 || shared == null || alpha <= .004f) return;
        block ??= new MaterialPropertyBlock();
        block.SetFloat(AlphaId, Mathf.Clamp01(alpha));
        block.SetColor(WarmId, tint);
        for (int i = 0; i < used; i++)
        {
            var mesh = meshes[i];
            int first = i < firstLook.Count ? firstLook[i] : 0;
            Matrix4x4 at = move * places[i];
            for (int part = 0; part < mesh.subMeshCount; part++)
            {
                int index = Mathf.Min(first + part, looks.Count - 1);
                var look = index >= 0 ? looks[index] : new Look { st = new Vector4(1f, 1f, 0f, 0f), color = Color.white };
                block.SetTexture(MainTexId, look.texture != null ? look.texture : Texture2D.whiteTexture);
                block.SetVector(MainTexStId, look.st);
                block.SetColor(ColorId, look.color);
                block.SetFloat(CutId, look.cut);
                Graphics.DrawMesh(mesh, at, shared, 0, null, part, block, ShadowCastingMode.Off, false);
            }
        }
    }

    /// <summary>The baked meshes go.</summary>
    public void Release()
    {
        foreach (var mesh in meshes) if (mesh != null) Object.Destroy(mesh);
        meshes.Clear(); places.Clear(); firstLook.Clear(); looks.Clear();
        used = 0;
    }

    private static Material Material()
    {
        if (material != null) return material;
        var shader = Resources.Load<Shader>(GhostShader);
        if (shader == null) return null;
        return material = new Material(shader) { name = "Body Bake (Runtime)", hideFlags = HideFlags.HideAndDontSave };
    }

    // What a material draws as its own surface (DodgeDash.LookOf): the shader's main texture and colour, the tiling, and
    // whether the texture's alpha cuts it (cut-out and see-through materials only).
    private static Look LookOf(Material source)
    {
        var look = new Look { st = new Vector4(1f, 1f, 0f, 0f), color = Color.white };
        var shader = source != null ? source.shader : null;
        if (shader == null) return look;
        var main = MainOf(shader);
        if (main.hasTexture)
        {
            look.texture = source.GetTexture(main.texture);
            Vector2 scale = source.GetTextureScale(main.texture), offset = source.GetTextureOffset(main.texture);
            look.st = new Vector4(scale.x, scale.y, offset.x, offset.y);
        }
        if (main.hasColor) look.color = source.GetColor(main.color);
        look.cut = source.renderQueue >= (int)RenderQueue.AlphaTest ? 1f : 0f;
        return look;
    }

    // A shader's main texture and colour, as DodgeDash finds them: [MainTexture]/[MainColor] first (lilToon keeps a hidden
    // _BaseMap/_BaseColor), then _MainTex/_Color, then URP's names.
    private static Main MainOf(Shader shader)
    {
        if (mains.TryGetValue(shader, out var main)) return main;
        int texture = -1, color = -1;
        for (int i = 0, count = shader.GetPropertyCount(); i < count; i++)
        {
            var flags = shader.GetPropertyFlags(i);
            var type = shader.GetPropertyType(i);
            if (texture < 0 && (flags & ShaderPropertyFlags.MainTexture) != 0 && type == ShaderPropertyType.Texture) texture = i;
            if (color < 0 && (flags & ShaderPropertyFlags.MainColor) != 0 && type == ShaderPropertyType.Color) color = i;
        }
        if (texture < 0) texture = shader.FindPropertyIndex("_MainTex");
        if (texture < 0) texture = shader.FindPropertyIndex("_BaseMap");
        if (color < 0) color = shader.FindPropertyIndex("_Color");
        if (color < 0) color = shader.FindPropertyIndex("_BaseColor");
        main = new Main
        {
            hasTexture = texture >= 0, texture = texture >= 0 ? shader.GetPropertyNameId(texture) : 0,
            hasColor = color >= 0, color = color >= 0 ? shader.GetPropertyNameId(color) : 0,
        };
        mains[shader] = main;
        return main;
    }
}

/// <summary>
/// A second skin over a body or a gun for a glow: each of its renderers gets a copy that shares its mesh (and its bones,
/// for a skinned one) and draws every sub-mesh with one overlay material, so the glow follows the body exactly and covers
/// a many-material outfit whole. An overlay material added to the renderer itself, as HitFlashRenderer does, draws only
/// the last sub-mesh. A copy shows only while its source shows to this eye: a head hidden from its own first-person
/// camera, which casts only shadows, stays dark. Blend shapes are copied each frame so faces match. CHARGE's gold
/// (ChargeGlow) and a Point's heated guns (GunHeat) use it.
/// </summary>
public sealed class BodyShell
{
    private struct Pair { public Renderer source, copy; public SkinnedMeshRenderer skinSource, skinCopy; public int shapes; }

    private readonly List<Pair> pairs = new List<Pair>();
    private readonly List<Material> fill = new List<Material>();
    private MaterialPropertyBlock block;

    public int Count => pairs.Count;

    /// <summary>A shell over every mesh and skinned renderer under <paramref name="root"/> (none inside a canvas).</summary>
    public static BodyShell Over(GameObject root, Material material)
    {
        var shell = new BodyShell();
        if (root == null || material == null) return shell;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) shell.Add(renderer, material);
        return shell;
    }

    /// <summary>A shell over these renderers only (a gun's).</summary>
    public static BodyShell Over(IEnumerable<Renderer> renderers, Material material)
    {
        var shell = new BodyShell();
        if (renderers == null || material == null) return shell;
        foreach (var renderer in renderers) shell.Add(renderer, material);
        return shell;
    }

    private void Add(Renderer source, Material material)
    {
        if (source == null || source.GetComponentInParent<Canvas>() != null) return;
        if (!(source is SkinnedMeshRenderer) && !(source is MeshRenderer)) return;
        // A shell over a shell, or over one of the game's own see-through effects, would only double them.
        if (source.gameObject.name.StartsWith("Glow Shell")) return;
        var host = new GameObject("Glow Shell") { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
        host.transform.SetParent(source.transform, false);
        var pair = new Pair { source = source };
        int parts;
        if (source is SkinnedMeshRenderer skin)
        {
            if (skin.sharedMesh == null) { Object.Destroy(host); return; }
            var copy = host.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = skin.sharedMesh;
            copy.rootBone = skin.rootBone;
            copy.bones = skin.bones;
            copy.localBounds = skin.localBounds;
            copy.updateWhenOffscreen = skin.updateWhenOffscreen;
            copy.quality = skin.quality;
            parts = skin.sharedMesh.subMeshCount;
            pair.skinSource = skin;
            pair.skinCopy = copy;
            pair.shapes = skin.sharedMesh.blendShapeCount;
            pair.copy = copy;
        }
        else
        {
            var filter = source.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) { Object.Destroy(host); return; }
            host.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var copy = host.AddComponent<MeshRenderer>();
            parts = filter.sharedMesh.subMeshCount;
            pair.copy = copy;
        }
        fill.Clear();
        for (int i = 0; i < Mathf.Max(1, parts); i++) fill.Add(material);
        pair.copy.sharedMaterials = fill.ToArray();
        pair.copy.shadowCastingMode = ShadowCastingMode.Off;
        pair.copy.receiveShadows = false;
        pair.copy.lightProbeUsage = LightProbeUsage.Off;
        pair.copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
        pairs.Add(pair);
    }

    /// <summary>
    /// Keeps the shells with their sources this frame: shown while the source shows to this eye (enabled, not hidden, not
    /// a first-person shadow), blend shapes copied, the glow's values set from <paramref name="values"/>.
    /// </summary>
    public void Sync(System.Action<MaterialPropertyBlock> values)
    {
        block ??= new MaterialPropertyBlock();
        block.Clear();
        values?.Invoke(block);
        for (int i = pairs.Count - 1; i >= 0; i--)
        {
            var pair = pairs[i];
            if (pair.source == null || pair.copy == null)
            {
                if (pair.copy != null) Object.Destroy(pair.copy.gameObject);
                pairs.RemoveAt(i);
                continue;
            }
            bool shows = pair.source.enabled && pair.source.gameObject.activeInHierarchy && !pair.source.forceRenderingOff
                && pair.source.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
            if (pair.copy.enabled != shows) pair.copy.enabled = shows;
            if (!shows) continue;
            if (pair.copy.gameObject.layer != pair.source.gameObject.layer) pair.copy.gameObject.layer = pair.source.gameObject.layer;
            if (pair.skinCopy != null && pair.skinSource != null)
            {
                for (int shape = 0; shape < pair.shapes; shape++)
                {
                    float weight = pair.skinSource.GetBlendShapeWeight(shape);
                    if (pair.skinCopy.GetBlendShapeWeight(shape) != weight) pair.skinCopy.SetBlendShapeWeight(shape, weight);
                }
            }
            pair.copy.SetPropertyBlock(block);
        }
    }

    /// <summary>The shells go.</summary>
    public void Release()
    {
        foreach (var pair in pairs) if (pair.copy != null) Object.Destroy(pair.copy.gameObject);
        pairs.Clear();
    }
}
