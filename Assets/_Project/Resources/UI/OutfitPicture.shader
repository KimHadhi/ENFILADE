// The wardrobe's pictures on their plates and tiles (OutfitPictureGraphic). The user, 2026-11-12: "귀걸이 같은 건 … 검어서
// 그런가? 진짜 안 보이는데". Three things make a piece read on a dark plate:
// - It stays see-through as the table says (per picture, uv0.z: Plate.alpha or Tile.alpha).
// - A thin light outline runs round its silhouette (per picture, uv0.w: Plate.edge or Tile.edge, times _EdgeColor's
//   alpha), _EdgeWidth pixels of a 256-pixel picture wide whatever the picture's own size.
// - Its thin bits (a chain, a ring's wire), which have little of the picture covered round them, keep up to
//   _ThinAlpha of their opacity, so the see-through doesn't eat them.
// The numbers are Tables/WardrobePictures.csv's Edge and Thin rows (OutfitPictureLook). Otherwise it is a UI shader as
// UI/Default is: it clips in the scrolled list (RectMask2D) and under masks.
Shader "UI/ENFILADE/OutfitPicture"
{
    Properties
    {
        [PerRendererData] _MainTex ("Picture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EdgeWidth ("Outline width (pixels of a 256 picture)", Float) = 2
        _EdgeColor ("Outline colour", Color) = (1,1,1,0.8)
        _ThinAlpha ("Thin bits' opacity", Range(0, 1)) = 0.95
        _ThinBroad ("Cover round a pixel that is no longer thin", Range(0.05, 1)) = 0.6

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="False"
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
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float4 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _EdgeWidth;
            fixed4 _EdgeColor;
            float _ThinAlpha;
            float _ThinBroad;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                const float2 ways[8] =
                {
                    float2(1, 0), float2(0.7071, 0.7071), float2(0, 1), float2(-0.7071, 0.7071),
                    float2(-1, 0), float2(-0.7071, -0.7071), float2(0, -1), float2(0.7071, -0.7071)
                };
                float2 uv = i.texcoord.xy;
                half4 picture = tex2D(_MainTex, uv) + _TextureSampleAdd;
                // Round the pixel at the outline's width and at half of it: the most cover there (the outline reaches
                // that far out of the silhouette) and how much is covered on average (a thin bit has little round it).
                float2 reach = _EdgeWidth / 256.0;
                half most = 0;
                half around = 0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    half far = tex2D(_MainTex, uv + ways[k] * reach).a;
                    half near = tex2D(_MainTex, uv + ways[k] * reach * 0.5).a;
                    most = max(most, max(far, near));
                    around += far + near;
                }
                around *= 0.0625;
                half fill = i.texcoord.z;
                half thin = 1 - smoothstep(0, _ThinBroad, around);
                half body = picture.a * lerp(fill, max(fill, _ThinAlpha), thin);
                half edge = saturate(most - picture.a) * i.texcoord.w * _EdgeColor.a;
                // The piece over its outline.
                half alpha = body + edge * (1 - body);
                half3 rgb = (picture.rgb * body + _EdgeColor.rgb * edge * (1 - body)) / max(alpha, 1e-4);
                half4 color = half4(rgb, alpha) * i.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
