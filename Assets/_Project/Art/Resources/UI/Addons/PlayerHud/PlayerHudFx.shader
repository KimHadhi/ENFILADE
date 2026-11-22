// HUD Mk II's special effects (sfx.lua, "Cool stuff from 4HUD"), as one pass laying the HUD's own picture on the screen.
// The addon draws its HUD into a render target, darkens every other line of it (scanlines), and lays it on the screen
// three times: a cyan copy a hair to one side, a red copy added a hair to the other, and the HUD itself at alpha 0.75
// (aberration); while a hit's glitch runs it lays it in horizontal strips moved sideways, tinted pink. Unity's canvas
// draws the HUD into _MainTex with premultiplied alpha; Garry's Mod's render target kept the alpha each layer was drawn
// with squared (its blend writes src.a * src.a), so a faint box is fainter still once laid down: a.a is that alpha here.
// The three layers (cyan over, red added, the HUD over) fold into one premultiplied result:
//   colour = main * Am + (1 - Am) * (cyan * Ac + red * Ar),   alpha = 1 - (1 - Ac) * (1 - Am)
// Vertex data (PlayerHudFxImage): uv0 the HUD's own place, uv1.x / uv1.y the cyan and red copies' shift in u, the vertex
// colour's rgb the HUD's tint and its alpha whether the copies are drawn (1) or not (0).
Shader "Hidden/HudAddons/PlayerHudFx"
{
    Properties
    {
        [PerRendererData] _MainTex ("HUD", 2D) = "black" {}
        _Alpha ("HUD alpha", Float) = 0.75
        _Scan ("Scanline keep (1 = off)", Float) = 0.6863
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "False" }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 shift : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 shift : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float _Alpha;
            float _Scan;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                o.shift = v.shift;
                return o;
            }

            // The HUD's picture outside its edges is empty, as the addon's strips moved off the screen show nothing.
            float4 Hud(float2 uv)
            {
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                return tex2D(_MainTex, saturate(uv)) * inside;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 main = Hud(i.uv);
                float am = main.a * main.a * _Alpha;
                float3 colour = main.rgb * i.color.rgb * am;
                float keep = 1.0 - am;
                if (i.color.a > 0.5)
                {
                    float4 cyan = Hud(i.uv + float2(i.shift.x, 0.0));
                    float4 red = Hud(i.uv + float2(i.shift.y, 0.0));
                    float ac = cyan.a * cyan.a * _Alpha;
                    float ar = red.a * red.a * _Alpha;
                    colour += (1.0 - am) * (float3(0.0, cyan.g, cyan.b) * ac + float3(red.r * ar, 0.0, 0.0));
                    keep = (1.0 - ac) * (1.0 - am);
                }
                // Every other line of the screen: the addon's black lines (alpha 175, blended src.a, src.a) kept 175/255 of
                // the HUD's colours there and left its alpha alone.
                float odd = fmod(floor(i.pos.y), 2.0);
                colour *= lerp(1.0, _Scan, odd);
                return fixed4(colour, 1.0 - keep);
            }
            ENDCG
        }
    }
}
