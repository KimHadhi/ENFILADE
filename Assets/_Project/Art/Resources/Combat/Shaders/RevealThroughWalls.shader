// Drawn on a copy of an enemy's mesh near the end of a wave, and only where something stands in front of it (ZTest
// Greater): the last enemies show through walls as flat silhouettes. EnemyReveal makes and removes the copies.
// A body covers parts of itself (an arm across the chest), so first its own seen surface is marked in the stencil, and
// the silhouette never shows there; then each pixel is drawn once, so overlapping layers (arm over chest, clothes over
// skin) don't stack into darker patches. URP draws SRPDefaultUnlit before UniversalForward for each object.
Shader "Hidden/ENFILADE/RevealThroughWalls"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.2, 0.15, 0.63)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings { float4 positionCS : SV_POSITION; };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            return output;
        }
        ENDHLSL

        // Where the body itself is seen: its front surface, which the real body already drew at the same depth.
        Pass
        {
            Name "RevealSeen"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back
            Stencil { Ref 64 ReadMask 64 WriteMask 64 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // Hidden behind something that isn't the body: one flat layer, which then marks the pixel as drawn.
        Pass
        {
            Name "Reveal"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Back
            Stencil { Ref 64 ReadMask 64 WriteMask 64 Comp NotEqual Pass Replace }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
