using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A thin circle line for the HUD: a band of quads round the rect's middle with <see cref="FeelAssets.HairLine"/> mapped
/// across it, so the line keeps soft edges at any thickness. The HUD draws without anti-aliasing, and a ring of one to three
/// pixels built from plain quads steps like a staircase; FeelAssets.Ring cannot do it either, as its stroke is a share of
/// the ring's size and grows with it. Used by the headshot's snapping gold ring (<see cref="HitMarkerHUD"/>).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SoftRingGraphic : MaskableGraphic
{
    // Enough that the outline stays round at the largest ring the headshot draws (a radius of about 60 px).
    private const int Segments = 72;
    private float radius = 20f, thickness = 2f;

    public override Texture mainTexture => FeelAssets.HairLine;

    /// <summary>radius: from the rect's middle to the line's middle (px). thickness: how thick the line looks (px).</summary>
    public void Set(float lineRadius, float lineThickness)
    {
        lineRadius = Mathf.Max(0f, lineRadius); lineThickness = Mathf.Max(0f, lineThickness);
        if (Mathf.Approximately(radius, lineRadius) && Mathf.Approximately(thickness, lineThickness)) return;
        radius = lineRadius; thickness = lineThickness;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        // The hair line texture is solid through its middle third only, so the band is drawn HairLineSpan times as wide.
        float half = thickness * FeelAssets.HairLineSpan * .5f;
        if (radius <= 0f || half <= 0f) return;
        float inner = Mathf.Max(0f, radius - half), outer = radius + half;
        Color32 tint = color;
        Vector2 middle = rectTransform.rect.center;
        for (int i = 0; i <= Segments; i++)
        {
            float angle = i * Mathf.PI * 2f / Segments;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            // U stays in the texture's middle, away from the fade at its ends; V runs across the line.
            mesh.AddVert(middle + direction * inner, tint, new Vector2(.5f, 0f));
            mesh.AddVert(middle + direction * outer, tint, new Vector2(.5f, 1f));
        }
        for (int i = 0; i < Segments; i++)
        {
            int k = i * 2;
            mesh.AddTriangle(k, k + 1, k + 3);
            mesh.AddTriangle(k, k + 3, k + 2);
        }
    }
}
