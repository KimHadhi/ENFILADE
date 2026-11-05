#ifndef RVFX_LIT_SURFACE_CORE_INCLUDED
#define RVFX_LIT_SURFACE_CORE_INCLUDED
// Called in Shader Graph's fragment stage; native URP/HDRP Lit handles lighting.
void RVFXLitSurface_float(float4 UV, out float3 BaseColor, out float3 NormalTS,
    out float Metallic, out float Smoothness, out float Occlusion, out float3 Emission)
{
    float2 uv = UV.xy * _BaseMap_ST.xy + _BaseMap_ST.zw;
    float4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
    float4 metal = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
    BaseColor = base.rgb * _BaseColor.rgb;
    NormalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
    Metallic = saturate(metal.r * _Metallic);
    Smoothness = saturate(metal.a * _Smoothness);
    Occlusion = lerp(1.0, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g, _OcclusionStrength);
    Emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb;
}
#endif
