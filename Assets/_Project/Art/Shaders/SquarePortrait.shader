// The HUD portrait tile (HudPortrait), drawn whole on one square quad, back to front:
//   two slabs leaning behind it at their own angles (pink peeking out top left, a black shadow bottom right), Persona-style;
//   a black mat around the picture;
//   the picture (the player's profile picture, ProfilePicture) with two corners cut like the menus' cards (KitStyle.Cut),
//   a thin near-white edge, a soft dark vignette and the pink bar along its foot, as on the lobby's profile chip;
//   a faint frame line on the mat, with faint corner brackets on it.
// _GlowColor, _GlowIntensity and _GlowBloomSpread could be driven by UltimatePortraitGlow; since round 4 (2026-11-11) the
// ultimate is the magazine beside the tile (UltimateMagazine), so that is off and the brackets stay a faint white.
// Sizes are fractions of the quad's side (the quad is square). Output is premultiplied, so the glow adds light.
Shader "UI/Custom/SquarePortrait"
{
    Properties
    {
        [PerRendererData] _MainTex ("Picture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Picture)]
        _Half ("Picture Half Size", Range(0.1, 0.45)) = 0.27
        _Cut ("Cut Corners (top right, bottom left)", Range(0.0, 0.15)) = 0.05
        _EdgeWidth ("Edge Line Width", Range(0.0, 0.02)) = 0.005
        _EdgeColor ("Edge Line Color", Color) = (1, 0.96, 0.97, 0.85)
        _BarHeight ("Bottom Bar Height", Range(0.0, 0.06)) = 0.02
        _BarColor ("Bottom Bar Color", Color) = (1, 0.31, 0.47, 1)
        _VignetteColor ("Vignette Color", Color) = (0.055, 0.055, 0.065, 1)
        _VignetteIntensity ("Vignette Intensity", Range(0.0, 1.0)) = 0.45
        _VignetteStart ("Vignette Start (0 centre, 1 edge)", Range(0.0, 1.0)) = 0.55

        [Header(Mat And Slabs)]
        _MatWidth ("Black Mat Width", Range(0.0, 0.05)) = 0.024
        _MatColor ("Black Mat Color", Color) = (0.055, 0.055, 0.065, 0.96)
        _SlabColor ("Back Slab Color", Color) = (1, 0.31, 0.47, 1)
        _SlabHalf ("Back Slab Half Size", Range(0.1, 0.48)) = 0.3
        _SlabAngle ("Back Slab Angle (deg, + anticlockwise)", Range(-30, 30)) = 6
        _SlabOffset ("Back Slab Offset (xy)", Vector) = (-0.02, 0.018, 0, 0)
        _ShadowColor ("Shadow Slab Color", Color) = (0.055, 0.055, 0.065, 0.9)
        _ShadowHalf ("Shadow Slab Half Size", Range(0.1, 0.48)) = 0.295
        _ShadowAngle ("Shadow Slab Angle (deg, + anticlockwise)", Range(-30, 30)) = -3.5
        _ShadowOffset ("Shadow Slab Offset (xy)", Vector) = (0.024, -0.024, 0, 0)

        [Header(Corner Brackets)]
        _GapWidth ("Gap From Picture To Bracket", Range(0.0, 0.05)) = 0.008
        _BorderThickness ("Bracket Width", Range(0.002, 0.04)) = 0.012
        _BracketLength ("Bracket Length (share of each half edge)", Range(0.1, 1.0)) = 0.4
        _FrameColor ("Faint Frame Line Color", Color) = (1, 0.96, 0.97, 0.18)

        [Header(Glow)]
        _GlowColor ("Glow Color", Color) = (1, 0.31, 0.47, 1)
        _GlowIntensity ("Glow Intensity", Range(0.0, 10.0)) = 1.0
        _GlowBloomSpread ("Glow Spread", Range(0.001, 0.06)) = 0.005

        // --- UGUI Canvas ---
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _Half, _Cut, _EdgeWidth, _BarHeight, _VignetteIntensity, _VignetteStart;
            fixed4 _EdgeColor, _BarColor, _VignetteColor;
            float _MatWidth, _SlabHalf, _SlabAngle, _ShadowHalf, _ShadowAngle;
            fixed4 _MatColor, _SlabColor, _ShadowColor;
            float4 _SlabOffset, _ShadowOffset;
            float _GapWidth, _BorderThickness, _BracketLength;
            fixed4 _FrameColor;
            fixed4 _GlowColor;
            float _GlowIntensity, _GlowBloomSpread;

            float SdBox(float2 p, float size)
            {
                float2 d = abs(p) - size;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            // A square with its top-right and bottom-left corners cut by c: the menus' card shape.
            float SdCutSquare(float2 p, float size, float c)
            {
                float box = SdBox(p, size);
                float cut = (abs(p.x + p.y) - (2.0 * size - c)) * 0.70710678;
                return max(box, cut);
            }

            // p in the frame of a shape turned by degrees (anticlockwise) about its centre.
            float2 Unturn(float2 p, float degrees)
            {
                float r = radians(degrees);
                float s = sin(r), c = cos(r);
                return float2(c * p.x + s * p.y, -s * p.x + c * p.y);
            }

            // How much of the pixel lies inside (d < 0), one pixel of soft edge.
            float Fill(float d, float px) { return saturate(0.5 - d / px); }

            // Premultiplied "over".
            float4 Over(float4 below, float3 rgb, float a) { return float4(rgb * a + below.rgb * (1.0 - a), a + below.a * (1.0 - a)); }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPos);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                float px = max(max(fwidth(p.x), fwidth(p.y)), 1e-5);
                float4 o = 0;

                // 1. The slabs leaning behind.
                o = Over(o, _SlabColor.rgb, Fill(SdBox(Unturn(p - _SlabOffset.xy, _SlabAngle), _SlabHalf), px) * _SlabColor.a);
                o = Over(o, _ShadowColor.rgb, Fill(SdBox(Unturn(p - _ShadowOffset.xy, _ShadowAngle), _ShadowHalf), px) * _ShadowColor.a);

                // 2. The black mat.
                float d = SdCutSquare(p, _Half, _Cut);
                o = Over(o, _MatColor.rgb, Fill(d - _MatWidth, px) * _MatColor.a);

                // 3. The picture: vignette, edge line, then the pink bar along its foot.
                float inside = Fill(d, px);
                float2 pictureUv = p / (2.0 * _Half) + 0.5;
                fixed4 tex = (tex2D(_MainTex, pictureUv) + _TextureSampleAdd) * fixed4(i.color.rgb, 1.0);
                float ring = max(abs(p.x), abs(p.y)) / max(_Half, 1e-4);
                float vignette = saturate(smoothstep(_VignetteStart, 1.0, ring) * _VignetteIntensity) * _VignetteColor.a;
                float3 picture = lerp(tex.rgb, _VignetteColor.rgb, vignette);
                picture = lerp(picture, _EdgeColor.rgb, Fill(-d - _EdgeWidth, px) * _EdgeColor.a * step(1e-5, _EdgeWidth));
                picture = lerp(picture, _BarColor.rgb, Fill(p.y + _Half - _BarHeight, px) * _BarColor.a * step(1e-5, _BarHeight));
                o = Over(o, picture, inside * tex.a);

                // 4. The frame line on the mat, and the brackets at the corners.
                float rail = d - _GapWidth - _BorderThickness * 0.5;
                float core = Fill(abs(rail) - _BorderThickness * 0.5, px);
                float2 q = abs(p);
                float along = (q.x > q.y ? q.y : q.x) / max(_Half + _GapWidth, 1e-4);
                float corner = smoothstep(1.0 - _BracketLength - 0.03, 1.0 - _BracketLength, along);
                o = Over(o, _FrameColor.rgb, core * (1.0 - corner) * _FrameColor.a);

                // 5. The glow: the brackets' core and a halo, added as light; softer over the picture.
                float halo = exp(-abs(rail) / max(_GlowBloomSpread, 1e-4)) * lerp(1.0, 0.35, inside);
                float glow = (core * 2.0 + halo * 1.2) * corner;
                o.rgb += _GlowColor.rgb * _GlowIntensity * glow;
                o.a = saturate(o.a + glow);

                #ifdef UNITY_UI_CLIP_RECT
                o *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif
                o *= i.color.a;

                #ifdef UNITY_UI_ALPHACLIP
                clip(o.a - 0.001);
                #endif
                if (o.a <= 0.001) discard;
                return o;
            }
            ENDCG
        }
    }
}
