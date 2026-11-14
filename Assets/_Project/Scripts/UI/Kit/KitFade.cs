using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fades a graphic in across its width: nothing at its left edge, its full colour at its right (the vertices' alpha), for a
/// texture that must fade without a mask, such as the assault band's halftone (AssaultBar). Works on any UI graphic whose
/// mesh has vertices at its edges (an image, a raw image).
/// </summary>
[DisallowMultipleComponent]
public sealed class KitFade : BaseMeshEffect
{
    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || graphic == null) return;
        var rect = graphic.rectTransform.rect;
        if (rect.width <= 0f) return;
        var vertex = new UIVertex();
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            float share = Mathf.Clamp01((vertex.position.x - rect.xMin) / rect.width);
            vertex.color.a = (byte)Mathf.RoundToInt(vertex.color.a * share);
            mesh.SetUIVertex(vertex, i);
        }
    }
}
