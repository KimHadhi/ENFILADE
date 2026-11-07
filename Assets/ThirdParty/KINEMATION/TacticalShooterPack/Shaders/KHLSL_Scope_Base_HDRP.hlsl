#ifndef KINEMATION_SCOPE_BASE_HDRP_INCLUDED
#define KINEMATION_SCOPE_BASE_HDRP_INCLUDED

#if defined(SCOPE_HDRP_FOG_PASS)
#define OPAQUE_FOG_PASS
#define ATMOSPHERE_NO_AERIAL_PERSPECTIVE

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/SkyUtils.hlsl"

// Reuse HDRP active fog parameters, but deliberately skip the host camera
// view-dependent volumetric buffer.
#define SHADOW_LOW
#define AREA_SHADOW_LOW
#define _EnableVolumetricFog 0
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/AtmosphericScattering/AtmosphericScattering.hlsl"
#undef _EnableVolumetricFog

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

float4 ScopeFogFragment(ScopeFogVaryings input) : SV_Target
{
    float2 positionSS = input.positionCS.xy;
    float depth = _ScopeDepthTexture.Load(int3(positionSS, 0));
    PositionInputs positionInput = GetPositionInput(
        positionSS,
        _ScreenSize.zw,
        depth,
        UNITY_MATRIX_I_VP,
        UNITY_MATRIX_V);
    float3 viewDirectionWS = GetSkyViewDirWS(positionSS);
    float3 fogColor;
    float3 fogOpacity;
    EvaluateAtmosphericScattering(
        positionInput,
        viewDirectionWS,
        fogColor,
        fogOpacity);
    float fogTransmission = 1.0 - fogOpacity.x;
    if (IsSky(depth))
    {
        float3 sky = SampleSkyTexture(-viewDirectionWS, 0.0, 0).rgb;
        fogColor += sky * GetCurrentExposureMultiplier() * fogTransmission;
    }
    return float4(fogColor, fogTransmission);
}
#elif defined(SCOPE_HDRP_FORWARD_PASS)
#define _DISABLE_DECALS
#define USE_CLUSTERED_LIGHTLIST
#define SHADERPASS SHADERPASS_FORWARD
#define HAS_LIGHTLOOP

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"

float4 SampleScopeSource(float2 renderTextureUv)
{
    return _RenderTexture.Sample(
        sampler_RenderTexture,
        renderTextureUv);
}

OpticContext GetHDOpticContext(FragInputs input)
{
    OpticContext context;
    context.uv = input.texCoord0.xy;
    context.surfacePosWS = input.positionRWS;
    context.surfaceNormalWS = normalize(input.tangentToWorld[2]);
    context.surfaceTangentWS = normalize(input.tangentToWorld[0]);
    context.lensOriginWS = TransformObjectToWorld(_LensCenterOS.xyz);
    context.lensForwardWS = dot(_LensNormalOS.xyz, _LensNormalOS.xyz) > 1.0e-6
                                ? normalize(TransformObjectToWorldNormal(_LensNormalOS.xyz))
                                : -context.surfaceNormalWS;
    context.fallbackRightWS = normalize(
        TransformObjectToWorldDir(float3(1.0, 0.0, 0.0)));
    context.cameraPosWS = GetCurrentViewPosition();

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

void GetSurfaceAndBuiltinData(
    FragInputs input,
    float3 viewDirectionWS,
    inout PositionInputs posInput,
    out SurfaceData surfaceData,
    out BuiltinData builtinData)
{
    OpticContext context = GetHDOpticContext(input);
    ClipLens(context.apertureUv);
    float glassImperfection = GetGlassImperfection(context.apertureUv);
    float scopeVisibility;
    float3 lensRightWS;
    float3 lensUpWS;
    float3 scopeEmission = EvaluateScopeEmission(
        context,
        0.0,
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

    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD
        | MATERIALFEATUREFLAGS_LIT_SPECULAR_COLOR;
    surfaceData.baseColor = glass.baseColor;
    surfaceData.specularOcclusion = 1.0;
    surfaceData.normalWS = glass.normalWS;
    surfaceData.perceptualSmoothness = glass.smoothness;
    surfaceData.ambientOcclusion = 1.0;
    surfaceData.metallic = 0.0;
    surfaceData.specularColor = glass.specularColor;
    surfaceData.tangentWS = context.surfaceTangentWS;
    surfaceData.geomNormalWS = context.surfaceNormalWS;
    surfaceData.ior = 1.5;
    surfaceData.transmittanceColor = 1.0;
    surfaceData.atDistance = 1.0;

    InitBuiltinData(
        posInput,
        1.0,
        glass.normalWS,
        -glass.normalWS,
        input.texCoord1,
        input.texCoord2,
        builtinData);
    // The optic texture is already camera-exposed. Convert it back before
    // the forward Lit pass applies the current exposure to this emission.
    builtinData.emissiveColor = scopeEmission
        * GetInverseCurrentExposureMultiplier();
    PostInitBuiltinData(
        viewDirectionWS,
        posInput,
        surfaceData,
        builtinData);
}
#elif defined(SCOPE_HDRP_DEPTH_PASS)
#define _ALPHATEST_ON 1
#define _DISABLE_DECALS
#define WRITE_NORMAL_BUFFER
#define SHADERPASS SHADERPASS_DEPTH_ONLY

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"

void GetSurfaceAndBuiltinData(
    FragInputs input,
    float3 viewDirectionWS,
    inout PositionInputs posInput,
    out SurfaceData surfaceData,
    out BuiltinData builtinData)
{
    OpticContext context;
    context.uv = input.texCoord0.xy;
    context.surfacePosWS = input.positionRWS;
    context.surfaceNormalWS = normalize(input.tangentToWorld[2]);
    context.surfaceTangentWS = normalize(input.tangentToWorld[0]);
    context.lensOriginWS = TransformObjectToWorld(_LensCenterOS.xyz);
    context.lensForwardWS = dot(_LensNormalOS.xyz, _LensNormalOS.xyz) > 1.0e-6
                                ? normalize(TransformObjectToWorldNormal(_LensNormalOS.xyz))
                                : -context.surfaceNormalWS;
    context.fallbackRightWS = normalize(
        TransformObjectToWorldDir(float3(1.0, 0.0, 0.0)));
    context.cameraPosWS = GetCurrentViewPosition();

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
    float3 lensRightWS;
    float3 lensUpWS;
    GetLensBasis(
        context.fallbackRightWS,
        context.lensForwardWS,
        lensRightWS,
        lensUpWS);
    float3 glassAxisWS;
    float apertureRadius;
    float3 glassNormalWS = EvaluateGlassNormal(
        context,
        lensRightWS,
        lensUpWS,
        glassAxisWS,
        apertureRadius);
    float glassImperfection = GetGlassImperfection(context.apertureUv);

    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD
        | MATERIALFEATUREFLAGS_LIT_SPECULAR_COLOR;
    surfaceData.baseColor = 0.0;
    surfaceData.specularOcclusion = 1.0;
    surfaceData.normalWS = glassNormalWS;
    surfaceData.perceptualSmoothness = saturate(
        _GlassSmoothness - (glassImperfection * 0.4));
    surfaceData.ambientOcclusion = 1.0;
    surfaceData.specularColor = 0.0;
    surfaceData.tangentWS = context.surfaceTangentWS;
    surfaceData.geomNormalWS = context.surfaceNormalWS;

    InitBuiltinData(
        posInput,
        1.0,
        glassNormalWS,
        -glassNormalWS,
        input.texCoord1,
        input.texCoord2,
        builtinData);
    PostInitBuiltinData(
        viewDirectionWS,
        posInput,
        surfaceData,
        builtinData);
}
#endif

#endif
