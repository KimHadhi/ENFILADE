// A bullet's tracer on a two-point LineRenderer (MuzzleEffects): uv.x runs from the tail (0) to the head (1), uv.y across.
// A white-hot core runs down the middle with a hot head at the front, inside an orange glow that falls off to the
// sides; both fade toward the tail, so the streak reads as something flying, not a bar. In HDR, so bloom does the rest.
// The vertex colour's alpha dims it (a pellet's, or one shrinking into its hit).
// What one frame draws is the comet (the hot head and its tail, _Comet of the drawn length) plus, behind it, the rest of
// the way the bullet flew since the last frame (_Step of the drawn length), which never goes darker than _Swept: a
// frame can carry the bullet further than the comet is long (10 m at 60 fps against a 5.5 m streak), and each frame's
// streak then stood apart from the last with nothing between, the gap widest on screen right after the muzzle (user,
// 2026-11-12: suppressed tracers looked cut off midway; an open gun's flame had hidden it).
// Blended premultiplied: light added as before, and _Cover of what is behind the bright part taken away, so over a bright
// sky or a sunlit wall the hot orange still shows instead of only whitening what is there. Over the dark it is the same
// as adding light.
Shader "Hidden/ENFILADE/BulletTracer"
{
    Properties
    {
        _CoreColor ("Core", Color) = (8, 4.83, 1.97, 1)
        _GlowColor ("Glow", Color) = (3, 0.4, 0.03, 1)
        _Core ("Core share of the width", Range(0.02, 1)) = 0.24
        _CorePixels ("The core is never thinner than this (pixels)", Float) = 1.6
        _Tail ("Fade toward the tail", Float) = 1.6
        _Head ("Hot head share of the length", Range(0, 1)) = 0.16
        _Swept ("Least brightness of the way flown since the last frame", Range(0, 1)) = 0.3
        _Cover ("Share of what is behind the bright part it covers", Range(0, 1)) = 0.6
        // Per tracer, from MuzzleEffects through a MaterialPropertyBlock.
        _Comet ("The comet's share of the drawn length", Float) = 1
        _Step ("The share of the drawn length flown since the last frame", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Tracer"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _GlowColor;
                half _Core;
                half _CorePixels;
                half _Tail;
                half _Head;
                half _Swept;
                half _Cover;
                half _Comet;
                half _Step;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // A pixel at the line's edge is shaded at its centre, which may lie just outside the line.
                half across = saturate(abs(input.uv.y * 2.0h - 1.0h));
                half along = saturate(input.uv.x);
                // The line's width on screen (pixels), from how fast uv.y runs across it. A far tracer is a few pixels
                // wide, and its core, a quarter of that, fell under one pixel and flickered away; it keeps _CorePixels.
                float pixels = 1.0 / max(length(float2(ddx(input.uv.y), ddy(input.uv.y))), 1e-4);
                half coreShare = max(_Core, (half)(_CorePixels / pixels));
                // The core: solid down the middle, soft at its edge.
                half core = 1.0h - smoothstep(coreShare * 0.45h, coreShare, across);
                // The glow: strongest in the middle, gone at the line's edge.
                half glow = 1.0h - across;
                glow *= glow * glow;
                // Along the comet: nothing at its tail, full toward the head, and the head itself hotter still. The very
                // tip softens over its last few percent, so the front isn't a hard cut.
                half fromHead = 1.0h - along;
                half comet = saturate(1.0h - fromHead / max(_Comet, 0.001h));
                half body = pow(max(comet, 0.0001h), _Tail);
                // The way flown since the last frame keeps at least _Swept, so the frames join into one unbroken line.
                body = max(body, _Swept * (1.0h - smoothstep(_Step - 0.02h, _Step, fromHead)));
                half hot = smoothstep(1.0h - max(_Head, 0.001h), 1.0h, comet);
                half tip = 1.0h - smoothstep(0.965h, 1.0h, comet);
                half heat = saturate(body * 0.65h + hot);
                half3 color = _GlowColor.rgb * glow * body + _CoreColor.rgb * core * heat * tip;
                half fade = input.color.a;
                // A little wider than the glow, so a bright sky right round the white core turns hot orange. Squared, it
                // keeps about the sky's own brightness there (5% darker at most on a pale sky, 11% on a white overcast,
                // 1% on a blue sky or a sunlit wall): a hue, not a dark outline. Linear, it darkened by up to a fifth.
                half rim = 1.0h - across;
                half cover = _Cover * rim * rim * saturate(body * 1.5h) * fade;
                return half4(color * input.color.rgb * fade, saturate(cover));
            }
            ENDHLSL
        }
    }
}
