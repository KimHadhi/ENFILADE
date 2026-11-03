using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The glass pane one side of the HUD stands on, so that side reads as a plane in depth instead of tilted text: a faint
/// fill thinning toward the middle, a grid, ticks along the top and bottom edges, a closed outline with the outer edge
/// strongest, the accent's tab and brackets on the outer corners and small brackets on the inner ones. The plane's turn
/// shows in every one of those lines: the inner edge is shorter, the top and bottom close in, the grid and ticks pack
/// tighter toward the middle. It sits behind everything on its side's HudPlane and a little deeper, so the content
/// floats just in front of it. The player turns it on in Settings (HUD 그리드 배경, off by default); Hud.csv's Board row
/// shapes it.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HudBoard : MaskableGraphic, ITableBound
{
    private static readonly Color Glass = new Color(.035f, .043f, .059f);
    private const float Line = 2f, TickLength = 11f, TabWidth = 6f, TabLength = 80f, TabFromTop = 26f;
    private const float OuterArm = 26f, OuterThick = 3f, OuterGap = 7f, InnerArm = 14f, InnerThick = 2f, InnerGap = 5f;

    [Tooltip("Whether the boards can show at all; the player's setting decides when this allows it.")]
    [SerializeField] private bool show = true;
    [Tooltip("The board's width from its outer edge (px at 1080p).")]
    [SerializeField, Min(40)] private float width = 560f;
    [Tooltip("Room between the board and the screen's edge, top and bottom (px).")]
    [SerializeField, Min(0)] private float margin = 30f;
    [Tooltip("How far behind the side's content the board sits (px), so the content floats in front of it.")]
    [SerializeField] private float behind = 14f;
    [Tooltip("The glass's darkness at the outer edge and at the inner edge (0-1).")]
    [SerializeField, Range(0f, 1f)] private float fillOuter = .32f, fillInner = .05f;
    [Tooltip("Spacing of the grid's lines (px).")]
    [SerializeField, Min(8)] private float gridStep = 64f;
    [SerializeField, Range(0f, 1f)] private float gridAlpha = .07f;
    [Tooltip("Opacity of the outline at the outer edge, and at the inner one.")]
    [SerializeField, Range(0f, 1f)] private float lineAlpha = .62f, innerLineAlpha = .34f;
    [Tooltip("Spacing of the ticks along the top and bottom edges (px).")]
    [SerializeField, Min(8)] private float tickStep = 32f;
    [SerializeField, Range(0f, 1f)] private float tickAlpha = .5f;
    [Tooltip("The outer corners' brackets and the tab on the outer edge.")]
    [SerializeField] private Color accent = new Color(.937f, .522f, .259f);

    private HudPlane.Side side;
    private RectTransform canvasRect;
    private Vector2 laidFor;

    /// <summary>Puts a board behind everything on a side's plane (a HudPlane group centred on its canvas).</summary>
    public static HudBoard Add(RectTransform group, HudPlane.Side side)
    {
        var rect = new GameObject("Board", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(group, false);
        rect.gameObject.layer = group.gameObject.layer;
        rect.SetAsFirstSibling();
        var board = rect.gameObject.AddComponent<HudBoard>();
        board.side = side;
        board.raycastTarget = false;
        GameTables.Bind(HudCamera.TableName, "Board", board);
        board.LayOut();
        return board;
    }

    public void OnTableApplied() => LayOut();

    protected override void OnEnable()
    {
        base.OnEnable();
        GameSettings.Changed += SetVerticesDirty;
    }

    protected override void OnDisable()
    {
        GameSettings.Changed -= SetVerticesDirty;
        base.OnDisable();
    }

    private void LateUpdate()
    {
        if (canvasRect == null && canvas != null) canvasRect = (RectTransform)canvas.rootCanvas.transform;
        if (canvasRect != null && canvasRect.rect.size != laidFor) LayOut();
    }

    // The board's outer edge sits margin in from the screen's edge on its side; the group's origin is the canvas centre.
    private void LayOut()
    {
        if (canvasRect == null && canvas != null) canvasRect = (RectTransform)canvas.rootCanvas.transform;
        if (canvasRect == null) return;
        laidFor = canvasRect.rect.size;
        float sign = side == HudPlane.Side.Left ? -1f : 1f;
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(.5f, .5f);
        rectTransform.pivot = new Vector2(side == HudPlane.Side.Left ? 0f : 1f, .5f);
        rectTransform.sizeDelta = new Vector2(width, Mathf.Max(0f, laidFor.y - margin * 2f));
        rectTransform.anchoredPosition3D = new Vector3(sign * (laidFor.x * .5f - margin), 0f, behind);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = rectTransform.rect;
        if (!show || !GameSettings.Current.hudBoards || r.width <= 0f || r.height <= 0f) return;
        float top = r.yMax, bottom = r.yMin;

        // The glass: darkest at the outer edge, thinning across the first half, nearly clear at the inner edge.
        float mid = Mathf.Lerp(fillInner, fillOuter, .26f);
        Span(mesh, 0f, .45f, bottom, top, Tint(Glass, fillOuter), Tint(Glass, mid));
        Span(mesh, .45f, 1f, bottom, top, Tint(Glass, mid), Tint(Glass, fillInner));

        // The grid fades toward the inner edge; its columns are what pack tighter with depth.
        Color gridOut = Tint(Color.white, gridAlpha), gridIn = Tint(Color.white, gridAlpha * .45f);
        for (float x = gridStep; x < r.width - 1f; x += gridStep)
        {
            float f = x / r.width;
            Color c = Color.Lerp(gridOut, gridIn, f);
            Span(mesh, f, (x + 1f) / r.width, bottom, top, c, c);
        }
        for (float y = bottom + gridStep; y < top - 1f; y += gridStep)
            Span(mesh, 0f, 1f, y, y + 1f, gridOut, gridIn);

        // Ticks under the top edge and over the bottom one.
        Color tickOut = Tint(Color.white, tickAlpha), tickIn = Tint(Color.white, tickAlpha * .35f);
        for (float x = 0f; x < r.width - Line; x += tickStep)
        {
            float f0 = x / r.width, f1 = (x + Line) / r.width;
            Color c = Color.Lerp(tickOut, tickIn, f0);
            Span(mesh, f0, f1, top - Line - TickLength, top - Line, c, c);
            Span(mesh, f0, f1, bottom + Line, bottom + Line + TickLength, c, c);
        }

        // The closed outline: the outer edge strongest, the inner one fainter, the top and bottom shading between.
        Color edgeOut = Tint(Color.white, lineAlpha), edgeIn = Tint(Color.white, innerLineAlpha);
        Span(mesh, 0f, Line / r.width, bottom, top, edgeOut, edgeOut);
        Span(mesh, 1f - Line / r.width, 1f, bottom, top, edgeIn, edgeIn);
        Span(mesh, 0f, 1f, top - Line, top, edgeOut, edgeIn);
        Span(mesh, 0f, 1f, bottom, bottom + Line, edgeOut, edgeIn);

        // The tab on the outer edge, and the brackets: the accent's at the outer corners, small white ones at the inner.
        Box(mesh, -2f, -2f + TabWidth, top - TabFromTop - TabLength, top - TabFromTop, accent);
        Bracket(mesh, -OuterGap, top + OuterGap, 1f, -1f, OuterArm, OuterThick, accent);
        Bracket(mesh, -OuterGap, bottom - OuterGap, 1f, 1f, OuterArm, OuterThick, accent);
        Color small = Tint(Color.white, .55f);
        Bracket(mesh, r.width + InnerGap, top + InnerGap, -1f, -1f, InnerArm, InnerThick, small);
        Bracket(mesh, r.width + InnerGap, bottom - InnerGap, -1f, 1f, InnerArm, InnerThick, small);
    }

    private static Color Tint(Color c, float alpha) { c.a = alpha; return c; }

    // x measured in from the outer edge (px); the board's local x, mirrored for the right side.
    private float X(float fromOuter)
    {
        Rect r = rectTransform.rect;
        return side == HudPlane.Side.Left ? r.xMin + fromOuter : r.xMax - fromOuter;
    }

    // A band from share a to share b of the width (0 = outer edge), coloured from its outer end to its inner end.
    private void Span(VertexHelper mesh, float a, float b, float y0, float y1, Color outer, Color inner)
    {
        float w = rectTransform.rect.width;
        Quad(mesh, X(a * w), X(b * w), y0, y1, outer, inner);
    }

    // A solid box, x in px from the outer edge (negative reaches past it).
    private void Box(VertexHelper mesh, float x0, float x1, float y0, float y1, Color c) => Quad(mesh, X(x0), X(x1), y0, y1, c, c);

    // An L just outside a corner at (cx px in from the outer edge, cy). Its arms run back along the board's edges:
    // hx = +1 inward from beyond the outer edge or -1 back from beyond the inner one, vy = -1 down from above the top or
    // +1 up from below the bottom.
    private void Bracket(VertexHelper mesh, float cx, float cy, float hx, float vy, float arm, float thick, Color c)
    {
        Box(mesh, Mathf.Min(cx, cx + hx * arm), Mathf.Max(cx, cx + hx * arm), Mathf.Min(cy, cy + vy * thick), Mathf.Max(cy, cy + vy * thick), c);
        Box(mesh, Mathf.Min(cx, cx + hx * thick), Mathf.Max(cx, cx + hx * thick), Mathf.Min(cy, cy + vy * arm), Mathf.Max(cy, cy + vy * arm), c);
    }

    private void Quad(VertexHelper mesh, float x0, float x1, float y0, float y1, Color c0, Color c1)
    {
        if (Mathf.Approximately(x0, x1) || y1 <= y0) return;
        int index = mesh.currentVertCount;
        Color32 a = c0 * color, b = c1 * color;
        mesh.AddVert(new Vector2(x0, y0), a, Vector2.zero);
        mesh.AddVert(new Vector2(x0, y1), a, Vector2.zero);
        mesh.AddVert(new Vector2(x1, y1), b, Vector2.zero);
        mesh.AddVert(new Vector2(x1, y0), b, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index + 2, index + 3, index);
    }
}
