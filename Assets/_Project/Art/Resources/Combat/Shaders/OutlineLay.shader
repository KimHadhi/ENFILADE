// Lays the outline band from the mask OutlineMask drew (EnemyOutlines, OutlinePass), over the whole picture by one triangle.
// The band is laid by true distance from the nearest pixel that wants one, found in two one-way searches: across each
// row (pass 0), then down the column through those rows (pass 1, the root of across squared plus down squared), so its
// width is the same all round the body, at any distance, on any mesh. Out to the band's width it is the body's colour,
// then a thin dark edge fades out so the band reads on a bright wall as on a dark one. Over a marked body's hidden parts
// the fill.
// _OutlineWidths: x the band (px), y how far the search reaches (whole px: the band and the dark edge), z the edge's alpha.
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

        TEXTURE2D(_OutlineEdge);
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

        // Across: the nearest pixel along this row that wants a band (whole px; 1000 for none), and its colour and strength.
        Pass
        {
            Name "Across"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Across { half4 colour : SV_Target0; half reach : SV_Target1; };

            Across Frag(Varyings input)
            {
                int2 at = int2(input.positionCS.xy);
                int reach = (int)_OutlineWidths.y;
                half nearest = 1000.0h;
                half4 found = 0;
                [loop] for (int dx = -reach; dx <= reach; dx++)
                {
                    half4 mask = LOAD_TEXTURE2D(_OutlineEdge, Inside(at + int2(dx, 0)));
                    half away = (half)abs(dx);
                    if (mask.a > 1.0h && away < nearest)
                    {
                        nearest = away;
                        found = half4(mask.rgb, mask.a - 1.0h);
                    }
                }
                Across output;
                output.colour = found;
                output.reach = nearest;
                return output;
            }
            ENDHLSL
        }

        // Lay: the band and its dark edge where no marked body is, the fill where one is hidden. Never where this PC's own
        // first-person body, gun or scope lens is (stencil bit 128, OwnViewMask).
        Pass
        {
            Name "Lay"
            Blend One OneMinusSrcAlpha
            Stencil { Ref 0 ReadMask 128 Comp Equal }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                int2 at = int2(input.positionCS.xy);
                half4 here = LOAD_TEXTURE2D(_OutlineEdge, at);
                if (here.a > 0.25h) return LOAD_TEXTURE2D(_OutlineFill, at);
                int reach = (int)_OutlineWidths.y;
                float nearest = 1000.0;
                half4 colour = 0;
                [loop] for (int dy = -reach; dy <= reach; dy++)
                {
                    int2 row = Inside(at + int2(0, dy));
                    float across = LOAD_TEXTURE2D(_OutlineAcrossReach, row).r;
                    if (across > reach) continue;
                    float away = sqrt(across * across + (float)(dy * dy));
                    if (away < nearest)
                    {
                        nearest = away;
                        colour = LOAD_TEXTURE2D(_OutlineAcross, row);
                    }
                }
                half band = saturate(_OutlineWidths.x + 0.5 - nearest) * colour.a;
                half dark = saturate(_OutlineWidths.y + 0.5 - nearest) * _OutlineWidths.z * colour.a * (1.0h - band);
                return half4(colour.rgb * band, band + dark);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
