// A bullet's tracer on a two-point LineRenderer (MuzzleEffects): uv.x runs from the tail (0) to the head (1), uv.y across.
// A white-hot core runs down the middle with a hot head at the front, inside an orange glow that falls off to the
// sides; both fade toward the tail, so the streak reads as something flying, not a bar. Additive, in HDR, so bloom does
// the rest. The vertex colour's alpha dims it (a pellet's, or one shrinking into its hit).
Shader "Hidden/ENFILADE/BulletTracer"
{
    Properties
    {
        _CoreColor ("Core", Color) = (6, 4.8, 3.2, 1)
        _GlowColor ("Glow", Color) = (2.4, 0.95, 0.25, 1)
        _Core ("Core share of the width", Range(0.02, 1)) = 0.24
        _Tail ("Fade toward the tail", Float) = 1.6
        _Head ("Hot head share of the length", Range(0, 1)) = 0.16
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Tracer"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
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
                half _Tail;
                half _Head;
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
                half across = abs(input.uv.y * 2.0h - 1.0h);
                half along = saturate(input.uv.x);
                // The core: solid down the middle, soft at its edge.
                half core = 1.0h - smoothstep(_Core * 0.45h, _Core, across);
                // The glow: strongest in the middle, gone at the line's edge.
                half glow = 1.0h - across;
                glow *= glow * glow;
                // Along the streak: nothing at the tail, full toward the head, and the head itself hotter still. The very
                // tip softens over its last few percent, so the front isn't a hard cut.
                half body = pow(max(along, 0.0001h), _Tail);
                half hot = smoothstep(1.0h - max(_Head, 0.001h), 1.0h, along);
                half tip = 1.0h - smoothstep(0.965h, 1.0h, along);
                half3 color = _GlowColor.rgb * glow * body + _CoreColor.rgb * core * saturate(body * 0.65h + hot) * tip;
                return half4(color * input.color.rgb * input.color.a, 1.0h);
            }
            ENDHLSL
        }
    }
}
