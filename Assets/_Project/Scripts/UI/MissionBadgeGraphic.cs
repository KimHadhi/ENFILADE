using UnityEngine;
using UnityEngine.UI;

// The angled orange objective tab stays crisp at any Canvas resolution.
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
