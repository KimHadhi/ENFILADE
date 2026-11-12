using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Console /hits: draws where everything on the screen takes the pointer, over everything, so the hit shapes can be seen
/// lining up with what is drawn (user, 2026-11-12: "옷장이나 설정창 슬라이더나 마우스 커서대로 선택이 잘 안 됨"; KitHit).
/// Each raycast target that answers a click, a press or the pointer coming onto it (a button, a slider, a tile, a key
/// slot) gets its hit outline in pale blue: a leaned plate's parallelogram, a cut card's corners, else the square the
/// raycast tries (with its padding). While a hover leans a piece, the place it rests and still takes the pointer is
/// outlined in yellow. What the pointer would pick now is outlined in pink and filled, with its name by the cursor and,
/// after a dot, the very graphic the raycast found; a raycast target that answers nothing but sits on top (something
/// see-through eating the click) is outlined in grey and named instead. Drawn from the same shapes the filters test,
/// through each canvas's own camera, so the lobby's turned wings show turned outlines. Again /hits turns it off.
/// </summary>
[DisallowMultipleComponent]
public sealed class KitHitView : MonoBehaviour
{
    // Line widths on the screen (px) and how often the canvases are looked for again (s).
    private const float Thin = 1.5f, Bold = 3f, FindEvery = .5f;
    private static readonly Color Takes = new Color(.38f, .88f, 1f, .75f), Rests = new Color(1f, .86f, .3f, .85f),
        Picked = new Color(1f, .31f, .47f, 1f), PickedFill = new Color(1f, .31f, .47f, .16f), Blocks = new Color(.75f, .75f, .78f, .9f);

    private static KitHitView instance;
    private KitHitLines lines;
    private RectTransform pointerBox;
    private TextMeshProUGUI pointerLabel;
    private GraphicRaycaster[] raycasters = new GraphicRaycaster[0];
    private float findAt;
    private PointerEventData pointer;
    private readonly List<RaycastResult> results = new List<RaycastResult>();
    private readonly List<Vector2> outline = new List<Vector2>(), onScreen = new List<Vector2>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>Console /hits: turns the view on, or off again.</summary>
    public static string Command(string args)
    {
        if (instance != null)
        {
            Destroy(instance.gameObject);
            instance = null;
            return Texts.Get("dev.hits.off");
        }
        var host = new GameObject("Hit Shapes", typeof(RectTransform));
        RuntimeSceneHierarchy.Keep(host, "Dev");
        instance = host.AddComponent<KitHitView>();
        return Texts.Get("dev.hits.on");
    }

    private void Awake()
    {
        // Over every other canvas, and with no raycaster of its own: it never takes the pointer.
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        lines = KitStyle.Stretch("Lines", transform).gameObject.AddComponent<KitHitLines>();
        lines.raycastTarget = false;
        KitStyle.Label(KitStyle.TopLeft("Legend", transform, new Vector2(16f, 10f), new Vector2(1600f, 24f)), Texts.Get("dev.hits.legend"), 15f,
            TextAlignmentOptions.MidlineLeft);
        pointerBox = KitStyle.TopLeft("Pointer", transform, Vector2.zero, new Vector2(900f, 22f));
        pointerLabel = KitStyle.Label(pointerBox, "", 14f, TextAlignmentOptions.MidlineLeft, Picked);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= findAt)
        {
            findAt = Time.unscaledTime + FindEvery;
            raycasters = FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None);
        }
        var mouse = Mouse.current;
        Vector2 at = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        RaycastResult top = default;
        var system = EventSystem.current;
        if (system != null && mouse != null)
        {
            if (pointer == null) pointer = new PointerEventData(system);
            pointer.position = at;
            results.Clear();
            system.RaycastAll(pointer, results);
            foreach (var hit in results)
                if (hit.gameObject != null) { top = hit; break; }
        }
        var picked = Handler(top.gameObject);

        lines.Clear();
        foreach (var raycaster in raycasters)
        {
            if (raycaster == null || !raycaster.isActiveAndEnabled || !raycaster.TryGetComponent(out Canvas canvas)) continue;
            var camera = raycaster.eventCamera;
            var graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
            for (int i = 0; i < graphics.Count; i++)
            {
                var graphic = graphics[i];
                if (graphic == null || !graphic.isActiveAndEnabled || !graphic.raycastTarget || graphic.canvasRenderer.cull || graphic.depth == -1)
                    continue;
                var handler = Handler(graphic.gameObject);
                if (handler != null) Draw(graphic, camera, handler == picked);
            }
        }
        // Something on top that answers nothing: whatever it covers can't be clicked there.
        if (top.gameObject != null && picked == null && top.gameObject.TryGetComponent(out Graphic cover))
            Draw(cover, top.module != null ? top.module.eventCamera : null, false, Blocks);
        lines.Show();
        Name(at, top.gameObject, picked);
    }

    // What a press on this object reaches: a click, a press, or the pointer coming onto it, as the event system finds them.
    private static GameObject Handler(GameObject target)
    {
        if (target == null) return null;
        return ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) ?? ExecuteEvents.GetEventHandler<IPointerDownHandler>(target)
            ?? ExecuteEvents.GetEventHandler<IPointerEnterHandler>(target);
    }

    private void Draw(Graphic graphic, Camera camera, bool picked, Color? color = null)
    {
        var rect = graphic.rectTransform;
        var box = rect.rect;
        var tint = picked ? Picked : color ?? Takes;
        if (Shaped(graphic))
        {
            float slant = KitHit.SlantOf(graphic);
            var cut = KitHit.CutOf(graphic, box);
            KitHit.Outline(box, slant, cut, Vector2.zero, outline);
            Trace(rect, camera, tint, picked);
            var lean = KitHit.LeanOf(graphic);
            if (lean == Vector2.zero) return;
            KitHit.Outline(box, slant, cut, -lean, outline);
            Trace(rect, camera, Rests, false);
            return;
        }
        // A plain square, as wide as its padding makes it (a hover's lean pads it back over where it rests).
        var pad = graphic.raycastPadding;
        var square = Rect.MinMaxRect(box.xMin + pad.x, box.yMin + pad.y, box.xMax - pad.z, box.yMax - pad.w);
        KitHit.Outline(square, 0f, Vector2.zero, Vector2.zero, outline);
        Trace(rect, camera, tint, picked);
    }

    // Whether a filter of the kit holds this graphic to its drawn outline (else the raycast takes its whole square).
    private static bool Shaped(Graphic graphic) =>
        (graphic.TryGetComponent(out KitSkew skew) && skew.isActiveAndEnabled) || (graphic.TryGetComponent(out KitHitShape shape) && shape.isActiveAndEnabled);

    // The outline (in the graphic's space) through its canvas's camera onto the screen and into this view.
    private void Trace(RectTransform rect, Camera camera, Color color, bool filled)
    {
        onScreen.Clear();
        var view = lines.rectTransform;
        foreach (var corner in outline)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(corner));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(view, screen, null, out var local);
            onScreen.Add(local);
        }
        if (filled) lines.Fill(onScreen, PickedFill);
        for (int i = 0; i < onScreen.Count; i++) lines.Line(onScreen[i], onScreen[(i + 1) % onScreen.Count], color, filled ? Bold : Thin);
    }

    // By the cursor: what a press would reach and, after a dot, the graphic the raycast found on top, when it is another.
    private void Name(Vector2 at, GameObject top, GameObject picked)
    {
        bool shown = top != null;
        if (pointerBox.gameObject.activeSelf != shown) pointerBox.gameObject.SetActive(shown);
        if (!shown) return;
        pointerBox.anchoredPosition = new Vector2(at.x + 18f, at.y - Screen.height - 6f);
        pointerLabel.color = picked != null ? Picked : Blocks;
        string text = picked != null ? picked.name : top.name;
        if (picked != null && top != picked) text += "  ·  " + top.name;
        if (pointerLabel.text != text) pointerLabel.text = text;
    }
}
