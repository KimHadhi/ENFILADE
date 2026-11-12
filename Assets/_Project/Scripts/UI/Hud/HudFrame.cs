using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a group the HUD prefab drew for a 1920 x 1080 screen at the distances from the screen's edges it was drawn with,
/// whatever the screen's shape. GameHUD's Left Panel, Right Panel and Center are 100 px boxes centred on the canvas whose
/// contents were laid out from a frame the size of the reference screen (Mission stretches its box out to 1920 x 1080,
/// Health hangs off its box's bottom-left corner, the weapon slots off its right side). The canvas is that size only on a
/// 16:9 screen (CanvasScaler matches width and height half and half): on a 4:3 screen it is 1663 x 1247 and the HUD ran
/// 129 px off the left and right edges (the objective bar's key cap and the ult magazine fully off), on 21:9 it is
/// 2217 x 935 and the HUD ran 72 px off the top and bottom (2026-11-12). The box grows or shrinks by what the canvas
/// differs from the reference, so its corners and edges, and whatever hangs on them, keep their margins to the screen's;
/// its centre stays the canvas centre, so HudPlane still turns it about the canvas edge and HudBoard still measures from
/// it. On a 16:9 screen nothing changes. A narrower screen gives the box a negative width, which only moves where its
/// edges are (nothing draws on the box itself).
/// It fits before any other script's LateUpdate: some place themselves against the box once per canvas size (ChatFrame
/// reads the box's corner when the canvas changes and not again), so the box must already have its new size then.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-20000)]
public sealed class HudFrame : MonoBehaviour
{
    private RectTransform rect, canvasRect;
    private Vector2 drawn, reference = new Vector2(1920f, 1080f), laidFor;

    /// <summary>
    /// Keeps a box hung on its canvas centre at its drawn margins to the screen's edges. A group already stretched over
    /// the canvas (HudPlane.Create's) follows the edges by itself and is left alone.
    /// </summary>
    public static void Keep(RectTransform group)
    {
        if (group == null || group.GetComponent<HudFrame>() != null || group.anchorMin != group.anchorMax) return;
        var frame = group.gameObject.AddComponent<HudFrame>();
        frame.rect = group;
        frame.drawn = group.sizeDelta;
        frame.Fit();
    }

    private void LateUpdate() => Fit();

    private void Fit()
    {
        if (rect == null) return;
        if (canvasRect == null)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var root = canvas.rootCanvas;
            canvasRect = (RectTransform)root.transform;
            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) reference = scaler.referenceResolution;
        }
        // The canvas has no size until it is first laid out; the box keeps its drawn size till then.
        Vector2 size = canvasRect.rect.size;
        if (size.x < 1f || size.y < 1f || size == laidFor) return;
        laidFor = size;
        rect.sizeDelta = drawn + (size - reference);
    }
}
