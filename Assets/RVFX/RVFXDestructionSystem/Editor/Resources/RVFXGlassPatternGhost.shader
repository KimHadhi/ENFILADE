Shader "Hidden/RVFX/GlassPatternGhost"
{
    SubShader
    {
        Tags { "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct Varying { float4 position : SV_POSITION; float3 normal : TEXCOORD0; };
            Varying vert(Input input)
            {
                Varying output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                return output;
            }
            float4 frag(Varying input) : SV_Target
            {
                float shade = .7 + .3 * saturate(dot(normalize(input.normal), normalize(float3(.3,.8,.5))));
                return float4(float3(.47,.53,.57) * shade, .28);
            }
            ENDCG
        }
    }
}
