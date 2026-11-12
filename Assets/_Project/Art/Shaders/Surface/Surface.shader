// The maps' surfaces (map dressing stage 4; the user, 2026-11-12: "슬슬 그레이박스 말고 좀 더 퀄리티 있게 작업 가능한가").
// A URP Lit surface whose textures are laid on in metres by box mapping (SurfaceInput.hlsl), so the greybox builder's
// scaled cubes need no UVs and boxes side by side run one texture across their seams. Each material has a side set
// (walls) and a top set (floors, roofs, the tops of things), each a colour, an OpenGL normal, a roughness and an
// optional ambient occlusion map as ambientCG ships them (Art/Textures/Surfaces), read separately, so nobody packs
// channels. A gentle patchiness over metres hides the repeat on long walls. Materials are written by the builder from
// Tables/MapSurfaces (GreyboxBuilder.Surfaces); the passes follow URP 17.3's Lit, with the lit pass forward-only so the
// surfaces also draw under the deferred renderer.
Shader "ENFILADE/Surface"
{
    Properties
    {
        [MainTexture] _BaseMap("Side colour", 2D) = "white" {}
        [MainColor] _BaseColor("Side tint", Color) = (1, 1, 1, 1)
        [Normal][NoScaleOffset] _BumpMap("Side normal (OpenGL)", 2D) = "bump" {}
        [NoScaleOffset] _RoughnessMap("Side roughness", 2D) = "white" {}
        [NoScaleOffset] _OcclusionMap("Side ambient occlusion", 2D) = "white" {}
        _Tile("Side: metres one repeat covers", Float) = 3

        [NoScaleOffset] _TopMap("Top colour", 2D) = "white" {}
        _TopColor("Top tint", Color) = (1, 1, 1, 1)
        [Normal][NoScaleOffset] _TopBumpMap("Top normal (OpenGL)", 2D) = "bump" {}
        [NoScaleOffset] _TopRoughnessMap("Top roughness", 2D) = "white" {}
        [NoScaleOffset] _TopOcclusionMap("Top ambient occlusion", 2D) = "white" {}
        _TopTile("Top: metres one repeat covers", Float) = 3

        _RoughnessScale("Roughness scale", Range(0, 2)) = 1
        _Metallic("Metallic", Range(0, 1)) = 0
        _BumpScale("Normal strength", Range(0, 2)) = 1
        _OcclusionStrength("Ambient occlusion strength", Range(0, 1)) = 1
        _Variation("Large-scale variation", Range(0, 0.5)) = 0.12
        _VariationSize("Variation size (m)", Float) = 9
        [ToggleUI] _ObjectSpace("Map in the object's own space (moving parts)", Float) = 0

        // For URP's shared shadow and depth passes, which read them only when alpha clipping is on (never here).
        [HideInInspector] _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5

            #pragma vertex SurfacePassVertex
            #pragma fragment SurfacePassFragment

            // Universal pipeline keywords, as URP 17.3's Lit.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // Unity defined keywords.
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "SurfaceInput.hlsl"
            #include "SurfaceForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_instancing
            // Point and spot light shadows: the light's position instead of a direction.
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma multi_compile_instancing

            #include "SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // For screen-space ambient occlusion and screen-space decals: the face's own normal (the bumps are left out,
        // as Lit does without a normal map), tagged forward-only so the deferred renderer draws it too.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5

            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            #include "SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
