// Lays the outline band from the mask OutlineMask drew (EnemyOutlines, OutlinePass), over the whole picture by one triangle.
// The mask is drawn with the camera's samples a pixel, so each pixel knows how much of it a marked body covers, and the band is
// laid by the distance from the body's edge to a fraction of a pixel: across each row (pass 1), then down the column
// through those rows (pass 2, the root of across squared plus down squared, less the covered share), so its width is the
// same all round the body, at any distance, on any mesh, and it slides with the body instead of stepping whole pixels.
// Until 2026-11-21 it was laid from whole pixels: a picture drifting a fraction of a pixel a frame, as an aimed sight's
// breathing moves it, turned whole pixels of the band on and off (OutlineBench "sway": 90 a frame on a reaper at 12 m),
// and the user saw a pinged target's outline flicker while aiming. Out to the band's width it is the body's colour, then
// a thin dark edge fades out so the band reads on a bright wall as on a dark one. Over a marked body's hidden parts the fill.
// _OutlineWidths: x the band (px), y the dark edge's outer side (px), z the edge's alpha, w how far the search reaches (px).
Shader "Hidden/ENFILADE/OutlineLay"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

        TEXTURE2D(_OutlineGathered);
        TEXTURE2D(_OutlineCover);
        TEXTURE2D(_OutlineFill);
        TEXTURE2D(_OutlineAcross);
        TEXTURE2D(_OutlineAcrossReach);
        float4 _OutlineSize;
        float4 _OutlineWidths;

        struct Varyings { float4 positionCS : SV_POSITION; };

        Varyings Vert(uint vertexID : SV_VertexID)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
            return output;
        }

        int2 Inside(int2 at) { return clamp(at, int2(0, 0), int2(_OutlineSize.xy) - 1); }
        ENDHLSL

        // Gather: each pixel's four samples as one. The band's colour and strength (the mean of the samples that want a
        // band), how much of the pixel wants a band and how much any marked body covers, and the fill (its mean).
        Pass
        {
            Name "Gather"
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            // The mask has the camera's samples a pixel (OutlinePass picks the material with that keyword).
            #pragma multi_compile_local OUTLINE_SAMPLES_1 OUTLINE_SAMPLES_2 OUTLINE_SAMPLES_4 OUTLINE_SAMPLES_8
            #if defined(OUTLINE_SAMPLES_8)
                #define OUTLINE_SAMPLES 8
            #elif defined(OUTLINE_SAMPLES_4)
                #define OUTLINE_SAMPLES 4
            #elif defined(OUTLINE_SAMPLES_2)
                #define OUTLINE_SAMPLES 2
            #else
                #define OUTLINE_SAMPLES 1
            #endif
            #if OUTLINE_SAMPLES > 1
                Texture2DMS<float4, OUTLINE_SAMPLES> _OutlineEdgeMS;
                Texture2DMS<float4, OUTLINE_SAMPLES> _OutlineFillMS;
                #define OUTLINE_LOAD(textureName, at, sample) textureName.Load(at, sample)
            #else
                Texture2D<float4> _OutlineEdgeMS;
                Texture2D<float4> _OutlineFillMS;
                #define OUTLINE_LOAD(textureName, at, sample) textureName.Load(int3(at, 0))
            #endif

            struct Gathered { half4 colour : SV_Target0; half2 cover : SV_Target1; half4 fill : SV_Target2; };

            Gathered Frag(Varyings input)
            {
                int2 at = int2(input.positionCS.xy);
                float4 colour = 0;
                float4 fill = 0;
                float wanting = 0, covered = 0;
                [unroll] for (int i = 0; i < OUTLINE_SAMPLES; i++)
                {
                    float4 edge = OUTLINE_LOAD(_OutlineEdgeMS, at, i);
                    if (edge.a > 0.25) covered += 1.0;
                    if (edge.a > 1.0)
                    {
                        wanting += 1.0;
                        colour += float4(edge.rgb, edge.a - 1.0);
                    }
                    fill += OUTLINE_LOAD(_OutlineFillMS, at, i);
                }
                Gathered output;
                output.colour = (half4)(wanting > 0.0 ? colour / wanting : 0.0);
                output.cover = (half2)(float2(wanting, covered) / OUTLINE_SAMPLES);
                output.fill = (half4)(fill / OUTLINE_SAMPLES);
                return output;
            }
            ENDHLSL
        }

        // Across: along this row, the pixel that wants a band whose edge lies nearest (its distance, less how much of it
        // wants one: a pixel half covered has the body's edge through its middle), its distance and share, and its colour.
        Pass
        {
            Name "Across"
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            struct Across { half4 colour : SV_Target0; half2 reach : SV_Target1; };

            Across Frag(Varyings input)
            {
                int2 at = int2(input.positionCS.xy);
                int reach = (int)_OutlineWidths.w;
                float best = 1000.0;
                half4 found = 0;
                half2 where = half2(1000.0h, 0.0h);
                [loop] for (int dx = -reach; dx <= reach; dx++)
                {
                    int2 there = Inside(at + int2(dx, 0));
                    float share = LOAD_TEXTURE2D(_OutlineCover, there).r;
                    if (share <= 0.0) continue;
                    float score = abs(dx) + 0.5 - share;
                    if (score < best)
                    {
                        best = score;
                        found = LOAD_TEXTURE2D(_OutlineGathered, there);
                        where = half2((half)abs(dx), (half)share);
                    }
                }
                Across output;
                output.colour = found;
                output.reach = where;
                return output;
            }
            ENDHLSL
        }

        // Lay: the band and its dark edge over what of the pixel no marked body covers, the fill over what it covers where
        // hidden. Never where this PC's own first-person body, gun or scope lens is (stencil bit 128, OwnViewMask).
        Pass
        {
            Name "Lay"
            Blend One OneMinusSrcAlpha
            Stencil { Ref 0 ReadMask 128 Comp Equal }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                int2 at = int2(input.positionCS.xy);
                half4 fill = LOAD_TEXTURE2D(_OutlineFill, at);
                float covered = LOAD_TEXTURE2D(_OutlineCover, at).g;
                if (covered >= 0.999) return fill;
                int reach = (int)_OutlineWidths.w;
                float nearest = 1000.0;
                half4 colour = 0;
                [loop] for (int dy = -reach; dy <= reach; dy++)
                {
                    int2 row = Inside(at + int2(0, dy));
                    float2 there = LOAD_TEXTURE2D(_OutlineAcrossReach, row).rg;
                    if (there.y <= 0.0) continue;
                    float away = sqrt(there.x * there.x + (float)(dy * dy)) + 0.5 - there.y;
                    if (away < nearest)
                    {
                        nearest = away;
                        colour = LOAD_TEXTURE2D(_OutlineAcross, row);
                    }
                }
                half band = saturate(_OutlineWidths.x + 0.5 - nearest) * colour.a;
                half dark = saturate(_OutlineWidths.y + 0.5 - nearest) * _OutlineWidths.z * colour.a * (1.0h - band);
                return fill + half4(colour.rgb * band, band + dark) * (half)(1.0 - covered);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
