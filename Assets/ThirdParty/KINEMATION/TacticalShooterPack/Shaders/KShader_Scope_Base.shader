Shader "KINEMATION/Tactical Shooter Pack/Base Scope"
{
    Properties
    {
        [NoScaleOffset] _RenderTexture ("RenderTexture", 2D) = "black" {}
        [HideInInspector] _ScopeVisibility ("Scope Visibility", Range(0, 1)) = 1
        [HideInInspector] _ScopeTexelSize ("Scope Texel Size", Vector) = (0.0009765625, 0.0009765625, 1024, 1024)
        [NoScaleOffset] _Reticle ("Reticle", 2D) = "white" {}
        _ReticleColor ("ReticleColor", Color) = (0, 0.4528302, 0.05357282, 1)
        _ReticleSize ("ReticleSize", Vector) = (25, 25, 0, 0)
        _ReticleOffset ("ReticleOffset", Vector) = (0.5, 0.5, 0, 0)
        _ReticleEmission ("ReticleEmission", Float) = 10
        [Min(1)] _Magnification ("Magnification", Float) = 1
        [Toggle] _UseReticleZoom ("UseReticleZoom", Float) = 0
        _Distortion ("Distortion", Range(0, 0.3)) = 0.1
        _LensNormalOS ("Lens Normal OS", Vector) = (0, 0, 1, 0)
        _LensCenterOS ("Lens Center OS", Vector) = (0, 0, 0, 0)
        [Min(0.0001)] _LensRadiusOS ("Lens Radius OS", Float) = 1
        [Toggle] _UseLensMask ("UseLensMask", Float) = 1

        [Header(Physical Optics)]
        _OcularDiameter ("Ocular Diameter (mm)", Range(20, 60)) = 35
        _EyeRelief ("Eye Relief (mm)", Range(10, 200)) = 80
        _ExitPupilDiameter ("Exit Pupil Diameter (mm)", Range(1, 20)) = 8
        _EyeReliefFarFadeRange ("Eye Relief Far Fade Range (mm)", Range(1, 300)) = 120
        _VignetteSoftness ("Vignette Softness", Range(0.001, 0.5)) = 0.05

        [Header(Image Optics)]
        _LensDistortion ("Lens Distortion (Barrel - / Pincushion +)", Range(-1, 1)) = 0
        _LensDistortionEdgeBias ("Lens Distortion Edge Bias", Range(0, 1)) = 0.65
        _LateralChromaticAberration ("Lateral CA (Edge %)", Range(-2, 2)) = 0
        _ScopeSharpening ("Scope Sharpening", Range(0, 1)) = 0.35
        _ScopeEdgeTintStrength ("Scope Edge Tint Strength", Range(0, 1)) = 0
        _ScopeEdgeTintSize ("Scope Edge Tint Size", Range(0.001, 1)) = 0.35
        _ScopeEdgeTintInner ("Scope Edge Tint Inner", Color) = (1, 1, 1, 1)
        _ScopeEdgeTintOuter ("Scope Edge Tint Outer", Color) = (1, 1, 1, 1)

        [Header(Coated Glass)]
        _GlassTintInner ("Glass Tint Inner", Color) = (0, 0.378894, 1, 1)
        _GlassTintOuter ("Glass Tint Outer", Color) = (0, 0.088542, 0.040684, 1)
        _GlassReflectionStrength ("Glass Reflection Strength", Range(0, 2)) = 1
        _ScopeReflectionStrength ("Visible Scope Reflection Strength", Range(0, 1)) = 0.2
        _GlassSmoothness ("Glass Smoothness", Range(0, 1)) = 0.9
        _GlassCurvature ("Glass Curvature", Range(0, 1)) = 0.35
        _GlassEdgeDarkness ("Glass Edge Darkness", Range(0, 1)) = 0.6
        _GlassCoatingStrength ("Glass Coating Strength", Range(0, 1)) = 0.2
        _GlassImperfectionStrength ("Glass Imperfection Strength", Range(0, 1)) = 0
        _GlassImperfectionScale ("Glass Imperfection Scale", Range(1, 128)) = 48
        _GlassImperfectionSeed ("Glass Imperfection Seed", Float) = 0
    }

    HLSLINCLUDE
    Texture2D _RenderTexture;
    SamplerState sampler_RenderTexture;
    float4 _RenderTexture_TexelSize;
    Texture2D _Reticle;
    SamplerState sampler_Reticle;

    float4 SampleScopeSource(float2 renderTextureUv);

    cbuffer UnityPerMaterial
    {
        float4 _ReticleColor;
        float4 _ReticleSize;
        float4 _ReticleOffset;
        float4 _LensNormalOS;
        float4 _LensCenterOS;
        float4 _ScopeTexelSize;
        float4 _GlassTintInner;
        float4 _GlassTintOuter;
        float _ReticleEmission;
        float _Magnification;
        float _ScopeVisibility;
        float _UseReticleZoom;
        float _Distortion;
        float _UseLensMask;
        float _LensRadiusOS;
        float _OcularDiameter;
        float _EyeRelief;
        float _ExitPupilDiameter;
        float _EyeReliefFarFadeRange;
        float _VignetteSoftness;
        float _LensDistortion;
        float _LensDistortionEdgeBias;
        float _LateralChromaticAberration;
        float _ScopeSharpening;
        float _ScopeEdgeTintStrength;
        float _ScopeEdgeTintSize;
        float _GlassImperfectionStrength;
        float _GlassImperfectionScale;
        float _GlassImperfectionSeed;
        float4 _ScopeEdgeTintInner;
        float4 _ScopeEdgeTintOuter;
        float _GlassReflectionStrength;
        float _ScopeReflectionStrength;
        float _GlassSmoothness;
        float _GlassCurvature;
        float _GlassEdgeDarkness;
        float _GlassCoatingStrength;
    };

    struct OpticContext
    {
        float2 uv;
        float2 apertureUv;
        float2 authoredApertureDiameterWS;
        float useAuthoredAperture;
        float3 surfacePosWS;
        float3 surfaceNormalWS;
        float3 surfaceTangentWS;
        float3 lensOriginWS;
        float3 lensForwardWS;
        float3 fallbackRightWS;
        float3 cameraPosWS;
    };

    struct GlassSurface
    {
        float3 baseColor;
        float3 specularColor;
        float3 normalWS;
        float smoothness;
    };

    float3 ScopeNormalizeSafe(float3 value)
    {
        return value * rsqrt(max(dot(value, value), 1.0e-8));
    }

    float ScopeHash21(float2 value)
    {
        value = frac(value * float2(123.34, 456.21));
        value += dot(value, value + 45.32);
        return frac(value.x * value.y);
    }

    float ScopeValueNoise(float2 value)
    {
        float2 cell = floor(value);
        float2 cellUv = frac(value);
        float2 blend = cellUv * cellUv * (3.0 - (2.0 * cellUv));
        float noiseX0 = lerp(
            ScopeHash21(cell),
            ScopeHash21(cell + float2(1.0, 0.0)),
            blend.x);
        float noiseX1 = lerp(
            ScopeHash21(cell + float2(0.0, 1.0)),
            ScopeHash21(cell + float2(1.0, 1.0)),
            blend.x);
        return lerp(noiseX0, noiseX1, blend.y);
    }

    float GetGlassImperfection(float2 apertureUv)
    {
        if (_GlassImperfectionStrength <= 0.0)
            return 0.0;

        float scale = max(_GlassImperfectionScale, 1.0);
        float seed = _GlassImperfectionSeed;
        float noise = ScopeValueNoise(
            apertureUv * scale + float2(seed, seed * 1.61803399));
        float grime = smoothstep(0.55, 0.95, noise);
        return grime * saturate(_GlassImperfectionStrength);
    }

    float GetLensAlpha(float2 uv)
    {
        return step(0.5, 1.0 - distance(uv, float2(0.5, 0.5)));
    }

    void ClipLens(float2 uv)
    {
        float useLensMask = step(0.5, _UseLensMask);
        clip(lerp(1.0, GetLensAlpha(uv) - 0.5, useLensMask));
    }

    float2 GetBaseScopeUv(float3 viewDirLS)
    {
        return (viewDirLS.xy * float2(-1.0, -1.0)) + float2(0.5, 0.5);
    }

    float2 GetDistortedScopeUv(float3 viewDirLS)
    {
        float distortion = (_Distortion * 10.0) + 1.0;
        return GetBaseScopeUv(viewDirLS) + (-viewDirLS.xy * distortion);
    }

    float2 GetCollimatedReticleUv(float3 viewDirLS)
    {
        float axialDirection = max(abs(viewDirLS.z), 1.0e-4);
        return float2(0.5, 0.5) - (viewDirLS.xy / axialDirection);
    }

    float2 ApplyLensDistortion(float2 fieldUv)
    {
        float2 field = (fieldUv - float2(0.5, 0.5)) * 2.0;
        float radiusSq = saturate(dot(field, field));
        float radialTerm = lerp(
            radiusSq,
            radiusSq * radiusSq,
            saturate(_LensDistortionEdgeBias));
        float distortion = _LensDistortion * 0.35;
        float inverseRadialScale = rcp(max(
            1.0 + (distortion * radialTerm),
            0.5));
        return float2(0.5, 0.5)
            + ((fieldUv - float2(0.5, 0.5)) * inverseRadialScale);
    }

    float3 ProjectAxisOnLensPlane(float3 axisWS, float3 lensForwardWS, float3 fallbackAxisWS)
    {
        float3 projectedAxis = axisWS - (lensForwardWS * dot(axisWS, lensForwardWS));
        float projectedLengthSq = dot(projectedAxis, projectedAxis);

        if (projectedLengthSq > 1.0e-6)
        {
            return projectedAxis * rsqrt(projectedLengthSq);
        }

        projectedAxis = fallbackAxisWS - (lensForwardWS * dot(fallbackAxisWS, lensForwardWS));
        projectedLengthSq = dot(projectedAxis, projectedAxis);

        if (projectedLengthSq > 1.0e-6)
        {
            return projectedAxis * rsqrt(projectedLengthSq);
        }

        float3 alternateAxis = abs(lensForwardWS.y) < 0.999
                                   ? float3(0.0, 1.0, 0.0)
                                   : float3(1.0, 0.0, 0.0);
        return ScopeNormalizeSafe(cross(lensForwardWS, alternateAxis));
    }

    void GetLensBasis(
        float3 fallbackRightWS,
        float3 lensForwardWS,
        out float3 lensRightWS,
        out float3 lensUpWS)
    {
        lensRightWS = ProjectAxisOnLensPlane(fallbackRightWS, lensForwardWS, fallbackRightWS);
        lensUpWS = ScopeNormalizeSafe(cross(lensForwardWS, lensRightWS));
    }

    float3 IntersectAnalyticSphereOS(
        float3 surfaceFromCenterOS,
        float3 cameraFromCenterOS,
        float sphereRadiusOS,
        out float validIntersection)
    {
        float3 rayDirectionOS = ScopeNormalizeSafe(
            surfaceFromCenterOS - cameraFromCenterOS);
        float rayOffset = dot(cameraFromCenterOS, rayDirectionOS);
        float discriminant = rayOffset * rayOffset
            - (dot(cameraFromCenterOS, cameraFromCenterOS)
                - sphereRadiusOS * sphereRadiusOS);
        float root = sqrt(max(discriminant, 0.0));
        float nearT = -rayOffset - root;
        float farT = -rayOffset + root;
        float sphereT = nearT > 0.0 ? nearT : farT;
        validIntersection = step(0.0, discriminant) * step(0.0, sphereT);
        return cameraFromCenterOS + rayDirectionOS * sphereT;
    }

    void GetObjectApertureGeometry(
        float2 surfaceUv,
        float3 surfacePosOS,
        float3 surfaceNormalOS,
        float3 cameraPosOS,
        out float2 apertureUv,
        out float3 apertureRightOS,
        out float3 apertureUpOS,
        out float apertureRadiusOS,
        out float useAuthoredAperture)
    {
        float3 lensNormalOS = dot(_LensNormalOS.xyz, _LensNormalOS.xyz) > 1.0e-6
                                  ? normalize(_LensNormalOS.xyz)
                                  : float3(0.0, 0.0, 1.0);
        apertureRightOS = ProjectAxisOnLensPlane(
            float3(1.0, 0.0, 0.0),
            lensNormalOS,
            float3(0.0, 0.0, 1.0));
        apertureUpOS = ScopeNormalizeSafe(cross(lensNormalOS, apertureRightOS));

        float3 centerToSurfaceOS = surfacePosOS - _LensCenterOS.xyz;
        apertureRadiusOS = max(length(centerToSurfaceOS), 1.0e-5);
        float sphereAlignment = abs(dot(
            ScopeNormalizeSafe(centerToSurfaceOS),
            ScopeNormalizeSafe(surfaceNormalOS)));

        // A sphere's authored normal is radial from its object-space center.
        // Use its projected position instead of seam-split latitude/longitude UVs.
        useAuthoredAperture = step(0.98, sphereAlignment);

        if (useAuthoredAperture > 0.5)
        {
            // Intersect in object space so flattened spheres remain smooth.
            // The triangle-interpolated radius otherwise varies across each face.
            float sphereRadiusOS = max(_LensRadiusOS, 1.0e-4);
            float validIntersection;
            float3 analyticSurfaceOS = IntersectAnalyticSphereOS(
                centerToSurfaceOS,
                cameraPosOS - _LensCenterOS.xyz,
                sphereRadiusOS,
                validIntersection);
            centerToSurfaceOS = lerp(
                centerToSurfaceOS, analyticSurfaceOS, validIntersection);
            apertureRadiusOS = sphereRadiusOS;
        }

        float2 sphericalApertureUv = float2(0.5, 0.5) + (0.5 * float2(
            dot(centerToSurfaceOS, apertureRightOS),
            dot(centerToSurfaceOS, apertureUpOS)) / apertureRadiusOS);
        apertureUv = lerp(surfaceUv, sphericalApertureUv, useAuthoredAperture);
    }

    float3 ProjectOnLensPlane(float3 surfacePosWS, float3 planeOriginWS, float3 planeNormalWS)
    {
        return surfacePosWS - (planeNormalWS * dot(surfacePosWS - planeOriginWS, planeNormalWS));
    }

    float3 ProjectViewRayOnLensPlane(
        float3 surfacePosWS,
        float3 planeOriginWS,
        float3 planeNormalWS,
        float3 cameraPosWS)
    {
        float3 rayDirWS = ScopeNormalizeSafe(surfacePosWS - cameraPosWS);
        float denominator = dot(rayDirWS, planeNormalWS);
        float safeDenominator = abs(denominator) > 1.0e-5
                                    ? denominator
                                    : (denominator < 0.0
                                           ? -1.0e-5
                                           : 1.0e-5);
        float planeT = dot(planeOriginWS - cameraPosWS, planeNormalWS) / safeDenominator;
        float3 rayPlanePosWS = cameraPosWS + (rayDirWS * planeT);
        float3 normalPlanePosWS = ProjectOnLensPlane(surfacePosWS, planeOriginWS, planeNormalWS);
        float validProjection = step(1.0e-5, abs(denominator)) * step(0.0, planeT);
        return lerp(normalPlanePosWS, rayPlanePosWS, validProjection);
    }

    float3 GetLensSpaceVector(
        float3 vectorWS,
        float3 lensRightWS,
        float3 lensUpWS,
        float3 lensForwardWS)
    {
        return float3(
            dot(vectorWS, lensRightWS),
            dot(vectorWS, lensUpWS),
            dot(vectorWS, lensForwardWS));
    }

    float GetScopeFovMask(float2 distortedUv)
    {
        float apertureDistance = length(distortedUv - float2(0.5, 0.5));
        float softness = max(_VignetteSoftness, fwidth(apertureDistance) * 1.5);
        return 1.0 - smoothstep(0.5 - softness, 0.5, apertureDistance);
    }

    float GetEyeBoxMask(float2 opticalPointMm, float3 lensSpaceCameraPosMm)
    {
        if (lensSpaceCameraPosMm.z <= 1.0e-4)
        {
            return 0.0;
        }

        float ocularRadius = max(_OcularDiameter, 1.0e-4) * 0.5;
        float exitPupilRadius = max(_ExitPupilDiameter * 0.5, 1.0e-5);
        float eyeRelief = max(_EyeRelief, 1.0e-4);

        // Signed ray intersection with the fixed exit-pupil plane. Keeping this
        // value unclamped makes both near and far eye positions contract naturally.
        float exitPupilPlaneT = 1.0 - (eyeRelief / lensSpaceCameraPosMm.z);
        float2 exitPupilPointMm = lensSpaceCameraPosMm.xy
            + ((opticalPointMm - lensSpaceCameraPosMm.xy) * exitPupilPlaneT);
        float pupilDistanceMm = length(exitPupilPointMm);
        float authoredSoftnessMm = max(_VignetteSoftness, 0.0) * ocularRadius;
        float boundaryAntialiasingMm = fwidth(pupilDistanceMm) * 1.5;
        float boundarySoftnessMm = max(
            authoredSoftnessMm,
            max(boundaryAntialiasingMm, 1.0e-5));

        float pupilMask = 1.0 - smoothstep(
            exitPupilRadius - boundarySoftnessMm,
            exitPupilRadius + boundarySoftnessMm,
            pupilDistanceMm);
        float farDistanceMm = max(lensSpaceCameraPosMm.z - eyeRelief, 0.0);
        float farFadeRangeMm = max(_EyeReliefFarFadeRange, 1.0e-4);
        float farVisibility = 1.0 - smoothstep(
            0.0,
            farFadeRangeMm,
            farDistanceMm);
        return pupilMask * farVisibility;
    }

    float2 GetReticleUv(float2 projectedUv)
    {
        float reticleZoom = lerp(1.0, max(_Magnification, 1.0), step(0.5, _UseReticleZoom));
        float2 scale = rcp(max(_ReticleSize.xy * reticleZoom, 1.0e-4));
        float2 offset = ((1.0 - scale) * 0.5) + _ReticleOffset.xy;
        return (projectedUv * scale) + offset;
    }

    float2 GetLensPhysicalScale(OpticContext context)
    {
        if (context.useAuthoredAperture > 0.5)
        {
            return max(_OcularDiameter, 1.0e-4)
                / max(context.authoredApertureDiameterWS, 1.0e-5);
        }

        float3 lensPlanePosWS = ProjectOnLensPlane(
            context.surfacePosWS,
            context.lensOriginWS,
            context.lensForwardWS);
        float3 positionDx = ddx(lensPlanePosWS);
        float3 positionDy = ddy(lensPlanePosWS);
        float2 uvDx = ddx(context.uv);
        float2 uvDy = ddy(context.uv);
        float uvDeterminant = (uvDx.x * uvDy.y) - (uvDx.y * uvDy.x);
        float safeUvDeterminant = abs(uvDeterminant) > 1.0e-8
                                      ? uvDeterminant
                                      : (uvDeterminant < 0.0 ? -1.0e-8 : 1.0e-8);
        float3 worldPerUvX = ((positionDx * uvDy.y) - (positionDy * uvDx.y))
            / safeUvDeterminant;
        float3 worldPerUvY = ((positionDy * uvDx.x) - (positionDx * uvDy.x))
            / safeUvDeterminant;

        return max(_OcularDiameter, 1.0e-4)
            / max(float2(length(worldPerUvX), length(worldPerUvY)), 1.0e-5);
    }

    float3 EvaluateScopeEmission(
        OpticContext context,
        float useProjectedReticleUv,
        float glassImperfection,
        out float scopeVisibility,
        out float3 lensRightWS,
        out float3 lensUpWS)
    {
        GetLensBasis(
            context.fallbackRightWS,
            context.lensForwardWS,
            lensRightWS,
            lensUpWS);

        float3 lensPlaneSamplePosWS = ProjectViewRayOnLensPlane(
            context.surfacePosWS,
            context.lensOriginWS,
            context.lensForwardWS,
            context.cameraPosWS);
        float3 viewDirectionWS = ScopeNormalizeSafe(
            context.cameraPosWS - lensPlaneSamplePosWS);
        float3 viewDirectionLS = ScopeNormalizeSafe(GetLensSpaceVector(
            viewDirectionWS,
            lensRightWS,
            lensUpWS,
            context.lensForwardWS));

        float2 distortedUv = GetDistortedScopeUv(viewDirectionLS);
        // Preserve the existing PiP projection: the second camera supplies the
        // image, while the viewer-to-lens direction controls where it is sampled.
        float2 lensDistortedUv = ApplyLensDistortion(distortedUv);
        float2 lensField = (lensDistortedUv - float2(0.5, 0.5))
            / max(_Magnification, 1.0);
        float2 projectedScopeUv = float2(0.5, 0.5) + lensField;
        float2 renderTextureUv = projectedScopeUv;
        renderTextureUv.y = 1.0 - renderTextureUv.y;

        float2 physicalPerWorld = GetLensPhysicalScale(context);
        float axialPhysicalPerWorld = (physicalPerWorld.x + physicalPerWorld.y) * 0.5;
        float3 cameraToLensWS = context.cameraPosWS - context.lensOriginWS;
        float3 lensSpaceCameraPosWS = GetLensSpaceVector(
            cameraToLensWS,
            lensRightWS,
            lensUpWS,
            context.lensForwardWS);
        float3 lensSpaceCameraPosMm = float3(
            lensSpaceCameraPosWS.xy * physicalPerWorld,
            lensSpaceCameraPosWS.z * axialPhysicalPerWorld);

        // Eye relief uses the actual ray intersection on the fixed ocular plane
        // in millimeters.
        float3 opticalPointWS = lensPlaneSamplePosWS - context.lensOriginWS;
        float2 opticalPointMm = float2(
            dot(opticalPointWS, lensRightWS),
            dot(opticalPointWS, lensUpWS)) * physicalPerWorld;

        float2 reticleProjectedUv = distortedUv;
        float2 reticleUv = GetReticleUv(reticleProjectedUv);
        reticleUv.y = 1.0 - reticleUv.y;
        float scopeFovMask = GetScopeFovMask(distortedUv);
        float eyeBoxMask = GetEyeBoxMask(opticalPointMm, lensSpaceCameraPosMm);
        scopeVisibility = saturate(scopeFovMask * eyeBoxMask) * saturate(_ScopeVisibility);
        if (scopeVisibility == 0.0)
            return 0.0;

        float4 renderTextureSample = SampleScopeSource(renderTextureUv);
        if (_LateralChromaticAberration != 0.0)
        {
            float lateralColor = _LateralChromaticAberration * 0.005;
            float2 redUv = float2(0.5, 0.5)
                + (lensField * (1.0 + lateralColor));
            float2 blueUv = float2(0.5, 0.5)
                + (lensField * (1.0 - lateralColor));
            redUv.y = 1.0 - redUv.y;
            blueUv.y = 1.0 - blueUv.y;
            renderTextureSample.r = SampleScopeSource(redUv).r;
            renderTextureSample.b = SampleScopeSource(blueUv).b;
        }

        if (_ScopeSharpening > 0.0)
        {
            float2 texel = _RenderTexture_TexelSize.xy;
            float3 blur = (
                    SampleScopeSource(renderTextureUv - float2(texel.x, 0.0)).rgb
                    + SampleScopeSource(renderTextureUv + float2(texel.x, 0.0)).rgb
                    + SampleScopeSource(renderTextureUv - float2(0.0, texel.y)).rgb
                    + SampleScopeSource(renderTextureUv + float2(0.0, texel.y)).rgb)
                * 0.25;
            renderTextureSample.rgb = max(
                renderTextureSample.rgb
                + ((renderTextureSample.rgb - blur) * _ScopeSharpening),
                0.0);
        }

        float2 opticalField = (distortedUv - float2(0.5, 0.5)) * 2.0;
        float edgeTintGradient = smoothstep(
            1.0 - saturate(_ScopeEdgeTintSize),
            1.0,
            saturate(length(opticalField)));
        float3 edgeTint = lerp(
            max(_ScopeEdgeTintInner.rgb, 0.0),
            max(_ScopeEdgeTintOuter.rgb, 0.0),
            edgeTintGradient);
        renderTextureSample.rgb *= lerp(
            float3(1.0, 1.0, 1.0),
            edgeTint,
            edgeTintGradient * saturate(_ScopeEdgeTintStrength));
        float4 reticleSample = _Reticle.Sample(sampler_Reticle, reticleUv);
        float4 reticleColor = reticleSample * _ReticleColor;
        float4 scopeColor = lerp(renderTextureSample, reticleColor, reticleSample.a);
        float4 scopeEmission = lerp(
            renderTextureSample,
            reticleColor * _ReticleEmission,
            reticleSample.a);

        float imperfectionTransmission = 1.0 - (glassImperfection * 0.18);
        return max(scopeColor.rgb, scopeEmission.rgb)
            * scopeVisibility
            * imperfectionTransmission;
    }

    float3 EvaluateGlassNormal(
        OpticContext context,
        float3 lensRightWS,
        float3 lensUpWS,
        out float3 glassAxisWS,
        out float apertureRadius)
    {
        // Choose the lens side per view, independently of the mesh normals.
        float cameraFacing = dot(
            context.cameraPosWS - context.lensOriginWS,
            context.lensForwardWS) >= 0.0 ? 1.0 : -1.0;
        glassAxisWS = context.lensForwardWS * cameraFacing;

        float2 apertureUv = (context.apertureUv - float2(0.5, 0.5)) * 2.0;
        apertureRadius = saturate(length(apertureUv));
        float3 radialDirectionWS = (
            lensRightWS * apertureUv.x + lensUpWS * apertureUv.y)
            / max(apertureRadius, 1.0e-5);
        // Match the Unreal lens curvature, with a maximum rim angle of 35 degrees.
        float curvatureAngle = apertureRadius
            * sqrt(saturate(_GlassCurvature)) * 0.610865238;
        return ScopeNormalizeSafe(
            glassAxisWS * cos(curvatureAngle)
            + radialDirectionWS * sin(curvatureAngle));
    }

    GlassSurface EvaluateGlassSurface(
        OpticContext context,
        float scopeVisibility,
        float glassImperfection,
        float3 lensRightWS,
        float3 lensUpWS)
    {
        GlassSurface glass;
        float3 glassAxisWS;
        float apertureRadius;
        glass.normalWS = EvaluateGlassNormal(
            context,
            lensRightWS,
            lensUpWS,
            glassAxisWS,
            apertureRadius);

        float3 viewDirectionWS = ScopeNormalizeSafe(
            context.cameraPosWS - context.surfacePosWS);
        float viewAlignment = saturate(abs(dot(viewDirectionWS, glassAxisWS)));
        float radialTint = smoothstep(0.0, 1.0, apertureRadius);
        float3 glassTint = lerp(
            _GlassTintInner.rgb,
            _GlassTintOuter.rgb,
            radialTint);
        float edgeMask = smoothstep(0.65, 1.0, apertureRadius);
        float edgeShade = 1.0 - (saturate(_GlassEdgeDarkness) * edgeMask);
        float coatingStrength = saturate(_GlassCoatingStrength);

        float coatingPhase = (apertureRadius * 9.42477796)
            + ((1.0 - viewAlignment) * 6.28318531);
        float3 spectralColor = 0.5 + (0.5 * cos(
            coatingPhase + float3(0.0, 2.09439510, 4.18879020)));
        float spectralAmount = coatingStrength
            * (0.25 + (0.75 * apertureRadius))
            * (0.35 + (0.65 * (1.0 - viewAlignment)));

        glass.baseColor = max(
            glassTint * (0.24 * coatingStrength),
            0.0) * edgeShade;
        glass.specularColor = saturate(
            0.04 * max(_GlassReflectionStrength, 0.0)
            * lerp(
                float3(1.0, 1.0, 1.0),
                0.7 + (0.6 * spectralColor),
                spectralAmount));
        float visibleReflectionStrength = lerp(
            1.0,
            saturate(_ScopeReflectionStrength),
            saturate(scopeVisibility));
        glass.specularColor *= visibleReflectionStrength;
        glass.baseColor *= 1.0 - (glassImperfection * 0.12);
        glass.specularColor *= 1.0 - (glassImperfection * 0.3);
        glass.smoothness = saturate(
            _GlassSmoothness - (glassImperfection * 0.4));
        return glass;
    }

    GlassSurface EvaluateGlassSurface(
        OpticContext context,
        float scopeVisibility,
        float glassImperfection)
    {
        float3 lensRightWS;
        float3 lensUpWS;
        GetLensBasis(
            context.fallbackRightWS,
            context.lensForwardWS,
            lensRightWS,
            lensUpWS);
        return EvaluateGlassSurface(
            context,
            scopeVisibility,
            glassImperfection,
            lensRightWS,
            lensUpWS);
    }

    float2 ScopePackNormalOctQuadEncode(float3 normalWS)
    {
        normalWS *= rcp(max(dot(abs(normalWS), 1.0), 1.0e-6));
        float fold = saturate(-normalWS.z);
        return normalWS.xy + float2(
            normalWS.x >= 0.0 ? fold : -fold,
            normalWS.y >= 0.0 ? fold : -fold);
    }

    float3 ScopePackFloat2To888(float2 value)
    {
        uint2 packedValue = (uint2)(saturate(value) * 4095.5);
        uint2 highBits = packedValue >> 8;
        uint2 lowBits = packedValue & 255;
        return uint3(lowBits, highBits.x | (highBits.y << 4)) / 255.0;
    }
    ENDHLSL

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
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }

        Cull Back
        ZWrite On
        ZTest LEqual
        Blend Off

        Pass
        {
            Name "ForwardOnly"
            Tags
            {
                "LightMode" = "ForwardOnly"
            }
            ZTest LEqual

            Stencil
            {
                WriteMask 3
                Ref 0
                Comp Always
                Pass Replace
            }

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
            #define SCOPE_HDRP_FORWARD_PASS
            #include "KHLSL_Scope_Base_HDRP.hlsl"

            #include_with_pragmas "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags
            {
                "LightMode" = "DepthForwardOnly"
            }
            ZWrite On

            Stencil
            {
                WriteMask 8
                Ref 0
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH

            #define SCOPE_HDRP_DEPTH_PASS
            #include "KHLSL_Scope_Base_HDRP.hlsl"

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
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
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }

        Cull Back
        ZWrite On
        ZTest LEqual
        Blend Off

        Pass
        {
            Name "UniversalForward"
            Tags
            {
                "LightMode" = "UniversalForwardOnly"
            }
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ScopeVertex
            #pragma fragment ScopeFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS

            #define SCOPE_URP_FORWARD_PASS
            #include "KHLSL_Scope_Base_URP.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ScopeDepthVertex
            #pragma fragment ScopeDepthFragment
            #pragma multi_compile_instancing

            #define SCOPE_URP_DEPTH_PASS
            #include "KHLSL_Scope_Base_URP.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags
            {
                "LightMode" = "DepthNormalsOnly"
            }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ScopeDepthNormalsVertex
            #pragma fragment ScopeDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #define SCOPE_URP_DEPTH_NORMALS_PASS
            #include "KHLSL_Scope_Base_URP.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
