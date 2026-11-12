using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Leans a graphic into a parallelogram, the FPS-lobby slant: its top edge sits <see cref="Slant"/> px right of its
/// bottom edge, whatever its height. Works on any UI graphic (an image, a lid, a bar) by moving its vertices, so the
/// rect and layout stay square. Text is left upright (put this on the plate, not the label).
/// Clicks follow the lean (2026-11-12): a raycast tests the square rect, so the pointer near a leaned edge picked the
/// neighbour or missed the plate it sat on ("옷장이나 설정창 슬라이더나 마우스 커서대로 선택이 잘 안 됨"). While its graphic
/// is a raycast target, a point counts only inside the parallelogram as drawn (KitHit.Allows, the filter below; what
/// lies under the graphic is held to it too, inside the graphic's own square), and the graphic's raycast square is
/// widened over the two overhangs (KitHit.Pad). A plate whose clicks are taken by a separate see-through image (the
/// lobby's ways to play, the settings tabs, 출격) leans that image the same way, so its hit shape matches the plate.
/// </summary>
[DisallowMultipleComponent]
public sealed class KitSkew : BaseMeshEffect, ICanvasRaycastFilter
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
            if (graphic != null)
            {
                graphic.SetVerticesDirty();
                KitHit.Pad(graphic);
            }
        }
    }

    /// <summary>Leans <paramref name="target"/> by <paramref name="by"/> px, adding the effect the first time.</summary>
    public static KitSkew On(Graphic target, float by)
    {
        if (target == null) return null;
        var skew = target.TryGetComponent(out KitSkew found) ? found : target.gameObject.AddComponent<KitSkew>();
        skew.Slant = by;
        target.SetVerticesDirty();
        KitHit.Pad(target);
        return skew;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (graphic != null) KitHit.Pad(graphic);
    }

    protected override void OnDisable()
    {
        if (graphic != null) KitHit.Pad(graphic, this);
        base.OnDisable();
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

    /// <summary>Takes the pointer only inside the parallelogram as drawn, while the graphic is a raycast target.</summary>
    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        var target = graphic;
        if (!isActiveAndEnabled || target == null || !target.raycastTarget) return true;
        return KitHit.Allows(target, screenPoint, eventCamera);
    }
}
