// Drawn on a copy of an enemy's mesh near the end of a wave, and only where something stands in front of it (ZTest
// Greater): the last enemies show through walls as flat silhouettes. EnemyReveal makes and removes the copies.
// A body covers parts of itself (an arm across the chest), so first its own seen surface is marked in the stencil, and
// the silhouette never shows there; then each pixel is drawn once, so overlapping layers (arm over chest, clothes over
// skin) don't stack into darker patches. URP draws SRPDefaultUnlit before UniversalForward for each object.
// What counts as hidden is SeeThrough.hlsl's rule, the same for every see-through drawing: something must stand clearly in
// front (more than the table's gap), and never the viewer's own first-person body, gun or scope lens (stencil bit 128,
// OwnViewMask). Smoke hides as a wall does: where the look to the body's seen surface runs through a cloud, the first
// pass draws the silhouette there too, over the smoke.
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
        #include "SeeThrough.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings { float4 positionCS : SV_POSITION; float eyeDepth : TEXCOORD0; float3 positionWS : TEXCOORD1; };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.eyeDepth = -TransformWorldToView(positionWS).z;
            output.positionWS = positionWS;
            return output;
        }
        ENDHLSL

        // Where the body itself is seen: its front surface, which the real body already drew at the same depth. It is
        // marked in the stencil so the hidden pass leaves it alone. Seen through smoke it is hidden all the same (smoke
        // writes no depth and is drawn before this, so the body and its outline are under the puffs): there the same
        // flat silhouette is drawn over the smoke, as much as the smoke hides (SmokeCover; nothing where no smoke is on
        // the way). Each pixel once (64 is not yet set), so overlapping layers of the body don't stack here either.
        Pass
        {
            Name "RevealSeen"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back
            Stencil { Ref 64 ReadMask 64 WriteMask 64 Comp NotEqual Pass Replace }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                return half4(_Color.rgb, _Color.a * SmokeCover(input.positionWS));
            }
            ENDHLSL
        }

        // Hidden behind something that isn't the body: one flat layer, which then marks the pixel as drawn. The stencil
        // test passes only where neither bit 64 (seen or drawn already) nor bit 128 (the viewer's own first-person things)
        // is set: 64 > (stencil & 192).
        Pass
        {
            Name "Reveal"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Back
            Stencil { Ref 64 ReadMask 192 WriteMask 64 Comp Greater Pass Replace }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                if (!BehindSomething(input.positionCS, input.eyeDepth)) discard;
                return _Color;
            }
            ENDHLSL
        }
    }
}
