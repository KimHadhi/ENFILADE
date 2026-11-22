// Arctic's Grenade Indicators' hologram material (materials/ugi/holo.vmt): VertexLitGeneric, $additive, its base texture
// ugi/holo, drawn with the engine's lighting suppressed, render.SetColorModulation(1, 0.65, 0) and render.SetBlend(0.5),
// over everything (cam.IgnoreZ). The picture is white with its static in the alpha, so what is added is the colour times
// the static times the blend (Source's additive blend with alpha: SrcAlpha One). The model's own UVs lay it on.
// Its 5 frames were cut 20 times a second (the AnimatedTexture proxy); the flicker rule makes them fade one into the
// next instead (_FrameA to _FrameB by _Mix, set by HudAddons.GrenadesHud). Drawn by HudAddons.GrenadeHoloPass.
Shader "Hidden/HudAddons/GrenadeHolo"
{
    Properties
    {
        _FrameA ("Frame", 2D) = "white" {}
        _FrameB ("Next frame", 2D) = "white" {}
        _Mix ("Into the next frame", Range(0, 1)) = 0
        _Color ("Colour (rgb) and blend (a)", Color) = (1, 0.65, 0, 0.5)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FrameA);
            SAMPLER(sampler_FrameA);
            TEXTURE2D(_FrameB);

            CBUFFER_START(UnityPerMaterial)
                float4 _FrameA_ST;
                float4 _FrameB_ST;
                half4 _Color;
                half _Mix;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 a = SAMPLE_TEXTURE2D(_FrameA, sampler_FrameA, input.uv);
                half4 b = SAMPLE_TEXTURE2D(_FrameB, sampler_FrameA, input.uv);
                half4 picture = lerp(a, b, _Mix);
                return half4(picture.rgb * _Color.rgb, picture.a * _Color.a);
            }
            ENDHLSL
        }
    }
}
