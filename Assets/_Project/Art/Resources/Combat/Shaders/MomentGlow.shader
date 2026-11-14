// A body glowing in a moment's colour (BodyShell over everyone a CHARGE took, ChargeGlow): light added over the body,
// strongest at its edges as the eye sees them and faint over its face, pushed out a hair along the normals so it reads
// as a glow round the outline. Additive, so it only ever brightens; per renderer values come through a property block.
// Two passes, as RevealThroughWalls draws its silhouettes (URP draws SRPDefaultUnlit before UniversalForward for each
// object):
// - where the body is in sight, the glow, and those pixels marked in the stencil so the body never shows through itself
//   (an arm behind the chest);
// - where something else stands in front of it, _Through of the same glow, each pixel once: a friend's outline through a
//   wall (the user, 2026-11-14, of the CHARGE: "노란 윤곽선과 좀 덜 해진 노란 빛으로 아군을 벽 넘어서 볼 수 있게끔").
// _Near clears it within that distance of the eye, and it comes back over _NearWidth past it: a player's own hands and gun
// in front of their eyes stay plain to aim past ("이건 조준이 아예 안 되잖아 노래져서").
Shader "Hidden/ENFILADE/MomentGlow"
{
    Properties
    {
        _Color ("Glow colour (above 1 blooms)", Color) = (1, 0.8, 0.22, 1)
        _Strength ("Strength (0 off)", Float) = 1
        _Rim ("How tight to the edge (power)", Float) = 2.2
        _Fill ("Share of the glow over the whole body (the rest at the edge)", Range(0, 1)) = 0.18
        _Inflate ("Pushed out along the normals (m)", Float) = 0.006
        _Through ("Share of it seen through what stands in front (0 none)", Range(0, 1)) = 0
        _Near ("None this close to the eye (m, 0 everywhere)", Float) = 0
        _NearWidth ("Back to full over this much farther (m)", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Strength;
            half _Rim;
            half _Fill;
            float _Inflate;
            half _Through;
            float _Near;
            float _NearWidth;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + normalWS * _Inflate;
            output.positionCS = TransformWorldToHClip(positionWS);
            output.normalWS = normalWS;
            // From the surface to the eye, not normalised: its length is how far the eye is.
            output.viewWS = GetWorldSpaceViewDir(positionWS);
            return output;
        }

        half3 Glow(Varyings input)
        {
            half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
            half edge = pow(1.0h - facing, max(0.1h, _Rim));
            half glow = (_Fill + edge * (1.0h - _Fill)) * _Strength;
            if (_Near > 0.0) glow *= (half)saturate((length(input.viewWS) - _Near) / max(0.001, _NearWidth));
            return _Color.rgb * glow;
        }
        ENDHLSL

        // In sight: the glow, and the pixel marked (stencil bit 32; RevealThroughWalls keeps to bit 64).
        Pass
        {
            Name "GlowSeen"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Stencil { Ref 32 ReadMask 32 WriteMask 32 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return half4(Glow(input), 0); }
            ENDHLSL
        }

        // Behind something that isn't a glowing body already drawn there: _Through of the glow, then the pixel marked so
        // overlapping parts (clothes over skin) don't stack.
        Pass
        {
            Name "GlowThrough"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Greater
            Cull Back
            Stencil { Ref 32 ReadMask 32 WriteMask 32 Comp NotEqual Pass Replace }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                clip(_Through - 0.001h);
                return half4(Glow(input) * _Through, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
