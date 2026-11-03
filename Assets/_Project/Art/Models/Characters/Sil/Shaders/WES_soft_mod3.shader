Shader "Unlit/WES_soft_mod3"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
		_Color("Color", Color) = (1,1,1,1)
		_RimColor("Rim Color", Color) = (0,0,0,0)
		_InvFade("Inv fade", Range(0, 100)) = 100
		_DistortionSharpness ("Distortion Sharpness", Range(1,10)) = 3
		_DistortionPower ("Distortion Power", Range(-2,2)) = 1
	}
	SubShader
	{
		Tags{ "RenderType" = "Transparent" "Queue" = "Transparent-1" }
		LOD 100

		// GrabPass is Built-in only. URP copies what is already drawn into _CameraOpaqueTexture before transparent objects draw.
		Pass
		{
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				half3 normal : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
				float4 grabPos : TEXCOORD1;
				half3 viewNormal : TEXCOORD2;
				float vn : TEXCOORD3;
				float frustumFactor : TEXCOORD4;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			fixed4 _RimColor;
			float _InvFade;
			float _DistortionSharpness;
			float _DistortionPower;
			UNITY_DECLARE_SCREENSPACE_TEXTURE(_CameraOpaqueTexture);

			UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
			float4 _CameraDepthTexture_TexelSize;

			// ref: https://github.com/lukis101/VRCUnityStuffs/blob/master/Shaders/DJL/Overlays/WorldPosOblique.shader
			
			#define PM UNITY_MATRIX_P
			inline float4 CalculateFrustumCorrection()
			{
				float x1 = -PM._31/(PM._11*PM._34);
				float x2 = -PM._32/(PM._22*PM._34);
				return float4(x1, x2, 0, PM._33/PM._34 + x1*PM._13 + x2*PM._23);
			}
			inline float CorrectedLinearEyeDepth(float z, float B)
			{
				return 1.0 / (z/PM._34 + B);
			}
			#undef PM

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_SETUP_INSTANCE_ID(v);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.grabPos = ComputeScreenPos(o.vertex); // URP's copy isn't flipped the way a grab texture is
				COMPUTE_EYEDEPTH(o.grabPos.z);

				float3 viewDir = normalize(mul(UNITY_MATRIX_MV, v.vertex).xyz);
				o.viewNormal = COMPUTE_VIEW_NORMAL;
				o.vn = dot(viewDir, o.viewNormal);
				o.frustumFactor = dot(o.vertex, CalculateFrustumCorrection());
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				fixed4 col = tex2D(_MainTex, i.uv);

				float perspectiveDivide = 1.0f / i.vertex.w;
				i.frustumFactor *= perspectiveDivide;
				float2 depthPos = i.grabPos.xy * perspectiveDivide;

				float depthZ = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, depthPos);
				float sceneZ = CorrectedLinearEyeDepth(depthZ, i.frustumFactor);
				float partZ = i.grabPos.z;
				if (sceneZ > partZ) {
					float fade = saturate(_InvFade * (sceneZ - partZ));
					col.a *= fade;
				}

				col *= _Color;
				float rimLight = pow(1 - abs(i.vn), 4);
				col.rgb += rimLight * _RimColor;

				float2 grabUV = i.grabPos.xy / i.grabPos.w;
				float2 distortion = pow(saturate(1 + i.vn), _DistortionSharpness) * col.a;
				float2 grabAspectRatio = float2(_ScreenParams.x / _ScreenParams.y,1);
				grabUV -= distortion * i.viewNormal.xy / (i.viewNormal.z + 1) * _DistortionPower / grabAspectRatio;
				fixed3 baseColor = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraOpaqueTexture, grabUV);
				col.rgb = lerp(baseColor, col.rgb, col.a);
				col.a = 1;

				return col;
			}
			ENDCG
		}
	}
}