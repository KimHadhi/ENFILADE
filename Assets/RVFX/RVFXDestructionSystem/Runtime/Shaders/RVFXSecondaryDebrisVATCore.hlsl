#ifndef RVFX_SECONDARY_DEBRIS_VAT_CORE_INCLUDED
#define RVFX_SECONDARY_DEBRIS_VAT_CORE_INCLUDED

// Pipeline-neutral secondary debris VAT deformation core.
// The owning shader declares the RVFX uniforms and provides these sampling macros before including this file:
// RVFX_DEBRIS_VAT_SAMPLE_POSITION, RVFX_DEBRIS_VAT_SAMPLE_ROTATION, and RVFX_DEBRIS_VAT_SAMPLE_SCALE_ALIVE.
// This core intentionally returns object-space data only. Render-pipeline wrappers own all world/clip transforms,
// lighting, shadow, depth, and depth-normal output.

struct RVFXVATVertexData
{
    float3 positionOS;
    float3 normalOS;
    float alive;
};

float2 RVFXVAT_GetUV(float debrisId, float frameIndex)
{
    float debrisCount = max(1.0, _RVFX_DebrisVAT_DebrisCount);
    float frameCount = max(1.0, _RVFX_DebrisVAT_FrameCount);

    float safeDebrisId = clamp(debrisId, 0.0, debrisCount - 1.0);
    float safeFrame = clamp(frameIndex, 0.0, frameCount - 1.0);

    return float2(
        (safeDebrisId + 0.5) / debrisCount,
        (safeFrame + 0.5) / frameCount);
}

float4 RVFXVAT_NormalizeQuaternion(float4 q)
{
    float lenSq = max(dot(q, q), 0.0000001);
    return q * rsqrt(lenSq);
}

float4 RVFXVAT_NlerpQuaternion(float4 a, float4 b, float t)
{
    if (dot(a, b) < 0.0)
        b = -b;

    return RVFXVAT_NormalizeQuaternion(lerp(a, b, t));
}

float3 RVFXVAT_RotateByQuaternion(float3 value, float4 q)
{
    q = RVFXVAT_NormalizeQuaternion(q);
    return value + 2.0 * cross(q.xyz, cross(q.xyz, value) + q.w * value);
}

float3 RVFXVAT_DecodePosition(float4 packedPosition)
{
    if (_RVFX_DebrisVAT_PositionEncodingMode > 0.5)
        return _RVFX_DebrisVAT_PositionMin.xyz + packedPosition.xyz * max(_RVFX_DebrisVAT_PositionRange.xyz, float3(0.000001, 0.000001, 0.000001));

    return packedPosition.xyz;
}

float4 RVFXVAT_DecodeRotation(float4 packedRotation)
{
    if (_RVFX_DebrisVAT_RotationEncodingMode > 0.5)
        packedRotation = packedRotation * 2.0 - 1.0;

    return RVFXVAT_NormalizeQuaternion(packedRotation);
}

float4 RVFXVAT_DecodeScaleAlive(float4 packedScaleAlive)
{
    float3 scale = packedScaleAlive.xyz;

    if (_RVFX_DebrisVAT_ScaleEncodingMode > 0.5)
        scale = _RVFX_DebrisVAT_ScaleMin.xyz + packedScaleAlive.xyz * max(_RVFX_DebrisVAT_ScaleRange.xyz, float3(0.000001, 0.000001, 0.000001));

    return float4(scale, packedScaleAlive.a);
}

RVFXVATVertexData RVFXVAT_Deform(float3 inputPositionOS, float3 inputNormalOS, float debrisId)
{
    RVFXVATVertexData output;

    debrisId = max(0.0, debrisId);
    float totalFrames = max(1.0, _RVFX_DebrisVAT_FrameCount);
    float frame = clamp(_RVFX_DebrisVAT_Frame, 0.0, totalFrames - 1.0);

    float frame0 = floor(frame);
    float frame1 = min(frame0 + 1.0, totalFrames - 1.0);
    float frameT = saturate(frame - frame0);

    float2 uv0 = RVFXVAT_GetUV(debrisId, frame0);
    float2 uv1 = RVFXVAT_GetUV(debrisId, frame1);

    float4 packedPos0 = RVFX_DEBRIS_VAT_SAMPLE_POSITION(uv0);
    float4 packedPos1 = RVFX_DEBRIS_VAT_SAMPLE_POSITION(uv1);

    float4 packedRot0 = RVFX_DEBRIS_VAT_SAMPLE_ROTATION(uv0);
    float4 packedRot1 = RVFX_DEBRIS_VAT_SAMPLE_ROTATION(uv1);

    float4 scaleAlive0 = RVFXVAT_DecodeScaleAlive(RVFX_DEBRIS_VAT_SAMPLE_SCALE_ALIVE(uv0));
    float4 scaleAlive1 = RVFXVAT_DecodeScaleAlive(RVFX_DEBRIS_VAT_SAMPLE_SCALE_ALIVE(uv1));

    float3 pos0 = RVFXVAT_DecodePosition(packedPos0);
    float3 pos1 = RVFXVAT_DecodePosition(packedPos1);
    float4 rot0 = RVFXVAT_DecodeRotation(packedRot0);
    float4 rot1 = RVFXVAT_DecodeRotation(packedRot1);

    float active0 = step(0.5, scaleAlive0.a);
    float active1 = step(0.5, scaleAlive1.a);

    float3 position;
    float3 scale;
    float4 rotation;
    float alive;

    if (active0 > 0.5 && active1 > 0.5)
    {
        position = lerp(pos0.xyz, pos1.xyz, frameT);
        scale = max(lerp(scaleAlive0.xyz, scaleAlive1.xyz, frameT), float3(0.0, 0.0, 0.0));
        rotation = RVFXVAT_NlerpQuaternion(rot0, rot1, frameT);
        alive = 1.0;
    }
    else if (active0 > 0.5)
    {
        position = pos0.xyz;
        scale = max(scaleAlive0.xyz, float3(0.0, 0.0, 0.0));
        rotation = RVFXVAT_NormalizeQuaternion(rot0);
        alive = 1.0;
    }
    else if (active1 > 0.5)
    {
        position = pos1.xyz;
        scale = max(scaleAlive1.xyz, float3(0.0, 0.0, 0.0));
        rotation = RVFXVAT_NormalizeQuaternion(rot1);
        alive = 1.0;
    }
    else
    {
        position = float3(0.0, 0.0, 0.0);
        scale = float3(0.0, 0.0, 0.0);
        rotation = float4(0.0, 0.0, 0.0, 1.0);
        alive = 0.0;
    }

    output.positionOS = RVFXVAT_RotateByQuaternion(inputPositionOS * scale, rotation) + position;
    output.normalOS = RVFXVAT_RotateByQuaternion(inputNormalOS, rotation);
    output.alive = alive;

    return output;
}

#endif