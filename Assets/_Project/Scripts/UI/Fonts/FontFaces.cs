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

    /// <summary>The face with this key, or null.</summary>
    public TMP_FontAsset Font(string key)
    {
        if (string.IsNullOrEmpty(key) || faces == null) return null;
        foreach (var face in faces)
            if (face != null && string.Equals(face.key, key, StringComparison.OrdinalIgnoreCase)) return face.font;
        return null;
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
