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
        EditorUtility.SetDirty(material);
        return material;
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
