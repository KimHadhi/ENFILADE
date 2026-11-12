using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A check mark filling its rect, drawn as a mesh like the HUD's other marks (HudShapes: sharp at any size, no sprite):
/// a short stroke down to the elbow and a long one up to the top right, one thickness, the elbow closed. The hub's ready
/// board puts it on a player's picture once they are ready (HubReadyBoard).
/// </summary>
public sealed class HubCheckMark : MaskableGraphic
{
    [SerializeField, Range(.04f, .4f), Tooltip("Stroke thickness, a share of the rect's shorter side.")]
    private float thickness = .19f;

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        Vector2 At(float x, float y) => new Vector2(rect.xMin + rect.width * x, rect.yMin + rect.height * y);
        Vector2 start = At(.08f, .5f), elbow = At(.38f, .16f), end = At(.94f, .84f);
        float width = Mathf.Min(rect.width, rect.height) * thickness;
        Color32 tint = color;
        // Each stroke runs half a width past the elbow, so the two overlap there instead of leaving a notch.
        Stroke(mesh, start, elbow, width, width * .5f, tint);
        Stroke(mesh, end, elbow, width, width * .5f, tint);
    }

    // A straight stroke from a to b, width wide, carried on past b by `beyond`.
    private static void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, float beyond, Color32 tint)
    {
        Vector2 along = (b - a).normalized;
        b += along * beyond;
        Vector2 side = new Vector2(-along.y, along.x) * (width * .5f);
        HudShapes.Quad(mesh, a - side, a + side, b + side, b - side, tint);
    }
}
