Shader "UI/Custom/HexGlowFrame"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)

        [Header(Hexagon Shape)]
        _HexRadius ("Inner Hex Radius", Range(0.2, 0.45)) = 0.3
        _CornerRounding ("Hex Smoothness", Range(0.001, 0.02)) = 0.0175

        [Header(Gap And Border)]
        _GapWidth ("Transparent Gap Width", Range(0.0, 0.08)) = 0.01
        _BorderThickness ("Bracket Border Width", Range(0.005, 0.05)) = 0.015

        [Header(Glow Effect)]
        // [HDR] 속성을 제거하여 인스펙터에서 Hex 코드 입력 가능 (기본값: #F21919)
        _GlowColor ("Glow Color", Color) = (0.949, 0.098, 0.098, 1.0)
        _GlowIntensity ("Glow Intensity", Range(1.0, 10.0)) = 1.0
        _GlowBloomSpread ("Glow Spread", Range(0.005, 0.06)) = 0.005

        [Header(Bracket Cutout)]
        _BracketRatio ("Bracket Coverage (Per Edge)", Range(0.3, 0.95)) = 0.95

        [Header(Inner Vignette)]
        _VignetteColor ("Vignette Color", Color) = (0.0, 0.0, 0.0, 1.0)
        _VignetteIntensity ("Vignette Intensity", Range(0.0, 1.0)) = 0.6
        _VignetteRadius ("Vignette Start Radius", Range(0.0, 1.0)) = 0.2
        _VignetteSmoothness ("Vignette Smoothness", Range(0.1, 2.0)) = 0.8

        // --- UGUI Canvas 필수 프로퍼티 ---
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _HexRadius;
            float _CornerRounding;
            float _GapWidth;
            float _BorderThickness;
            fixed4 _GlowColor;
            float _GlowIntensity;
            float _GlowBloomSpread;
            float _BracketRatio;

            // Vignette 프로퍼티
            fixed4 _VignetteColor;
            float _VignetteIntensity;
            float _VignetteRadius;
            float _VignetteSmoothness;

            // 정육각형 SDF: 꼭짓점이 위(pointy-top), 좌우가 수직 평평한 형태
            float sdPointyHexagon(float2 p, float r)
            {
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                p = abs(float2(p.y, p.x));
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
                return length(p) * sign(p.y);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPos);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv - 0.5;

                // 1. 내부 육각형 SDF 계산
                float dInner = sdPointyHexagon(p, _HexRadius);
                float innerMask = 1.0 - smoothstep(-_CornerRounding, _CornerRounding, dInner);

                // 2. 바깥 Glow 브래킷 육각형 SDF 계산
                float outerRadius = _HexRadius + _GapWidth;
                float dOuter = sdPointyHexagon(p, outerRadius);

                // 테두리 코어 선분 (두께 _BorderThickness)
                float borderCore = 1.0 - smoothstep(0.0, _CornerRounding * 2.0, abs(dOuter - (_BorderThickness * 0.5)));
                
                // 테두리 주변 은은한 빛 번짐(Bloom/Glow)
                float glowHalo = exp(-abs(dOuter) / _GlowBloomSpread);

                // 3. 6개 변 대칭 브래킷 분할 마스크
                float angle = atan2(p.y, p.x);
                const float SECTOR = 1.04719755; // 60도 (PI / 3)
                float normAngle = fmod(abs(angle) + (SECTOR * 0.5), SECTOR) - (SECTOR * 0.5);
                float edgePos = abs(normAngle) / (SECTOR * 0.5);

                float bracketCut = smoothstep(_BracketRatio, _BracketRatio - 0.06, edgePos);
                float bracketGlow = (borderCore * 2.0 + glowHalo * 1.2) * bracketCut;

                // 4. 텍스처 샘플링 (내부 마스크 영역)
                fixed4 tex = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                fixed3 innerColor = tex.rgb;

                // --- 내부 비네트(Vignette) 계산 ---
                // 중심(0,0)에서 육각형 반지름 대비 정규화된 거리 계산
                float normDist = length(p) / max(_HexRadius, 0.0001);
                float vigFactor = smoothstep(_VignetteRadius, _VignetteRadius + _VignetteSmoothness, normDist);
                vigFactor = saturate(vigFactor * _VignetteIntensity);

                // 비네트 색상과 블렌딩 (어두운 색상 또는 사용자 지정 색상)
                innerColor = lerp(innerColor, _VignetteColor.rgb, vigFactor * _VignetteColor.a);

                float innerAlpha = tex.a * innerMask;

                // 5. 발광 Glow 색상 (선택한 색상 * 강도)
                fixed3 glowRgb = _GlowColor.rgb * _GlowIntensity;

                // 6. 최종 색상 합성
                fixed3 finalRgb = (innerColor * innerAlpha) + (glowRgb * bracketGlow);
                float finalAlpha = saturate(innerAlpha + bracketGlow);

                #ifdef UNITY_UI_CLIP_RECT
                finalAlpha *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif

                if (finalAlpha <= 0.001)
                    discard;

                return fixed4(finalRgb, finalAlpha);
            }
            ENDCG
        }
    }
}