#ifndef ENFILADE_SKIN_DENT_INCLUDED
#define ENFILADE_SKIN_DENT_INCLUDED

// A friend's fingertip pressing into a character (SkinPoke.cs, a held F's point): the skin and everything the
// character wears give way under it in a soft dimple, and spring back with a little rebound when it lifts.
// Our own code, written for ENFILADE (not lilToon's, nor taken from any VRChat package). lilToon's vertex shader calls
// EnfiladeSkinDent from lil_common_vert.hlsl (the lines marked ENFILADE there; put them back after updating lilToon,
// Docs/Poke.md), before the vertex goes to the outline and to the screen, so every pass bends the same: the colour,
// the outline, the shadow, the depth, the motion.
//
// Up to four fingertips at once (one a player in a room). Each is a slot: SkinPoke writes the slot's numbers as globals
// and puts the slot's bit (1 << slot) in the shader user value (Renderer.SetShaderUserValue, Unity 6.3) of every
// renderer of the body it presses, its skin, hair and clothes, so nothing else bends: not the poking finger, not a
// third player beside it. Bits 0-3 of the user value are SkinPoke's.
//
// The dent: the skin may not stand in front of a bowl hanging from the fingertip (depth -r^2 / 2R at r from the
// finger's line, R the bowl's curve), so wherever the finger has gone in, the skin is pushed back onto the bowl and
// meets the fingertip, whatever the character's shape: nothing about the surface is worked out here. A soft maximum
// lets the skin give a little just before the finger arrives and rounds the rim; the dent keeps its full depth near the
// finger and eases out to nothing by its widest reach; and only skin a little in front of the fingertip moves, not an
// arm across the face nor the head's far side. The normals lean with the dimple's wall, so the toon light shades it.
// After the finger lifts, SkinPoke springs the slot's tip back out; past the surface the skin bulges out a touch
// (the shape's z) and settles.

#define ENF_DENT_SLOTS 4

float  _EnfDentCount;                 // slots in use (the highest + 1); 0: nobody presses anyone and nothing runs
float4 _EnfDentTip[ENF_DENT_SLOTS];   // xyz: the fingertip (world, m); w: the bowl's curve radius (m)
float4 _EnfDentPush[ENF_DENT_SLOTS];  // xyz: the way the finger presses, into the body (world, unit); w: the deepest the skin gives (m)
float4 _EnfDentShape[ENF_DENT_SLOTS]; // x: the rim's softness (m); y: the widest reach from the finger's line (m);
                                      // z: the rebound's bulge out of the skin (m); w: the bulge's width (m)

void EnfiladeSkinDent(inout float4 positionOS, inout float3 normalOS, bool hasNormal)
{
    #if defined(LIL_URP)
        if (_EnfDentCount < 0.5) return;
        uint mine = unity_RendererUserValue & 15u;
        if (mine == 0u) return;
        float3 p = lilToAbsolutePositionWS(lilTransformOStoWS(positionOS.xyz));
        float3 moved = 0.0;
        float3 lean = 0.0;
        [unroll]
        for (uint i = 0u; i < ENF_DENT_SLOTS; i++)
        {
            if ((mine & (1u << i)) == 0u || (float)i >= _EnfDentCount) continue;
            float4 tip = _EnfDentTip[i];
            float4 push = _EnfDentPush[i];
            float4 shape = _EnfDentShape[i];
            float3 d = push.xyz;
            float3 q = p - tip.xyz;
            float a = dot(q, d);                 // how much deeper than the fingertip (+ further in)
            float3 lat = q - a * d;              // out from the finger's line
            float r2 = dot(lat, lat);
            float reach = max(shape.y, 1e-4);
            if (r2 >= reach * reach) continue;
            float k = max(shape.x, 1e-4);
            float deepest = max(push.w, 1e-4);
            // Only near the fingertip: from a little in front of it (where the touched skin stood) to a little behind
            // it (as deep as the skin gives, and the bulge's width more).
            float window = saturate((a + deepest + 2.0 * k) / k) * saturate((deepest + shape.w + k - a) / k);
            if (window <= 0.0) continue;
            // Full near the finger, easing out over the outer half of the reach.
            float r = sqrt(r2);
            float s = saturate((r - 0.5 * reach) / (0.5 * reach));
            float fade = 1.0 - s * s * (3.0 - 2.0 * s);
            float fadeSlope = r > 1e-5 ? 6.0 * s * (1.0 - s) / (0.5 * reach) / r : 0.0;   // -(d fade / dr) / r
            float w = fade * window;
            // The bowl: the least depth the skin may stand at this far out; what stands in front of it goes back onto it.
            float bend = max(tip.w, 1e-4);
            float need = -0.5 * r2 / bend - a;
            float h = saturate(0.5 + 0.5 * need / k);
            float give = h * need + k * h * (1.0 - h);   // a soft max(need, 0)
            float slope = h;                             // d give / d need
            if (give > deepest) { give = deepest; slope = 0.0; }
            moved += d * (give * w);
            // The dimple's wall faces in toward the finger: the lateral slope of give * w.
            lean -= lat * (w * slope / bend + give * window * fadeSlope);
            // The rebound past the surface: a bump out of the skin, its width shape.w.
            if (shape.z > 0.0)
            {
                float width2 = max(shape.w * shape.w, 1e-8);
                float bump = exp(-r2 / width2) * w;
                moved -= d * (shape.z * bump);
                lean += lat * (2.0 * shape.z * bump / width2);
            }
        }
        if (dot(moved, moved) < 1e-12) return;
        positionOS.xyz = lilTransformWStoOS(lilToRelativePositionWS(p + moved));
        if (hasNormal)
        {
            float3 n = normalize(mul(normalOS, (float3x3)LIL_MATRIX_I_M));   // object to world, as a normal goes
            normalOS = mul(normalize(n + lean), (float3x3)LIL_MATRIX_M);      // and back
        }
    #endif
}

#endif
