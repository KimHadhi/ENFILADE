using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Distance culling by layer (the user's memo 40, "최적화, 컬링"): what lies on a layer named in Tables/Culling is
/// neither drawn nor casts a shadow beyond that row's distance from the camera. The distance is a sphere around the
/// camera, so turning on the spot never pops anything in or out. It applies to cameras that draw to the screen (the
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
        camera.layerCullSpherical = true;
    }

    private static void Read()
    {
        distances = new float[32];
        any = false;
        foreach (string id in GameTables.RowIds(TableName))
        {
            int layer = LayerMask.NameToLayer(id);
            if (layer < 0 || !GameTables.TryGetRow(TableName, id, out var row) || !row.TryGetValue("distance", out string text)
                || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float distance) || distance <= 0f)
                continue;
            distances[layer] = distance;
            any = true;
        }
    }
}
