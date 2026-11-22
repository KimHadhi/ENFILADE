using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A slanted plate drawn without a sprite or a shader: a parallelogram whose right and left edges lean by
/// <see cref="Configure"/>'s shear, filled over a share of its length (a tick at a plate's left end is the same plate,
/// filled a little).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SlantedPlate : MaskableGraphic
{
    private float share = 1f, slant;
    private Color fill = Color.white;

    public void Configure(float amount, float shear, Color tint)
    {
        share = amount;
        slant = shear;
        fill = tint;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f || fill.a <= 0f) return;
        float shear = Mathf.Clamp(slant, 0f, rect.width * .25f), right = (rect.width - shear) * Mathf.Clamp01(share);
        if (right <= 0f) return;
        int index = mesh.currentVertCount;
        Color32 tint = fill * color;
        mesh.AddVert(new Vector2(rect.xMin, rect.yMin), tint, Vector2.zero);
        mesh.AddVert(new Vector2(rect.xMin + shear, rect.yMax), tint, Vector2.zero);
        mesh.AddVert(new Vector2(rect.xMin + right + shear, rect.yMax), tint, Vector2.zero);
        mesh.AddVert(new Vector2(rect.xMin + right, rect.yMin), tint, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index + 2, index + 3, index);
    }
}
