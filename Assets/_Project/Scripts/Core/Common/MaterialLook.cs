using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// What a material draws as its own surface, for the effects that draw a body again with a material of their own and keep
/// each piece's look on it: the dodge's afterimages (DodgeDash) and a body baked as it stands (BodyBake: the Knight's
/// shadow double, the cover step's afterimages). That is its shader's main texture and the texture's tiling, its main
/// colour, and whether the texture's alpha cuts it. Only cut-out and see-through materials are cut: an opaque one ignores
/// its texture's alpha, and many keep something else there. Anything without a main texture draws white.
/// Both kept a copy of this until 0.9.38, when KINEMATION's mannequin came in as the test character (the user,
/// 2026-11-14: "키네메이션 기본 캐릭터 … 테스트용 캐릭터도 넣어줘"): its shader keeps the colour map in a texture called
/// _Color, which both copies read as a colour.
/// </summary>
public struct MaterialLook
{
    /// <summary>The main texture; null draws white.</summary>
    public Texture Texture;
    /// <summary>The texture's tiling and offset, as a _ST vector (x, y scale; z, w offset).</summary>
    public Vector4 Tiling;
    public Color Color;
    /// <summary>1 when the texture's alpha cuts the surface (cut-out and see-through materials), else 0.</summary>
    public float Cut;

    /// <summary>No texture, no tiling, white: a piece without a material.</summary>
    public static MaterialLook Plain => new MaterialLook { Tiling = new Vector4(1f, 1f, 0f, 0f), Color = Color.white };

    /// <summary>The look <paramref name="material"/> draws with (Plain for none).</summary>
    public static MaterialLook Of(Material material)
    {
        var look = Plain;
        var shader = material != null ? material.shader : null;
        if (shader == null) return look;
        var main = Mains.Of(shader);
        if (main.HasTexture)
        {
            look.Texture = material.GetTexture(main.Texture);
            Vector2 scale = material.GetTextureScale(main.Texture), offset = material.GetTextureOffset(main.Texture);
            look.Tiling = new Vector4(scale.x, scale.y, offset.x, offset.y);
        }
        if (main.HasColor) look.Color = material.GetColor(main.Color);
        look.Cut = material.renderQueue >= (int)RenderQueue.AlphaTest ? 1f : 0f;
        return look;
    }

    // A shader's main texture and colour property ids, found once per shader.
    private static class Mains
    {
        public readonly struct Main
        {
            public readonly int Texture, Color;
            public readonly bool HasTexture, HasColor;

            public Main(int texture, int color, bool hasTexture, bool hasColor)
            {
                Texture = texture;
                Color = color;
                HasTexture = hasTexture;
                HasColor = hasColor;
            }
        }

        private static readonly Dictionary<Shader, Main> found = new Dictionary<Shader, Main>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => found.Clear();

        // The ones the shader marks [MainTexture] and [MainColor] (URP Lit: _BaseMap, _BaseColor; lilToon: _MainTex, _Color;
        // lilToon also keeps a hidden, unused _BaseMap and _BaseColor for fallbacks, so going by the names alone would draw
        // its outfits white), else the names Unity takes as main (_MainTex, _Color: MToon, Poiyomi, the built-in shaders),
        // else URP's. By name, only a property of the kind asked for: KINEMATION's mannequin keeps its colour map in a
        // texture called _Color, which read as a colour came out wrong; with no other main texture that one is it.
        public static Main Of(Shader shader)
        {
            if (found.TryGetValue(shader, out var main)) return main;
            int texture = -1, color = -1;
            for (int i = 0, count = shader.GetPropertyCount(); i < count; i++)
            {
                var flags = shader.GetPropertyFlags(i);
                var type = shader.GetPropertyType(i);
                if (texture < 0 && (flags & ShaderPropertyFlags.MainTexture) != 0 && type == ShaderPropertyType.Texture) texture = i;
                if (color < 0 && (flags & ShaderPropertyFlags.MainColor) != 0 && type == ShaderPropertyType.Color) color = i;
            }
            if (texture < 0) texture = Named(shader, "_MainTex", ShaderPropertyType.Texture);
            if (texture < 0) texture = Named(shader, "_BaseMap", ShaderPropertyType.Texture);
            if (texture < 0) texture = Named(shader, "_Color", ShaderPropertyType.Texture);
            if (color < 0) color = Named(shader, "_Color", ShaderPropertyType.Color);
            if (color < 0) color = Named(shader, "_BaseColor", ShaderPropertyType.Color);
            main = new Main(texture >= 0 ? shader.GetPropertyNameId(texture) : 0, color >= 0 ? shader.GetPropertyNameId(color) : 0,
                texture >= 0, color >= 0);
            found[shader] = main;
            return main;
        }

        // The shader's property of that name when it is of that kind, else -1.
        private static int Named(Shader shader, string name, ShaderPropertyType type)
        {
            int index = shader.FindPropertyIndex(name);
            return index >= 0 && shader.GetPropertyType(index) == type ? index : -1;
        }
    }
}
