using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

/// <summary>
/// The skins a gun can wear, picked on the title screen's 무기 page (MainMenuWeapons; user, 2026-11-11: "무기 스킨 탭도
/// 만들 것"): a solid finish (a colour painted over the whole gun, its wear and shape kept by its normal, metal and
/// roughness maps), a camouflage pattern (drawn here, from the colours in the table), or another material the gun's pack
/// made for it (the Herrington's Chigurh finish). Glass, sights' reticles, rounds and brass are never painted. Kept per gun
/// in gun_setups.json (GunSetups), worn by this PC's own player's guns, and in a room by their copies on friends' PCs
/// (PlayerNetworkCombat sends LookText). The skins: Tables/GunSkins.csv; their names Texts.csv skin.&lt;id&gt;.
/// </summary>
public static class GunSkins
{
    public const string TableName = "GunSkins", Default = "default";

    public enum Kind { Default, Finish, Camo, Pack }
    public enum Pattern { Blobs, Digital, Tiger }

    public sealed class Skin
    {
        public string Id;
        public Kind Kind;
        /// <summary>Only this gun (its WeaponData name) may wear it; empty for any gun.</summary>
        public string Gun;
        public Color[] Colors = Array.Empty<Color>();
        public Pattern Pattern;
        public float Scale = 3f;
        /// <summary>A camo's texture side (px); 0 for the usual one (GunSkins.csv size).</summary>
        public int Size;
        /// <summary>A pack skin's material (Resources path) and the material of the gun's it stands in for (by name).</summary>
        public string Material, Replaces;
        public string Name => Texts.Get("skin." + Id);
        /// <summary>The colour its chip shows.</summary>
        public Color Swatch => Colors.Length > 0 ? Colors[0] : Color.gray;
    }

    private static List<Skin> catalog;
    private static readonly Dictionary<string, Texture2D> camos = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<(Material, string), Material> painted = new Dictionary<(Material, string), Material>();
    private static readonly Dictionary<string, Material> packs = new Dictionary<string, Material>();
    private static Texture2D blank;
    // Each renderer's own materials, before any skin went on.
    private static readonly ConditionalWeakTable<Renderer, Material[]> originals = new ConditionalWeakTable<Renderer, Material[]>();

    private static readonly int TintColor = Shader.PropertyToID("_TintColor"), BaseColorMap = Shader.PropertyToID("_BC"),
        BaseColor = Shader.PropertyToID("_BaseColor"), BaseMap = Shader.PropertyToID("_BaseMap"), MainTex = Shader.PropertyToID("_MainTex"),
        LegacyColor = Shader.PropertyToID("_Color");

    /// <summary>Raised when this PC's player picks a skin (the menu's preview and the guns follow).</summary>
    public static event Action<WeaponData, string> Chosen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        catalog = null;
        camos.Clear();
        painted.Clear();
        packs.Clear();
        blank = null;
        Chosen = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Listen()
    {
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget()
    {
        catalog = null;
        camos.Clear();
        painted.Clear();
    }

    /// <summary>The skins <paramref name="gun"/> can wear, the default first, in the table's order.</summary>
    public static List<Skin> For(WeaponData gun)
    {
        var list = new List<Skin>();
        foreach (var skin in Catalog)
            if (string.IsNullOrEmpty(skin.Gun) || (gun != null && string.Equals(skin.Gun, gun.name, StringComparison.Ordinal))) list.Add(skin);
        return list;
    }

    public static Skin Find(string id)
    {
        foreach (var skin in Catalog) if (string.Equals(skin.Id, id, StringComparison.Ordinal)) return skin;
        return null;
    }

    /// <summary>The skin this PC's player picked for the gun; the default when none.</summary>
    public static string ChosenFor(WeaponData gun)
    {
        string id = gun != null ? GunSetups.SkinOf(gun.name) : null;
        return string.IsNullOrEmpty(id) || Find(id) == null ? Default : id;
    }

    /// <summary>This PC's player picks a skin for a gun: kept in gun_setups.json.</summary>
    public static void Choose(WeaponData gun, string id)
    {
        if (gun == null) return;
        GunSetups.RememberSkin(gun.name, id == Default ? "" : id);
        Chosen?.Invoke(gun, id);
    }

    /// <summary>Every gun's skin this PC's player picked, as "gun=skin;…" for friends' PCs (only those not default).</summary>
    public static string LookText()
    {
        var text = new StringBuilder();
        var guns = FPSBodyAssets.Instance != null ? FPSBodyAssets.Instance.guns : null;
        if (guns == null) return "";
        foreach (var entry in guns)
        {
            if (entry.weapon == null) continue;
            string id = ChosenFor(entry.weapon);
            if (id == Default) continue;
            if (text.Length > 0) text.Append(';');
            text.Append(entry.weapon.name).Append('=').Append(id);
        }
        return text.ToString();
    }

    /// <summary>The skin a look text (LookText) gives a gun; the default when it names none.</summary>
    public static string SkinIn(string look, WeaponData gun)
    {
        if (string.IsNullOrEmpty(look) || gun == null) return Default;
        foreach (string pair in look.Split(';'))
        {
            int at = pair.IndexOf('=');
            if (at > 0 && string.Equals(pair.Substring(0, at), gun.name, StringComparison.Ordinal)) return pair.Substring(at + 1);
        }
        return Default;
    }

    /// <summary>
    /// Puts skin <paramref name="id"/> on a gun's model (its renderers below <paramref name="model"/>), or takes any off for
    /// the default. Each renderer keeps its own materials to go back to.
    /// </summary>
    public static void Wear(GameObject model, WeaponData gun, string id)
    {
        if (model == null) return;
        var skin = Find(id);
        if (skin != null && !string.IsNullOrEmpty(skin.Gun) && (gun == null || skin.Gun != gun.name)) skin = null;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer
                || renderer.GetComponent<TMPro.TMP_Text>() != null) continue;
            if (!originals.TryGetValue(renderer, out var own))
            {
                own = renderer.sharedMaterials;
                originals.Add(renderer, own);
            }
            var worn = new Material[own.Length];
            for (int i = 0; i < own.Length; i++) worn[i] = skin == null || skin.Kind == Kind.Default ? own[i] : Painted(own[i], skin);
            renderer.sharedMaterials = worn;
        }
    }

    // The material as the skin paints it, made once per material and skin; the material itself when the skin leaves it be.
    private static Material Painted(Material source, Skin skin)
    {
        if (source == null || !Paintable(source)) return source;
        if (painted.TryGetValue((source, skin.Id), out var made) && made != null) return made;
        switch (skin.Kind)
        {
            case Kind.Pack:
                made = string.Equals(source.name, skin.Replaces, StringComparison.Ordinal) ? Pack(skin.Material) : null;
                if (made == null) return source;
                break;
            case Kind.Finish:
            case Kind.Camo:
                made = new Material(source) { name = source.name + " (" + skin.Id + ")", hideFlags = HideFlags.DontSave };
                var texture = skin.Kind == Kind.Camo ? Camo(skin) : Blank;
                Color paint = skin.Kind == Kind.Camo ? Color.white : skin.Swatch;
                if (made.HasProperty(BaseColorMap)) made.SetTexture(BaseColorMap, texture);
                if (made.HasProperty(BaseMap)) made.SetTexture(BaseMap, texture);
                if (made.HasProperty(MainTex)) made.SetTexture(MainTex, texture);
                if (made.HasProperty(TintColor)) made.SetColor(TintColor, paint);
                if (made.HasProperty(BaseColor)) made.SetColor(BaseColor, paint);
                if (made.HasProperty(LegacyColor)) made.SetColor(LegacyColor, paint);
                break;
            default:
                return source;
        }
        painted[(source, skin.Id)] = made;
        return made;
    }

    // Glass, reticles, lights, rounds and brass keep their own look; and only a material with a base colour to paint.
    private static bool Paintable(Material material)
    {
        if (material.renderQueue >= 2450) return false;
        string name = material.name;
        foreach (string keep in Kept)
            if (name.IndexOf(keep, StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return material.HasProperty(BaseColorMap) || material.HasProperty(BaseMap);
    }

    private static readonly string[] Kept = { "Glass", "Lens", "Holograph", "Reticle", "RedDot", "Dot", "Ammo", "Casing", "Brass", "Primer", "Shell", "Scope" };

    private static Material Pack(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (!packs.TryGetValue(path, out var material) || material == null) packs[path] = material = Resources.Load<Material>(path);
        return material;
    }

    private static Texture2D Blank
    {
        get
        {
            if (blank != null) return blank;
            blank = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Skin Blank", hideFlags = HideFlags.DontSave };
            var white = new Color32[16];
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
            blank.SetPixels32(white);
            blank.Apply(false, true);
            return blank;
        }
    }

    // ---- Camouflage, drawn once per skin from its colours ----

    // The texture's side (px) when the table gives none (GunSkins.csv size).
    private const int CamoSize = 256;
    // Sampled as well as the GPU can: every mip level blended, and kept sharp along surfaces seen edge-on.
    private const int CamoAniso = 16;

    private static Texture2D Camo(Skin skin)
    {
        if (camos.TryGetValue(skin.Id, out var made) && made != null) return made;
        var colors = skin.Colors.Length > 0 ? skin.Colors : new[] { Color.gray, Color.black };
        int size = Mathf.Clamp(skin.Size > 0 ? skin.Size : CamoSize, 16, 2048);
        var pixels = new Color32[size * size];
        float scale = Mathf.Max(.5f, skin.Scale);
        // A seed from the skin's id, so a pattern is the same in every game and on every PC.
        int hash = 17;
        foreach (char c in skin.Id) hash = unchecked(hash * 31 + c);
        float ox = (hash & 255) * 1.37f, oy = ((hash >> 8) & 255) * 2.11f;
        // The digital pattern's squares, each the blobs' band at its corner: worked out once per square, not per pixel, as
        // its texture is drawn large (size) to keep the squares' edges sharp under filtering.
        float cells = 8f * scale;
        int across = Mathf.CeilToInt(cells) + 1;
        int[] squares = null;
        if (skin.Pattern == Pattern.Digital)
        {
            squares = new int[across * across];
            for (int j = 0; j < across; j++)
                for (int i = 0; i < across; i++)
                    squares[j * across + i] = Band(Noise(i / cells, j / cells, scale, ox, oy), colors.Length);
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                int band;
                switch (skin.Pattern)
                {
                    case Pattern.Digital:
                    {
                        // Square pixels of the blobs' bands.
                        int i = Mathf.Min(across - 1, Mathf.FloorToInt(u * cells)), j = Mathf.Min(across - 1, Mathf.FloorToInt(v * cells));
                        band = squares[j * across + i];
                        break;
                    }
                    case Pattern.Tiger:
                    {
                        // Long ragged stripes across the gun.
                        float wave = Mathf.Sin((u * 2f + Noise(u, v, scale * .5f, ox, oy) * .35f) * Mathf.PI * 2f * scale);
                        band = wave > .55f ? colors.Length - 1 : wave > .2f && colors.Length > 2 ? colors.Length - 2 : Band(Noise(u, v, scale * .4f, oy, ox), Mathf.Max(1, colors.Length - 2));
                        break;
                    }
                    default:
                        band = Band(Noise(u, v, scale, ox, oy), colors.Length);
                        break;
                }
                pixels[y * size + x] = colors[Mathf.Clamp(band, 0, colors.Length - 1)];
            }
        made = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Camo " + skin.Id, hideFlags = HideFlags.DontSave };
        made.wrapMode = TextureWrapMode.Repeat;
        // Never point-sampled (user, 2026-11-12: "M16X4 줌 했을 때 텍스쳐 지글 거리는 거"). The digital pattern was, to keep
        // its squares sharp, and a point sampler takes the one nearest texel of the one nearest mip level with no
        // anisotropic filtering. Where a gun's UVs crowd the pattern that is another texel for every pixel each time the
        // gun moves: the MX16A4 has no textures of its own, so its UVs were never laid out (two thirds of its vertices sit
        // at 0,0, and its skinny triangles smear the pattern into lines of up to 39 000 texels a metre at 256 px, up to 25
        // texels to a pixel when aimed at 1080p), and aimed down its top the whole gun crawled. Trilinear and anisotropic
        // sampling average those texels; the digital squares are drawn on a larger texture instead (GunSkins.csv size)
        // so their edges stay sharp up close.
        made.filterMode = FilterMode.Trilinear;
        made.anisoLevel = CamoAniso;
        made.SetPixels32(pixels);
        made.Apply(true, true);
        camos[skin.Id] = made;
        return made;
    }

    // Two octaves of Perlin noise, 0-1.
    private static float Noise(float u, float v, float scale, float ox, float oy)
        => Mathf.Clamp01(Mathf.PerlinNoise(ox + u * scale, oy + v * scale) * .7f + Mathf.PerlinNoise(oy + u * scale * 2.3f, ox + v * scale * 2.3f) * .3f);

    // Which colour a noise value falls in: equal shares of the noise's middle range.
    private static int Band(float noise, int count) => Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(.3f, .7f, noise) * count), 0, count - 1);

    // ---- The table ----

    private static List<Skin> Catalog
    {
        get
        {
            if (catalog != null) return catalog;
            catalog = new List<Skin> { new Skin { Id = Default, Kind = Kind.Default } };
            foreach (string id in GameTables.RowIds(TableName))
            {
                if (!GameTables.TryGetRow(TableName, id, out var row) || string.Equals(id, Default, StringComparison.Ordinal)) continue;
                var skin = new Skin { Id = id };
                if (row.TryGetValue("kind", out string kind)) Enum.TryParse(kind, true, out skin.Kind);
                if (row.TryGetValue("gun", out string gun)) skin.Gun = gun.Trim();
                if (row.TryGetValue("pattern", out string pattern)) Enum.TryParse(pattern, true, out skin.Pattern);
                if (row.TryGetValue("scale", out string scale) && float.TryParse(scale, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float s)) skin.Scale = s;
                if (row.TryGetValue("size", out string size) && int.TryParse(size, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int side)) skin.Size = side;
                if (row.TryGetValue("material", out string material)) skin.Material = material.Trim();
                if (row.TryGetValue("replaces", out string replaces)) skin.Replaces = replaces.Trim();
                if (row.TryGetValue("colors", out string colors))
                {
                    var list = new List<Color>();
                    foreach (string part in colors.Split(';'))
                        if (ColorUtility.TryParseHtmlString(part.Trim(), out var color)) list.Add(color);
                    skin.Colors = list.ToArray();
                }
                if (skin.Kind != Kind.Default) catalog.Add(skin);
            }
            return catalog;
        }
    }
}
