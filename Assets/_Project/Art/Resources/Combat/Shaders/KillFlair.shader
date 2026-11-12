// A kill feed row's weapon icon catching a flair (KillFlairFx, KillFlairs.csv): drawn on an Image over the icon with the
// icon's own sprite, so every effect takes the gun's own shape. _Mode picks it:
//   0 Solid: the shape in _FlairColor (a flash, a tint), its strength _Heat.
//   1 Engulf: the first CHARGE draft (C1, kept for other uses: the user, "저거 좀 멋져서 나중에 다른 이펙트에 쓸 수 있을 것
//     같다"): the whole shape glows from deep orange at the stock to white-gold at the muzzle, tongues of flame climb off
//     its top edges, and all of it shows only where the fire has reached: from the right end (the muzzle) back by
//     _Progress (0-1).
//   4 Barrel: the CHARGE kill as the user drew it (2026-11-12: "딱 총구쪽에서 총열까지 정도만 불타오르게 감싸는 느낌"): fire
//     catches at the muzzle and wraps only the front of the gun, _Front of its length (_Span: where the drawing lies on the
//     canvas, uv; WeaponIconSpans.csv), in layers round the shape, red against the metal, orange, then yellow licking out
//     and up, the grip left clean; the shape itself only warmed under it. _Progress carries it from the muzzle back.
//   2 Gild: the shape turns gold from the muzzle back by _Progress with a white-hot band on the front, a thin lick of
//     flame on its top.
//   3 Trail: the shape burns at once and flames stream off it to the left, as if it charged forward.
// _Lick is how far the flames reach, in the sprite's uv (its height for the tongues, its width for the trail). _Heat
// fades all of it; the vertex alpha too. Premultiplied, on top of the icon, which it covers where it burns.
Shader "Hidden/ENFILADE/KillFlair"
{
    Properties
    {
        [PerRendererData] _MainTex ("Icon", 2D) = "white" {}
        _FlairColor ("Colour", Color) = (1, .8, .22, 1)
        _Mode ("Mode", Float) = 1
        _Progress ("Progress", Range(0, 1)) = 1
        _Heat ("Heat", Range(0, 1)) = 1
        _Lick ("Lick", Float) = .12
        _Seed ("Seed", Float) = 0
        _Span ("Span", Vector) = (0, 1, 0, 1)
        _Front ("Front", Float) = .5

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
            Name "KillFlair"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _FlairColor;
            float _Mode, _Progress, _Heat, _Lick, _Seed, _Front;
            float4 _Span;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f Vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }
            float Shape(float2 uv) { return tex2D(_MainTex, uv).a; }

            fixed4 Frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float a = Shape(uv);
                float t = _Time.y + _Seed;
                // Where the fire has reached: from the muzzle (u = 1) back toward the stock.
                float front = 1.0 - _Progress;
                float reached = smoothstep(front - .05, front + .015, uv.x);
                float4 col = 0;
                if (_Mode < .5)
                {
                    col = float4(_FlairColor.rgb, 1) * a;
                }
                else if (_Mode < 1.5)
                {
                    float flicker = .78 + .22 * Noise(float2(uv.x * 22.0 + t * 1.3, t * 4.0));
                    float3 body = lerp(float3(1, .32, .07), float3(1, .95, .78), saturate(uv.x * 1.15 - .08)) * flicker;
                    // Tongues: the shape smeared upward, torn by noise rising through it; only outside the shape itself.
                    float tongue = 0;
                    [unroll] for (int k = 1; k <= 6; k++)
                    {
                        float lift = _Lick * k / 6.0;
                        float s = Shape(uv - float2(0, lift));
                        float tear = Noise(float2(uv.x * 30.0, (uv.y - t * .9) * 14.0 + k));
                        tongue = max(tongue, s * (1.0 - k / 7.0) * smoothstep(.25, .75, tear + .3 * (1.0 - k / 6.0)));
                    }
                    tongue *= 1.0 - a;
                    float3 flame = lerp(float3(1, .3, .06), float3(1, .86, .32), tongue);
                    col.rgb = body * a + flame * tongue;
                    col.a = a + tongue * .9;
                    col *= reached;
                }
                else if (_Mode > 3.5)
                {
                    // Barrel: the front _Front of the gun, from the muzzle (its right end) back; the fire's own edge is torn.
                    float gunSpan = max(_Span.y - _Span.x, .001);
                    float jag = (Noise(float2(uv.y * 24.0, t * 3.0)) - .5) * .07 * gunSpan;
                    float back = _Span.y - _Front * gunSpan;
                    float reach = _Span.y + .02 - _Progress * (_Span.y + .02 - back);
                    float zone = smoothstep(reach - .015, reach + .01, uv.x + jag) * smoothstep(back - .025 * gunSpan, back + .02 * gunSpan, uv.x + jag);
                    // The shape grown outward in pixels of the 512 x 160 canvas: 6 px and 13 px all round, and up to 24 px above it
                    // where the flames climb.
                    float nearRing = 0, farRing = 0, climb = 0;
                    [unroll] for (int k = 0; k < 8; k++)
                    {
                        float angle = k * .785398;
                        float2 way = float2(cos(angle) / 512.0, sin(angle) / 160.0);
                        nearRing = max(nearRing, Shape(uv + way * 6.0));
                        farRing = max(farRing, Shape(uv + way * 13.0));
                    }
                    [unroll] for (int j = 1; j <= 4; j++) climb = max(climb, Shape(uv - float2(0, j * 6.0 / 160.0)) * (1.0 - j / 5.0));
                    float rise = (uv.y - t * .55) * 16.0;
                    float tear = smoothstep(.32, .72, Noise(float2(uv.x * 58.0, rise)) + .18);
                    float lick = smoothstep(.4, .8, Noise(float2(uv.x * 34.0 + 7.0, rise * .8)) + .1);
                    float red = saturate(nearRing - a) * (.65 + .35 * tear);
                    float orange = saturate(farRing - nearRing) * tear;
                    float yellow = saturate(climb - farRing) * lick;
                    float3 glow = float3(1, .3, .06) * red + float3(1, .58, .1) * orange + float3(1, .86, .3) * yellow;
                    float cover = max(max(red * .95, orange * .9), yellow * .8);
                    // The metal under it warms, the muzzle hottest, so the gun's own shape still reads through the fire.
                    float warm = a * .55 * lerp(.6, 1.0, saturate((uv.x - back) / gunSpan));
                    col.rgb = (glow + float3(1, .5, .16) * warm) * zone;
                    col.a = max(cover, warm) * zone;
                }
                else if (_Mode < 2.5)
                {
                    float3 gold = lerp(float3(1, .6, .16), float3(1, .95, .72), uv.x) * lerp(float3(1, 1, 1), _FlairColor.rgb * 1.2, .35);
                    float band = exp(-pow((uv.x - front) * 38.0, 2.0)) * step(.001, _Progress) * (1.0 - step(.999, _Progress));
                    float lick = Shape(uv - float2(0, _Lick * .35)) * (1.0 - a) * (.55 + .45 * Noise(float2(uv.x * 40.0, t * 5.0)));
                    col.rgb = gold * a * reached + float3(1, 1, 1) * band * a + float3(1, .82, .35) * lick * reached;
                    col.a = max(a * saturate(reached + band), lick * .8 * reached);
                }
                else
                {
                    float trail = 0;
                    [unroll] for (int k = 1; k <= 6; k++)
                    {
                        float s = Shape(uv + float2(_Lick * k / 6.0, 0));
                        float tear = Noise(float2((uv.x + t * .6) * 26.0, uv.y * 18.0 + k));
                        trail = max(trail, s * (1.0 - k / 7.0) * (.45 + .55 * tear));
                    }
                    trail *= 1.0 - a;
                    float3 core = lerp(float3(1, .45, .1), float3(1, 1, .9), saturate(uv.x));
                    col.rgb = core * a + lerp(float3(1, .32, .05), float3(1, .82, .28), trail) * trail;
                    col.a = a + trail * .85;
                }
                return col * saturate(_Heat) * i.color.a;
            }
            ENDCG
        }
    }
}
