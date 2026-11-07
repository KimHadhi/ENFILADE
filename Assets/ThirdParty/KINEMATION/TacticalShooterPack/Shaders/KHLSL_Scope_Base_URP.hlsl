#ifndef KINEMATION_SCOPE_BASE_URP_INCLUDED
#define KINEMATION_SCOPE_BASE_URP_INCLUDED

#if defined(SCOPE_URP_SKY_PASS)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

struct ScopeSkyVaryings
{
    float4 positionCS : SV_POSITION;
};

ScopeSkyVaryings ScopeSkyVertex(uint vertexID : SV_VertexID)
{
    ScopeSkyVaryings output;
    output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
    return output;
}

half4 ScopeSkyFragment(ScopeSkyVaryings input) : SV_Target
{
    float2 uv = input.positionCS.xy / _ScreenParams.xy;
#if UNITY_REVERSED_Z
float depth = 0.0;
#else
float depth = 1.0;
#endif
float3 positionWS = ComputeWorldSpacePosition(
    uv,
    depth,
    UNITY_MATRIX_I_VP);
float3 rayDirectionWS = normalize(positionWS - _WorldSpaceCameraPos);
half4 encodedSky = half4(SAMPLE_TEXTURECUBE_LOD(
    _GlossyEnvironmentCubeMap,
    sampler_GlossyEnvironmentCubeMap,
    rayDirectionWS,
    0.0));
    return half4 (
    DecodeHDREnvironment(encodedSky, _GlossyEnvironmentCubeMap_HDR),
        1.0);
}
#elif defined(SCOPE_URP_FOG_PASS)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct ScopeFogVaryings
{
    float4 positionCS : SV_POSITION;
};

ScopeFogVaryings ScopeFogVertex(uint vertexID : SV_VertexID)
{
    ScopeFogVaryings output;
    output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
    return output;
}

half4 ScopeFogFragment(ScopeFogVaryings input) : SV_Target
{
    float rawDepth = _ScopeDepthTexture.Load(int3(input.positionCS.xy, 0));
    if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE)
        return half4(0.0, 0.0, 0.0, 1.0);

    float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float nearToFarDepth = max(eyeDepth - _ProjectionParams.y, 0.0);
    half fogFactor = ComputeFogFactorZ0ToFar(nearToFarDepth);

    half3 fogColor = MixFog(half3(0.0, 0.0, 0.0), fogFactor);
    half3 foggedWhite = MixFog(half3(1.0, 1.0, 1.0), fogFactor);
    half transmittance = saturate(
        (foggedWhite.x + foggedWhite.y + foggedWhite.z
            - fogColor.x - fogColor.y - fogColor.z) / 3.0);
    return half4(fogColor, transmittance);
}
#elif defined(SCOPE_URP_FORWARD_PASS)
#define _SPECULAR_SETUP 1

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float4 SampleScopeSource(float2 renderTextureUv)
{
    return _RenderTexture.Sample(sampler_RenderTexture, renderTextureUv);
}

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
    VertexNormalInputs normals = GetVertexNormalInputs(
        input.normalOS,
        input.tangentOS);
    output.positionCS = positions.positionCS;
    output.positionWS = positions.positionWS;
    output.uv = input.uv;
    output.normalWS = normals.normalWS;
    output.tangentWS = half4(
        normals.tangentWS,
        input.tangentOS.w * GetOddNegativeScale());
    output.fogFactor = ComputeFogFactor(positions.positionCS.z);
    output.vertexLighting = VertexLighting(
        positions.positionWS,
        normals.normalWS);
    output.shadowCoord = GetShadowCoord(positions);
    return output;
}

OpticContext GetURPOpticContext(ScopeVaryings input)
{
    OpticContext context;
    context.uv = input.uv;
    context.surfacePosWS = input.positionWS;
    context.surfaceNormalWS = normalize(input.normalWS);
    context.surfaceTangentWS = normalize(input.tangentWS.xyz);
    context.lensOriginWS = TransformObjectToWorld(_LensCenterOS.xyz);
    context.lensForwardWS = dot(_LensNormalOS.xyz, _LensNormalOS.xyz) > 1.0e-6
                                ? normalize(TransformObjectToWorldNormal(_LensNormalOS.xyz))
                                : -context.surfaceNormalWS;
    context.fallbackRightWS = normalize(
        TransformObjectToWorldDir(float3(1.0, 0.0, 0.0)));
    context.cameraPosWS = GetCameraPositionWS();

    float3 apertureRightOS;
    float3 apertureUpOS;
    float apertureRadiusOS;
    GetObjectApertureGeometry(
        context.uv,
        TransformWorldToObject(context.surfacePosWS),
        TransformWorldToObjectNormal(context.surfaceNormalWS),
        TransformWorldToObject(context.cameraPosWS),
        context.apertureUv,
        apertureRightOS,
        apertureUpOS,
        apertureRadiusOS,
        context.useAuthoredAperture);
    float3 apertureRightEdgeWS = TransformObjectToWorld(
        _LensCenterOS.xyz + (apertureRightOS * apertureRadiusOS));
    float3 apertureUpEdgeWS = TransformObjectToWorld(
        _LensCenterOS.xyz + (apertureUpOS * apertureRadiusOS));
    context.authoredApertureDiameterWS = 2.0 * float2(
        length(apertureRightEdgeWS - context.lensOriginWS),
        length(apertureUpEdgeWS - context.lensOriginWS));
    return context;
}

half4 ScopeFragment(ScopeVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    OpticContext context = GetURPOpticContext(input);
    ClipLens(context.apertureUv);
    float glassImperfection = GetGlassImperfection(context.apertureUv);
    float scopeVisibility;
    float3 lensRightWS;
    float3 lensUpWS;
    float3 scopeEmission = EvaluateScopeEmission(
        context,
        1.0,
        glassImperfection,
        scopeVisibility,
        lensRightWS,
        lensUpWS);
    GlassSurface glass = EvaluateGlassSurface(
        context,
        scopeVisibility,
        glassImperfection,
        lensRightWS,
        lensUpWS);

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = glass.baseColor;
    surfaceData.metallic = 0.0;
    surfaceData.specular = glass.specularColor;
    surfaceData.smoothness = glass.smoothness;
    surfaceData.normalTS = half3(0.0, 0.0, 1.0);
    surfaceData.emission = scopeEmission;
    surfaceData.occlusion = 1.0;
    surfaceData.alpha = 1.0;
    surfaceData.clearCoatMask = 0.0;
    surfaceData.clearCoatSmoothness = 0.0;

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalWS = glass.normalWS;
    inputData.viewDirectionWS = SafeNormalize(
        context.cameraPosWS - context.surfacePosWS);
    inputData.shadowCoord = input.shadowCoord;
    inputData.fogCoord = input.fogFactor;
    inputData.vertexLighting = VertexLighting(
        input.positionWS,
        glass.normalWS);
    inputData.bakedGI = SampleSH(inputData.normalWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(
        input.positionCS);
    inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    return color;
}
#elif defined(SCOPE_URP_DEPTH_PASS)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct ScopeDepthAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ScopeDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionOS : TEXCOORD1;
    half3 normalOS : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

ScopeDepthVaryings ScopeDepthVertex(ScopeDepthAttributes input)
{
    ScopeDepthVaryings output = (ScopeDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv = input.uv;
    output.positionOS = input.positionOS.xyz;
    output.normalOS = input.normalOS;
    return output;
}

half4 ScopeDepthFragment(ScopeDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 apertureUv;
    float3 apertureRightOS;
    float3 apertureUpOS;
    float apertureRadiusOS;
    float useAuthoredAperture;
    GetObjectApertureGeometry(
        input.uv,
        input.positionOS,
        input.normalOS,
        TransformWorldToObject(GetCameraPositionWS()),
        apertureUv,
        apertureRightOS,
        apertureUpOS,
        apertureRadiusOS,
        useAuthoredAperture);
    ClipLens(apertureUv);
    return 0.0;
}
#elif defined(SCOPE_URP_DEPTH_NORMALS_PASS)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct ScopeDepthNormalsAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ScopeDepthNormalsVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

ScopeDepthNormalsVaryings ScopeDepthNormalsVertex(
    ScopeDepthNormalsAttributes input)
{
    ScopeDepthNormalsVaryings output = (ScopeDepthNormalsVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs positions = GetVertexPositionInputs(
        input.positionOS.xyz);
    output.positionCS = positions.positionCS;
    output.positionWS = positions.positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv = input.uv;
    return output;
}

half4 ScopeDepthNormalsFragment(
    ScopeDepthNormalsVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    OpticContext context;
    context.uv = input.uv;
    context.surfacePosWS = input.positionWS;
    context.surfaceNormalWS = normalize(input.normalWS);
    context.surfaceTangentWS = 0.0;
    context.lensOriginWS = TransformObjectToWorld(_LensCenterOS.xyz);
    context.lensForwardWS = dot(_LensNormalOS.xyz, _LensNormalOS.xyz) > 1.0e-6
                                ? normalize(TransformObjectToWorldNormal(_LensNormalOS.xyz))
                                : -context.surfaceNormalWS;
    context.fallbackRightWS = normalize(
        TransformObjectToWorldDir(float3(1.0, 0.0, 0.0)));
    context.cameraPosWS = GetCameraPositionWS();

    float3 apertureRightOS;
    float3 apertureUpOS;
    float apertureRadiusOS;
    GetObjectApertureGeometry(
        context.uv,
        TransformWorldToObject(context.surfacePosWS),
        TransformWorldToObjectNormal(context.surfaceNormalWS),
        TransformWorldToObject(context.cameraPosWS),
        context.apertureUv,
        apertureRightOS,
        apertureUpOS,
        apertureRadiusOS,
        context.useAuthoredAperture);
    float3 apertureRightEdgeWS = TransformObjectToWorld(
        _LensCenterOS.xyz + (apertureRightOS * apertureRadiusOS));
    float3 apertureUpEdgeWS = TransformObjectToWorld(
        _LensCenterOS.xyz + (apertureUpOS * apertureRadiusOS));
    context.authoredApertureDiameterWS = 2.0 * float2(
        length(apertureRightEdgeWS - context.lensOriginWS),
        length(apertureUpEdgeWS - context.lensOriginWS));
    ClipLens(context.apertureUv);
    float glassImperfection = GetGlassImperfection(context.apertureUv);
    float3 normalWS = EvaluateGlassSurface(
        context,
        0.0,
        glassImperfection).normalWS;

#if defined(_GBUFFER_NORMALS_OCT)
float2 octNormalWS = ScopePackNormalOctQuadEncode(normalWS);
float2 remappedOctNormalWS = saturate((octNormalWS * 0.5) + 0.5);
    return half4 (
    ScopePackFloat2To888(remappedOctNormalWS),
        0.0);
#else
return half4(normalWS, 0.0);
#endif
}
#endif

#endif
