using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A filled triangle for the UI: its three corners given in its own rect, 0 to 1 across and up. The HUD's pink accent
/// shards (CounterBanner) are these.
/// </summary>
public sealed class UITriangle : MaskableGraphic
{
    [SerializeField] private Vector2 first = new Vector2(0f, 1f), second = new Vector2(1f, 1f), third = new Vector2(0f, 0f);

    public void SetCorners(Vector2 a, Vector2 b, Vector2 c)
    {
        first = a; second = b; third = c;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = new Vector3(rect.x + first.x * rect.width, rect.y + first.y * rect.height); vh.AddVert(vertex);
        vertex.position = new Vector3(rect.x + second.x * rect.width, rect.y + second.y * rect.height); vh.AddVert(vertex);
        vertex.position = new Vector3(rect.x + third.x * rect.width, rect.y + third.y * rect.height); vh.AddVert(vertex);
        vh.AddTriangle(0, 1, 2);
    }
}
