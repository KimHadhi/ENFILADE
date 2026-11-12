using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small UI line icon with no font-glyph or texture dependency: a speech box for what people say, a prompt chevron for
/// the console, and for a system notice the HUD's nested diamond (a thin outline round a solid core, the mark the alert
/// band and the menus use for news), so a notice reads as the game's before its words are read.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChatChannelIcon : MaskableGraphic
{
    [SerializeField] private ChatChannel channel;
    public void SetChannel(ChatChannel value) { if (channel == value) return; channel = value; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (channel == ChatChannel.Console)
        {
            Line(vh, new Vector2(.15f,.8f), new Vector2(.48f,.5f));
            Line(vh, new Vector2(.48f,.5f), new Vector2(.15f,.2f));
            Line(vh, new Vector2(.55f,.2f), new Vector2(.92f,.2f));
        }
        else if (channel == ChatChannel.Broadcast)
        {
            Line(vh, new Vector2(.5f,.97f), new Vector2(.97f,.5f));
            Line(vh, new Vector2(.97f,.5f), new Vector2(.5f,.03f));
            Line(vh, new Vector2(.5f,.03f), new Vector2(.03f,.5f));
            Line(vh, new Vector2(.03f,.5f), new Vector2(.5f,.97f));
            Diamond(vh, new Vector2(.5f,.5f), .24f);
        }
        else
        {
            Line(vh, new Vector2(.15f,.1f), new Vector2(.15f,.9f));
            Line(vh, new Vector2(.15f,.9f), new Vector2(.85f,.9f));
            Line(vh, new Vector2(.85f,.9f), new Vector2(.85f,.1f));
            Line(vh, new Vector2(.85f,.1f), new Vector2(.15f,.1f));
            Line(vh, new Vector2(.32f,.63f), new Vector2(.68f,.63f));
            Line(vh, new Vector2(.32f,.38f), new Vector2(.58f,.38f));
        }
    }
    private void Line(VertexHelper vh, Vector2 from, Vector2 to)
    {
        Rect rect = GetPixelAdjustedRect();
        from = rect.min + Vector2.Scale(from, rect.size); to = rect.min + Vector2.Scale(to, rect.size);
        Vector2 d = (to - from).normalized;
        Vector2 n = new Vector2(-d.y, d.x) * Mathf.Max(1f, rect.width / 12f) * .5f;
        var quad = new UIVertex[4];
        Vector2[] points = { from - n, from + n, to + n, to - n };
        for (int i = 0; i < 4; i++) { quad[i] = UIVertex.simpleVert; quad[i].position = points[i]; quad[i].color = color; }
        vh.AddUIVertexQuad(quad);
    }
    // A solid diamond about centre (0-1 of the rect) reaching radius (share of the rect's size) each way.
    private void Diamond(VertexHelper vh, Vector2 centre, float radius)
    {
        Rect rect = GetPixelAdjustedRect();
        Vector2 middle = rect.min + Vector2.Scale(centre, rect.size), reach = rect.size * radius;
        var quad = new UIVertex[4];
        Vector2[] points = { middle + new Vector2(0f, -reach.y), middle + new Vector2(-reach.x, 0f), middle + new Vector2(0f, reach.y), middle + new Vector2(reach.x, 0f) };
        for (int i = 0; i < 4; i++) { quad[i] = UIVertex.simpleVert; quad[i].position = points[i]; quad[i].color = color; }
        vh.AddUIVertexQuad(quad);
    }
}
