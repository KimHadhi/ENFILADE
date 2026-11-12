#ifndef ENFILADE_SURFACE_INPUT_INCLUDED
#define ENFILADE_SURFACE_INPUT_INCLUDED

// The material and the box mapping of ENFILADE/Surface (Surface.shader), shared by all its passes so the per-material
// buffer is the same in each (the SRP Batcher needs that), the way URP's Lit shares LitInput.hlsl.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half4 _TopColor;
float _Tile;
float _TopTile;
half _RoughnessScale;
half _Metallic;
half _BumpScale;
half _OcclusionStrength;
half _Variation;
float _VariationSize;
half _ObjectSpace;
half _Cutoff;
CBUFFER_END

// _BaseMap, _BumpMap and their samplers come from URP's SurfaceInput.hlsl. Every map is read with _BaseMap's sampler:
// one sampler state (repeat, trilinear, anisotropic as the colour map is imported) keeps the pass under the limit and
// all maps of a set filter alike.
TEXTURE2D(_RoughnessMap);
TEXTURE2D(_OcclusionMap);
TEXTURE2D(_TopMap);
TEXTURE2D(_TopBumpMap);
TEXTURE2D(_TopRoughnessMap);
TEXTURE2D(_TopOcclusionMap);

// Where a vertex's textures are laid from. The world, so boxes side by side run one texture across their seams and a
// scaled cube needs no UVs. A moving part (a door, a scanner, the pushed airliner) takes its own space in metres
// instead, or its texture would slide over it as it moves; a statically batched object has its vertices in the world
// and an identity matrix, so for it the two are the same.
void SurfaceMapping(float3 positionOS, float3 normalOS, float3 positionWS, float3 normalWS, out float3 mapPosition, out float3 mapNormal)
{
    if (_ObjectSpace > 0.5)
    {
        float4x4 m = GetObjectToWorldMatrix();
        float3 scale = float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22));
        mapPosition = positionOS * scale;
        mapNormal = normalOS;
    }
    else
    {
        mapPosition = positionWS;
        mapNormal = normalWS;
    }
}

// One projection per pixel, the one the face looks along most (its normal's largest component). The builder's
// geometry is boxes and ramps, flat faces each wholly on one projection, so the usual three-way blend would only cost
// three times the samples for no visible change. Faces looking up read the top set (floors, roofs, the tops of
// walls); the rest, ceilings included, the side set.
struct SurfaceProjection
{
    float2 uv;          // in repeats of the texture
    float2 dx;          // the uv's screen gradients, from the mapping position's: continuous over a box's edge, where
    float2 dy;          // the projection changes, so the mip level doesn't jump there and draw a line along every edge
    float3 tangent;     // the direction of +u in the mapping space
    float3 bitangent;   // the direction of +v
    bool top;
};

SurfaceProjection ProjectSurface(float3 p, float3 n, float3 dpdx, float3 dpdy)
{
    SurfaceProjection o;
    float3 a = abs(n);
    if (a.y >= a.x && a.y >= a.z)
    {
        o.top = n.y > 0.0;
        float tile = o.top ? _TopTile : _Tile;
        o.uv = p.xz / tile;
        o.dx = dpdx.xz / tile;
        o.dy = dpdy.xz / tile;
        o.tangent = float3(1.0, 0.0, 0.0);
        o.bitangent = float3(0.0, 0.0, 1.0);
    }
    else if (a.x >= a.z)
    {
        // u runs to the viewer's right on both the +x and the -x face, so neither shows the texture mirrored.
        float s = n.x >= 0.0 ? 1.0 : -1.0;
        o.top = false;
        o.uv = float2(p.z * s, p.y) / _Tile;
        o.dx = float2(dpdx.z * s, dpdx.y) / _Tile;
        o.dy = float2(dpdy.z * s, dpdy.y) / _Tile;
        o.tangent = float3(0.0, 0.0, s);
        o.bitangent = float3(0.0, 1.0, 0.0);
    }
    else
    {
        float s = n.z >= 0.0 ? 1.0 : -1.0;
        o.top = false;
        o.uv = float2(-p.x * s, p.y) / _Tile;
        o.dx = float2(-dpdx.x * s, dpdx.y) / _Tile;
        o.dy = float2(-dpdy.x * s, dpdy.y) / _Tile;
        o.tangent = float3(-s, 0.0, 0.0);
        o.bitangent = float3(0.0, 1.0, 0.0);
    }
    return o;
}

// Smooth value noise, 0..1, for the large-scale variation: a texture repeating every 3 m down a 36 m wall shows its
// repeat as a pattern, and a gentle patchiness over metres hides it. Procedural, so it costs no texture read.
float SurfaceHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float SurfaceNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(SurfaceHash(i + float3(0.0, 0.0, 0.0)), SurfaceHash(i + float3(1.0, 0.0, 0.0)), f.x);
    float b = lerp(SurfaceHash(i + float3(0.0, 1.0, 0.0)), SurfaceHash(i + float3(1.0, 1.0, 0.0)), f.x);
    float c = lerp(SurfaceHash(i + float3(0.0, 0.0, 1.0)), SurfaceHash(i + float3(1.0, 0.0, 1.0)), f.x);
    float d = lerp(SurfaceHash(i + float3(0.0, 1.0, 1.0)), SurfaceHash(i + float3(1.0, 1.0, 1.0)), f.x);
    return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
}

// The surface at a pixel, and its normal in the world with the set's normal map laid on along the projection. The
// projection's directions are first made to lie in the face (Gram-Schmidt against the face's own normal): a ramp is
// mapped from above, and without this its bumps would be lit as if it were flat.
void InitializeSurfaceData(float3 mapPosition, float3 mapNormal, float3 normalWS, out SurfaceData surfaceData, out float3 perturbedNormalWS)
{
    float3 dpdx = ddx(mapPosition);
    float3 dpdy = ddy(mapPosition);
    SurfaceProjection p = ProjectSurface(mapPosition, mapNormal, dpdx, dpdy);

    half4 colour;
    half4 packedNormal;
    half rough;
    half occlusion;
    half4 tint;
    UNITY_BRANCH
    if (p.top)
    {
        colour = SAMPLE_TEXTURE2D_GRAD(_TopMap, sampler_BaseMap, p.uv, p.dx, p.dy);
        packedNormal = SAMPLE_TEXTURE2D_GRAD(_TopBumpMap, sampler_BaseMap, p.uv, p.dx, p.dy);
        rough = SAMPLE_TEXTURE2D_GRAD(_TopRoughnessMap, sampler_BaseMap, p.uv, p.dx, p.dy).r;
        occlusion = SAMPLE_TEXTURE2D_GRAD(_TopOcclusionMap, sampler_BaseMap, p.uv, p.dx, p.dy).r;
        tint = _TopColor;
    }
    else
    {
        colour = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, p.uv, p.dx, p.dy);
        packedNormal = SAMPLE_TEXTURE2D_GRAD(_BumpMap, sampler_BaseMap, p.uv, p.dx, p.dy);
        rough = SAMPLE_TEXTURE2D_GRAD(_RoughnessMap, sampler_BaseMap, p.uv, p.dx, p.dy).r;
        occlusion = SAMPLE_TEXTURE2D_GRAD(_OcclusionMap, sampler_BaseMap, p.uv, p.dx, p.dy).r;
        tint = _BaseColor;
    }

    // Two octaves of the noise, metres across, as a gentle brighter/darker patchiness (and a little rougher/smoother).
    float size = max(_VariationSize, 0.01);
    half patch = (half)(SurfaceNoise(mapPosition / size) * 0.65 + SurfaceNoise(mapPosition / (size * 0.37) + 17.0) * 0.35);
    half shade = 1.0h + _Variation * (patch * 2.0h - 1.0h);

    half3 normalTS = UnpackNormalScale(packedNormal, _BumpScale);
    float3 t = p.tangent;
    float3 b = p.bitangent;
    if (_ObjectSpace > 0.5)
    {
        t = TransformObjectToWorldDir(t);
        b = TransformObjectToWorldDir(b);
    }
    float3 n = normalize(normalWS);
    t = normalize(t - n * dot(n, t));
    b = normalize(b - n * dot(n, b) - t * dot(t, b));
    perturbedNormalWS = normalize(t * normalTS.x + b * normalTS.y + n * normalTS.z);

    surfaceData = (SurfaceData)0;
    surfaceData.albedo = saturate(colour.rgb * tint.rgb * shade);
    surfaceData.specular = half3(0.0h, 0.0h, 0.0h);
    surfaceData.metallic = _Metallic;
    surfaceData.smoothness = saturate(1.0h - rough * _RoughnessScale * (1.0h + 0.5h * _Variation * (patch * 2.0h - 1.0h)));
    surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
    surfaceData.emission = half3(0.0h, 0.0h, 0.0h);
    surfaceData.occlusion = lerp(1.0h, occlusion, _OcclusionStrength);
    surfaceData.alpha = 1.0h;
    surfaceData.clearCoatMask = 0.0h;
    surfaceData.clearCoatSmoothness = 0.0h;
}

#endif
