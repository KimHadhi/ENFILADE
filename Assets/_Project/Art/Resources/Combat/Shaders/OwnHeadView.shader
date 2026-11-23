// Lays the own head's hair, ears and hat (drawn into a picture of their own by their own shaders, OwnHeadView) over the
// player's own view, sample by sample. What lies nearer the camera than _OwnHeadFade.x shows not at all, from _OwnHeadFade.y
// all of it, in between that share of its colour, so a strand that swings into the camera thins out and goes instead of
// being cut where it crosses the near clip plane. The camera's own depth decides what is in front (another player's finger at
// the face covers the hair). Pass 0 lays the colour; pass 1 writes the depth and the own-view mark (stencil bit 128,
// OwnViewMask) where at least half of it shows, so what is drawn later goes behind it.
// _OwnHeadSize: x, y the picture's pixels; z, w how far a pixel at the picture's right and top edges looks aside per metre ahead.
Shader "Hidden/ENFILADE/OwnHeadView"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        #if defined(OWN_HEAD_SAMPLES_8)
            #define OWN_HEAD_SAMPLES 8
        #elif defined(OWN_HEAD_SAMPLES_4)
            #define OWN_HEAD_SAMPLES 4
        #elif defined(OWN_HEAD_SAMPLES_2)
            #define OWN_HEAD_SAMPLES 2
        #else
            #define OWN_HEAD_SAMPLES 1
        #endif
        #if OWN_HEAD_SAMPLES > 1
            Texture2DMS<float4, OWN_HEAD_SAMPLES> _OwnHeadColor;
            Texture2DMS<float, OWN_HEAD_SAMPLES> _OwnHeadDepth;
            #define OWN_HEAD_LOAD(textureName, at, sampleIndex) textureName.Load(at, sampleIndex)
            #define OWN_HEAD_SAMPLE_ARG , uint sampleIndex : SV_SampleIndex
            #define OWN_HEAD_SAMPLE sampleIndex
        #else
            Texture2D<float4> _OwnHeadColor;
            Texture2D<float> _OwnHeadDepth;
            #define OWN_HEAD_LOAD(textureName, at, sampleIndex) textureName.Load(int3(at, 0))
            #define OWN_HEAD_SAMPLE_ARG
            #define OWN_HEAD_SAMPLE 0
        #endif

        float4 _OwnHeadFade;
        float4 _OwnHeadSize;

        struct Varyings { float4 positionCS : SV_POSITION; };

        Varyings Vert(uint vertexID : SV_VertexID)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
            return output;
        }

        // What of the head lies at this sample: its colour as much as it shows (premultiplied), its depth, and whether it is
        // solid there (wrote depth) or only see-through.
        bool OwnHead(float4 positionCS, uint sampleIndex, out half4 colour, out float rawDepth, out bool solid)
        {
            int2 at = int2(positionCS.xy);
            rawDepth = OWN_HEAD_LOAD(_OwnHeadDepth, at, sampleIndex);
            half4 drawn = OWN_HEAD_LOAD(_OwnHeadColor, at, sampleIndex);
            #if UNITY_REVERSED_Z
                solid = rawDepth > 0.0;
            #else
                solid = rawDepth < 1.0;
            #endif
            // A solid sample shows whole whatever alpha its shader wrote; a see-through one as much as it covered the clear.
            half cover = solid ? 1.0h : saturate(drawn.a);
            colour = half4(0.0h, 0.0h, 0.0h, 0.0h);
            if (cover <= 0.0h) return false;
            half fade = 1.0h;
            if (solid)
            {
                float2 aside = (positionCS.xy / _OwnHeadSize.xy * 2.0 - 1.0) * _OwnHeadSize.zw;
                float away = LinearEyeDepth(rawDepth, _ZBufferParams) * length(float3(aside, 1.0));
                fade = (half)saturate((away - _OwnHeadFade.x) / max(0.0001, _OwnHeadFade.y - _OwnHeadFade.x));
            }
            colour = half4(drawn.rgb * fade, cover * fade);
            return colour.a > 0.0h;
        }
        ENDHLSL

        Pass
        {
            Name "Colour"
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local OWN_HEAD_SAMPLES_1 OWN_HEAD_SAMPLES_2 OWN_HEAD_SAMPLES_4 OWN_HEAD_SAMPLES_8

            half4 Frag(Varyings input OWN_HEAD_SAMPLE_ARG, out float depth : SV_Depth) : SV_Target
            {
                half4 colour;
                float rawDepth;
                bool solid;
                if (!OwnHead(input.positionCS, OWN_HEAD_SAMPLE, colour, rawDepth, solid)) discard;
                // A see-through sample has no depth of its own: it lies just past the near clip plane.
                depth = solid ? rawDepth : UNITY_NEAR_CLIP_VALUE;
                return colour;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Depth"
            ColorMask 0
            ZWrite On
            Stencil { Ref 128 WriteMask 128 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local OWN_HEAD_SAMPLES_1 OWN_HEAD_SAMPLES_2 OWN_HEAD_SAMPLES_4 OWN_HEAD_SAMPLES_8

            float Frag(Varyings input OWN_HEAD_SAMPLE_ARG) : SV_Depth
            {
                half4 colour;
                float rawDepth;
                bool solid;
                if (!OwnHead(input.positionCS, OWN_HEAD_SAMPLE, colour, rawDepth, solid) || !solid || colour.a < 0.5h) discard;
                return rawDepth;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
