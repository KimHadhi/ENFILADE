Shader "Hidden/RVFX/RVFXDestructionSystem/GPUDebrisPoints"
{
    // RVFX_PIPELINE_INDEPENDENT_PASS: direct draw/capture, no pipeline lighting dependency.
    HLSLINCLUDE
            #include "UnityCG.cginc"

            struct DebrisParticle
            {
                float4 positionLife;
                float4 velocityRadius;
                float4 rotationQuat;
                float4 angularVelocityDamping;
            };

            StructuredBuffer<DebrisParticle> _Particles;
            float _TimeNow;
            float4 _PointColor;

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float active : TEXCOORD2;
                float4 color : COLOR0;
            };

            float3 RotateByQuat(float3 v, float4 q)
            {
                q = normalize(q);
                return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
            }

            float2 GetTriangleCorner(uint vertexID)
            {
                uint c = vertexID % 6;

                if (c == 0) return float2(-0.5, -0.5);
                if (c == 1) return float2(-0.5,  0.5);
                if (c == 2) return float2( 0.5,  0.5);
                if (c == 3) return float2(-0.5, -0.5);
                if (c == 4) return float2( 0.5,  0.5);
                return float2(0.5, -0.5);
            }

            // CPU visual mesh uses a local cube from -0.5 to +0.5 and object scale = radius * 2.
            // GPU must follow the same convention so Point Radius / Scale looks identical in both modes.
            void GetCpuMatchingCubeVertex(uint vertexID, out float3 localPosition, out float3 localNormal)
            {
                uint face = vertexID / 6;
                float2 c = GetTriangleCorner(vertexID);

                if (face == 0)
                {
                    localPosition = float3(0.5, c.y, -c.x);
                    localNormal = float3(1.0, 0.0, 0.0);
                    return;
                }

                if (face == 1)
                {
                    localPosition = float3(-0.5, c.y, c.x);
                    localNormal = float3(-1.0, 0.0, 0.0);
                    return;
                }

                if (face == 2)
                {
                    localPosition = float3(c.x, 0.5, -c.y);
                    localNormal = float3(0.0, 1.0, 0.0);
                    return;
                }

                if (face == 3)
                {
                    localPosition = float3(c.x, -0.5, c.y);
                    localNormal = float3(0.0, -1.0, 0.0);
                    return;
                }

                if (face == 4)
                {
                    localPosition = float3(c.x, c.y, 0.5);
                    localNormal = float3(0.0, 0.0, 1.0);
                    return;
                }

                localPosition = float3(-c.x, c.y, -0.5);
                localNormal = float3(0.0, 0.0, -1.0);
            }

            v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                v2f o;

                DebrisParticle p = _Particles[instanceID];
                float active = step(_TimeNow, p.positionLife.w);
                float radius = max(0.001, p.velocityRadius.w);

                float3 localPosition;
                float3 localNormal;
                GetCpuMatchingCubeVertex(vertexID, localPosition, localNormal);

                float4 q = p.rotationQuat;
                if (dot(q, q) <= 0.0001)
                    q = float4(0.0, 0.0, 0.0, 1.0);

                float visualSize = radius * 2.0;
                float3 worldOffset = RotateByQuat(localPosition * visualSize, q);
                float3 normalWS = normalize(RotateByQuat(localNormal, q));
                float3 worldPosition = p.positionLife.xyz + worldOffset;

                o.position = UnityWorldToClipPos(worldPosition);
                o.worldPosition = worldPosition;
                o.normalWS = normalWS;
                o.active = active;
                o.color = _PointColor;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(i.active - 0.5);

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
            // Keep both sides enabled for robustness. The GPU cube uses the same
            // -0.5..0.5 local cube convention as the CPU lit box, but procedural
            // triangles can still be sensitive to platform-specific winding.
            Cull Off

            HLSLPROGRAM
#pragma target 4.5
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
            // Keep both sides enabled for robustness. The GPU cube uses the same
            // -0.5..0.5 local cube convention as the CPU lit box, but procedural
            // triangles can still be sensitive to platform-specific winding.
            Cull Off

            HLSLPROGRAM
#pragma target 4.5
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
            // Keep both sides enabled for robustness. The GPU cube uses the same
            // -0.5..0.5 local cube convention as the CPU lit box, but procedural
            // triangles can still be sensitive to platform-specific winding.
            Cull Off

            HLSLPROGRAM
#pragma target 4.5
#pragma vertex vert
#pragma fragment frag
ENDHLSL
        }
    }
    Fallback Off
}
