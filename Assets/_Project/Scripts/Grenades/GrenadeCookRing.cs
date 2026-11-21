using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The cook ring round the crosshair: [MW] Quick Grenade and Grenade Indicator's fuse timer (Indicator:DrawTimer), kept as
/// it is (the user, 2026-11-22: "원본대로 하라고"). One ring all the way round, its inside at a radius and its width
/// outward, drawn as the addon's DrawArc draws it: a polygon of so many sides starting at 0° (the screen's right) and
/// going round with the screen's y, one flat colour, hard edges (surface.DrawPoly, nothing feathered). It took the place
/// of the earlier ring (a dark band with a white arc running down, after "Quick Nade Timer HUD"). GrenadeHUD makes it
/// beside the HUD crosshair and says what to show (its radius closing in as the fuse burns, its colour and strength).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class GrenadeCookRing : MaskableGraphic
{
    private float inner = 40f, width = 2f;
    private int sides = 30;

    public static GrenadeCookRing Create(Transform parent)
    {
        var host = new GameObject("Grenade Fuse Ring", typeof(RectTransform), typeof(CanvasRenderer), typeof(GrenadeCookRing));
        host.layer = parent.gameObject.layer;
        var rect = (RectTransform)host.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        var ring = host.GetComponent<GrenadeCookRing>();
        ring.raycastTarget = false;
        return ring;
    }

    /// <summary>The ring's inside radius and its width (canvas px), its number of sides and its colour, alpha included.</summary>
    public void Show(float innerRadius, float thickness, int segments, Color tint)
    {
        innerRadius = Mathf.Max(0f, innerRadius);
        thickness = Mathf.Max(0f, thickness);
        segments = Mathf.Max(1, segments);
        if (Mathf.Approximately(inner, innerRadius) && Mathf.Approximately(width, thickness) && sides == segments && color == tint) return;
        inner = innerRadius;
        width = thickness;
        sides = segments;
        rectTransform.sizeDelta = Vector2.one * ((inner + width) * 2f);
        color = tint;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (width <= 0f) return;
        float outer = inner + width;
        Color32 tint = color;
        for (int i = 0; i <= sides; i++)
        {
            float angle = i * Mathf.PI * 2f / sides;
            // The addon's screen has y going down, so its angles go round clockwise as seen; the corners sit where its do.
            var way = new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle));
            vh.AddVert(way * outer, tint, Vector4.zero);
            vh.AddVert(way * inner, tint, Vector4.zero);
        }
        for (int i = 0; i < sides; i++)
        {
            int a = i * 2;
            vh.AddTriangle(a, a + 2, a + 3);
            vh.AddTriangle(a, a + 3, a + 1);
        }
    }
}
