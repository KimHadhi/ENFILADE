using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Map dressing, stage 4 (the user, 2026-11-12: "슬슬 그레이박스 말고 좀 더 퀄리티 있게 작업 가능한가"): real surfaces on the
/// builder's boxes. Each material slot the maps draw with (Concrete, Road, Metal, Wood, and the tower's Wall, Floor,
/// Trim, Steel) gets a material of the shader ENFILADE/Surface (Art/Shaders/Surface), written under
/// Art/Materials/Surfaces from its row of Tables/MapSurfaces: "&lt;scene&gt;.&lt;slot&gt;" if the scene has its own,
/// else "&lt;slot&gt;". The shader lays the textures on in metres from the world, so no box needs UVs; faces looking up
/// take the row's top set, the rest its side set. The textures are ambientCG sets the user put in
/// Art/Textures/Surfaces (colour, OpenGL normal, roughness, optional ambient occlusion), set to the import the shader
/// expects as they are used. A slot without a row, with textures missing, or while the shader doesn't compile keeps
/// the grey material it had, so a map always builds. Parts that move get a variant mapped in their own space (MovingSurface).
/// </summary>
public sealed partial class GreyboxBuilder
{
    private const string SurfaceTable = "MapSurfaces", SurfaceShaderName = "ENFILADE/Surface";
    private const string SurfaceTextures = "Assets/_Project/Art/Textures/Surfaces/";
    private const string SurfaceMaterialParent = "Assets/_Project/Art/Materials", SurfaceMaterialFolder = SurfaceMaterialParent + "/Surfaces";
    // The moving variants made in this build, so each is written once.
    private static readonly Dictionary<Material, Material> movingSurfaces = new Dictionary<Material, Material>();
    // What this build dressed, for its closing window.
    private readonly List<string> surfaceNotes = new List<string>();

    /// <summary>
    /// The scene's surface for a slot (see the class): the material written from its row of Tables/MapSurfaces, or
    /// <paramref name="fallback"/> when there is no row, its textures are missing or the surface shader has errors.
    /// </summary>
    public Material Surface(string slot, Material fallback)
    {
        string id = sceneName + "." + slot;
        if (!GameTables.TryGetRow(SurfaceTable, id, out var row))
        {
            id = slot;
            if (!GameTables.TryGetRow(SurfaceTable, id, out row)) return fallback;
        }
        var material = SurfaceMaterial(id, row);
        if (material == null) return fallback;
        // Its row also says which decals its boxes wear (Decals).
        surfaceRows[material] = id;
        string side = Text(row, "side"), top = Text(row, "top");
        surfaceNotes.Add(slot + " " + side + (top.Length > 0 && top != side ? "/" + top : ""));
        return material;
    }

    /// <summary>
    /// The same surface mapped in the object's own space, for a part that moves (a door, a gate, a scanner, the pushed
    /// airliner): a world-mapped texture would slide over it as it goes. Any other material comes back as it is.
    /// </summary>
    public static Material MovingSurface(Material surface)
    {
        if (surface == null || surface.shader == null || surface.shader.name != SurfaceShaderName) return surface;
        if (surface.HasProperty("_ObjectSpace") && surface.GetFloat("_ObjectSpace") > .5f) return surface;
        if (movingSurfaces.TryGetValue(surface, out var known) && known != null) return known;
        string path = AssetDatabase.GetAssetPath(surface);
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".mat", System.StringComparison.Ordinal)) return surface;
        string movingPath = path.Substring(0, path.Length - ".mat".Length) + ".Moving.mat";
        var moving = AssetDatabase.LoadAssetAtPath<Material>(movingPath);
        if (moving == null)
        {
            moving = new Material(surface) { name = surface.name + ".Moving" };
            AssetDatabase.CreateAsset(moving, movingPath);
        }
        else moving.CopyPropertiesFromMaterial(surface);
        moving.SetFloat("_ObjectSpace", 1f);
        EditorUtility.SetDirty(moving);
        movingSurfaces[surface] = moving;
        return moving;
    }

    // A moving part's materials, each its moving variant.
    private static Material[] MovingMaterials(Material[] materials)
    {
        for (int i = 0; i < materials.Length; i++) materials[i] = MovingSurface(materials[i]);
        return materials;
    }

    // A new build: the variants are written again from this build's surfaces.
    private static void BeginSurfaces() => movingSurfaces.Clear();

    // The closing window's line about the surfaces.
    private string SurfaceReport()
        => "\n벽·바닥 재질(Tables/MapSurfaces): " + (surfaceNotes.Count > 0 ? string.Join(", ", surfaceNotes)
            : "이 맵에 맞는 줄이 없거나 셰이더·텍스처가 없어 예전 회색이에요(콘솔을 보세요).");

    private static Material SurfaceMaterial(string id, IReadOnlyDictionary<string, string> row)
    {
        var shader = SurfaceShader();
        if (shader == null) return null;
        string sideId = Text(row, "side"), topId = Text(row, "top");
        var side = FindSurfaceSet(sideId);
        if (!side.Complete)
        {
            Debug.LogWarning("[Greybox] " + SurfaceTable + " " + id + ": the textures '" + sideId + "' are missing or incomplete in "
                             + SurfaceTextures + " (a _Color, a _NormalGL and a _Roughness map); this slot keeps its grey look.");
            return null;
        }
        var top = side;
        if (topId.Length > 0 && topId != sideId)
        {
            top = FindSurfaceSet(topId);
            if (!top.Complete)
            {
                Debug.LogWarning("[Greybox] " + SurfaceTable + " " + id + ": the top textures '" + topId + "' are missing or incomplete; the side set covers the tops too.");
                top = side;
            }
        }
        if (!AssetDatabase.IsValidFolder(SurfaceMaterialFolder)) AssetDatabase.CreateFolder(SurfaceMaterialParent, "Surfaces");
        string path = SurfaceMaterialFolder + "/" + id + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = id };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader) material.shader = shader;
        float tile = Mathf.Max(.05f, Number(row, "tile", 3f));
        material.SetTexture("_BaseMap", side.Colour);
        material.SetTexture("_BumpMap", side.Normal);
        material.SetTexture("_RoughnessMap", side.Roughness);
        material.SetTexture("_OcclusionMap", side.Occlusion);
        material.SetColor("_BaseColor", Colour(row, "tint", Color.white));
        material.SetFloat("_Tile", tile);
        material.SetTexture("_TopMap", top.Colour);
        material.SetTexture("_TopBumpMap", top.Normal);
        material.SetTexture("_TopRoughnessMap", top.Roughness);
        material.SetTexture("_TopOcclusionMap", top.Occlusion);
        material.SetColor("_TopColor", Colour(row, "topTint", Color.white));
        material.SetFloat("_TopTile", Mathf.Max(.05f, Number(row, "topTile", tile)));
        material.SetFloat("_RoughnessScale", Mathf.Max(0f, Number(row, "roughness", 1f)));
        material.SetFloat("_Metallic", Mathf.Clamp01(Number(row, "metallic", 0f)));
        material.SetFloat("_BumpScale", Mathf.Max(0f, Number(row, "normal", 1f)));
        material.SetFloat("_OcclusionStrength", Mathf.Clamp01(Number(row, "ao", 1f)));
        material.SetFloat("_Variation", Mathf.Clamp(Number(row, "variation", .12f), 0f, .5f));
        material.SetFloat("_VariationSize", Mathf.Max(.5f, Number(row, "variationSize", 9f)));
        material.SetFloat("_ObjectSpace", 0f);
        SetWindows(material, Text(row, "windows"));
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---- Looks by name and painted windows (map dressing stage 5: the user, 2026-11-12, "1장은 어딜 가든 벽 텍스쳐가
    // 똑같아서 아쉽더라. 그리고 맵에 밀도가 없어") ----

    private const string WindowTable = "MapWindows";
    // This scene's name prefixes and the looks they give (Tables/MapSurfaces "boxes"), longest first; read once.
    private List<(string prefix, string look)> nameLooks;
    // The looks made this build, and the layout's solid boxes each covers (FitWindows).
    private readonly Dictionary<string, Material> looks = new Dictionary<string, Material>();
    private readonly Dictionary<string, List<Block>> lookBlocks = new Dictionary<string, List<Block>>();

    /// <summary>
    /// The scene's look named <paramref name="look"/>: the row "&lt;scene&gt;.&lt;look&gt;" or "&lt;look&gt;" of
    /// Tables/MapSurfaces, written once per build; null without a row or its textures.
    /// </summary>
    public Material Look(string look)
    {
        if (looks.TryGetValue(look, out var known)) return known;
        return looks[look] = Surface(look, null);
    }

    // The look a box of the layout takes by its name: a building's walls, parapets and rooms all start with its name, so
    // each building wears its own wall without the layout naming a material. Null when no row of this scene claims it.
    private string LookNameFor(string name)
    {
        if (nameLooks == null)
        {
            nameLooks = new List<(string, string)>();
            string scenePrefix = sceneName + ".";
            foreach (string id in GameTables.RowIds(SurfaceTable))
            {
                if (!id.StartsWith(scenePrefix, System.StringComparison.Ordinal) || !GameTables.TryGetRow(SurfaceTable, id, out var row)) continue;
                foreach (string part in Text(row, "boxes").Split(';'))
                {
                    string start = part.Trim();
                    if (start.Length > 0) nameLooks.Add((start, id.Substring(scenePrefix.Length)));
                }
            }
            nameLooks.Sort((a, c) => c.prefix.Length.CompareTo(a.prefix.Length));
        }
        foreach (var (start, look) in nameLooks)
            if (name.StartsWith(start, System.StringComparison.Ordinal)) return look;
        return null;
    }

    // A box of the layout that took a look: kept so the look's windows fit the building.
    private void CoveredBy(string look, Block block)
    {
        if (!lookBlocks.TryGetValue(look, out var list)) lookBlocks[look] = list = new List<Block>();
        list.Add(block);
    }

    // A look's painted windows from its row's style in Tables/MapWindows, laid on a grid from the world's origin: right
    // as it is for the city beyond the edge, whose blocks are built on that grid (Skyline), and fitted to its building
    // by FitWindows for a look that covers boxes of the layout.
    private static void SetWindows(Material material, string style)
    {
        if (style.Length == 0 || !GameTables.TryGetRow(WindowTable, style, out var row))
        {
            if (style.Length > 0) Debug.LogWarning("[Greybox] " + WindowTable + ": no window style '" + style + "' (" + material.name + " has no windows).");
            material.SetFloat("_Windows", 0f);
            return;
        }
        float spacing = Mathf.Max(.5f, Number(row, "spacing", 3f)), floor = Mathf.Max(1f, Number(row, "floor", 3f));
        float sill = Number(row, "sill", .9f);
        material.SetFloat("_Windows", 1f);
        material.SetVector("_WindowSize", new Vector4(Number(row, "width", 1.2f), Number(row, "height", 1.5f), Number(row, "frame", .08f), 0f));
        material.SetVector("_WindowGrid", new Vector4(spacing, 0f, spacing, 0f));
        material.SetVector("_WindowRows", new Vector4(floor, sill, Number(row, "ground", sill), 9999f));
        material.SetColor("_WindowColor", Colour(row, "glass", new Color(.16f, .19f, .22f)));
        material.SetFloat("_WindowGloss", Mathf.Clamp01(Number(row, "gloss", .9f)));
        material.SetFloat("_WindowVariety", Mathf.Clamp01(Number(row, "variety", .35f)));
    }

    // Each look with windows that covers boxes of the layout, its grid fitted to them once the layout stands: across,
    // to the extent of all its boxes (a building's walls and parapets), a whole number of panes per side starting at its
    // corner; up, floors counted down from the roof of its biggest box (the building, not its parapet or a roof room),
    // so the top floor's panes end under the roof and nothing over it gets any.
    private void FitWindows()
    {
        foreach (var pair in lookBlocks)
        {
            if (!looks.TryGetValue(pair.Key, out var material) || material == null || pair.Value.Count == 0) continue;
            if (!material.HasProperty("_Windows") || material.GetFloat("_Windows") < .5f) continue;
            Bounds all = pair.Value[0].Box;
            Block main = pair.Value[0];
            foreach (var block in pair.Value)
            {
                all.Encapsulate(block.Box);
                if (block.Size.x * block.Size.y * block.Size.z > main.Size.x * main.Size.y * main.Size.z) main = block;
            }
            Vector4 grid = material.GetVector("_WindowGrid"), rows = material.GetVector("_WindowRows");
            float spacing = Mathf.Max(.5f, grid.x), floor = Mathf.Max(1f, rows.x), sill = rows.y, ground = rows.z;
            float acrossX = Mathf.Max(1f, Mathf.Round(all.size.x / spacing)), acrossZ = Mathf.Max(1f, Mathf.Round(all.size.z / spacing));
            material.SetVector("_WindowGrid", new Vector4(all.size.x / acrossX, -all.min.x, all.size.z / acrossZ, -all.min.z));
            float roof = main.Box.max.y;
            material.SetVector("_WindowRows", new Vector4(floor, roof - floor + sill, main.Box.min.y + ground, roof));
            EditorUtility.SetDirty(material);
        }
    }

    // The surface shader, or null (and a warning) while it is missing or doesn't compile: then the maps keep their
    // grey materials instead of drawing pink.
    private static Shader SurfaceShader()
    {
        var shader = Shader.Find(SurfaceShaderName);
        if (shader == null)
        {
            Debug.LogWarning("[Greybox] shader " + SurfaceShaderName + " not found (Art/Shaders/Surface); the maps keep their grey look.");
            return null;
        }
        if (ShaderUtil.ShaderHasError(shader))
        {
            Debug.LogWarning("[Greybox] shader " + SurfaceShaderName + " has compile errors (see the Console); the maps keep their grey look until it compiles.");
            return null;
        }
        return shader;
    }

    // One ambientCG set: its maps as the shader reads them.
    private struct SurfaceSet
    {
        public Texture2D Colour, Normal, Roughness, Occlusion;
        public bool Complete => Colour != null && Normal != null && Roughness != null;
    }

    // The set in Art/Textures/Surfaces/<id>: ambientCG names its maps <id>_<size>-<format>_<map> (Color, NormalGL,
    // Roughness, AmbientOcclusion). NormalDX would light upside down here, so only NormalGL counts.
    private static SurfaceSet FindSurfaceSet(string id)
    {
        var set = new SurfaceSet();
        if (string.IsNullOrEmpty(id)) return set;
        string folder = SurfaceTextures + id;
        if (!AssetDatabase.IsValidFolder(folder)) return set;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith("_Color", System.StringComparison.Ordinal)) set.Colour = PrepareSurfaceTexture(path, false, false);
            else if (name.EndsWith("_NormalGL", System.StringComparison.Ordinal)) set.Normal = PrepareSurfaceTexture(path, true, true);
            else if (name.EndsWith("_Roughness", System.StringComparison.Ordinal)) set.Roughness = PrepareSurfaceTexture(path, false, true);
            else if (name.EndsWith("_AmbientOcclusion", System.StringComparison.Ordinal)) set.Occlusion = PrepareSurfaceTexture(path, false, true);
        }
        return set;
    }

    // The import the shader expects: a normal map imported as one; roughness and occlusion as data (linear), colour as
    // colour (sRGB); repeating, mipmapped, trilinear and anisotropic, so floors seen at a slant stay sharp. Reimports
    // only a texture that isn't so already (a set added later; the 28 sets of 11-12 came in with their .meta files set).
    private static Texture2D PrepareSurfaceTexture(string path, bool normal, bool linear)
    {
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool srgb = !linear;
            bool ready = importer.textureType == type && (normal || importer.sRGBTexture == srgb) && importer.mipmapEnabled
                         && importer.wrapMode == TextureWrapMode.Repeat && importer.filterMode == FilterMode.Trilinear
                         && importer.anisoLevel >= 8;
            if (!ready)
            {
                importer.textureType = type;
                if (!normal) importer.sRGBTexture = srgb;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
