Shader "UI/PoliceAssaultChevron"
{
    Properties
    {
        [PerRendererData] _MainTex ("Chevron Sprite", 2D) = "white" {}

        [Header(Reference Chevron Colors)]
        _BaseDarkColor ("평상시 어두운 레드 (#450404)", Color) = (0.27, 0.015, 0.015, 0.70)
        _LitNeonColor ("켜졌을 때 선명한 레드 (#F21B1B)", Color) = (0.95, 0.10, 0.10, 1.0)

        [Header(Chasing Wave Animation)]
        _BPM ("웨이브 속도 (배너와 동일 BPM)", Float) = 115.0
        _WaveWidth ("빛 줄기 길이 (꼬리 폭)", Range(0.15, 1.2)) = 0.55
        _WaveSoftness ("빛 앞머리 부드러움", Range(0.05, 0.5)) = 0.18
        _PhaseOffset ("박자 타이밍 오프셋", Range(0.0, 1.0)) = 0.0

        [Header(Direction Settings)]
        [Toggle] _ReverseDir ("방향 반전 (우측 <<< 이미지용)", Float) = 0
        [Toggle] _SymmetryMode ("좌우 대칭 모드 (통합 이미지용)", Float) = 0

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
            fixed4 _TextureSampleAdd;
            fixed4 _BaseDarkColor;
            fixed4 _LitNeonColor;
            float _BPM;
            float _WaveWidth;
            float _WaveSoftness;
            float _PhaseOffset;
            float _ReverseDir;
            float _SymmetryMode;

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
                fixed4 sprite = (tex2D(_MainTex, IN.uv) + _TextureSampleAdd);

                float coordX = IN.uv.x;
                if (_SymmetryMode > 0.5)
                {
                    coordX = 1.0 - abs(IN.uv.x - 0.5) * 2.0;
                }
                else if (_ReverseDir > 0.5)
                {
                    coordX = 1.0 - IN.uv.x;
                }

                float t = frac(_Time.y * (_BPM / 60.0) + _PhaseOffset);
                float waveHead = lerp(-0.3, 1.3 + _WaveWidth, t);
                float dist = waveHead - coordX;

                float frontEdge = smoothstep(-_WaveSoftness, 0.0, dist);
                float tailFade = 1.0 - smoothstep(0.0, _WaveWidth, dist);
                float waveMask = saturate(frontEdge * tailFade);

                fixed4 col = lerp(_BaseDarkColor, _LitNeonColor, waveMask);
                col.a *= sprite.a * IN.color.a;

                return col;
            }
            ENDCG
        }
    }
}