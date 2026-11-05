#ifndef RVFX_PIECE_TRANSFORM_VAT_SHADER_GRAPH_INCLUDED
#define RVFX_PIECE_TRANSFORM_VAT_SHADER_GRAPH_INCLUDED

// Shader Graph bridge for the pipeline-neutral RVFX main VAT core.
// All property names intentionally match the existing HLSL playback shader so
// baked materials and RVFXPieceTransformPlayback can use either implementation.
#define RVFX_PIECE_VAT_SAMPLE_DATA(UV) SAMPLE_TEXTURE2D_LOD(_RVFX_DataTex, sampler_RVFX_DataTex, UV, 0)
#define RVFX_PIECE_VAT_SAMPLE_POSITION(UV) SAMPLE_TEXTURE2D_LOD(_RVFX_PositionTex, sampler_RVFX_PositionTex, UV, 0)
#define RVFX_PIECE_VAT_SAMPLE_ROTATION(UV) SAMPLE_TEXTURE2D_LOD(_RVFX_RotationTex, sampler_RVFX_RotationTex, UV, 0)
#define RVFX_PIECE_VAT_SAMPLE_REST_POSITION(UV) SAMPLE_TEXTURE2D_LOD(_RVFX_RestPositionTex, sampler_RVFX_RestPositionTex, UV, 0)
#define RVFX_PIECE_VAT_SAMPLE_REST_ROTATION(UV) SAMPLE_TEXTURE2D_LOD(_RVFX_RestRotationTex, sampler_RVFX_RestRotationTex, UV, 0)

#include "../../../../Runtime/Shaders/RVFXPieceTransformVATCore.hlsl"

void RVFXPieceTransformVAT_float(
    float3 Position,
    float3 Normal,
    float PieceId,
    out float3 OutPosition,
    out float3 OutNormal)
{
    RVFXDeformedVertex deformed = RVFX_DeformVertex(Position, Normal, PieceId);
    OutPosition = deformed.positionOS;
    OutNormal = deformed.normalOS;
}

#undef RVFX_PIECE_VAT_SAMPLE_DATA
#undef RVFX_PIECE_VAT_SAMPLE_POSITION
#undef RVFX_PIECE_VAT_SAMPLE_ROTATION
#undef RVFX_PIECE_VAT_SAMPLE_REST_POSITION
#undef RVFX_PIECE_VAT_SAMPLE_REST_ROTATION

#endif
