// Marks the pixels where this PC's own player stands in front of the world on their own screen (stencil bit 128). It is
// drawn on OwnViewMask's copies of the player's solid meshes, after the world and before anything see-through, so no
// see-through pass (SeeThrough.hlsl) takes the player's own body, gun or scope lens for the wall a foe stands behind.
// Nothing is drawn, neither colour nor depth. _Skip leaves out a part one looks through (a sight's glass), so a foe
// behind it still shows.
Shader "Hidden/ENFILADE/OwnViewMask"
{
    Properties
    {
        _Skip ("Leave this part out (1: a part one looks through)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+1" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "OwnViewMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Off
            Stencil { Ref 128 WriteMask 128 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Skip;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(0.5h - _Skip);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
