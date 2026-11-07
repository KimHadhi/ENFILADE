Shader "KINEMATION/Gameplay Framework/Shooter Core/Reflex Scope"
{
    Properties
    {
        [NoScaleOffset] _Reticle("Reticle", 2D) = "white" {}
        [HDR] _EmissiveColor("Emissive Color", Color) = (1, 1, 1, 1)
        _ReticleDepth("Reticle Depth", Float) = -0.5
        _ReticleSize("Reticle Size", Vector) = (0.45, 0.45, 0, 0)
        _ReticleUVOffset("Reticle UV Offset", Vector) = (0, 0, 0, 0)
        _MaskSize("Mask Size", Range(0, 0.5)) = 0.45
        _MaskSoftness("Mask Softness", Range(0, 1)) = 0.05
        _AlphaCutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5

        [Header(Glass)]
        _GlassStrength("Glass Strength", Range(0, 1)) = 1
        _GlassTint("Glass Tint", Color) = (0.12, 0.18, 0.2, 1)
        _GlassOpacity("Glass Opacity", Range(0, 1)) = 0.12
        _GlassSmoothness("Glass Smoothness", Range(0, 1)) = 0.9
        _GlassVignetteSize("Glass Vignette Size", Range(0, 1)) = 0.2
        _GlassVignetteSoftness("Glass Vignette Softness", Range(0.001, 0.5)) = 0.05
        _GlassVignetteIntensity("Glass Vignette Intensity", Range(0, 1)) = 0.4

        [HideInInspector] _BlendMode("_BlendMode", Float) = 0
        [HideInInspector] _EnableBlendModePreserveSpecularLighting("_EnableBlendModePreserveSpecularLighting", Float) = 1
        [HideInInspector] _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        [HideInInspector] _MainTex("Main Texture", 2D) = "white" {}
        [HideInInspector] _Color("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.high-definition"
        }

        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "HDLitShader"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags
            {
                "LightMode" = "Forward"
            }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment SCREEN_SPACE_SHADOWS_OFF SCREEN_SPACE_SHADOWS_ON
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH

            #define _SURFACE_TYPE_TRANSPARENT
            #define _DISABLE_DECALS
            #define SUPPORT_BLENDMODE_PRESERVE_SPECULAR_LIGHTING
            #define USE_CLUSTERED_LIGHTLIST
            #define SHADERPASS SHADERPASS_FORWARD
            #define HAS_LIGHTLOOP

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"

            TEXTURE2D(_Reticle);
            SAMPLER(sampler_Reticle);

            CBUFFER_START(UnityPerMaterial)
                float4 _EmissiveColor;
                float4 _ReticleSize;
                float4 _ReticleUVOffset;
                float4 _GlassTint;
                float _ReticleDepth;
                float _MaskSize;
                float _MaskSoftness;
                float _AlphaCutoff;
                float _GlassStrength;
                float _GlassOpacity;
                float _GlassSmoothness;
                float _GlassVignetteSize;
                float _GlassVignetteSoftness;
                float _GlassVignetteIntensity;
                float _BlendMode;
                float _EnableBlendModePreserveSpecularLighting;
            CBUFFER_END

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"

            float2 ReticleAspectScale(FragInputs input)
            {
                float3 positionDx = ddx(input.positionRWS);
                float3 positionDy = ddy(input.positionRWS);
                float2 uvDx = ddx(input.texCoord0.xy);
                float2 uvDy = ddy(input.texCoord0.xy);
                float determinant = uvDx.x * uvDy.y - uvDx.y * uvDy.x;
                float inverseDeterminant = sign(determinant) * rcp(max(abs(determinant), 1.0e-8));
                float3 positionPerU = (positionDx * uvDy.y - positionDy * uvDx.y) * inverseDeterminant;
                float3 positionPerV = (positionDy * uvDx.x - positionDx * uvDy.x) * inverseDeterminant;
                float2 uvWorldSize = max(float2(length(positionPerU), length(positionPerV)), 1.0e-5);
                return uvWorldSize / max(min(uvWorldSize.x, uvWorldSize.y), 1.0e-5);
            }

            float2 ReticleUV(FragInputs input, float3 viewDirectionWS)
            {
                float3 viewDirectionTS = float3(
                    dot(viewDirectionWS, normalize(input.tangentToWorld[0])),
                    dot(viewDirectionWS, normalize(input.tangentToWorld[1])),
                    dot(viewDirectionWS, normalize(input.tangentToWorld[2])));
                float2 centeredUV = (input.texCoord0.xy - 0.5) * ReticleAspectScale(input);
                return (centeredUV + viewDirectionTS.xy * _ReticleDepth)
                    / max(abs(_ReticleSize.xy), 1.0e-5) + 0.5 + _ReticleUVOffset.xy;
            }

            float SoftMask(float2 uv)
            {
                float softness = max(_MaskSoftness, 1.0e-5);
                float2 fadedEdges = smoothstep(_MaskSize - softness, _MaskSize, abs(uv - 0.5));
                return saturate((1.0 - fadedEdges.x) * (1.0 - fadedEdges.y));
            }

            float GlassVignette(float2 uv)
            {
                const float vignetteEpsilon = 1.0e-4;
                float vignetteCoverage = saturate(_GlassVignetteSize);

                if (vignetteCoverage <= vignetteEpsilon)
                {
                    return 0.0;
                }

                if (vignetteCoverage >= 1.0 - vignetteEpsilon)
                {
                    return 1.0;
                }

                float2 surfaceUV = abs(uv * 2.0 - 1.0);
                float softness = clamp(_GlassVignetteSoftness, vignetteEpsilon, 1.0);
                float feather = min(softness, vignetteCoverage);
                float axisBlend = saturate(
                    0.5 + 0.5 * (surfaceUV.x - surfaceUV.y) / feather);
                float surfaceShape = lerp(surfaceUV.y, surfaceUV.x, axisBlend)
                    + feather * axisBlend * (1.0 - axisBlend);

                return smoothstep(
                    1.0 - vignetteCoverage,
                    1.0 - vignetteCoverage + feather,
                    surfaceShape);
            }

            void GetSurfaceAndBuiltinData(
                FragInputs input,
                float3 viewDirectionWS,
                inout PositionInputs posInput,
                out SurfaceData surfaceData,
                out BuiltinData builtinData)
            {
                float4 reticle = SAMPLE_TEXTURE2D(_Reticle, sampler_Reticle, ReticleUV(input, viewDirectionWS));
                float sampledReticleAlpha = saturate(reticle.a * SoftMask(input.texCoord0.xy));
                float reticleAlpha = sampledReticleAlpha * step(_AlphaCutoff, sampledReticleAlpha);
                float glassStrength = saturate(_GlassStrength);
                if (glassStrength <= 0.0) clip(reticleAlpha - 1.0e-5);
                float vignette = GlassVignette(input.texCoord0.xy) * saturate(_GlassVignetteIntensity);
                float glassAlpha = lerp(saturate(_GlassOpacity), 1.0, vignette) * glassStrength;
                float glassWeight = glassStrength * (1.0 - reticleAlpha);
                float opacity = reticleAlpha + glassAlpha * (1.0 - reticleAlpha);
                float3 normalWS = normalize(input.tangentToWorld[2]);

                ZERO_INITIALIZE(SurfaceData, surfaceData);
                surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD
                    | MATERIALFEATUREFLAGS_LIT_SPECULAR_COLOR;
                surfaceData.baseColor = _GlassTint.rgb * (1.0 - vignette) * glassWeight;
                surfaceData.specularOcclusion = glassWeight;
                surfaceData.normalWS = normalWS;
                surfaceData.perceptualSmoothness = saturate(_GlassSmoothness);
                surfaceData.ambientOcclusion = 1.0;
                surfaceData.metallic = 0.0;
                surfaceData.specularColor = 0.04 * glassWeight;
                surfaceData.tangentWS = normalize(input.tangentToWorld[0]);
                surfaceData.geomNormalWS = normalWS;
                surfaceData.ior = 1.5;
                surfaceData.transmittanceColor = 1.0;
                surfaceData.atDistance = 1.0;

                InitBuiltinData(posInput, opacity, normalWS, -normalWS, input.texCoord1, input.texCoord2, builtinData);
                builtinData.emissiveColor = reticle.rgb * _EmissiveColor.rgb * reticleAlpha;
                PostInitBuiltinData(viewDirectionWS, posInput, surfaceData, builtinData);
            }

            #include_with_pragmas "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
            ENDHLSL
        }
    }

    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.universal"
        }

        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "UniversalForward"
            Tags
            {
                "LightMode" = "UniversalForward"
            }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ScopeVertex
            #pragma fragment ScopeFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS

            #define _SURFACE_TYPE_TRANSPARENT
            // URP 17 evaluates this keyword as a numeric preprocessor condition.
            #define _ALPHAPREMULTIPLY_ON 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D (_Reticle);
            SAMPLER (sampler_Reticle);

            CBUFFER_START (UnityPerMaterial)
            float4 _EmissiveColor;
            float4 _ReticleSize;
            float4 _ReticleUVOffset;
            float4 _GlassTint;
            float _ReticleDepth;
            float _MaskSize;
            float _MaskSoftness;
            float _AlphaCutoff;
            float _GlassStrength;
            float _GlassOpacity;
            float _GlassSmoothness;
            float _GlassVignetteSize;
            float _GlassVignetteSoftness;
            float _GlassVignetteIntensity;
            float _BlendMode;
            float _EnableBlendModePreserveSpecularLighting;
            CBUFFER_END

            struct ScopeAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ScopeVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half3 vertexLighting : TEXCOORD5;
                float4 shadowCoord : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ScopeVaryings ScopeVertex(ScopeAttributes input)
            {
                ScopeVaryings output = (ScopeVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.uv = input.uv;
                output.normalWS = normals.normalWS;
                output.tangentWS = half4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.vertexLighting = VertexLighting(positions.positionWS, normals.normalWS);
                output.shadowCoord = GetShadowCoord(positions);
                return output;
            }

            float2 ReticleAspectScaleURP(ScopeVaryings input)
            {
                float3 positionDx = ddx(input.positionWS);
                float3 positionDy = ddy(input.positionWS);
                float2 uvDx = ddx(input.uv);
                float2 uvDy = ddy(input.uv);
                float determinant = uvDx.x * uvDy.y - uvDx.y * uvDy.x;
                float inverseDeterminant = sign(determinant) * rcp(max(abs(determinant), 1.0e-8));
                float3 positionPerU = (positionDx * uvDy.y - positionDy * uvDx.y) * inverseDeterminant;
                float3 positionPerV = (positionDy * uvDx.x - positionDx * uvDy.x) * inverseDeterminant;
                float2 uvWorldSize = max(float2(length(positionPerU), length(positionPerV)), 1.0e-5);
                return uvWorldSize / max(min(uvWorldSize.x, uvWorldSize.y), 1.0e-5);
            }

            float2 ReticleUVURP(ScopeVaryings input, float3 viewDirectionWS)
            {
                float3 normalWS = normalize(input.normalWS);
                float3 tangentWS = normalize(input.tangentWS.xyz);
                float3 bitangentWS = normalize(cross(normalWS, tangentWS) * input.tangentWS.w);
                float3 viewDirectionTS = float3(
                    dot(viewDirectionWS, tangentWS),
                    dot(viewDirectionWS, bitangentWS),
                    dot(viewDirectionWS, normalWS));
                float2 centeredUV = (input.uv - 0.5) * ReticleAspectScaleURP(input);
                return (centeredUV + viewDirectionTS.xy * _ReticleDepth)
                    / max(abs(_ReticleSize.xy), 1.0e-5) + 0.5 + _ReticleUVOffset.xy;
            }

            float GlassVignette(float2 uv)
            {
                const float vignetteEpsilon = 1.0e-4;
                float vignetteCoverage = saturate(_GlassVignetteSize);

                if (vignetteCoverage <= vignetteEpsilon)
                {
                    return 0.0;
                }

                if (vignetteCoverage >= 1.0 - vignetteEpsilon)
                {
                    return 1.0;
                }

                float2 surfaceUV = abs(uv * 2.0 - 1.0);
                float softness = clamp(_GlassVignetteSoftness, vignetteEpsilon, 1.0);
                float feather = min(softness, vignetteCoverage);
                float axisBlend = saturate(
                    0.5 + 0.5 * (surfaceUV.x - surfaceUV.y) / feather);
                float surfaceShape = lerp(surfaceUV.y, surfaceUV.x, axisBlend)
                    + feather * axisBlend * (1.0 - axisBlend);

                return smoothstep(
                    1.0 - vignetteCoverage,
                    1.0 - vignetteCoverage + feather,
                    surfaceShape);
            }

            half4 ScopeFragment(ScopeVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half4 reticle = SAMPLE_TEXTURE2D(_Reticle, sampler_Reticle, ReticleUVURP(input, viewDirectionWS));
                float2 fadedEdges = smoothstep(_MaskSize - max(_MaskSoftness, 1.0e-5), _MaskSize, abs(input.uv - 0.5));
                float sampledReticleAlpha = saturate(reticle.a * (1.0 - fadedEdges.x) * (1.0 - fadedEdges.y));
                float reticleAlpha = sampledReticleAlpha * step(_AlphaCutoff, sampledReticleAlpha);
                half3 reticleColor = reticle.rgb * _EmissiveColor.rgb * reticleAlpha;
                float glassStrength = saturate(_GlassStrength);
                if (glassStrength <= 0.0) return half4(reticleColor, reticleAlpha);
                float vignette = GlassVignette(input.uv) * saturate(_GlassVignetteIntensity);
                float glassAlpha = lerp(saturate(_GlassOpacity), 1.0, vignette);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = _GlassTint.rgb * (1.0 - vignette);
                surfaceData.metallic = 0.0;
                surfaceData.specular = 0.04;
                surfaceData.smoothness = saturate(_GlassSmoothness);
                surfaceData.normalTS = half3(0.0, 0.0, 1.0);
                surfaceData.emission = 0.0;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = glassAlpha;
                surfaceData.clearCoatMask = 0.0;
                surfaceData.clearCoatSmoothness = 0.0;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = SafeNormalize(viewDirectionWS);
                inputData.shadowCoord = input.shadowCoord;
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = input.vertexLighting;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color *= glassStrength * (1.0 - reticleAlpha);
                color.rgb += reticleColor;
                color.a += reticleAlpha;
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
