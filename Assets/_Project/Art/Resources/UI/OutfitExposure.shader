// The wardrobe photo studio's exposure (OutfitStudio): a dark piece's picture (black metal, dark cloth) lifted toward a
// readable middle by _Gain, and whatever then passes _Knee bent softly under white, so nothing burns out. Alpha is kept
// as it was. The studio measures the gain on the piece's first picture (Tables/WardrobePictures.csv Exposure rows) and
// leaves a picture that is light enough alone (it doesn't come here at all).
Shader "Hidden/ENFILADE/OutfitExposure"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "black" {}
        _Gain ("Gain", Float) = 1
        _Knee ("Knee", Float) = 0.75
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Gain;
            float _Knee;

            // Straight up to the knee, then bending toward 1 without ever reaching it.
            float3 Shoulder(float3 c)
            {
                float room = max(1.0 - _Knee, 1e-4);
                float3 over = max(c - _Knee, 0.0);
                return min(c, _Knee) + room * (1.0 - exp(-over / room));
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                float3 rgb = c.rgb;
            #if defined(UNITY_COLORSPACE_GAMMA)
                rgb = GammaToLinearSpace(rgb);
            #endif
                // The film holds colour over a cleared black, the piece's colour times its cover, so one multiply lifts the
                // piece and its soft edges alike.
                rgb = Shoulder(rgb * _Gain);
            #if defined(UNITY_COLORSPACE_GAMMA)
                rgb = LinearToGammaSpace(rgb);
            #endif
                return float4(rgb, c.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
