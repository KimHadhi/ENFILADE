// What may be drawn through walls: one rule for every see-through drawing, the enemy outlines (OutlineMask, EnemyOutlines:
// pings, bounties, holders, the Ghost's sight, a wave's last enemies) and MomentGlow's second pass (a friend's CHARGE outline).
// The outlines test it by the hardware against the camera's own depth buffer, sample by sample (the body's depth pulled
// SeeThroughGap nearer the eye); MomentGlow by BehindSomething below, pixel by pixel. A pixel of a body
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
// Smoke hides too, though nothing of it is in that depth: a body's seen surface with smoke on the way shows the same
// silhouette, as much as the smoke hides it (SmokeCover below; OutlineMask counts it as hidden).
#ifndef ENFILADE_SEE_THROUGH_INCLUDED
#define ENFILADE_SEE_THROUGH_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

// Set from the table (0 until then: the same number as the table's).
float _SeeThroughGap;

// How clearly something must stand in front (m): the table's gap, 0.12 until OwnViewMask has pushed it.
float SeeThroughGap() { return _SeeThroughGap > 0.0 ? _SeeThroughGap : 0.12; }

// Whether something in the scene stands in front of this fragment by more than the gap. positionCS is the fragment's
// SV_Position, eyeDepth its distance along the view (from the vertex shader: -TransformWorldToView(positionWS).z).
bool BehindSomething(float4 positionCS, float eyeDepth)
{
    float2 uv = positionCS.xy / _ScaledScreenParams.xy;
    float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
    return eyeDepth - scene > SeeThroughGap();
}

// ---- Smoke ----
// Smoke hides as a wall does, so a body shows through it by the same rule (the user, 2026-11-18: "연막탄 내부에서도 핑
// 찍힌 적이 선명하게 보이도록"). Smoke is puffs that write no depth, so the scene's depth cannot say where it is: the clouds
// are the game's own spheres instead, the ones a zombie's eyes are judged by (SmokeScreen.Through), and the length of
// smoke that hides is the game's too (GrenadeRules smokeHide). SmokeSight pushes them before each frame is drawn;
// _SmokeCount is 0 while no cloud hangs.
#define ENFILADE_SMOKE_MOST 8
float4 _SmokeSpheres[ENFILADE_SMOKE_MOST];
float _SmokeCount;
float _SmokeHide;

// How much of the look from the eye to this point (world) runs through smoke (m), every cloud together: the chord of
// each sphere along the look, cut where the look begins and where it ends.
float SmokeOnTheWay(float3 positionWS)
{
    float3 toward = positionWS - _WorldSpaceCameraPos;
    float reach = length(toward);
    float3 way = toward / max(reach, 0.0001);
    float total = 0.0;
    for (int i = 0; i < ENFILADE_SMOKE_MOST; i++)
    {
        float4 cloud = _SmokeSpheres[i];
        float3 middle = cloud.xyz - _WorldSpaceCameraPos;
        float along = dot(middle, way);
        float3 aside = middle - way * along;
        float room = cloud.w * cloud.w - dot(aside, aside);
        float halfChord = sqrt(max(room, 0.0));
        float inside = max(0.0, min(reach, along + halfChord) - max(0.0, along - halfChord));
        // Only the clouds that hang (the rest of the array is stale), and only those the look crosses.
        total += inside * step(i + 0.5, _SmokeCount) * step(0.0, room);
    }
    return total;
}

// How hidden in smoke this point is: 0 with none on the way, 1 from the length that blinds a zombie on.
half SmokeCover(float3 positionWS)
{
    float cover = saturate(SmokeOnTheWay(positionWS) / max(_SmokeHide, 0.0001));
    return (half)(cover * step(0.5, _SmokeCount));
}

#endif
