// The flame a shot throws out of the muzzle (MuzzleFlame): cloud puffs that face the camera, with boiling edges and a
// colour that cools from white through cream and apricot to salmon; sparks stretched along their flight; a faint smoke
// wisp. Each quad carries its own shape and moment in its vertex data, so one draw holds every live shot of a gun.
// Premultiplied alpha: the colour is light added on top (it blooms in a dark room) and the alpha only dims what is
// behind, so against a bright sky it still reads as a cloud. The puff is the one the user picked in the 2026-11-06
// mockup, ported line for line. Its colours lean from that mockup's CS2 cream and salmon toward the orange the user
// liked in the old flash sprite (GunMuzzleFlash2.png: white-yellow heart, cream, tan, orange rim), by Orange Tone.
Shader "Hidden/ENFILADE/MuzzleFlame"
{
    Properties
    {
        _Brightness ("Brightness", Range(0, 4)) = 1.4
        _Warmth ("Orange Tone (0 CS2 cream, 1 old flash orange)", Range(0, 1)) = 0.75
        _SmokeTint ("Smoke", Color) = (0.42, 0.42, 0.43, 1)
        _SoftDepth ("Fade Into Walls Within (m)", Range(0, 0.3)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "MuzzleFlame"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Brightness;
                half _Warmth;
                half4 _SmokeTint;
                float _SoftDepth;
            CBUFFER_END

            // Linear colour by temperature, coolest first: CS2's cream and salmon...
            static const half3 Ember = half3(.42, .12, .09) * .12;
            static const half3 Salmon = half3(.9, .36, .27) * .35;
            static const half3 Apricot = half3(1, .58, .36) * .6;
            static const half3 Cream = half3(1, .8, .55) * .95;
            static const half3 Hot = half3(1, .95, .84) * 1.6;
            static const half3 White = half3(1, 1, .97) * 2.6;
            // ...and the old flash sprite's orange, its hues sampled from the texture by brightness band.
            static const half3 WarmEmber = half3(.55, .18, .04) * .15;
            static const half3 WarmSalmon = half3(1, .49, .12) * .5;
            static const half3 WarmApricot = half3(1, .61, .29) * .75;
            static const half3 WarmCream = half3(1, .7, .38) * 1.1;
            static const half3 WarmHot = half3(1, .93, .7) * 1.75;
            static const half3 WarmWhite = half3(1, .98, .85) * 2.8;

            struct Attributes
            {
                float3 center : POSITION;  // the element's centre, object space
                float4 corner : TEXCOORD0; // corner (-1..1), half width, half length
                float4 axis : TEXCOORD1;   // stretch axis in object space (zero faces the camera), turn (radians)
                float4 look : TEXCOORD2;   // seed, heat, alpha, kind (0 puff, 1 spark, 2 smoke)
                float4 extra : TEXCOORD3;  // boil, roughness, occlusion
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 look : TEXCOORD1;
                float4 extra : TEXCOORD2;  // boil, roughness, occlusion, eye depth
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 centerVS = TransformWorldToView(TransformObjectToWorld(input.center));
                float3 positionVS;
                if (dot(input.axis.xyz, input.axis.xyz) < 1e-8)
                {
                    float s, c;
                    sincos(input.axis.w, s, c);
                    float2 turned = float2(c * input.corner.x - s * input.corner.y, s * input.corner.x + c * input.corner.y);
                    positionVS = centerVS + float3(turned * input.corner.z, 0);
                }
                else
                {
                    // Long along its flight, wide across the line of sight.
                    float3 axisVS = normalize(TransformWorldToViewDir(TransformObjectToWorldDir(input.axis.xyz)));
                    float3 side = cross(axisVS, normalize(-centerVS));
                    float sideLength = length(side);
                    side = sideLength > 1e-4 ? side / sideLength : float3(1, 0, 0);
                    positionVS = centerVS + axisVS * (input.corner.x * input.corner.w) + side * (input.corner.y * input.corner.z);
                }
                output.positionCS = TransformWViewToHClip(positionVS);
                output.uv = input.corner.xy;
                output.look = input.look;
                output.extra = float4(input.extra.xyz, -positionVS.z);
                return output;
            }

            float FlameHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float FlameNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(FlameHash(i), FlameHash(i + float2(1, 0)), u.x), lerp(FlameHash(i + float2(0, 1)), FlameHash(i + float2(1, 1)), u.x), u.y);
            }

            float FlameFbm(float2 p)
            {
                float sum = 0, amplitude = .5;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    sum += amplitude * FlameNoise(p);
                    p = p * 2.03 + float2(17.1, 9.2);
                    amplitude *= .5;
                }
                return sum / .9375;
            }

            half3 FlameRamp(float t)
            {
                half3 ember = lerp(Ember, WarmEmber, _Warmth), salmon = lerp(Salmon, WarmSalmon, _Warmth);
                half3 apricot = lerp(Apricot, WarmApricot, _Warmth), cream = lerp(Cream, WarmCream, _Warmth);
                half3 hot = lerp(Hot, WarmHot, _Warmth), white = lerp(White, WarmWhite, _Warmth);
                t = clamp(t, 0, 1.3);
                if (t < .3) return lerp(ember, salmon, t / .3);
                if (t < .55) return lerp(salmon, apricot, (t - .3) / .25);
                if (t < .78) return lerp(apricot, cream, (t - .55) / .23);
                if (t < 1) return lerp(cream, hot, (t - .78) / .22);
                return lerp(hot, white, (t - 1) / .3);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float seed = input.look.x, heat = input.look.y, alpha = input.look.z, kind = input.look.w;
                float boil = input.extra.x, rough = input.extra.y, occlusion = input.extra.z;
                float2 uv = input.uv;
                float r = length(uv);
                // Fades into whatever stands just behind it instead of cutting a hard line through a wall.
                float scene = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams);
                float soft = _SoftDepth > 0 ? saturate((scene - input.extra.w) / _SoftDepth) : 1;

                if (kind > 1.5)
                {
                    float n = FlameFbm(uv * 1.6 + float2(seed * 3.1, boil * .2));
                    float d = saturate(1 - (r + (n - .5) * .7) / .95);
                    d *= d * alpha * soft;
                    return half4(_SmokeTint.rgb * d, d);
                }
                if (kind > .5)
                {
                    float d = pow(saturate(1 - abs(uv.y)) * saturate(1 - abs(uv.x)), 1.5) * alpha * soft;
                    return half4(FlameRamp(heat) * (1.4 * _Brightness * d), 0);
                }
                float lumps = FlameFbm(uv * 1.7 + float2(seed * 3.1, seed * 1.7) + float2(0, boil * .35));
                float billows = FlameFbm(uv * 4.2 + float2(seed * 5.3, -seed * 2.9) + float2(boil * .6, 0));
                float edge = r + (lumps - .5) * rough + (billows - .5) * rough * .45;
                float density = saturate(1 - edge / .9);
                density = density * density * (3 - 2 * density);
                half3 color = FlameRamp(heat * (.5 + .5 * saturate(1 - r)) * (.82 + .36 * billows)) * _Brightness;
                float a = density * alpha * soft;
                return half4(color * a, a * occlusion);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
