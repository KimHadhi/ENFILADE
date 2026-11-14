using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// dev-text: file (log lines for whoever makes an avatar mod; nothing here is shown to players)

/// <summary>
/// Gives a mod avatar its materials, as near to what its maker saw as this game can draw (the user, 2026-11-14: "자기가
/// 원하는 쉐이더 쓰고 싶다면 쓰게 해 주고. 최대한 원하는 느낌이 그대로 뜨게. 유저를 제약하지 마"). A shader is compiled
/// code: the game can run one only if it was built for this Unity and this render pipeline (URP), so a shader compiled
/// in a VRChat project (Unity 2022, the built-in pipeline) can't be carried over as it is. Each material of the avatar's
/// file (ModAvatarFile.MaterialData) is therefore made in the first of three ways that works:
/// 1. Kept: the bundle brought the material with a shader this game can draw (the maker exported from a Unity 6 URP
///    project: any shader of their own, a Shader Graph, a locked Poiyomi URP). Used as it is.
/// 2. By name: the game has a shader of the same name (lilToon, which nearly every BOOTH avatar wears, all of its
///    variants: ModShaderLibrary). A new material of the game's shader takes every number, colour and texture the
///    maker set. lilToon's keyword variant (lilToonMulti) goes to the plain variant its own settings name.
/// 3. Converted: any other shader. A lilToon material in the same drawing mode (opaque, cut-out, see-through, with an
///    outline where the maker had one) takes what carries over: properties of the same name, and those the table
///    Tables/ShaderMap names for that shader family (Poiyomi's matcap is lilToon's _MatCapTex …). Said once in the log,
///    with the shader's name, so the maker knows which materials are an approximation.
/// The stand-ins on the loaded prefab are swapped for these once, so every copy made of it wears them.
/// </summary>
internal static class ModMaterials
{
    public const string MapTable = "ShaderMap";
    private const string Toon = "toon";

    private static readonly ShaderTagId LightMode = new ShaderTagId("LightMode");
    // Properties that say how to draw rather than what: a converted material keeps the game shader's own.
    private static readonly string[] DrawState =
    {
        "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_BlendOp", "_BlendOpAlpha", "_ZWrite", "_ZTest", "_ColorMask", "_OffsetFactor",
        "_OffsetUnits", "_AlphaToMask", "_ZClip"
    };
    // Where a shader of no known family keeps its picture and its colour, most usual first.
    private static readonly string[] MainTextures = { "_MainTex", "_BaseMap", "_BaseColorMap", "_BaseColorTexture", "_BaseTexture", "_Albedo", "_AlbedoMap", "_DiffuseMap", "_Diffuse", "_ColorMap", "_MainTexture", "_Tex" };
    private static readonly string[] MainColors = { "_Color", "_BaseColor", "_MainColor", "_Tint", "_TintColor" };
    private static readonly string[] Cutoffs = { "_Cutoff", "_AlphaCutoff", "_Cutout", "_AlphaClipThreshold", "_Clip" };
    private static readonly string[] Culls = { "_Cull", "_CullMode", "_Culling", "_CullingMode" };

    private sealed class Rule
    {
        public string Id, Match, Use, Outline;
        public readonly List<(string from, string to, float scale)> Copy = new List<(string, string, float)>();
        public readonly List<(string name, float value)> Set = new List<(string, float)>();
    }

    private static readonly HashSet<int> dressed = new HashSet<int>();
    private static readonly HashSet<string> told = new HashSet<string>();
    private static readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>(StringComparer.Ordinal);
    private static List<Rule> rules;
    private static bool listening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        dressed.Clear();
        told.Clear();
        textures.Clear();
        rules = null;
        listening = false;
    }

    /// <summary>
    /// Swaps the stand-in materials on a prefab loaded from a mod's bundle for the real ones, once per prefab.
    /// <paramref name="whose"/> names the avatar in messages.
    /// </summary>
    internal static void Apply(GameObject prefab, ModAvatarFile file, AssetBundle bundle, string whose)
    {
        if (prefab == null || file == null || !dressed.Add(prefab.GetInstanceID())) return;
        var data = new Dictionary<string, ModAvatarFile.MaterialData>(StringComparer.Ordinal);
        foreach (var material in file.materials ?? Array.Empty<ModAvatarFile.MaterialData>())
            if (material != null && !string.IsNullOrEmpty(material.name)) data[material.name] = material;
        var made = new Dictionary<Material, Material>();
        int kept = 0, named = 0, converted = 0;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            var shared = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < shared.Length; i++)
            {
                var standIn = shared[i];
                if (standIn == null) continue;
                if (!made.TryGetValue(standIn, out var real))
                {
                    real = standIn;
                    try
                    {
                        if (data.TryGetValue(standIn.name, out var described)) real = Make(described, standIn, bundle, whose, ref kept, ref named, ref converted);
                        else if (!Draws(standIn.shader)) Tell($"{whose}: 재질 '{standIn.name}'는 아바타 파일에 설명이 없고 쉐이더({ShaderName(standIn)})도 이 게임에서 그릴 수 없습니다. 다시 내보내 주세요.");
                    }
                    catch (Exception error)
                    {
                        Tell($"{whose}: 재질 '{standIn.name}'를 만들지 못했습니다({error.GetType().Name}: {error.Message}).");
                    }
                    made[standIn] = real;
                }
                if (real == null || real == standIn) continue;
                shared[i] = real;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = shared;
        }
        Debug.Log($"[Mods] {whose}: 재질 {made.Count}개 — 번들의 쉐이더 그대로 {kept}, 게임의 같은 쉐이더로 {named}, 바꿔 그림 {converted}");
    }

    private static Material Make(ModAvatarFile.MaterialData data, Material standIn, AssetBundle bundle, string whose, ref int kept, ref int named,
        ref int converted)
    {
        if (data.keep)
        {
            if (Draws(standIn.shader))
            {
                kept++;
                return standIn;
            }
            Tell($"{whose}: '{data.shader}' 쉐이더는 번들에 담겨 왔지만 이 게임(Unity {Application.unityVersion}, URP)에서 그릴 수 없습니다. Unity 6 + URP 프로젝트에서 URP용 쉐이더로 내보내야 그대로 쓸 수 있습니다. 지금은 바꿔 그립니다.");
        }
        // lilToon's keyword variant: the plain variant its own settings name has every feature, with no keywords to miss.
        string wanted = !string.IsNullOrEmpty(data.original) ? data.original : data.shader;
        string plain = LilMulti(wanted, data);
        Shader shader = ModShaderLibrary.Find(plain ?? wanted);
        if (shader == null && !string.IsNullOrEmpty(data.original)) shader = ModShaderLibrary.Find(data.shader);
        if (shader != null && Draws(shader))
        {
            named++;
            var same = new Material(shader) { name = data.name };
            Fill(same, data, bundle, false);
            Finish(same, data, true);
            return same;
        }
        converted++;
        return Convert(data, bundle, whose);
    }

    // ---- Which shader ----

    /// <summary>
    /// Whether this game draws anything with the shader: it must work on this machine and have a pass URP draws (its own
    /// forward passes, or a plain pass with no lighting mode, as old unlit shaders have). A shader made for the built-in
    /// pipeline has only that pipeline's lit passes, which URP leaves undrawn.
    /// </summary>
    internal static bool Draws(Shader shader)
    {
        if (shader == null || !shader.isSupported) return false;
        bool builtIn = false, plain = false;
        for (int i = 0; i < shader.passCount; i++)
        {
            string mode = shader.FindPassTagValue(i, LightMode).name;
            if (mode == "UniversalForward" || mode == "UniversalForwardOnly" || mode == "LightweightForward") return true;
            if (mode == "ForwardBase" || mode == "ForwardAdd" || mode == "Deferred" || mode == "PrepassBase" || mode == "PrepassFinal"
                || mode == "Vertex" || mode == "VertexLM" || mode == "VertexLMRGBM") builtIn = true;
            else if (string.IsNullOrEmpty(mode) || mode == "SRPDefaultUnlit") plain = true;
        }
        return plain && !builtIn;
    }

    // lilToonMulti picks its features with shader keywords, and a build holds only the keyword sets its own materials
    // use; the same material on the plain lilToon variant its settings name looks the same and misses nothing.
    private static string LilMulti(string shader, ModAvatarFile.MaterialData data)
    {
        if (string.IsNullOrEmpty(shader) || shader.IndexOf("lilToonMulti", StringComparison.Ordinal) < 0) return null;
        int mode = Mathf.RoundToInt(Number(data, "_TransparentMode", 0f));
        bool outline = Number(data, "_UseOutline", 0f) > .5f || shader.EndsWith("Outline", StringComparison.Ordinal);
        if (shader.EndsWith("Fur", StringComparison.Ordinal)) mode = mode == 5 ? 5 : 4;
        else if (shader.EndsWith("Gem", StringComparison.Ordinal)) mode = 6;
        else if (shader.EndsWith("Refraction", StringComparison.Ordinal)) mode = 3;
        return mode switch
        {
            1 => outline ? "Hidden/lilToonCutoutOutline" : "Hidden/lilToonCutout",
            2 => outline ? "Hidden/lilToonTransparentOutline" : "Hidden/lilToonTransparent",
            3 => "Hidden/lilToonRefraction",
            4 => "Hidden/lilToonFur",
            5 => "Hidden/lilToonFurCutout",
            6 => "Hidden/lilToonGem",
            _ => outline ? "Hidden/lilToonOutline" : "lilToon",
        };
    }

    // ---- Converting ----

    private static Material Convert(ModAvatarFile.MaterialData data, AssetBundle bundle, string whose)
    {
        string name = !string.IsNullOrEmpty(data.original) ? data.original : data.shader;
        var rule = RuleFor(name);
        // 0 opaque, 1 cut out, 2 see-through.
        int mode = ModeOf(data);
        bool outline = rule != null && !string.IsNullOrEmpty(rule.Outline) && Number(data, rule.Outline, 0f) > 0f;
        Shader target = rule != null && rule.Use.Length > 0 && !string.Equals(rule.Use, Toon, StringComparison.OrdinalIgnoreCase)
            ? ModShaderLibrary.Find(rule.Use) : null;
        if (target == null || !Draws(target)) target = ToonShader(mode, outline).OrNull() ?? ToonShader(mode, false).OrNull() ?? ToonShader(0, false);
        if (target == null)
        {
            Tell($"{whose}: 바꿔 그릴 쉐이더(lilToon)를 찾지 못했습니다. 재질 '{data.name}'는 번들의 것 그대로 둡니다.");
            return null;
        }
        var made = new Material(target) { name = data.name };
        Fill(made, data, bundle, true);
        if (rule != null)
            foreach (var (from, to, scale) in rule.Copy) Carry(made, data, bundle, from, to, scale);
        // The picture and the colour, wherever this shader keeps them.
        if (made.HasProperty("_MainTex") && made.GetTexture("_MainTex") == null)
        {
            if (!string.IsNullOrEmpty(data.mainTexture)) Carry(made, data, bundle, data.mainTexture, "_MainTex", 1f);
            for (int i = 0; i < MainTextures.Length && made.GetTexture("_MainTex") == null; i++) Carry(made, data, bundle, MainTextures[i], "_MainTex", 1f);
        }
        if (!Has(data.vectors, "_Color"))
        {
            if (!string.IsNullOrEmpty(data.mainColor)) Carry(made, data, bundle, data.mainColor, "_Color", 1f);
            else
                foreach (string colour in MainColors)
                    if (Has(data.vectors, colour))
                    {
                        Carry(made, data, bundle, colour, "_Color", 1f);
                        break;
                    }
        }
        foreach (string cutoff in Cutoffs)
            if (Has(data.floats, cutoff))
            {
                Carry(made, data, bundle, cutoff, "_Cutoff", 1f);
                break;
            }
        foreach (string cull in Culls)
            if (Has(data.floats, cull))
            {
                Carry(made, data, bundle, cull, "_Cull", 1f);
                break;
            }
        // lilToon draws a feature only when its switch is on.
        Switch(made, "_UseBumpMap", "_BumpMap");
        Switch(made, "_UseMatCap", "_MatCapTex");
        if (made.HasProperty("_UseEmission") && (made.GetTexture("_EmissionMap") != null || Lit(data)))
            made.SetFloat("_UseEmission", 1f);
        if (rule != null)
            foreach (var (property, value) in rule.Set)
                if (made.HasProperty(property)) made.SetFloat(property, value);
        Finish(made, data, false);
        if (told.Add("shader|" + name))
            Debug.Log($"[Mods] {whose}: 쉐이더 '{name}'는 이 게임에 없어 '{target.name}'로 바꿔 그립니다(재질의 그림·색·같은 이름의 값은 옮김, 표 {MapTable}"
                + (rule != null ? $"의 '{rule.Id}' 줄" : "") + "). 똑같이 보려면 lilToon으로 만들거나, Unity 6 + URP 프로젝트에서 그 쉐이더의 URP판으로 내보내세요.");
        return made;
    }

    private static Shader ToonShader(int mode, bool outline) => ModShaderLibrary.Find(mode switch
    {
        1 => outline ? "Hidden/lilToonCutoutOutline" : "Hidden/lilToonCutout",
        2 => outline ? "Hidden/lilToonTransparentOutline" : "Hidden/lilToonTransparent",
        _ => outline ? "Hidden/lilToonOutline" : "lilToon",
    });

    // How the maker's material was drawn, from what its shader family calls it: the render type tag, the queue, the
    // keywords and mode numbers of the usual shaders (Standard's _Mode, URP's _Surface, MToon's _BlendMode).
    private static int ModeOf(ModAvatarFile.MaterialData data)
    {
        foreach (var tag in data.tags ?? Array.Empty<ModAvatarFile.Tag>())
        {
            if (!string.Equals(tag.name, "RenderType", StringComparison.Ordinal)) continue;
            if (string.Equals(tag.value, "TransparentCutout", StringComparison.Ordinal)) return 1;
            if (string.Equals(tag.value, "Transparent", StringComparison.Ordinal) || string.Equals(tag.value, "Fade", StringComparison.Ordinal)) return 2;
        }
        foreach (string keyword in data.keywords ?? Array.Empty<string>())
        {
            if (keyword == "_ALPHABLEND_ON" || keyword == "_ALPHAPREMULTIPLY_ON" || keyword == "_SURFACE_TYPE_TRANSPARENT") return 2;
            if (keyword == "_ALPHATEST_ON") return 1;
        }
        if (data.queue >= (int)RenderQueue.Transparent - 100) return 2;
        if (data.queue >= (int)RenderQueue.AlphaTest) return 1;
        if (Number(data, "_Surface", 0f) > .5f) return 2;
        if (Number(data, "_AlphaClip", 0f) > .5f) return 1;
        int mode = Mathf.RoundToInt(Number(data, "_Mode", Number(data, "_BlendMode", 0f)));
        return mode <= 0 ? 0 : mode == 1 ? 1 : 2;
    }

    private static bool Lit(ModAvatarFile.MaterialData data)
    {
        foreach (string keyword in data.keywords ?? Array.Empty<string>())
            if (keyword == "_EMISSION") return true;
        return false;
    }

    private static void Switch(Material material, string flag, string texture)
    {
        if (material.HasProperty(flag) && material.HasProperty(texture) && material.GetTexture(texture) != null) material.SetFloat(flag, 1f);
    }

    // One of the maker's properties under another name on the made material (a number times scale).
    private static void Carry(Material made, ModAvatarFile.MaterialData data, AssetBundle bundle, string from, string to, float scale)
    {
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || !made.HasProperty(to)) return;
        foreach (var number in data.floats ?? Array.Empty<ModAvatarFile.Number>())
            if (number.name == from)
            {
                made.SetFloat(to, number.value * scale);
                return;
            }
        foreach (var four in data.vectors ?? Array.Empty<ModAvatarFile.Four>())
            if (four.name == from)
            {
                if (four.color) made.SetColor(to, new Color(four.x, four.y, four.z, four.w));
                else made.SetVector(to, new Vector4(four.x, four.y, four.z, four.w));
                return;
            }
        foreach (var picture in data.textures ?? Array.Empty<ModAvatarFile.Picture>())
            if (picture.name == from)
            {
                var texture = TextureOf(bundle, picture);
                if (texture == null) return;
                made.SetTexture(to, texture);
                made.SetTextureScale(to, picture.scale == Vector2.zero ? Vector2.one : picture.scale);
                made.SetTextureOffset(to, picture.offset);
                return;
            }
    }

    // ---- Filling ----

    // Every property of the maker's that the made material's shader has, under the same name. Converting leaves the
    // properties that say how to draw to the game's shader.
    private static void Fill(Material made, ModAvatarFile.MaterialData data, AssetBundle bundle, bool converting)
    {
        foreach (var number in data.floats ?? Array.Empty<ModAvatarFile.Number>())
        {
            if (string.IsNullOrEmpty(number.name) || !made.HasProperty(number.name)) continue;
            if (converting && (Array.IndexOf(DrawState, number.name) >= 0 || number.name.StartsWith("_Stencil", StringComparison.Ordinal))) continue;
            if (number.whole) made.SetInteger(number.name, Mathf.RoundToInt(number.value));
            else made.SetFloat(number.name, number.value);
        }
        foreach (var four in data.vectors ?? Array.Empty<ModAvatarFile.Four>())
        {
            if (string.IsNullOrEmpty(four.name) || !made.HasProperty(four.name)) continue;
            if (four.color) made.SetColor(four.name, new Color(four.x, four.y, four.z, four.w));
            else made.SetVector(four.name, new Vector4(four.x, four.y, four.z, four.w));
        }
        foreach (var picture in data.textures ?? Array.Empty<ModAvatarFile.Picture>())
        {
            if (string.IsNullOrEmpty(picture.name) || !made.HasProperty(picture.name)) continue;
            var texture = TextureOf(bundle, picture);
            if (texture != null) made.SetTexture(picture.name, texture);
            made.SetTextureScale(picture.name, picture.scale == Vector2.zero ? Vector2.one : picture.scale);
            made.SetTextureOffset(picture.name, picture.offset);
        }
    }

    // What isn't a property: the queue, the tags, and (for the maker's own shader) its keywords.
    private static void Finish(Material made, ModAvatarFile.MaterialData data, bool same)
    {
        if (same)
        {
            if (data.keywords != null && data.keywords.Length > 0) made.shaderKeywords = data.keywords;
            foreach (var tag in data.tags ?? Array.Empty<ModAvatarFile.Tag>())
                if (!string.IsNullOrEmpty(tag.name)) made.SetOverrideTag(tag.name, tag.value ?? "");
            foreach (string pass in data.off ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(pass)) made.SetShaderPassEnabled(pass, false);
        }
        // The maker's order among their own see-through pieces (hair over the face, lenses over the eyes).
        if (data.queue >= 0 && (same || Mathf.Abs(data.queue - made.shader.renderQueue) <= 500)) made.renderQueue = data.queue;
        made.enableInstancing = data.instancing;
        made.doubleSidedGI = data.doubleSidedGI;
    }

    private static Texture TextureOf(AssetBundle bundle, ModAvatarFile.Picture picture)
    {
        if (bundle == null || string.IsNullOrEmpty(picture.asset)) return null;
        string key = bundle.GetInstanceID() + "|" + picture.asset + "|" + picture.sub;
        if (textures.TryGetValue(key, out var known) && known != null) return known;
        Texture found = null;
        if (string.IsNullOrEmpty(picture.sub)) found = bundle.LoadAsset<Texture>(picture.asset);
        if (found == null)
            foreach (var each in bundle.LoadAssetWithSubAssets<Texture>(picture.asset))
                if (each != null && (string.IsNullOrEmpty(picture.sub) || each.name == picture.sub))
                {
                    found = each;
                    break;
                }
        textures[key] = found;
        return found;
    }

    private static float Number(ModAvatarFile.MaterialData data, string name, float fallback)
    {
        foreach (var number in data.floats ?? Array.Empty<ModAvatarFile.Number>())
            if (number.name == name) return number.value;
        return fallback;
    }

    private static bool Has(ModAvatarFile.Number[] list, string name)
    {
        foreach (var each in list ?? Array.Empty<ModAvatarFile.Number>())
            if (each.name == name) return true;
        return false;
    }

    private static bool Has(ModAvatarFile.Four[] list, string name)
    {
        foreach (var each in list ?? Array.Empty<ModAvatarFile.Four>())
            if (each.name == name) return true;
        return false;
    }

    private static string ShaderName(Material material) => material.shader != null ? material.shader.name : "없음";

    // ---- The table ----

    // The first row, in the table's order, whose match is in the shader's name; a row matching "*" takes the rest.
    private static Rule RuleFor(string shader)
    {
        if (rules == null)
        {
            if (!listening)
            {
                GameTables.Reloaded += Forget;
                listening = true;
            }
            rules = new List<Rule>();
            foreach (string id in GameTables.RowIds(MapTable))
            {
                if (id.StartsWith("#", StringComparison.Ordinal) || !GameTables.TryGetRow(MapTable, id, out var row)) continue;
                var rule = new Rule { Id = id, Match = row.Text("match"), Use = row.Text("use"), Outline = row.Text("outline") };
                foreach (string pair in row.Text("copy").Split('|'))
                {
                    int to = pair.IndexOf('>');
                    if (to <= 0) continue;
                    string target = pair.Substring(to + 1).Trim();
                    float scale = 1f;
                    int times = target.IndexOf('*');
                    if (times > 0)
                    {
                        if (!TableCells.TryNumber(target.Substring(times + 1).Trim(), out scale)) scale = 1f;
                        target = target.Substring(0, times).Trim();
                    }
                    rule.Copy.Add((pair.Substring(0, to).Trim(), target, scale));
                }
                foreach (string pair in row.Text("set").Split('|'))
                {
                    int equals = pair.IndexOf('=');
                    if (equals > 0 && TableCells.TryNumber(pair.Substring(equals + 1).Trim(), out float value)) rule.Set.Add((pair.Substring(0, equals).Trim(), value));
                }
                if (rule.Match.Length > 0) rules.Add(rule);
            }
        }
        shader ??= "";
        foreach (var rule in rules)
            if (rule.Match != "*" && shader.IndexOf(rule.Match, StringComparison.OrdinalIgnoreCase) >= 0) return rule;
        foreach (var rule in rules)
            if (rule.Match == "*") return rule;
        return null;
    }

    private static void Forget() => rules = null;

    private static void Tell(string message)
    {
        if (told.Add(message)) Debug.LogWarning("[Mods] " + message);
    }
}
