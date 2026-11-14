// What may be drawn through walls: one rule for every see-through drawing, RevealThroughWalls (the Ghost's sight, pings,
// bounties, holders, a wave's last enemies) and MomentGlow's second pass (a friend's CHARGE outline). A pixel of a body
// counts as hidden only where something in the scene stands clearly in front of it:
// - by more than _SeeThroughGap (m; Tables/Hud/SeeThrough.csv gap, pushed here by OwnViewMask). A copy of a body and the
//   body itself differ by a hair (skinning, blend shapes, depth precision). The depth bias meant to cover that is spread
//   over many more pixels in a magnified scope's picture, and there the hair read as the whole foe hidden, so a foe in
//   the open got its silhouette drawn over it (the user, 2026-11-14: "확대 조준경으로 보이는 확대댄 적과, 그냥 시야로 보이는
//   적 두 개가 뜨는 버그").
// - never by the viewer's own first-person body, gun or scope lens. OwnViewMask marks those pixels in stencil bit 128, and
//   each see-through pass leaves a marked pixel alone (its Stencil block reads bit 128 too). The lens writes depth like
//   any solid part, so every foe behind it used to count as hidden and was drawn over the lens at the eye's own size,
//   beside the scope's own magnified picture.
// The scene's depth is URP's camera depth texture (the PC pipeline asset asks for it), copied after the opaque world.
#ifndef ENFILADE_SEE_THROUGH_INCLUDED
#define ENFILADE_SEE_THROUGH_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

// Set from the table (0 until then: the same number as the table's).
float _SeeThroughGap;

// Whether something in the scene stands in front of this fragment by more than the gap. positionCS is the fragment's
// SV_Position, eyeDepth its distance along the view (from the vertex shader: -TransformWorldToView(positionWS).z).
bool BehindSomething(float4 positionCS, float eyeDepth)
{
    float2 uv = positionCS.xy / _ScaledScreenParams.xy;
    float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
    float gap = _SeeThroughGap > 0.0 ? _SeeThroughGap : 0.12;
    return eyeDepth - scene > gap;
}

#endif
