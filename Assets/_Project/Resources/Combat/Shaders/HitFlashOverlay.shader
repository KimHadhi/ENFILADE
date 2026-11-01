// Drawn as an extra material over an enemy's own mesh while it reacts to a hit or is marked.
// Whole-body tint (Minecraft-style red), a red rim, up to four glowing hit spots, a white kill flash, and an outline.
// Premultiplied output: tint and flash cover, glow adds light. Properties are driven by HitFlashRenderer.
Shader "Hidden/Morphang/HitFlashOverlay"
{
    Properties
    {
        _TintColor ("Tint Color", Color) = (1, 0.08, 0.04, 0.55)
        _Tint ("Tint", Range(0, 1)) = 0
        _RimColor ("Rim Color", Color) = (1, 0.22, 0.1, 1)
        _Rim ("Rim", Range(0, 1)) = 0
        _GlowColor ("Glow Color", Color) = (1, 0.45, 0.18, 1)
        _HitRadius ("Hit Radius", Float) = 0.32
        _Hit0 ("Hit 0", Vector) = (0, 0, 0, 0)
        _Hit1 ("Hit 1", Vector) = (0, 0, 0, 0)
        _Hit2 ("Hit 2", Vector) = (0, 0, 0, 0)
        _Hit3 ("Hit 3", Vector) = (0, 0, 0, 0)
        _White ("White Flash", Range(0, 1)) = 0
        [HDR] _OutlineColor ("Outline Color", Color) = (1.6, 0.07, 0.12, 1)
        _OutlineWidth ("Outline Width (px at 1080p)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _TintColor;
            half _Tint;
            half4 _RimColor;
            half _Rim;
            half4 _GlowColor;
            float _HitRadius;
            float4 _Hit0, _Hit1, _Hit2, _Hit3;
            half _White;
            float4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        ENDHLSL

        Pass
        {
            Name "HitFlash"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

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

            // w is the spot's current strength; the falloff is soft so the spot reads as heat, not a decal.
            half Spot(float4 hit, float3 positionWS)
            {
                float d = distance(positionWS, hit.xyz) / max(0.001, _HitRadius);
                half falloff = saturate(1.0 - d);
                return hit.w * falloff * falloff * (3.0 - 2.0 * falloff);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = pow(1.0 - saturate(dot(normal, view)), 2.5);

                half tint = saturate(_Tint * _TintColor.a);
                half rim = saturate(fresnel * _Rim);
                half alpha = saturate(tint + rim * (1.0 - tint));
                half3 color = _TintColor.rgb * tint + _RimColor.rgb * rim * (1.0 - tint);

                half glow = Spot(_Hit0, input.positionWS) + Spot(_Hit1, input.positionWS)
                    + Spot(_Hit2, input.positionWS) + Spot(_Hit3, input.positionWS);
                // The hot centre whitens a little so a single spot is visible even over the red tint.
                color += _GlowColor.rgb * glow * 1.8 + saturate(glow - 0.6) * 0.8;
                alpha = saturate(alpha + glow * 0.35);

                color = lerp(color, half3(1.0, 1.0, 1.0), _White);
                alpha = max(alpha, _White);
                return half4(color, alpha);
            }
            ENDHLSL
        }

        // The body's back faces pushed out along their normals: only the rim past the silhouette survives the depth test.
        // URP runs this LightMode as well as the one above, so one material draws both passes.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(Attributes input) : SV_POSITION
            {
                if (_OutlineWidth <= 0.0) return float4(0.0, 0.0, 0.0, 0.0);
                float4 position = TransformObjectToHClip(input.positionOS.xyz);
                float2 normal = TransformWorldToHClipDir(TransformObjectToWorldNormal(input.normalOS)).xy;
                // Same width on screen at any distance or resolution.
                float2 pixel = float2(_ScaledScreenParams.y / _ScaledScreenParams.x, 1.0) * 2.0 / 1080.0;
                position.xy += normalize(normal + float2(0.00001, 0.0)) * pixel * _OutlineWidth * position.w;
                return position;
            }

            half4 Frag() : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
