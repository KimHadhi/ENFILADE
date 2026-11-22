// The MW3 Death Screen addon's picture work (lua/autorun/mw3_deathscreen.lua), drawn by HudAddons.DeathScreenPass into the
// world camera's picture after its post effects and before the HUD camera draws over it, in the order the addon's hooks
// ran in Garry's Mod: RenderScreenspaceEffects (the colour), then HUDPaintBackground (the blur, the vignette, the blood),
// then the HUD. Garry's Mod did all of it on the screen's 8-bit gamma-encoded values, so it is done here on those too:
// pass 0 encodes the picture into an 8-bit target, the blur reads and writes such targets, and pass 2 lays the two
// pictures on in the same encoding and decodes back to the camera's linear light. Blending the pictures in linear light
// (what a UI canvas does in this project) would show the vignette's soft edges lighter than the addon's.
// The maths of passes 0 and 1 is Garry's Mod's own, read from its compiled shaders (garrysmod_dir.vpk:
// shaders/fxc/g_colourmodify_ps20.vcs and g_screenspace_ps20.vcs, and the offsets game_shader_generic_garrysmod.dll gives
// the latter for pp/blurscreen's $blur).
Shader "Hidden/ENFILADE/AddonDeathScreen"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

        TEXTURE2D(_DeathSource);
        TEXTURE2D(_DeathVignette);
        TEXTURE2D(_DeathBlood);
        // xy: the target's size in pixels, zw: one over it.
        float4 _DeathSize;
        // GMod's colour modify at this level: $pp_colour_addr/addg/addb; brightness, contrast, colour; mulr/mulg/mulb.
        float4 _DeathAdd;
        float4 _DeathModify;
        float4 _DeathMul;
        // xy: the blur's sample spacing as a share of the picture.
        float4 _DeathStep;
        // x: the vignette's opacity, y: the blood's (0-1).
        float4 _DeathFilm;

        struct Varyings { float4 positionCS : SV_POSITION; };

        // One triangle over the whole target.
        Varyings Vert(uint vertexID : SV_VertexID)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
            return output;
        }

        float3 Sampled(float2 uv)
        {
            return SAMPLE_TEXTURE2D_LOD(_DeathSource, sampler_LinearClamp, uv, 0).rgb;
        }
        ENDHLSL

        // 0: DrawColorModify (pp/colour, g_colourmodify_ps20) on the picture's encoded values, which it first takes as the
        // screen shows them (0-1). Each channel gets its add; the colour pulls it toward the three channels' mean (not a
        // luminance); the mul terms add each channel's lead over the other two, squared; then the brightness is added and
        // the contrast multiplies. The result is held to 0-1 as Garry's Mod's 8-bit frame buffer held it.
        Pass
        {
            Name "ColourModify"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float3 shown = LOAD_TEXTURE2D(_DeathSource, uint2(input.positionCS.xy)).rgb;
                float3 c = saturate(LinearToSRGB(max(shown, 0.0))) + _DeathAdd.rgb;
                float grey = (c.r + c.g + c.b) * (1.0 / 3.0);
                float3 lead = float3(max(c.r - c.g, 0.0) + max(c.r - c.b, 0.0),
                                     max(c.g - c.r, 0.0) + max(c.g - c.b, 0.0),
                                     max(c.b - c.r, 0.0) + max(c.b - c.g, 0.0));
                float3 o = c * _DeathModify.z + grey * (1.0 - _DeathModify.z) + _DeathMul.rgb * lead * lead;
                o = (o + _DeathModify.x) * _DeathModify.y;
                return float4(saturate(o), 1.0);
            }
            ENDHLSL
        }

        // 1: one of pp/blurscreen's passes (GMODScreenspace with $blur, g_screenspace_ps20): the mean of nine filtered
        // samples, the pixel and its eight neighbours _DeathStep away (a 3x3 box with that spacing), read from the last
        // pass's encoded picture as the addon's UpdateScreenEffectTexture copy was.
        Pass
        {
            Name "Blur"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionCS.xy * _DeathSize.zw;
                float2 d = _DeathStep.xy;
                float3 sum = Sampled(uv);
                sum += Sampled(uv + float2(-d.x, -d.y));
                sum += Sampled(uv + float2(d.x, -d.y));
                sum += Sampled(uv + float2(-d.x, d.y));
                sum += Sampled(uv + float2(d.x, d.y));
                sum += Sampled(uv + float2(-d.x, 0.0));
                sum += Sampled(uv + float2(0.0, -d.y));
                sum += Sampled(uv + float2(d.x, 0.0));
                sum += Sampled(uv + float2(0.0, d.y));
                return float4(sum * (1.0 / 9.0), 1.0);
            }
            ENDHLSL
        }

        // 2: the vignette, then the blood over it, each stretched over the whole screen (the addon's
        // DrawTexturedRect(0, 0, ScrW(), ScrH())) and laid on with its own alpha times the draw colour's (UnlitGeneric,
        // $translucent, white vertex colour), in the encoded values; then back to linear light for the camera.
        // The camera's own picture is stored bottom row first (UniversalCameraData.IsHandleYFlipped), as Unity's pictures
        // are, so a pixel's row counted from that first row is the picture's v.
        Pass
        {
            Name "Film"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float3 c = LOAD_TEXTURE2D(_DeathSource, uint2(input.positionCS.xy)).rgb;
                float2 uv = input.positionCS.xy * _DeathSize.zw;
                float4 vignette = SAMPLE_TEXTURE2D_LOD(_DeathVignette, sampler_LinearClamp, uv, 0);
                c = lerp(c, LinearToSRGB(vignette.rgb), vignette.a * _DeathFilm.x);
                float4 blood = SAMPLE_TEXTURE2D_LOD(_DeathBlood, sampler_LinearClamp, uv, 0);
                c = lerp(c, LinearToSRGB(blood.rgb), blood.a * _DeathFilm.y);
                return float4(SRGBToLinear(saturate(c)), 1.0);
            }
            ENDHLSL
        }
    }
}
