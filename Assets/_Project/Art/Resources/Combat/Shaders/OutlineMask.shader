// A marked body drawn into the outline mask (EnemyOutlines, OutlinePass): one pass, the nearest marked body winning each
// pixel by its own depth. Each pixel says whether that body is seen or hidden there by the one see-through rule
// (SeeThrough.hlsl: something clearly in front of it, never the viewer's own first-person things; smoke on the way as
// much as it hides), which band it wants there, and the fill over it if hidden. OutlineLay lays the band from this.
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
        Pass
        {
            Name "OutlineMask"
            ZWrite On
            ZTest LEqual
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SeeThrough.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Seen;
                half4 _Hidden;
                half4 _Fill;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float eyeDepth : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            struct Masks { half4 band : SV_Target0; half4 fill : SV_Target1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.eyeDepth = -TransformWorldToView(positionWS).z;
                output.positionWS = positionWS;
                return output;
            }

            Masks Frag(Varyings input)
            {
                half hidden = BehindSomething(input.positionCS, input.eyeDepth) ? 1.0h : SmokeCover(input.positionWS);
                half4 band = hidden >= 0.5h ? _Hidden : _Seen;
                Masks output;
                output.band = half4(band.rgb, band.a > 0.001h ? 1.0h + saturate(band.a) : 0.5h);
                half fill = saturate(_Fill.a) * hidden;
                output.fill = half4(_Fill.rgb * fill, fill);
                return output;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
