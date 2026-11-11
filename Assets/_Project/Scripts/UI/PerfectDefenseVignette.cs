using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The rim over the slowed counter window a perfect evade (or a Knight's parry) opens. Deep red-black since 2026-11-11
/// (user: "a plain dodge white, a success red"; it was a dark navy): the success's red burst (DodgeFlash) settles into it.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PerfectDefenseVignette : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); Rect rect = GetPixelAdjustedRect(); const int columns = 48, rows = 28;
        float strength = CombatFeedbackSettings.Instance != null ? CombatFeedbackSettings.Instance.perfectVignetteStrength : .8f;
        for (int y = 0; y <= rows; y++)
        for (int x = 0; x <= columns; x++)
        {
            float u = (float)x / columns, v = (float)y / rows;
            float radius = new Vector2((u - .5f) * 2f, (v - .5f) * 2f).magnitude;
            float alpha = Mathf.SmoothStep(0f, strength, Mathf.InverseLerp(.38f, 1.15f, radius));
            mesh.AddVert(new Vector3(rect.xMin + rect.width * u, rect.yMin + rect.height * v), new Color(.09f, .005f, .015f, alpha), Vector2.zero);
            if (x == columns || y == rows) continue;
            int a = y * (columns + 1) + x;
            mesh.AddTriangle(a, a + columns + 1, a + 1); mesh.AddTriangle(a + 1, a + columns + 1, a + columns + 2);
        }
    }
}
