// A dodge's afterimage (DodgeDash): the body baked at that moment, drawn as the character itself, faintly see-through,
// as in the user's reference video. Each renderer's own main texture and colour come in per draw (lilToon _MainTex/_Color,
// URP _BaseMap/_BaseColor, whatever the outfit uses), so one shared material serves any outfit or modded character.
// The first pass puts only the ghost's nearest surface into depth, so the second shows one see-through skin rather than
// every layer inside it (hair under hair, the far arm through the body). Cut-out and see-through parts (hair cards,
// lace) stay cut by their texture's alpha (_Cut 1); opaque ones ignore it, as their own material does.
Shader "Hidden/ENFILADE/DodgeGhost"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _Cut ("Cut by the texture's alpha (1 cut-out or see-through, 0 opaque)", Float) = 0
        _Alpha ("See-through (0 gone, 1 solid)", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Cut;
            half _Alpha;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = TRANSFORM_TEX(input.uv, _MainTex);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }
        ENDHLSL

        // URP draws an object's SRPDefaultUnlit pass before its UniversalForward pass: depth first, then colour.
        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth

            half4 FragDepth(Varyings input) : SV_Target
            {
                clip(lerp(1.0, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * _Color.a, _Cut) - 0.5);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                half4 surface = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                clip(lerp(1.0, surface.a, _Cut) - 0.5);
                // A soft half-light from the sun keeps the body's shape readable without the scene's full lighting.
                Light sun = GetMainLight();
                half shade = lerp(0.7, 1.0, saturate(dot(normalize(input.normalWS), sun.direction)));
                return half4(surface.rgb * shade, _Alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
