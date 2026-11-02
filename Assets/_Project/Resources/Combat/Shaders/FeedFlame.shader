// Fire for the UI, drawn on a RawImage, looping forever. With _Trail off it's one flame, growing from a point near the
// bottom and tearing into tongues as it climbs; vertex alpha is how tall it stands. With _Trail on it streams left off the
// left end of a kill feed row: blades of fire whose outer edges carry on the row's own, thinning to nothing, soft light
// around them, a few sparks thrown ahead. The row's end burns too and cools through the same colours along it, so there's no
// place where the row stops and the fire starts. The whole part of uv.y is how long it runs in px, and vertex
// alpha fades it. Vertex colour's rgb is the fire's colour. The whole part of uv.x is a seed, so fires side by side don't
// move as one.
Shader "Hidden/ENFILADE/FeedFlame"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        [Toggle] _Trail ("Trail", Float) = 0

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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Flame"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            #define TONGUE_COUNT 5
            #define SPARK_COUNT 4
            // The trail's image in px, and where the row's left end stands on it. StreakBadge sizes and places it to match.
            static const float2 TrailSize = float2(340.0, 96.0);
            static const float RowEnd = 196.0, RowMiddle = 48.0, RowHeight = 32.0, RowShear = 9.0;
            // Where each tongue leaves the row's end (px from the middle), share of the full length, half width (px), fan.
            static const float4 Tongues[TONGUE_COUNT] =
            {
                float4(0.0, 1.0, 9.0, 0.0), float4(7.5, 0.72, 8.5, 0.05), float4(-7.5, 0.6, 8.5, -0.06),
                float4(12.5, 0.42, 3.5, 0.12), float4(-12.5, 0.34, 3.5, -0.14)
            };

            float _Trail;

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

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                return Noise(p) * 0.55 + Noise(p * 2.1 + float2(1.7, 9.2)) * 0.3 + Noise(p * 4.3 + float2(8.3, 2.8)) * 0.15;
            }

            float SmoothMax(float a, float b, float k)
            {
                float h = max(k - abs(a - b), 0.0) / k;
                return max(a, b) + h * h * k * 0.25;
            }

            // The fire's colour from hottest at 0 to coolest at 1.
            float3 Cooled(float3 tint, float3 gold, float amount)
            {
                return lerp(lerp(gold, tint, smoothstep(0.0, 0.4, amount)), pow(tint, 1.8), smoothstep(0.35, 0.95, amount));
            }

            // Premultiplied.
            float4 Trail(float2 pixel, float3 tint, float full, float seed, float t)
            {
                float middle = pixel.y - RowMiddle;
                float left = RowEnd + RowShear * (middle / RowHeight + 0.5) - pixel.x;
                float along = max(left, 0.0), inside = -50.0;
                for (int i = 0; i < TONGUE_COUNT; i++)
                {
                    float4 tongue = Tongues[i];
                    float own = seed + i * 2.37;
                    float reach = full * tongue.y * (0.88 + 0.24 * sin(t * 2.1 + own * 3.7) * sin(t * 1.3 + own));
                    float through = left / reach, y = saturate(through);
                    float wave = sin(along * 0.09 - t * 2.4 + own * 2.1) * (0.7 + full * 0.014) * pow(y, 1.4);
                    float edge = tongue.z * pow(1.0 - y, 1.05) - abs(middle - (tongue.x + tongue.w * along + wave));
                    inside = SmoothMax(inside, edge - 3.0 * smoothstep(0.55, 1.0, through), 2.5);
                }

                float run = saturate(left / full);
                // Warm fire heats toward yellow, not pink; blue already holds that channel full.
                float3 gold = pow(tint, float3(0.35, 0.35, 1.0));
                float3 color = Cooled(tint, gold, run);
                float body = smoothstep(-0.5 - run, 0.7 + run, inside) * (1.0 - smoothstep(0.6, 1.0, run)) * smoothstep(-14.0, 2.0, left);
                float glow = exp(min(inside, 0.0) / 3.0) * 0.28 * (1.0 - run) * (1.0 - body) * smoothstep(-2.0, 8.0, left);
                float onRow = (1.0 - smoothstep(15.4, 16.6, abs(middle))) * (1.0 - smoothstep(-0.7, 0.7, left));
                // Along the row the fire cools through its own colours and thins away.
                float cool = saturate(-left / (60.0 + full * 0.35));
                float burn = pow(1.0 - smoothstep(0.0, 1.0, cool), 1.6) * onRow * (1.0 - body);
                float3 light = color * body + tint * glow + Cooled(tint, gold, cool) * burn;
                float cover = body + glow * 0.35 + burn * 0.92;

                for (int k = 0; k < SPARK_COUNT; k++)
                {
                    float life = frac(t * (0.42 + 0.3 * Hash(float2(k, seed))) + Hash(float2(seed, k + 0.5)));
                    float height = (Hash(float2(k + 0.5, seed + 0.5)) - 0.5) * 16.0 * (1.0 + life * 0.6) + sin(t * 2.0 + k) * 2.0;
                    float2 gap = pixel - float2(RowEnd - 6.0 - life * full, RowMiddle + height);
                    float spark = exp(-dot(gap, gap)) * pow(sin(life * 3.14159), 1.5) * 0.8;
                    light += gold * spark;
                    cover += spark * 0.7;
                }
                return float4(light, saturate(cover));
            }

            // One flame; p runs 0-1 across it and up it.
            void Flame(float2 p, float seed, float t, out float alpha, out float heat)
            {
                float x = (p.x - 0.5) * 2.0, y = p.y;
                float wobble = Fbm(float2(p.x * 2.2 + seed * 7.3, p.y * 1.5 - t * 1.6)) - 0.5;
                float lick = Fbm(float2(p.x * 5.2 + seed * 3.1, p.y * 2.6 - t * 3.1)) - 0.5;
                // The root narrows to nothing, so it grows out of a point and never stands on a cut edge.
                float root = smoothstep(-0.02, 0.3, y + lick * 0.22);
                float width = pow(saturate(1.0 - y), 0.7) * 0.78 * root;
                float body = width - abs(x + wobble * 1.3 * y);
                // The noise eats into it harder the higher it climbs, so the top splits and tears away.
                body += lick * (0.12 + 1.7 * y) * root;
                heat = saturate(body * 2.1 * (1.0 - y * 0.75));
                alpha = smoothstep(0.0, 0.1, body) * (1.0 - smoothstep(0.6, 1.05, y)) * smoothstep(0.0, 0.16, p.y + lick * 0.12);
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float seed = floor(input.uv.x);
                float2 uv = float2(input.uv.x - seed, frac(input.uv.y));
                float t = _Time.y * 1.6;
                float3 tint = input.color.rgb;
                if (_Trail > 0.5) return Trail(uv * TrailSize, tint, floor(input.uv.y), seed, t) * input.color.a;

                float alpha, heat;
                Flame(float2(uv.x, uv.y / max(input.color.a, 0.05)), seed, t, alpha, heat);
                float3 color = lerp(tint * 0.3, tint, smoothstep(0.02, 0.3, heat));
                color = lerp(color, lerp(tint, float3(1, 1, 1), 0.72), smoothstep(0.42, 0.95, heat));
                return fixed4(color * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
