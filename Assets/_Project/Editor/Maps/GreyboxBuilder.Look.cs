using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Map dressing, stage 1 (the user's World War Z look, 2026-11-12): each chapter's sky, sun, fog, ambient light and
/// reflections, from its row of Tables/MapLook (id = the scene's name). The sky is a Poly Haven HDRI under Art/Sky,
/// shown by a Skybox/Panoramic material the build writes beside the scene. The sun's direction, colour and strength
/// were measured from each HDRI (where its sun is and how much it stands out of the sky), and the fog colour is the
/// sky's own horizon at the row's exposure, so the distant haze melts into the sky the way the reference's far city
/// does. After the scene is saved, the environment (ambient light and the default reflection) is baked from that sky:
/// the only lighting bake, since nothing is lightmapped, so it takes seconds. A chapter without a row keeps the plain
/// default sky.
/// </summary>
public sealed partial class GreyboxBuilder
{
    private const string LookTable = "MapLook", SkyFolder = "Assets/_Project/Art/Sky/";
    // The scene's directional light, made the sun in Start.
    private Light sun;

    // Before the first save: the row's sky, sun, fog and light settings into the scene. Returns what was applied (for
    // the build's report), or null without a row.
    private string ApplyLook()
    {
        if (!GameTables.TryGetRow(LookTable, sceneName, out var row)) return null;
        string skyFile = Text(row, "sky");
        string skyPath = SkyFolder + skyFile;
        var sky = AssetDatabase.LoadAssetAtPath<Texture2D>(skyPath);
        if (sky == null) Debug.LogWarning("[Greybox] " + sceneName + ": sky not found at " + skyPath + " (Tables/MapLook).");
        else
        {
            PrepareSkyTexture(skyPath);
            RenderSettings.skybox = SkyMaterial(AssetDatabase.LoadAssetAtPath<Texture2D>(skyPath), row.Number("skyExposure", 1f),
                row.Number("skyRotation", 0f));
        }
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(row.Number("sunPitch", 50f), row.Number("sunYaw", 0f), 0f);
            sun.color = Colour(row, "sunColor", Color.white);
            sun.intensity = row.Number("sunIntensity", 1f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = Mathf.Clamp01(row.Number("shadowStrength", 1f));
            // Nothing is lightmapped. With probes (GreyboxBuilder.Probes) the sun is Mixed with Baked Indirect: drawn
            // and shadowed live as before, its bounce baked; without them it is plain realtime.
            sun.lightmapBakeType = BakesProbes ? LightmapBakeType.Mixed : LightmapBakeType.Realtime;
            RenderSettings.sun = sun;
        }
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = row.Number("ambientIntensity", 1f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 128;
        RenderSettings.reflectionIntensity = row.Number("reflectionIntensity", 1f);
        float density = row.Number("fogDensity", 0f);
        RenderSettings.fog = density > 0f;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = density;
        RenderSettings.fogColor = Colour(row, "fogColor", RenderSettings.fogColor);
        // No lightmaps and no realtime GI: the bake after saving is the sky's light, and with probes (the row's
        // probeSpacing, GreyboxBuilder.Probes) the probe volume's baked indirect light.
        string lightingPath = Folder + "/" + sceneName + "/Lighting.lighting";
        if (AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingPath) != null) AssetDatabase.DeleteAsset(lightingPath);
        var lighting = new LightingSettings { name = sceneName + " Lighting", bakedGI = false, realtimeGI = false };
        ProbeLighting(lighting);
        AssetDatabase.CreateAsset(lighting, lightingPath);
        Lightmapping.lightingSettings = lighting;
        return skyFile + ", 해 " + row.Number("sunPitch", 50f) + "°, 안개 " + density;
    }

    // An equirectangular sky wants its full 4K, no mip maps (they draw a thin seam where the picture wraps around),
    // and to wrap sideways but not over the poles.
    private static void PrepareSkyTexture(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
        if (importer.textureShape == TextureImporterShape.Texture2D && importer.maxTextureSize == 4096 && !importer.mipmapEnabled
            && importer.textureCompression == TextureImporterCompression.CompressedHQ
            && importer.wrapModeU == TextureWrapMode.Repeat && importer.wrapModeV == TextureWrapMode.Clamp)
            return;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.maxTextureSize = 4096;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }

    // The chapter's sky material, kept beside the scene and rewritten on every build. Latitude-longitude layout (no
    // six-frames keyword), 360 degrees.
    private Material SkyMaterial(Texture2D sky, float exposure, float rotation)
    {
        string path = Folder + "/" + sceneName + "/Sky.mat";
        var shader = Shader.Find("Skybox/Panoramic");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = sceneName + " Sky" };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (shader != null) material.shader = shader;
        material.SetTexture("_MainTex", sky);
        material.SetFloat("_Exposure", exposure);
        material.SetFloat("_Rotation", rotation);
        material.SetFloat("_Mapping", 1f);
        material.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
        material.SetFloat("_ImageType", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // After the scene is saved, since the bake keeps its data beside it: the ambient light and the default reflection
    // from the sky (Unity 6 keeps the old ones until the lighting is generated).
    private static bool BakeEnvironment()
    {
        bool done = Lightmapping.Bake();
        if (!done) Debug.LogWarning("[Greybox] the sky's ambient light and reflection did not bake; Generate Lighting in the Lighting window does it.");
        return done;
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out string text) ? text.Trim() : "";

    private static Color Colour(IReadOnlyDictionary<string, string> row, string key, Color fallback)
        => row.TryGetValue(key, out string text) && ColorUtility.TryParseHtmlString(text.Trim(), out Color colour) ? colour : fallback;
}
