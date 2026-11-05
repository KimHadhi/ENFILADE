Shader "Hidden/RVFX/SlicePatternSource"
{
    SubShader
    {
        Pass
        {
            ZWrite On
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
                float shade = .42 + .58 * saturate(dot(normalize(input.normal), normalize(float3(.4,.85,-.6))));
                return float4(float3(.55,.57,.59) * shade, 1);
            }
            ENDCG
        }
    }
}
