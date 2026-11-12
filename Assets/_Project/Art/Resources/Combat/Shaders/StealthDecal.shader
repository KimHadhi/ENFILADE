// The stealth view's paint on the ground (StealthView): where a zombie that isn't awake can see, as a cone that walls cut
// off, and how far a sound of the player's carries, as a ring running out. The box this is drawn on only covers the
// place; each pixel finds the surface behind it in the depth texture and asks whether that point lies inside, so it
// paints floors, steps and slopes alike and never walls or bodies (their surfaces don't face up). A cone's reach in
// each direction comes from rays cast from the zombie's eyes (StealthView), so it ends where its view does, the way a
// light on its head would cast it: the user's 2026-11-11 reference, a top-down view hidden behind walls by one camera
// on the character's head. The values come per draw from a MaterialPropertyBlock.
Shader "Hidden/ENFILADE/StealthDecal"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-30" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "StealthDecal"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define REACHES 24

            // Per draw, from StealthView's MaterialPropertyBlock.
            float4 _Tint;     // linear colour, and the strength
            float4 _Origin;   // the ground under its eyes (or where the sound was), and its reach (m)
            float4 _Facing;   // its forward on the ground (x, z), half its full view and half the edge of its sight (radians)
            float4 _Shape;    // 0 cone or 1 ring, the ring's radius (m), the ring's width (m), how far up or down it paints (m)
            float4 _Look;     // the edge of sight's share of the strength, a wave's phase (0-1), the rim's strength, a ring's fill
            float _Reaches[REACHES]; // how far it sees along rays spread evenly across the edge of its sight (m)

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            // Its reach at this angle off its nose (radians, the same sense the rays were cast in), between two rays.
            float ReachAt(float angle)
            {
                float edge = max(_Facing.w, 1e-3);
                float place = saturate((angle + edge) / (2.0 * edge)) * (REACHES - 1);
                int low = min((int)floor(place), REACHES - 1);
                int high = min(low + 1, REACHES - 1);
                return lerp(_Reaches[low], _Reaches[high], place - low);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
            #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(uv);
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
            #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                // Only what faces up, from the neighbouring pixels' positions.
                float3 normal = normalize(cross(ddy(world), ddx(world)));
                float ground = smoothstep(.6, .85, abs(normal.y));
                float band = 1.0 - smoothstep(_Shape.w * .7, _Shape.w, abs(world.y - _Origin.y));
                float2 flat = world.xz - _Origin.xz;
                float dist = length(flat);
                float alpha;
                if (_Shape.x > .5)
                {
                    // A ring running out to the sound's reach, faintly filled behind its edge.
                    float ring = 1.0 - smoothstep(0.0, _Shape.z, abs(dist - _Shape.y));
                    float inside = dist < _Shape.y ? _Look.w * (1.0 - .5 * dist / max(_Shape.y, 1e-3)) : 0.0;
                    alpha = max(ring, inside);
                }
                else
                {
                    float2 way = dist > 1e-4 ? flat / dist : _Facing.xy;
                    float angle = atan2(_Facing.x * way.y - _Facing.y * way.x, dot(_Facing.xy, way));
                    float side = abs(angle);
                    if (side > _Facing.w || dist > _Origin.w) discard;
                    float reach = min(ReachAt(angle), _Origin.w);
                    float within = 1.0 - smoothstep(reach - .12, reach, dist);
                    // Strongest across its full view, a share of that toward the edge of its sight.
                    float front = 1.0 - smoothstep(_Facing.z * .85, _Facing.z * 1.05, side);
                    float strength = lerp(_Look.x, 1.0, front);
                    // In from its feet, out toward the end of its sight.
                    float fromFeet = smoothstep(.25, 1.2, dist);
                    float toEnd = 1.0 - smoothstep(_Origin.w * .7, _Origin.w, dist);
                    // A bright rim along its sides and where a wall cuts it off.
                    float lateral = dist * sin(max(_Facing.w - side, 0.0));
                    float rim = (1.0 - smoothstep(.03, .09, lateral)) + (1.0 - smoothstep(.05, .14, reach - dist));
                    // A faint wave running out along it, as if it were looking.
                    float wave = frac(dist / max(_Origin.w, 1e-3) - _Look.y);
                    float sweep = smoothstep(.82, 1.0, wave) * .5;
                    alpha = within * fromFeet * (strength * toEnd * (1.0 + sweep) + saturate(rim) * _Look.z * (.4 + .6 * toEnd));
                }
                alpha *= ground * band * _Tint.a;
                return half4(_Tint.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
