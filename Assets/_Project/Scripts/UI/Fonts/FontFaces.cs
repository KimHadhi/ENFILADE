using System;
using TMPro;
using UnityEngine;

/// <summary>
/// The font faces the game draws with, by key, and the material presets made for them (Data/Resources/UI/FontFaces.asset).
/// Fonts.csv names a face and a preset for each text role (FontRoles); this asset is the one place where those names
/// meet files, so no script or table row holds a path. Everything listed here ships in a build, so a face goes in only
/// when a role uses it or may use it with one cell's change (all nine Pretendard weights already ship: the
/// PRETENDARD Weights router points at them).
/// </summary>
[CreateAssetMenu(menuName = "ENFILADE/Font Faces", fileName = "FontFaces")]
public sealed class FontFaces : ScriptableObject
{
    public const string ResourcePath = "UI/FontFaces";

    [Serializable]
    public sealed class Face
    {
        [Tooltip("The name Fonts.csv uses for this face, such as pretendard-8 or scoredream-6.")]
        public string key;
        public TMP_FontAsset font;
    }

    [Serializable]
    public sealed class Preset
    {
        [Tooltip("The name Fonts.csv uses for this material preset.")]
        public string key;
        [Tooltip("A TMP material made for one of the faces above: its atlas texture must be that face's.")]
        public Material material;
    }

    public Face[] faces = Array.Empty<Face>();
    public Preset[] presets = Array.Empty<Preset>();

    [Tooltip("TextMeshPro/Mobile/Distance Field. TMP gives a font made at runtime (FontFallbacks' operating-system fonts) " +
        "this shader by name, and a build only carries shaders something refers to.")]
    public Shader runtimeFontShader;

    private static FontFaces instance;

    public static FontFaces Instance => instance != null ? instance : instance = Resources.Load<FontFaces>(ResourcePath);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>Where a face's own TTF can ship instead of a TMP asset made in the editor: Resources/UI/Fonts/&lt;key&gt;.ttf.</summary>
    public const string ShippedFontsFolder = "UI/Fonts";

    // Faces made at run time from a shipped TTF, by key.
    private static readonly System.Collections.Generic.Dictionary<string, TMP_FontAsset> madeFaces =
        new System.Collections.Generic.Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The face with this key, or null. A key this asset doesn't list may be a TTF shipped in
    /// Resources/UI/Fonts (the Hitmarker faces of the 2026-11-22 HUD, the user's pick of the Garry's Mod addons' fonts):
    /// it becomes a dynamic TMP face the first time it is asked for, its glyphs drawn as they are met, so a face can come
    /// into the game as its file alone without the editor's Font Asset Creator. Hangul and anything else it lacks fall
    /// through to TMP's global fallbacks like every face's.</summary>
    public TMP_FontAsset Font(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (faces != null)
            foreach (var face in faces)
                if (face != null && string.Equals(face.key, key, StringComparison.OrdinalIgnoreCase)) return face.font;
        return Shipped(key);
    }

    private static TMP_FontAsset Shipped(string key)
    {
        if (madeFaces.TryGetValue(key, out var made) && made != null) return made;
        var file = Resources.Load<Font>(ShippedFontsFolder + "/" + key);
        if (file == null) return null;
        made = TMP_FontAsset.CreateFontAsset(file, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic, true);
        if (made == null) return null;
        made.name = key + " (shipped)";
        made.hideFlags = HideFlags.DontSave;
        return madeFaces[key] = made;
    }

    /// <summary>The preset material with this key, or null.</summary>
    public Material PresetMaterial(string key)
    {
        if (string.IsNullOrEmpty(key) || presets == null) return null;
        foreach (var preset in presets)
            if (preset != null && string.Equals(preset.key, key, StringComparison.OrdinalIgnoreCase)) return preset.material;
        return null;
    }
}
