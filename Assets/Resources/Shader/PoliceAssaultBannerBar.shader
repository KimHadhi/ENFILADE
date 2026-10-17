Shader "UI/PoliceAssaultBannerBar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        [Header(Reference Crimson Colors)]
        _BaseDarkColor ("평상시 어두운 바탕 (#1F0202)", Color) = (0.12, 0.008, 0.008, 0.84)
        _FlashGlowColor ("밝아질 때 중앙 핏빛 (#750606)", Color) = (0.46, 0.024, 0.024, 0.92)

        [Header(Center to Edge Pulse)]
        _BPM ("깜빡임 속도 (BPM)", Float) = 115.0
        _WaveSpread ("양옆으로 퍼져나가는 폭", Range(0.2, 1.5)) = 0.85
        _WaveSoftness ("빛 가장자리 그라데이션 부드러움", Range(0.1, 1.0)) = 0.55

        [Header(Edge Trim)]
        _SideFade ("양 끝단 페이드", Range(0.005, 0.08)) = 0.02

        // UI 필수 Stencil
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _BaseDarkColor;
            fixed4 _FlashGlowColor;
            float _BPM;
            float _WaveSpread;
            float _WaveSoftness;
            float _SideFade;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.worldPos = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPos);
                OUT.uv = v.texcoord;
                OUT.color = v.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.uv;

                // 1. 중앙(0.0)에서 양 끝(1.0)까지의 거리
                float distFromCenter = abs(uv.x - 0.5) * 2.0;

                // 2. 부드러운 호흡 주기 (0.0 ~ 1.0)
                float t = _Time.y * (_BPM / 60.0);
                float wave = sin(t * 6.2831853) * 0.5 + 0.5;
                float pulse = smoothstep(0.0, 1.0, wave);

                // 3. 밝아질 때만 중앙에서 양옆으로 퍼져나가는 마스크
                float currentRadius = pulse * _WaveSpread;
                float lightMask = 1.0 - smoothstep(currentRadius - _WaveSoftness, currentRadius + _WaveSoftness, distFromCenter);
                float finalLight = saturate(lightMask * pulse);

                // 4. 위아래 가장자리를 아주 살짝만 눌러주어 깊이감 추가
                float vertVignette = 1.0 - (abs(uv.y - 0.5) * 0.35);

                // 5. 색상 보간
                fixed4 col = lerp(_BaseDarkColor, _FlashGlowColor, finalLight);
                col.rgb *= vertVignette;

                // 6. 양 끝 마감
                float sideAlpha = smoothstep(0.0, _SideFade, uv.x) * smoothstep(1.0, 1.0 - _SideFade, uv.x);
                col.a *= sideAlpha * IN.color.a;

                return col;
            }
            ENDCG
        }
    }
}