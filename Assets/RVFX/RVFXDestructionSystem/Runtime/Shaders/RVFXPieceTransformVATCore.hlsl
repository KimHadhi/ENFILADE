#ifndef RVFX_PIECE_TRANSFORM_VAT_CORE_INCLUDED
#define RVFX_PIECE_TRANSFORM_VAT_CORE_INCLUDED

// Optional placement cache, bound by a per-renderer property block. Kept out
// of material assets so instances continue to share the original VAT/material.
#if defined(RVFX_PIECE_VAT_LEGACY_SAMPLERS)
sampler2D _RVFX_AdaptiveContacts;
sampler2D _RVFX_AdaptiveGPUOutput;
// Both placement textures have exactly one column per piece. Sample texel
// centres at LOD zero so the Built-in surface compiler can use CG samplers.
#define RVFX_READ_CONTACT(P, R) tex2Dlod(_RVFX_AdaptiveContacts, float4((float(P) + 0.5) / max(_RVFX_PieceCount, 1.0), (float(R) + 0.5) / 7.0, 0.0, 0.0))
#define RVFX_READ_POSE(P, R) tex2Dlod(_RVFX_AdaptiveGPUOutput, float4((float(P) + 0.5) / max(_RVFX_PieceCount, 1.0), (float(R) + 0.5) / 2.0, 0.0, 0.0))
#else
Texture2D<float4> _RVFX_AdaptiveContacts;
Texture2D<float4> _RVFX_AdaptiveGPUOutput;
#define RVFX_READ_CONTACT(P, R) _RVFX_AdaptiveContacts.Load(int3(P, R, 0))
#define RVFX_READ_POSE(P, R) _RVFX_AdaptiveGPUOutput.Load(int3(P, R, 0))
#endif
float _RVFX_AdaptiveEnabled;
float _RVFX_AdaptiveTime;
float3 _RVFX_AdaptiveGravity;
float _RVFX_AdaptiveGPUEnabled;

// Pipeline-neutral main fracture VAT deformation core.
// The owning shader declares the RVFX uniforms and provides these sampling macros before including this file:
// RVFX_PIECE_VAT_SAMPLE_DATA, RVFX_PIECE_VAT_SAMPLE_POSITION, RVFX_PIECE_VAT_SAMPLE_ROTATION,
// RVFX_PIECE_VAT_SAMPLE_REST_POSITION, and RVFX_PIECE_VAT_SAMPLE_REST_ROTATION.
// This core intentionally returns object-space data only. Render-pipeline wrappers own all world/clip transforms,
// lighting, shadow, depth, and depth-normal output.

struct RVFXDeformedVertex
{
    float3 positionOS;
    float3 normalOS;
};

float4 RVFX_QNormalize(float4 q)
{
    return q * rsqrt(max(dot(q, q), 0.000001));
}

float4 RVFX_QMul(float4 a, float4 b)
{
    return float4(
        a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
        a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
        a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
        a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
}

float4 RVFX_QNlerp(float4 a, float4 b, float t)
{
    b = dot(a, b) < 0.0 ? -b : b;
    return RVFX_QNormalize(lerp(a, b, t));
}

void RVFX_ApplyAdaptive(float pieceIndex, inout float3 position, inout float4 rotation)
{
    if (_RVFX_AdaptiveEnabled < 0.5) return;
    int piece = (int)clamp(floor(pieceIndex + 0.5), 0.0, max(0.0, _RVFX_PieceCount - 1.0));
    float4 hit = RVFX_READ_CONTACT(piece, 0);
    if (hit.w >= 0.0 && _RVFX_AdaptiveTime >= hit.w)
    {
        float t = _RVFX_AdaptiveTime - hit.w;
        float3 slide = RVFX_READ_CONTACT(piece, 2).xyz;
        float3 bounce = RVFX_READ_CONTACT(piece, 3).xyz;
        position = hit.xyz + slide * (0.24 * (1.0 - exp(-t * 6.0)))
            + bounce * (abs(sin(t * 13.0)) * exp(-t * 7.0));
        rotation = RVFX_READ_CONTACT(piece, 1);
        return;
    }
    float4 tail = RVFX_READ_CONTACT(piece, 4);
    if (tail.w < 0.0 || _RVFX_AdaptiveTime <= tail.w) return;
    float elapsed = _RVFX_AdaptiveTime - tail.w;
    float4 velocity = RVFX_READ_CONTACT(piece, 5);
    float3 axis = RVFX_READ_CONTACT(piece, 6).xyz;
    float halfAngle = velocity.w * elapsed * 0.5;
    float4 turn = float4(axis * sin(halfAngle), cos(halfAngle));
    float3 offset = position - tail.xyz;
    float3 crossTerm = 2.0 * cross(turn.xyz, offset);
    position = tail.xyz + velocity.xyz * elapsed + _RVFX_AdaptiveGravity * (0.5 * elapsed * elapsed)
        + offset + turn.w * crossTerm + cross(turn.xyz, crossTerm);
    rotation = RVFX_QNormalize(RVFX_QMul(turn, rotation));
}

float3 RVFX_QRotate(float4 q, float3 v)
{
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

float2 RVFX_DataUV(float pieceIndex, float dataRow)
{
    float pc = max(_RVFX_PieceCount, 1.0);
    float fc = max(_RVFX_FrameCount, 1.0);
    float totalRows = max(fc * 2.0 + 2.0, 1.0);

    float x = (clamp(pieceIndex, 0.0, pc - 1.0) + 0.5) / pc;
    float y = (clamp(dataRow, 0.0, totalRows - 1.0) + 0.5) / totalRows;

    return float2(x, y);
}

float2 RVFX_SplitFrameUV(float pieceIndex, float frame)
{
    float pc = max(_RVFX_PieceCount, 1.0);
    float fc = max(_RVFX_FrameCount, 1.0);
    float x = (clamp(pieceIndex, 0.0, pc - 1.0) + 0.5) / pc;
    float y = (clamp(frame, 0.0, fc - 1.0) + 0.5) / fc;
    return float2(x, y);
}

float2 RVFX_SplitRestUV(float pieceIndex)
{
    float pc = max(_RVFX_PieceCount, 1.0);
    float x = (clamp(pieceIndex, 0.0, pc - 1.0) + 0.5) / pc;
    return float2(x, 0.5);
}

float4 RVFX_ReadData(float pieceIndex, float dataRow)
{
    return RVFX_PIECE_VAT_SAMPLE_DATA(RVFX_DataUV(pieceIndex, dataRow));
}

float3 RVFX_DecodeRemap01(float3 packedValue, float3 minValue, float3 rangeValue, float encodingMode)
{
    float3 safeRange = max(abs(rangeValue), float3(0.000001, 0.000001, 0.000001));
    float3 remapped = minValue + saturate(packedValue) * safeRange;
    return encodingMode > 0.5 ? remapped : packedValue;
}

float4 RVFX_DecodeQuaternion01(float4 packedValue, float encodingMode)
{
    float4 decoded = encodingMode > 0.5 ? (packedValue * 2.0 - 1.0) : packedValue;
    return RVFX_QNormalize(decoded);
}

float3 RVFX_ReadSplitPosition(float pieceIndex, float frame)
{
    float3 packedValue = RVFX_PIECE_VAT_SAMPLE_POSITION(RVFX_SplitFrameUV(pieceIndex, frame)).xyz;
    return RVFX_DecodeRemap01(packedValue, _RVFX_PositionMin.xyz, _RVFX_PositionRange.xyz, _RVFX_PositionEncodingMode);
}

float4 RVFX_ReadSplitRotation(float pieceIndex, float frame)
{
    float4 packedValue = RVFX_PIECE_VAT_SAMPLE_ROTATION(RVFX_SplitFrameUV(pieceIndex, frame));
    return RVFX_DecodeQuaternion01(packedValue, _RVFX_RotationEncodingMode);
}

float3 RVFX_ReadSplitRestPosition(float pieceIndex)
{
    float3 packedValue = RVFX_PIECE_VAT_SAMPLE_REST_POSITION(RVFX_SplitRestUV(pieceIndex)).xyz;
    return RVFX_DecodeRemap01(packedValue, _RVFX_RestPositionMin.xyz, _RVFX_RestPositionRange.xyz, _RVFX_RestPositionEncodingMode);
}

float4 RVFX_ReadSplitRestRotation(float pieceIndex)
{
    float4 packedValue = RVFX_PIECE_VAT_SAMPLE_REST_ROTATION(RVFX_SplitRestUV(pieceIndex));
    return RVFX_DecodeQuaternion01(packedValue, _RVFX_RestRotationEncodingMode);
}

RVFXDeformedVertex RVFX_DeformVertex(float3 inputPositionOS, float3 inputNormalOS, float pieceIndex)
{
    RVFXDeformedVertex result;
    result.positionOS = inputPositionOS;
    result.normalOS = inputNormalOS;

    if (_RVFX_PlaybackEnabled <= 0.5)
        return result;

    float frame = clamp(_RVFX_Frame, 0.0, max(_RVFX_FrameCount - 1.0, 0.0));
    float frame0 = floor(frame);
    float frameFraction = saturate(frame - frame0);

    // Do not interpolate final -> first. Runtime wraps the frame value and snaps to frame 0.
    float frame1 = min(frame0 + 1.0, max(_RVFX_FrameCount - 1.0, 0.0));

    float3 currentPosition;
    float4 currentRotation;
    float3 restPosition;
    float4 restRotation;

    if (_RVFX_UseSplitTextures > 0.5)
    {
        if (_RVFX_AdaptiveGPUEnabled > 0.5)
        {
            // Poses were evaluated once per piece, not once per vertex.
            // Do not redundantly sample the original motion VAT here.
            int p = (int)clamp(floor(pieceIndex + 0.5), 0.0, max(0.0, _RVFX_PieceCount - 1.0));
            currentPosition = RVFX_READ_POSE(p, 0).xyz;
            currentRotation = RVFX_READ_POSE(p, 1);
        }
        else
        {
            float3 currentPosition0 = RVFX_ReadSplitPosition(pieceIndex, frame0);
            float3 currentPosition1 = RVFX_ReadSplitPosition(pieceIndex, frame1);
            currentPosition = lerp(currentPosition0, currentPosition1, frameFraction);

            float4 currentRotation0 = RVFX_ReadSplitRotation(pieceIndex, frame0);
            float4 currentRotation1 = RVFX_ReadSplitRotation(pieceIndex, frame1);
            currentRotation = RVFX_QNlerp(currentRotation0, currentRotation1, frameFraction);
        }

        restPosition = RVFX_ReadSplitRestPosition(pieceIndex);
        restRotation = RVFX_ReadSplitRestRotation(pieceIndex);
    }
    else
    {
        float positionRow0 = frame0;
        float positionRow1 = frame1;
        float rotationRow0 = _RVFX_FrameCount + frame0;
        float rotationRow1 = _RVFX_FrameCount + frame1;
        float restPositionRow = _RVFX_FrameCount * 2.0;
        float restRotationRow = _RVFX_FrameCount * 2.0 + 1.0;

        float3 currentPosition0 = RVFX_ReadData(pieceIndex, positionRow0).xyz;
        float3 currentPosition1 = RVFX_ReadData(pieceIndex, positionRow1).xyz;
        currentPosition = lerp(currentPosition0, currentPosition1, frameFraction);

        float4 currentRotation0 = RVFX_QNormalize(RVFX_ReadData(pieceIndex, rotationRow0));
        float4 currentRotation1 = RVFX_QNormalize(RVFX_ReadData(pieceIndex, rotationRow1));
        currentRotation = RVFX_QNlerp(currentRotation0, currentRotation1, frameFraction);

        restPosition = RVFX_ReadData(pieceIndex, restPositionRow).xyz;
        restRotation = RVFX_QNormalize(RVFX_ReadData(pieceIndex, restRotationRow));
    }

    if (_RVFX_AdaptiveGPUEnabled <= 0.5)
        RVFX_ApplyAdaptive(pieceIndex, currentPosition, currentRotation);
    float4 inverseRestRotation = float4(-restRotation.xyz, restRotation.w);
    float4 deltaRotation = RVFX_QNormalize(RVFX_QMul(currentRotation, inverseRestRotation));

    float3 restOffset = inputPositionOS - restPosition;

    result.positionOS = currentPosition + RVFX_QRotate(deltaRotation, restOffset);
    result.normalOS = normalize(RVFX_QRotate(deltaRotation, inputNormalOS));

    return result;
}

#endif
