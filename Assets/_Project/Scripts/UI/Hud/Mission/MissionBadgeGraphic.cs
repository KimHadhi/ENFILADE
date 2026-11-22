using UnityEngine;
using UnityEngine.UI;

// The objective bar's angled ! tab, drawn as a mesh so it stays crisp at any Canvas resolution.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MissionBadgeGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        vh.AddVert(new Vector3(r.xMin, r.yMin), color, Vector2.zero);
        vh.AddVert(new Vector3(r.xMax - 10, r.yMin), color, Vector2.zero);
        vh.AddVert(new Vector3(r.xMax, r.yMax), color, Vector2.zero);
        vh.AddVert(new Vector3(r.xMin, r.yMax), color, Vector2.zero);
        vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
    }
}
