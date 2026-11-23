using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Puts a camera that draws no world (the HUD's, the HUD effects', the standby black) on the pipeline's renderer that
/// runs no renderer features. URP runs a renderer's features on every camera that uses it, so the game renderer's SSAO
/// (PC_Renderer) ran a depth-normals pass and a full-screen ambient occlusion with its blur on each of these cameras every
/// frame, over a picture with nothing in it to darken (the 0.9.372 audit of the graphics settings; the user chose to take
/// it off them, 2026-11-23). PC_RPAsset lists PC_PlainRenderer for this. The renderer is found as the first one in the
/// current pipeline's list with no features, not by index or name, so a pipeline whose default renderer has none
/// (Mobile_RPAsset) simply keeps its default.
/// </summary>
public static class PlainRenderer
{
    /// <summary>Draws <paramref name="camera"/> with the current pipeline's first renderer that has no features.</summary>
    public static void Use(Camera camera)
    {
        if (camera == null || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return;
        var list = pipeline.rendererDataList;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == null || list[i].rendererFeatures.Count > 0) continue;
            camera.GetUniversalAdditionalCameraData().SetRenderer(i);
            return;
        }
    }
}
