// Drawn over a downed teammate so anyone can find them: a soft glow on the body and a bright outline, seen through walls.
// The body marks its silhouette in the stencil, so the outline only shows past it. Properties are driven by DownedAura.
Shader "Hidden/ENFILADE/TeammateAura"
{
    Properties
    {
        [HDR] _AuraColor ("Aura Color", Color) = (1, 0.7, 0.2, 1)
        _Rim ("Rim", Range(0, 1)) = 0.5
        _OutlineWidth ("Outline Width (px at 1080p)", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _AuraColor;
            half _Rim;
            float _OutlineWidth;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        ENDHLSL

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Back
            Stencil { Ref 97 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = pow(1.0 - saturate(dot(normal, view)), 2.0);
                return half4(_AuraColor.rgb * (0.12 + fresnel) * _Rim, 0.0);
            }
            ENDHLSL
        }

        // The body's back faces pushed out along their normals. Without a depth test to hide the middle, the stencil does it.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            Stencil { Ref 97 Comp NotEqual }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(Attributes input) : SV_POSITION
            {
                float4 position = TransformObjectToHClip(input.positionOS.xyz);
                float2 normal = TransformWorldToHClipDir(TransformObjectToWorldNormal(input.normalOS)).xy;
                // Same width on screen at any distance or resolution.
                float2 pixel = float2(_ScaledScreenParams.y / _ScaledScreenParams.x, 1.0) * 2.0 / 1080.0;
                position.xy += normalize(normal + float2(0.00001, 0.0)) * pixel * _OutlineWidth * position.w;
                return position;
            }

            half4 Frag() : SV_Target
            {
                return half4(_AuraColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
