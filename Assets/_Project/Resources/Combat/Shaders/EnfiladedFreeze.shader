// The ENFILADE tower's last kill, frozen (EnfiladedFinale; the user: "젠레스 존 제로 WIPE OUT 연출처럼"). The frame this PC
// saw at that moment, graded into the HUD's three colours: its darks to ink, its middles to the HUD's pink, its lights to
// paper, with a halftone of ink dots in the shadows, rays streaking out from the middle and the edges pressed dark.
// _Mix fades the grade in over the plain photo; _Zoom pushes the frame in a little; the RawImage's alpha fades it out.
Shader "Hidden/ENFILADE/EnfiladedFreeze"
{
    Properties
    {
        [PerRendererData] _MainTex ("Frame", 2D) = "white" {}
        _Ink ("Ink", Color) = (0.035, 0.03, 0.045, 1)
        _Accent ("Accent", Color) = (1, 0.31, 0.47, 1)
        _Paper ("Paper", Color) = (1, 0.96, 0.97, 1)
        _Mix ("Grade", Range(0, 1)) = 1
        _Low ("Darkest that counts", Range(0, 1)) = 0.08
        _High ("Lightest that counts", Range(0, 1)) = 0.72
        _Cell ("Halftone cell (px)", Float) = 7
        _Rays ("Rays", Float) = 46
        _RayLight ("Ray brightness", Range(0, 1)) = 0.35
        _Phase ("Ray drift", Float) = 0
        _Zoom ("Zoom", Float) = 1

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
            Name "Freeze"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Ink, _Accent, _Paper;
            float _Mix, _Low, _High, _Cell, _Rays, _RayLight, _Phase, _Zoom;

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

            float Hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 centred = input.uv - 0.5;
                float2 uv = 0.5 + centred / max(_Zoom, 0.01);
                float3 photo = tex2D(_MainTex, uv).rgb;
                float light = saturate((dot(photo, float3(0.299, 0.587, 0.114)) - _Low) / max(_High - _Low, 0.01));

                // Three stops: ink, pink, paper.
                float3 graded = light < 0.5 ? lerp(_Ink.rgb, _Accent.rgb, light * 2.0) : lerp(_Accent.rgb, _Paper.rgb, (light - 0.5) * 2.0);

                // Ink dots in the shadows, bigger the darker.
                float2 cell = frac(input.position.xy / max(_Cell, 1.0)) - 0.5;
                float radius = saturate(0.62 - light) * 0.85;
                float dots = 1.0 - smoothstep(radius - 0.08, radius + 0.08, length(cell));
                graded = lerp(graded, _Ink.rgb, dots * 0.55 * step(light, 0.62));

                // Rays from the middle, brighter toward the edges, each its own width.
                float2 across = centred;
                across.x *= _ScreenParams.x / _ScreenParams.y;
                float turn = atan2(across.y, across.x) / 6.2831853 + 0.5;
                float slot = floor(turn * _Rays);
                float within = frac(turn * _Rays + _Phase * (Hash(slot) - 0.5));
                float width = 0.08 + 0.18 * Hash(slot + 17.0);
                float ray = (1.0 - smoothstep(0.0, width, abs(within - 0.5))) * step(0.45, Hash(slot + 31.0));
                ray *= smoothstep(0.18, 0.75, length(across));
                graded = lerp(graded, _Paper.rgb, ray * _RayLight);

                // The edges pressed dark.
                graded *= 1.0 - smoothstep(0.45, 1.05, length(across)) * 0.55;

                float3 colour = lerp(photo, graded, _Mix);
                return float4(colour, input.color.a);
            }
            ENDCG
        }
    }
}
