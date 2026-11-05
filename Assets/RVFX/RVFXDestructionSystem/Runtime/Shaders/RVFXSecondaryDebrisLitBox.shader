Shader "Hidden/RVFX/RVFXDestructionSystem/SecondaryDebrisLitBox"
{
    // RVFX_PIPELINE_INDEPENDENT_PASS: direct draw/capture, no pipeline lighting dependency.
    HLSLINCLUDE
            #include "UnityCG.cginc"

            float4 _PointColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.position = UnityWorldToClipPos(worldPosition);
                o.worldPosition = worldPosition;
                o.normalWS = UnityObjectToWorldNormal(v.normal);
                o.color = _PointColor;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 lightDir = normalize(float3(0.35, 0.85, 0.25));
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPosition);

                float diffuse = saturate(dot(n, lightDir));
                float backFill = saturate(dot(n, -lightDir)) * 0.12;
                float rim = pow(1.0 - saturate(dot(n, viewDir)), 2.0) * 0.18;

                float lighting = 0.28 + diffuse * 0.72 + backFill + rim;
                return fixed4(i.color.rgb * lighting, i.color.a);
            }
    ENDHLSL

    SubShader
    {
        Tags { "Queue"="Geometry+50" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Blend Off
            Cull Off

            HLSLPROGRAM
#pragma target 3.0
#pragma vertex vert
#pragma fragment frag
ENDHLSL
        }
    }

    SubShader
    {
        Tags { "Queue"="Geometry+50" "RenderType"="Opaque" "RenderPipeline"="HDRenderPipeline" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Blend Off
            Cull Off

            HLSLPROGRAM
#pragma target 3.0
#pragma vertex vert
#pragma fragment frag
ENDHLSL
        }
    }

    SubShader
    {
        Tags { "Queue"="Geometry+50" "RenderType"="Opaque" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Blend Off
            Cull Off

            HLSLPROGRAM
#pragma target 3.0
#pragma vertex vert
#pragma fragment frag
ENDHLSL
        }
    }
    Fallback Off
}
