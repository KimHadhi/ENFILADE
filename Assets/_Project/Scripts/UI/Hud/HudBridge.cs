using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The thin band in the gap between two leaning HUD pieces, Persona-style (HudTilt.csv `bridge`). It runs along the
/// middle of the gap, clear of both pieces (`bridgeGap`) and no thicker than `bridgeWidth`, tapering where the gap closes,
/// so the space between them reads as part of the design even though each leans its own way. Its wide end ends in a
/// small triangle in the accent colour, with a shard of the same colour just past it. HudTilt hands it the edges every
/// frame; this draws them.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HudBridge : MaskableGraphic
{
    // The tip fills the wide end's full height and reaches back along the top this many times that height.
    private const float TipLength = 1.2f;
    // The shard just past the wide end, slanted like the bar's cells: its gap from the tip and its width along the top
    // (px at 1080p); it reaches the band's full height.
    private const float ShardGap = 3f, ShardWidth = 5f;

    private Vector3 topA, topB, bottomA, bottomB;
    private bool shown;
    private Color accent = new Color(1f, .31f, .47f);

    /// <summary>A wedge drawn first under parent, so every piece under it covers it.</summary>
    public static HudBridge Create(Transform parent)
    {
        var rect = new GameObject("Bridge (Runtime)", typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        var bridge = rect.gameObject.AddComponent<HudBridge>();
        bridge.raycastTarget = false;
        return bridge;
    }

    /// <summary>The wedge's base and accent colours.</summary>
    public void Paint(Color baseColor, Color accentColor)
    {
        color = baseColor;
        if (accent == accentColor) return;
        accent = accentColor;
        SetVerticesDirty();
    }

    /// <summary>
    /// The wedge between two edges in this object's space: its top from topA to topB, its bottom from bottomA to bottomB,
    /// A at the same end of both. A closed end has its top and bottom at one point.
    /// </summary>
    public void SetEdges(Vector3 newTopA, Vector3 newTopB, Vector3 newBottomA, Vector3 newBottomB)
    {
        if (shown && Near(newTopA, topA) && Near(newTopB, topB) && Near(newBottomA, bottomA) && Near(newBottomB, bottomB)) return;
        topA = newTopA; topB = newTopB; bottomA = newBottomA; bottomB = newBottomB;
        shown = true;
        SetVerticesDirty();
    }

    public void Hide()
    {
        if (!shown) return;
        shown = false;
        SetVerticesDirty();
    }

    private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-6f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!shown) return;
        float width = Vector3.Distance(topA, topB);
        if (width < .5f) return;
        Quad(vh, At(0f, 0f), At(1f, 0f), At(1f, 1f), At(0f, 1f), color);

        bool wideAtB = Vector3.Distance(topB, bottomB) >= Vector3.Distance(topA, bottomA);
        float height = Mathf.Max(Vector3.Distance(topB, bottomB), Vector3.Distance(topA, bottomA));
        float end = wideAtB ? 1f : 0f, outward = wideAtB ? 1f : -1f;
        float back = Mathf.Min(TipLength * height, width * .5f) / width;
        Triangle(vh, At(end, 0f), At(end - outward * back, 0f), At(end, 1f), accent);
        // Past the end, on the band's lines carried on.
        float near = ShardGap / width, far = (ShardGap + ShardWidth) / width, foot = (ShardGap + ShardWidth * .2f) / width;
        Triangle(vh, At(end + outward * near, 0f), At(end + outward * far, 0f), At(end + outward * foot, 1f), accent);
    }

    // A point of the band, or of its lines carried on past its ends: u across from A to B, v down from top to bottom.
    private Vector3 At(float u, float v) =>
        Vector3.Lerp(Vector3.LerpUnclamped(topA, topB, u), Vector3.LerpUnclamped(bottomA, bottomB, u), v);

    private static void Quad(VertexHelper vh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint)
    {
        int start = vh.currentVertCount;
        Vertex(vh, a, tint); Vertex(vh, b, tint); Vertex(vh, c, tint); Vertex(vh, d, tint);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static void Triangle(VertexHelper vh, Vector3 a, Vector3 b, Vector3 c, Color tint)
    {
        int start = vh.currentVertCount;
        Vertex(vh, a, tint); Vertex(vh, b, tint); Vertex(vh, c, tint);
        vh.AddTriangle(start, start + 1, start + 2);
    }

    private static void Vertex(VertexHelper vh, Vector3 position, Color tint)
    {
        var vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = tint;
        vh.AddVert(vertex);
    }
}
