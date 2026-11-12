// A gun heating from the muzzle back (GunHeat, a Point's ultimate; the user: "총구 쪽부터 달궈져서 빛나는 느낌 … 에이펙스
// 레전드의 윙맨 Thermal Rise"): light added over the gun's own shape, white-gold at the muzzle, orange, then a dull red
// that fades out by _HeatReach along the barrel toward the stock. It flickers along its length as hot metal does, its
// edges a little brighter. Additive, drawn by a shell over the gun (BodyShell); the muzzle and the barrel's way come in
// per gun through a property block, in world space, every frame.
Shader "Hidden/ENFILADE/GunHeat"
{
    Properties
    {
        _HeatOrigin ("The muzzle (world)", Vector) = (0, 0, 0, 0)
        _HeatAxis ("From the muzzle back along the gun (world, unit)", Vector) = (0, 0, -1, 0)
        _HeatReach ("How far back the heat goes (m)", Float) = 0.35
        _Heat ("Heat (0 cold, 1 at its hottest)", Float) = 1
        _Glow ("Brightness (above 1 blooms)", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Heat"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _HeatOrigin;
                float4 _HeatAxis;
                float _HeatReach;
                half _Heat;
                half _Glow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 viewWS : TEXCOORD2; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + normalWS * 0.0008;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // How far back from the muzzle this point of the gun is (m), and the heat left there.
                float along = dot(input.positionWS - _HeatOrigin.xyz, _HeatAxis.xyz);
                float k = saturate(along / max(0.001, _HeatReach));
                half heat = saturate((1.0h - (half)k) * _Heat * 1.15h) * step(-0.02, along);
                // Hot metal's restless glow: two slow waves along the barrel, crossed.
                half wave = sin(along * 90.0 + _Time.y * 7.0) * 0.5h + 0.5h;
                half wave2 = sin(along * 37.0 - _Time.y * 4.3 + input.positionWS.y * 60.0) * 0.5h + 0.5h;
                heat *= lerp(0.78h, 1.0h, wave * wave2);
                // Dull red, orange, gold, white as it gets hotter.
                half3 colour = lerp(half3(0.55h, 0.04h, 0.01h), half3(1.0h, 0.38h, 0.06h), saturate(heat * 2.0h));
                colour = lerp(colour, half3(1.0h, 0.85h, 0.45h), saturate(heat * 2.0h - 0.8h));
                colour = lerp(colour, half3(1.0h, 0.98h, 0.9h), saturate(heat * 3.0h - 2.2h));
                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half shape = 0.55h + 0.45h * (1.0h - facing);
                return half4(colour * heat * heat * shape * _Glow, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
