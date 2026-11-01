#ifndef GET_MAIN_DIRECTIONAL_LIGHT_INCLUDED
#define GET_MAIN_DIRECTIONAL_LIGHT_INCLUDED

// URP declares _MainLightColor in every shader, but the Shader Graph
// preview compiles this file on its own, so it needs a fallback.
#if defined(SHADERGRAPH_PREVIEW)
float4 _MainLightColor;
#endif

void GetMainDirectionalLight_float(
    out float3 LightColor
)
{
#if defined(SHADERGRAPH_PREVIEW)
    LightColor = float3(1.0, 1.0, 1.0);
#else
    LightColor = _MainLightColor.rgb;
#endif
}

#endif
