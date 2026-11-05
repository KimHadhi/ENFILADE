#ifndef RVFX_SECONDARY_DEBRIS_VAT_SHADER_GRAPH_INCLUDED
#define RVFX_SECONDARY_DEBRIS_VAT_SHADER_GRAPH_INCLUDED

// Shader Graph bridge for the pipeline-neutral RVFX debris VAT core.
#define RVFX_DEBRIS_VAT_SAMPLE_POSITION(UV) SAMPLE_TEXTURE2D_LOD(_RVFX_DebrisVAT_PositionTex, sampler_RVFX_DebrisVAT_PositionTex, UV, 0)
#define RVFX_DEBRIS_VAT_SAMPLE_ROTATION(UV) SAMPLE_TEXTURE2D_LOD(_RVFX_DebrisVAT_RotationTex, sampler_RVFX_DebrisVAT_RotationTex, UV, 0)
#define RVFX_DEBRIS_VAT_SAMPLE_SCALE_ALIVE(UV) SAMPLE_TEXTURE2D_LOD(_RVFX_DebrisVAT_ScaleAliveTex, sampler_RVFX_DebrisVAT_ScaleAliveTex, UV, 0)

#include "../../../../Runtime/Shaders/RVFXSecondaryDebrisVATCore.hlsl"

void RVFXSecondaryDebrisVAT_float(
    float3 Position,
    float3 Normal,
    float DebrisId,
    out float3 OutPosition,
    out float3 OutNormal)
{
    RVFXVATVertexData deformed = RVFXVAT_Deform(Position, Normal, DebrisId);

    // A collapsed triangle has zero area, so inactive debris disappears in all
    // generated passes without requiring a pipeline-specific fragment clip.
    OutPosition = deformed.alive > 0.5 ? deformed.positionOS : float3(0.0, 0.0, 0.0);
    OutNormal = deformed.normalOS;
}

#undef RVFX_DEBRIS_VAT_SAMPLE_POSITION
#undef RVFX_DEBRIS_VAT_SAMPLE_ROTATION
#undef RVFX_DEBRIS_VAT_SAMPLE_SCALE_ALIVE

#endif
