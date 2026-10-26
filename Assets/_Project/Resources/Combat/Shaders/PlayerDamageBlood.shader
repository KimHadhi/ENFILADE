// Full-screen damage overlay for the player, drawn by PlayerDamageGraphic on its overlay canvas.
// Vertex alpha carries the per-direction blood amount. It drives a smooth directional red gradient (the main read) that
// blood soaks into unevenly, plus sparse fine flecks near the struck edge. Nothing here is an opaque blob.
// A separate soft red vignette throbs with the heartbeat at low health. Vertices with uv.x < 0 (direction arcs and
// drips) pass their vertex colour straight through. Properties are driven every frame by PlayerDamageGraphic.ApplyShaderState.
// The project renders in linear space, where translucent red over a bright scene washes out to pink; the final alpha is
// remapped so the layers read with the strength they would have when blended in gamma space.
Shader "Hidden/ENFILADE/PlayerDamageBlood"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _BloodColor ("Blood", Color) = (0.36, 0.01, 0.02, 1)
        _FreshColor ("Fresh Blood", Color) = (0.62, 0.02, 0.035, 1)
        _GradientOpacity ("Gradient Opacity", Range(0, 1)) = 0.6
        _GradientReach ("Gradient Reach", Range(0.45, 1)) = 0.78
        _SpeckDensity ("Speck Density", Range(0, 1)) = 0.3
        _NoiseScale ("Noise Scale", Float) = 2.6
        _Seed ("Pattern Seed", Vector) = (0, 0, 0, 0)
        _Fresh ("Freshness", Range(0, 1)) = 0
        _Wash ("Full Screen Wash", Range(0, 1)) = 0
        _Pulse ("Heartbeat", Range(0, 1)) = 0
        _Vignette ("Heartbeat Vignette", Range(0, 1)) = 0
        _Clock ("Clock", Float) = 0
        _Aspect ("Aspect", Float) = 1.7778

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Blood"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _BloodColor, _FreshColor;
            float4 _Seed;
            float _GradientOpacity, _GradientReach, _SpeckDensity, _NoiseScale, _Fresh, _Wash, _Pulse, _Vignette, _Clock, _Aspect;

            struct Attributes { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1.0), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float sum = 0.0, amplitude = 0.5;
                for (int octave = 0; octave < 4; octave++)
                {
                    sum += amplitude * Noise(p);
                    p = mul(float2x2(1.6, 1.2, -1.2, 1.6), p) + 17.1;
                    amplitude *= 0.5;
                }
                return sum / 0.9375;
            }

            // Sparse droplets on a jittered grid, stretched along 'outward' like spray thrown off the edge.
            float Specks(float2 q, float density, float2 outward, float px)
            {
                float2 cell = floor(q);
                float result = 0.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 c = cell + float2(x, y);
                    float2 d = q - (c + float2(Hash(c + 7.1), Hash(c + 3.3)));
                    float radius = lerp(0.05, 0.19, pow(Hash(c + 11.7), 3.0));
                    float dist = length(float2(dot(d, outward) / 1.7, dot(d, float2(-outward.y, outward.x))));
                    result = max(result, step(Hash(c), density) * smoothstep(radius + px, radius - px, dist));
                }
                return result;
            }

            // Premultiplied "over": src lies on top of dst.
            float4 Over(float3 color, float alpha, float4 dst) { return float4(color * alpha + dst.rgb * (1.0 - alpha), alpha + dst.a * (1.0 - alpha)); }

            float4 Frag(Varyings input) : SV_Target
            {
                if (input.uv.x < -0.5) return input.color;
                if (input.color.a < 0.001 && _Vignette < 0.001 && _Wash < 0.002) return 0;

                float2 p = input.uv * 2.0 - 1.0;
                // Rounded-rectangle radius: hugs the screen edges and lets the corners fill first.
                float2 ap = abs(p);
                float r = pow(pow(ap.x, 3.0) + pow(ap.y, 3.0), 1.0 / 3.0);
                if (r < 0.44 && _Wash < 0.002) return 0;
                float raw = saturate(input.color.a);
                float amount = sqrt(raw);
                float2 ps = float2(p.x * _Aspect, p.y);
                float2 np = ps * _NoiseScale + _Seed.xy;

                // Main read: a smooth red gradient creeping in from the struck side. Blood soaks into it unevenly, so it
                // darkens and thickens in soft clumps but never forms a hard edge.
                float inner = lerp(1.05, _GradientReach, amount);
                float t = saturate((r - inner) / (1.3 - inner));
                float soak = Fbm(np + float2(0.0, _Clock * 0.03));
                float gradient = pow(t, 0.8) * _GradientOpacity * saturate(raw * 3.5) * lerp(0.65, 1.3, soak);
                float3 gradientColor = lerp(_FreshColor.rgb, _BloodColor.rgb, saturate(t * 0.7 + soak * 0.5 - 0.15 + (1.0 - _Fresh) * 0.25));

                // Fine flecks only near the struck edge; they retreat with the blood.
                float zone = saturate((r - lerp(1.1, 0.88, amount)) / 0.18) * saturate(raw * 4.0);
                float specks = 0.0;
                if (zone > 0.001)
                {
                    const float speckScale = 8.0;
                    float px = 2.0 / _ScreenParams.y * _NoiseScale * speckScale;
                    specks = Specks(np * speckScale, _SpeckDensity * zone, ps / max(length(ps), 0.0001), px);
                }

                // Low health: a soft red vignette that thumps inward on every beat.
                float vignetteEdge = smoothstep(lerp(0.82, 0.62, _Pulse), 1.3, r);
                float vignette = vignetteEdge * vignetteEdge * _Vignette * (0.5 + 0.5 * _Pulse);
                float3 vignetteColor = lerp(_FreshColor.rgb, _BloodColor.rgb * 0.8, smoothstep(0.95, 1.3, r));

                float4 result = 0;
                result = Over(_FreshColor.rgb * 0.8, _Wash * lerp(0.6, 1.0, saturate(r)), result);
                result = Over(vignetteColor, saturate(vignette), result);
                result = Over(gradientColor, saturate(gradient), result);
                result = Over(_BloodColor.rgb * 0.85, specks * 0.85, result);
                float alpha = saturate(result.a);
                return float4(result.rgb / max(alpha, 0.0001), 1.0 - pow(1.0 - alpha, 2.2));
            }
            ENDCG
        }
    }
    Fallback Off
}
