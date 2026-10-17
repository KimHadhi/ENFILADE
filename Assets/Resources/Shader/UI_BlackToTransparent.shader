Shader "UI/Black To Transparent"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _Color ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0.1, 3.0)) = 1.0
        _BlackLevel ("Black Removal", Range(0.0, 0.5)) = 0.02

        // Required for Unity UI masking
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)]
        _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
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

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;

            float _Intensity;
            float _BlackLevel;

            v2f vert(appdata_t v)
            {
                v2f OUT;

                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;

                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, IN.texcoord);

                // Find how bright this pixel is.
                // Pure black = 0
                // Bright pixels approach 1
                float brightness = max(tex.r, max(tex.g, tex.b));

                // Convert blackness into transparency.
                float alpha = saturate(
                    (brightness - _BlackLevel) /
                    max(0.0001, 1.0 - _BlackLevel)
                );

                fixed3 rgb = tex.rgb * _Intensity;

                fixed4 color;
                color.rgb = rgb * IN.color.rgb;
                color.a = alpha * tex.a * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(
                    IN.worldPosition.xy,
                    _ClipRect
                );
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }

            ENDCG
        }
    }
}