using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The fuse of a cooking throwable held on G, as a ring round the crosshair (the user's reference, GMod's "Quick Nade
/// Timer HUD": a dark band all the way round and a white arc over it that runs down as the fuse burns, red near the end).
/// Here in the HUD's own colours: a black band, a white arc from the top going back the way a clock goes, the game's pink
/// for the last share (GrenadeRules ringPink). Wide enough never to touch the crosshair (ringRadius, canvas px at 1080).
/// Drawn as a mesh so the line keeps one thickness at any size, with a soft pixel at its edges. GrenadeHUD makes it
/// beside the HUD crosshair and says what to show.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class GrenadeCookRing : MaskableGraphic
{
    private const int Segments = 96;
    private const float Feather = 1f;

    private float share = 1f, radius = 64f, line = 5f, edge = 2f;
    private Color fill = Color.white, back = new Color(0f, 0f, 0f, .75f);

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

    /// <summary>
    /// The share of the fuse left (0 to 1), the radius to the line's middle, the arc's thickness and the dark border round
    /// it (canvas px), and the arc's and the band's colours.
    /// </summary>
    public void Set(float left, float ringRadius, float thickness, float border, Color arc, Color band)
    {
        left = Mathf.Clamp01(left);
        ringRadius = Mathf.Max(1f, ringRadius);
        thickness = Mathf.Max(.5f, thickness);
        border = Mathf.Max(0f, border);
        if (Mathf.Abs(left - share) < .001f && Mathf.Approximately(radius, ringRadius) && Mathf.Approximately(line, thickness)
            && Mathf.Approximately(edge, border) && fill == arc && back == band) return;
        share = left;
        radius = ringRadius;
        line = thickness;
        edge = border;
        fill = arc;
        back = band;
        rectTransform.sizeDelta = Vector2.one * ((radius + line * .5f + edge + Feather) * 2f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float half = line * .5f;
        // The dark band all the way round, the arc's width and its border either side.
        if (back.a > 0f) Band(vh, radius - half - edge, radius + half + edge, 0f, 1f, back);
        // The fuse left: from the top, round the way a clock goes, as long as the share left.
        if (share > 0f && fill.a > 0f) Band(vh, radius - half, radius + half, 0f, share, fill);
    }

    // A band between two radii from one share of a turn to another (0 = the top, going clockwise), with a soft pixel at
    // its inner and outer edges.
    private static void Band(VertexHelper vh, float inner, float outer, float from, float to, Color color)
    {
        int steps = Mathf.Max(2, Mathf.CeilToInt(Segments * (to - from)));
        Color clear = color;
        clear.a = 0f;
        int start = vh.currentVertCount;
        for (int i = 0; i <= steps; i++)
        {
            float turn = Mathf.Lerp(from, to, i / (float)steps) * Mathf.PI * 2f;
            var way = new Vector2(Mathf.Sin(turn), Mathf.Cos(turn));
            vh.AddVert(way * (inner - Feather), clear, Vector4.zero);
            vh.AddVert(way * inner, color, Vector4.zero);
            vh.AddVert(way * outer, color, Vector4.zero);
            vh.AddVert(way * (outer + Feather), clear, Vector4.zero);
        }
        for (int i = 0; i < steps; i++)
        {
            int a = start + i * 4, b = a + 4;
            for (int ring = 0; ring < 3; ring++)
            {
                vh.AddTriangle(a + ring, b + ring, b + ring + 1);
                vh.AddTriangle(a + ring, b + ring + 1, a + ring + 1);
            }
        }
    }
}
