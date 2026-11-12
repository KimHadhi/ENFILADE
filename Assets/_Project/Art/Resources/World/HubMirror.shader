// The hub stage's mirror glass (HubMirror): the picture the mirror's camera took through the glass, laid on it turned left
// for right as a mirror shows it and a touch darker than the room, or dark glass with one soft band of sheen while the
// mirror isn't drawing (the results on the stage, too far, seen from behind). Unlit: the picture is already lit, and the
// eye's camera grades it with the rest of the frame.
// _Window maps the glass's own uv (0-1, u to the right of someone facing it) to the part of the glass the picture holds:
// picture uv = uv * _Window.xy + _Window.zw (HubMirror.Window).
Shader "Hidden/ENFILADE/HubMirror"
{
    Properties
    {
        _MainTex ("Reflection", 2D) = "black" {}
        _Window ("Window (scale xy, offset zw)", Vector) = (-1, 1, 1, 0)
        _Tint ("Tint", Color) = (0.93, 0.93, 0.93, 1)
        _Live ("Live", Range(0, 1)) = 0
        _Dark ("Dark glass", Color) = (0.035, 0.037, 0.045, 1)
        _Sheen ("Sheen", Range(0, 1)) = 0.06
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "HubMirror"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Window;
                half4 _Tint;
                half _Live;
                half4 _Dark;
                half _Sheen;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 picture = input.uv * _Window.xy + _Window.zw;
                half3 reflection = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, picture).rgb * _Tint.rgb;
                // Dark glass still reads as glass: near-black, one soft diagonal band of light across it.
                half band = saturate(1.0h - abs(input.uv.x * 0.55h + input.uv.y - 0.95h) * 3.0h);
                half3 dark = _Dark.rgb + band * band * _Sheen;
                return half4(lerp(dark, reflection, saturate(_Live)), 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
