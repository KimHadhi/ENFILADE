using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Leans a graphic into a parallelogram, the FPS-lobby slant: its top edge sits <see cref="Slant"/> px right of its
/// bottom edge, whatever its height. Works on any UI graphic (an image, a lid, a bar) by moving its vertices, so the
/// rect, layout and clicks stay square. Text is left upright (put this on the plate, not the label).
/// </summary>
[DisallowMultipleComponent]
public sealed class KitSkew : BaseMeshEffect
{
    [SerializeField] private float slant = 16f;

    /// <summary>How far right the top edge sits from the bottom edge (px; negative leans the other way).</summary>
    public float Slant
    {
        get => slant;
        set
        {
            if (Mathf.Approximately(slant, value)) return;
            slant = value;
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }

    /// <summary>Leans <paramref name="target"/> by <paramref name="by"/> px, adding the effect the first time.</summary>
    public static KitSkew On(Graphic target, float by)
    {
        if (target == null) return null;
        var skew = target.TryGetComponent(out KitSkew found) ? found : target.gameObject.AddComponent<KitSkew>();
        skew.Slant = by;
        target.SetVerticesDirty();
        return skew;
    }

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || graphic == null) return;
        var rect = graphic.rectTransform.rect;
        if (rect.height <= 0f) return;
        var vertex = new UIVertex();
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            // -0.5 at the bottom edge, +0.5 at the top.
            float k = (vertex.position.y - rect.center.y) / rect.height;
            vertex.position.x += k * slant;
            mesh.SetUIVertex(vertex, i);
        }
    }
}
