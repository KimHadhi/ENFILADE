using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The crosshair's ring while a gun that throws pellets is in hand (user, 2026-11-10: "샷건은 들었을 때 원 조준선으로"):
/// a circle as wide as the pellets' pattern, so what stands inside it is what the shot covers (WeaponController aims each
/// pellet from the eyes at its place in it). Drawn as a mesh, not a sprite: its line keeps one thickness whatever the
/// radius, which changes with the gun, the view angle and the shot's kick, and a soft pixel either side keeps it smooth.
/// A wider band under it in the outline colour is the crosshair's border. Made and driven by DynamicCrosshairView.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CrosshairRing : MaskableGraphic
{
    // Round enough at any radius a crosshair has; the soft edge either side of a band (px).
    private const int Segments = 72;
    private const float Feather = 1f;

    private float radius, thickness = 2f, outlineWidth;
    private Color outlineColor = Color.clear;

    public static CrosshairRing Create(Transform parent)
    {
        var host = new GameObject("Pattern Ring", typeof(RectTransform), typeof(CanvasRenderer), typeof(CrosshairRing));
        host.layer = parent.gameObject.layer;
        var rect = (RectTransform)host.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        var ring = host.GetComponent<CrosshairRing>();
        ring.raycastTarget = false;
        return ring;
    }

    /// <summary>The ring's radius to the middle of its line and the line's thickness (canvas px), its colour and its border.</summary>
    public void Set(float radius, float thickness, Color color, float outlineWidth, Color outlineColor)
    {
        radius = Mathf.Max(0f, radius);
        thickness = Mathf.Max(.5f, thickness);
        outlineWidth = Mathf.Max(0f, outlineWidth);
        if (Mathf.Approximately(this.radius, radius) && Mathf.Approximately(this.thickness, thickness) && this.color == color
            && Mathf.Approximately(this.outlineWidth, outlineWidth) && this.outlineColor == outlineColor) return;
        this.radius = radius;
        this.thickness = thickness;
        this.outlineWidth = outlineWidth;
        this.outlineColor = outlineColor;
        // The rect holds the whole drawing, for the canvas's culling.
        rectTransform.sizeDelta = Vector2.one * ((radius + thickness * .5f + outlineWidth + Feather) * 2f);
        this.color = color;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (radius <= 0f) return;
        if (outlineWidth > 0f && outlineColor.a > 0f)
        {
            // Fades with the crosshair's own opacity, as its parts' borders do (CrosshairOutline).
            var border = outlineColor;
            border.a *= color.a;
            Band(vh, thickness + outlineWidth * 2f, border);
        }
        Band(vh, thickness, color);
    }

    // One band round the middle, `width` across: solid over its width and fading to nothing over a pixel either side.
    private void Band(VertexHelper vh, float width, Color32 tint)
    {
        float inner = Mathf.Max(0f, radius - width * .5f), outer = radius + width * .5f;
        Color32 clear = tint;
        clear.a = 0;
        int first = vh.currentVertCount;
        for (int i = 0; i <= Segments; i++)
        {
            float angle = i * (Mathf.PI * 2f / Segments);
            var away = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            vh.AddVert(away * Mathf.Max(0f, inner - Feather), clear, Vector4.zero);
            vh.AddVert(away * inner, tint, Vector4.zero);
            vh.AddVert(away * outer, tint, Vector4.zero);
            vh.AddVert(away * (outer + Feather), clear, Vector4.zero);
        }
        for (int i = 0; i < Segments; i++)
        {
            int here = first + i * 4, next = here + 4;
            for (int strip = 0; strip < 3; strip++)
            {
                vh.AddTriangle(here + strip, here + strip + 1, next + strip + 1);
                vh.AddTriangle(here + strip, next + strip + 1, next + strip);
            }
        }
    }
}
