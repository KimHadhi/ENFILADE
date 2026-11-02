using UnityEngine;
using UnityEngine.UI;

/// <summary>A kill feed row's plate: slanted at the left end, shading from one colour to another, with a band of light that can cross it.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class KillRowPlate : MaskableGraphic
{
    private const float Shear = 9f, SheenWidth = .22f;
    private Color from, to, sheenTint;
    private float sheen = -1f;

    public void Paint(Color left, Color right)
    {
        from = left; to = right;
        raycastTarget = false;
        SetVerticesDirty();
    }

    /// <summary>position is 0-1 along the plate; below 0 there is no band.</summary>
    public void Sheen(float position, Color tint)
    {
        sheen = position; sheenTint = tint;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        Band(mesh, rect, 0f, 1f, from, to);
        if (sheen < 0f) return;
        Color clear = sheenTint;
        clear.a = 0f;
        float start = Mathf.Max(0f, sheen - SheenWidth), end = Mathf.Min(1f, sheen + SheenWidth), middle = Mathf.Clamp(sheen, 0f, 1f);
        Band(mesh, rect, start, middle, clear, sheenTint);
        Band(mesh, rect, middle, end, sheenTint, clear);
    }

    // Parallel to the left end, so the band of light leans with the plate.
    private void Band(VertexHelper mesh, Rect rect, float a, float b, Color left, Color right)
    {
        if (b <= a) return;
        float span = rect.width - Shear;
        float x0 = rect.xMin + a * span, x1 = rect.xMin + b * span;
        int index = mesh.currentVertCount;
        Color32 l = left * color, r = right * color;
        mesh.AddVert(new Vector2(x0, rect.yMin), l, Vector2.zero);
        mesh.AddVert(new Vector2(x0 + Shear, rect.yMax), l, Vector2.zero);
        mesh.AddVert(new Vector2(x1 + Shear, rect.yMax), r, Vector2.zero);
        mesh.AddVert(new Vector2(x1, rect.yMin), r, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index + 2, index + 3, index);
    }
}
