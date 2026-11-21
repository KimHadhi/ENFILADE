// The hologram of Arctic's Grenade Indicators (GrenadeIndicators, the Arctic rows of GrenadeIndicators.csv): a live
// grenade's own model drawn small in front of the eye, as the addon draws it. Its material is the addon's ugi/holo
// (VertexLitGeneric, $additive, engine lighting suppressed, so the texture as it is), tinted by render.SetColorModulation
// and made fainter by render.SetBlend, drawn with cam.IgnoreZ: light added over everything, never hidden by a wall or the
// player's own arms. The texture is static (white noise in its alpha). The addon steps through its five frames 20 times a
// second; here two neighbouring frames are mixed (_Mix) so the static drifts smoothly at the table's slower rate, the
// user's rule against flicker.
Shader "Hidden/ENFILADE/GrenadeHolo"
{
    Properties
    {
        _MainTex ("Static frame", 2D) = "white" {}
        _NextTex ("Next static frame", 2D) = "white" {}
        _Mix ("Share of the next frame", Range(0, 1)) = 0
        _Color ("Colour (render.SetColorModulation)", Color) = (1, 0.65, 0, 1)
        _Blend ("Strength (render.SetBlend)", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GrenadeHolo"
            Tags { "LightMode" = "UniversalForward" }
            // Source's $additive on a material drawn with an alpha below one: its colour times its alpha added on.
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NextTex);
            SAMPLER(sampler_NextTex);

            CBUFFER_START(UnityPerMaterial)
                half _Mix;
                half4 _Color;
                half _Blend;
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
                // The model's own UVs, wrapped here (the frames are imported clamped, as every HUD picture is), sampled
                // with the unwrapped UV's slopes so a wrap leaves no seam of the smallest mip.
                float2 uv = frac(input.uv);
                float2 dx = ddx(input.uv), dy = ddy(input.uv);
                half4 now = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv, dx, dy);
                half4 next = SAMPLE_TEXTURE2D_GRAD(_NextTex, sampler_NextTex, uv, dx, dy);
                half4 noise = lerp(now, next, _Mix);
                return half4(noise.rgb * _Color.rgb, noise.a * _Color.a * _Blend);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
