// Marks the pixels where this PC's own player stands in front of the world on their own screen (stencil bit 128). OwnViewMask
// draws it with the player's own renderers, on their solid submeshes only, after the opaque world and before anything
// transparent or see-through, so no see-through pass (SeeThrough.hlsl) takes the player's own body, gun or scope lens for
// the wall a foe stands behind. Nothing is drawn, neither colour nor depth. A part one looks through (a sight's glass) is
// never drawn with it, so a foe behind it still shows. The material sits on no renderer: its one pass is drawn from a
// command buffer (DrawRenderer), so the queue and light mode tags below do not decide when it draws.
Shader "Hidden/ENFILADE/OwnViewMask"
{
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
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
