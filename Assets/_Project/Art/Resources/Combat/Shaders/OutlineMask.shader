// A marked body drawn into the outline mask (EnemyOutlines, OutlinePass), into targets with as many samples a pixel as
// the camera's own, against the camera's own depth buffer: the hardware tests each sample, so whether the body is seen or
// hidden there is told to a fraction of a pixel, as the scene itself was drawn. Seen or hidden is the one see-through rule
// (SeeThrough.hlsl): hidden where something stands in front of the body by more than the gap. The body's depth is pulled
// that gap nearer the eye for the test, its place on screen kept, so the test is the rule itself:
// - pass "Hidden" (ZTest Greater): every sample where something stands in front by more than the gap;
// - pass "Seen" (ZTest LEqual): every sample where nothing does; smoke on the way hides as much as it covers (SmokeCover).
// OutlinePass draws every body's Hidden pass first and then their Seen passes, so the surface the eye sees wins each
// sample (an arm behind the chest is hidden there, the chest in front of it is seen).
// Until 2026-11-21 (the same day) each pixel was told once from the depth texture: the edge of whatever stood in front
// stepped whole pixels as the view moved and a line of fill flickered along it.
//   target 0: rgb the band's colour (HDR allowed); a 0 nothing marked here, 0.5 a marked body wanting no band from
//             here, 1 + the band's strength one that does.
//   target 1: the fill, premultiplied (rgb * a, a).
Shader "Hidden/ENFILADE/OutlineMask"
{
    Properties
    {
        _Seen ("Band where seen", Color) = (0, 0, 0, 0)
        _Hidden ("Band where hidden", Color) = (0, 0, 0, 0)
        _Fill ("Fill where hidden", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "SeeThrough.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Seen;
            half4 _Hidden;
            half4 _Fill;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
        struct Masks { half4 band : SV_Target0; half4 fill : SV_Target1; };

        // Where it is on screen, at the depth of a point the gap nearer the eye (never past the near plane).
        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float4 positionCS = TransformWorldToHClip(positionWS);
            float3 positionVS = TransformWorldToView(positionWS);
            positionVS.z = min(positionVS.z + SeeThroughGap(), -_ProjectionParams.y * 1.001);
            float4 nearer = TransformWViewToHClip(positionVS);
            positionCS.z = nearer.z / nearer.w * positionCS.w;
            output.positionCS = positionCS;
            output.positionWS = positionWS;
            return output;
        }

        // This body's band and fill at a sample hidden as much as `hidden` (0 seen, 1 hidden).
        Masks Mask(half hidden)
        {
            half4 band = hidden >= 0.5h ? _Hidden : _Seen;
            Masks output;
            output.band = half4(band.rgb, band.a > 0.001h ? 1.0h + saturate(band.a) : 0.5h);
            half fill = saturate(_Fill.a) * hidden;
            output.fill = half4(_Fill.rgb * fill, fill);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Hidden"
            ZWrite Off
            ZTest Greater
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            Masks Frag(Varyings input) { return Mask(1.0h); }
            ENDHLSL
        }

        Pass
        {
            Name "Seen"
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            Masks Frag(Varyings input) { return Mask(SmokeCover(input.positionWS)); }
            ENDHLSL
        }
    }
    Fallback Off
}
