// A runner's glowing eyes (ZombieBody.Eyes.cs), at the user's word: runners were deadly but couldn't be told apart.
// Billboard on: a quad turned to the camera at the eye, a white-hot red dot in a soft halo, never smaller on screen
// than _MinAngle (radians) so a runner far off still shows its eyes, lifted toward the camera so the face doesn't cover
// it. Billboard off: the eye's afterimage (a TrailRenderer), bright down its middle and fading to its edges; its colour
// gradient fades it along its length. Additive; bloom makes the glow.
Shader "Hidden/ENFILADE/RunnerEyes"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (6, 0.3, 0.12, 1)
        _Billboard ("Billboard", Float) = 1
        _Size ("Size (m)", Float) = 0.16
        _MinAngle ("Least size on screen (radians)", Float) = 0.004
        _Lift ("Toward the camera (m)", Float) = 0.04
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "RunnerEyes"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Billboard;
                float _Size;
                float _MinAngle;
                float _Lift;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                if (_Billboard > 0.5)
                {
                    float3 center = TransformWorldToView(TransformObjectToWorld(float3(0, 0, 0)));
                    float size = max(_Size, -center.z * _MinAngle);
                    float3 position = center + float3(input.positionOS.xy * size, _Lift);
                    output.positionCS = TransformWViewToHClip(position);
                }
                else
                {
                    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                }
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half glow;
                half3 tint = _Color.rgb;
                if (_Billboard > 0.5)
                {
                    float2 p = input.uv * 2.0 - 1.0;
                    float r2 = dot(p, p);
                    half core = exp(-r2 * 70.0);
                    half halo = exp(-r2 * 7.0) * 0.35;
                    glow = (core * 1.6 + halo) * saturate(1.0 - r2);
                    // The middle burns whiter.
                    tint = lerp(tint, max(tint.r, 1.0).xxx * half3(1.0, 0.55, 0.45), core * 0.6);
                }
                else
                {
                    half across = 1.0 - abs(input.uv.y * 2.0 - 1.0);
                    glow = across * across;
                }
                return half4(tint * glow * input.color.rgb * input.color.a, 0);
            }
            ENDHLSL
        }
    }
}
