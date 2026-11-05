#ifndef RVFX_SECONDARY_DEBRIS_INDIRECT_CORE_INCLUDED
#define RVFX_SECONDARY_DEBRIS_INDIRECT_CORE_INCLUDED
// Procedural mode avoids Unity's ordinary instancing matrix arrays. The graph
// vertex function below applies RVFX's world matrix; keep per-draw matrices
// intact for native Lit passes and HDRP camera-relative conversions.
void RVFXSecondaryDebrisIndirectSetup() {}
#if !defined(SHADERGRAPH_PREVIEW)
StructuredBuffer<float4x4> _RVFX_SecondaryDebrisMatrices;
#endif
void RVFXSecondaryDebrisIndirect_float(float3 Position, float3 Normal, float3 Tangent,
    float InstanceId, out float3 OutPosition, out float3 OutNormal, out float3 OutTangent)
{
#if defined(SHADERGRAPH_PREVIEW)
    OutPosition=Position; OutNormal=Normal; OutTangent=Tangent;
#else
    float4x4 instanceMatrix = _RVFX_SecondaryDebrisMatrices[(uint)InstanceId];
    float3 a=instanceMatrix._m00_m10_m20, b=instanceMatrix._m01_m11_m21, c=instanceMatrix._m02_m12_m22;
    // Inverse-transpose normal, including non-uniform scale and reflections.
    float det=dot(a,cross(b,c));
    float3 normalWS=(cross(b,c)*Normal.x+cross(c,a)*Normal.y+cross(a,b)*Normal.z) * (det<0?-1.0:1.0);
    float3 world=mul(instanceMatrix,float4(Position,1)).xyz;
#if defined(SHADEROPTIONS_CAMERA_RELATIVE_RENDERING) && SHADEROPTIONS_CAMERA_RELATIVE_RENDERING
    world=GetCameraRelativePositionWS(world);
#endif
    OutPosition=TransformWorldToObject(world);
    OutNormal=TransformWorldToObjectNormal(normalize(normalWS));
    OutTangent=TransformWorldToObjectDir(normalize(mul((float3x3)instanceMatrix,Tangent)));
#endif
}
#endif
