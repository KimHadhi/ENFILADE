using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Distance culling by layer (the user's memo 40, "최적화, 컬링"): what lies on a layer named in Tables/Culling is
/// neither drawn nor casts a shadow beyond that row's distance from the camera. Under URP the distance runs along the
/// view (URP has no sphere): toward the edges of the view things stay drawn a little further, and turning toward one
/// just past the distance may hide it. It applies to cameras that draw to the screen (the
/// player's view, third person, spectating), not to cameras drawing into a texture: a scope's magnified picture keeps
/// its long sight. Each camera is set once, the first time it draws. A row: id = a layer's name, distance in metres;
/// 0, a missing row or a layer that doesn't exist leaves that layer as far as the camera's far plane. Behind walls the
/// baked occlusion culling hides things whatever their distance (GreyboxBuilder.MarkStatic); this is for open ground.
/// </summary>
public static class CameraCulling
{
    public const string TableName = "Culling";
    private static readonly HashSet<int> prepared = new HashSet<int>();
    private static float[] distances;
    private static bool any;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        prepared.Clear();
        distances = null;
        RenderPipelineManager.beginCameraRendering -= Prepare;
        RenderPipelineManager.beginCameraRendering += Prepare;
    }

    private static void Prepare(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game || camera.targetTexture != null || !prepared.Add(camera.GetInstanceID())) return;
        if (distances == null) Read();
        if (!any) return;
        camera.layerCullDistances = distances;
        // The sphere is the built-in renderer's alone: under URP, which this project draws with, setting it only warns
        // ("You can use Camera.layerCullSpherical only with the built-in renderer", the user's console, 2026-11-12).
        if (GraphicsSettings.currentRenderPipeline == null) camera.layerCullSpherical = true;
    }

    private static void Read()
    {
        distances = new float[32];
        any = false;
        foreach (string id in GameTables.RowIds(TableName))
        {
            int layer = LayerMask.NameToLayer(id);
            if (layer < 0 || !GameTables.TryGetRow(TableName, id, out var row) || !row.TryGetValue("distance", out string text)
                || !TableCells.TryNumber(text, out float distance) || distance <= 0f)
                continue;
            distances[layer] = distance;
            any = true;
        }
    }
}
