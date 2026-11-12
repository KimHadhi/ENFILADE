// A body glowing in a moment's colour (BodyShell over everyone a CHARGE took, ChargeGlow): light added over the body,
// strongest at its edges as the eye sees them and faint over its face, pushed out a hair along the normals so it reads
// as a glow round the outline. Additive, so it only ever brightens; per renderer values come through a property block.
Shader "Hidden/ENFILADE/MomentGlow"
{
    Properties
    {
        _Color ("Glow colour (above 1 blooms)", Color) = (1, 0.8, 0.22, 1)
        _Strength ("Strength (0 off)", Float) = 1
        _Rim ("How tight to the edge (power)", Float) = 2.2
        _Fill ("Share of the glow over the whole body (the rest at the edge)", Range(0, 1)) = 0.18
        _Inflate ("Pushed out along the normals (m)", Float) = 0.006
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Strength;
                half _Rim;
                half _Fill;
                float _Inflate;
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
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half edge = pow(1.0h - facing, max(0.1h, _Rim));
                half glow = (_Fill + edge * (1.0h - _Fill)) * _Strength;
                return half4(_Color.rgb * glow, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
