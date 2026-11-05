Shader "Hidden/RVFX/Adaptive Height Capture"
{
    // RVFX_PIPELINE_INDEPENDENT_PASS: direct draw/capture, no pipeline lighting dependency.
    HLSLINCLUDE
            #include "UnityCG.cginc"
            float4 _RVFXCaptureRect;
            struct Varyings { float4 position:SV_POSITION; float height:TEXCOORD0; };
            Varyings Vert(float4 position:POSITION)
            {
                Varyings o; float3 world=mul(unity_ObjectToWorld,position).xyz;
                float2 uv=(world.xz-_RVFXCaptureRect.xy)/_RVFXCaptureRect.zw;
                o.position=float4(uv*2-1,0.5,1);
                #if UNITY_UV_STARTS_AT_TOP
                o.position.y=-o.position.y;
                #endif
                o.height=world.y; return o;
            }
            float Frag(Varyings i):SV_Target { return i.height; }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend One One
            BlendOp Max
            HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma target 4.5
ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend One One
            BlendOp Max
            HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma target 4.5
ENDHLSL
        }
    }

    SubShader
    {
        Tags {  }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend One One
            BlendOp Max
            HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma target 4.5
ENDHLSL
        }
    }
    Fallback Off
}
