using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One plain plate in the health bar's own slanted shape, filling its rect: the pieces a hit knocks off the bar
/// (HealthBarFlair). Shear is how far its top edge sits right of its bottom edge, as on SlantedHealthGraphic.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SlantedPiece : MaskableGraphic
{
    private float shear;

    public float Shear
    {
        get => shear;
        set { if (shear == value) return; shear = value; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0f || r.height <= 0f) return;
        // Centred on the rect, so it spins about its middle.
        float s = Mathf.Clamp(shear, -r.width, r.width) * .5f;
        Color32 tint = color;
        mesh.AddVert(new Vector3(r.xMin - s, r.yMin), tint, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMin + s, r.yMax), tint, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMax + s, r.yMax), tint, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMax - s, r.yMin), tint, Vector2.zero);
        mesh.AddTriangle(0, 1, 2);
        mesh.AddTriangle(2, 3, 0);
    }
}
