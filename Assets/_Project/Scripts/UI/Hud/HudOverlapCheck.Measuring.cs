using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HudOverlapCheck: Measuring. One part of the class; the rest is in HudOverlapCheck.cs and the files beside it.
public sealed partial class HudOverlapCheck
{
    /// <summary>
    /// Measures the HUD as drawn this frame and fills <paramref name="findings"/> with every overlap between two pieces
    /// and every piece past the screen's edge, deepest first. Call after the frame is drawn (end of frame) for this
    /// frame's letters and meshes.
    /// </summary>
    public static void Measure(List<Finding> findings)
    {
        var s = Current;
        findings.Clear();
        Gather(s);
        float tolerance = Mathf.Max(0f, s.tolerance);
        for (int i = 0; i < measured.Count; i++)
        {
            var a = measured[i];
            Outside(a, s, findings);
            for (int j = i + 1; j < measured.Count; j++)
            {
                var b = measured[j];
                if (!a.Bounds.Overlaps(b.Bounds)) continue;
                float deepest = 0f;
                Vector2[] shapeA = null, shapeB = null;
                foreach (var one in a.Shapes)
                {
                    Rect boxA = Box(one);
                    foreach (var other in b.Shapes)
                    {
                        if (!boxA.Overlaps(Box(other))) continue;
                        float depth = Depth(one, other);
                        if (depth > deepest) { deepest = depth; shapeA = one; shapeB = other; }
                    }
                }
                if (deepest > tolerance)
                    findings.Add(new Finding { A = a.Name, B = b.Name, Depth = deepest, At = (Middle(shapeA) + Middle(shapeB)) * .5f, ShapeA = shapeA, ShapeB = shapeB, ScreenHeight = Screen.height });
            }
        }
        findings.Sort((x, y) => y.Depth.CompareTo(x.Depth));
    }

    /// <summary>The pieces measured last, by name, each with how many shapes it had: what the check sees as the HUD.</summary>
    public static string Pieces()
    {
        var text = new StringBuilder();
        foreach (var piece in measured) text.Append(piece.Name).Append(" (").Append(piece.Shapes.Count).Append(")\n");
        return text.ToString();
    }

    private static void Outside(Piece piece, Settings s, List<Finding> findings)
    {
        float edge = Mathf.Max(0f, s.edge), width = Screen.width, height = Screen.height;
        float worst = 0f;
        Vector2[] worstShape = null;
        foreach (var shape in piece.Shapes)
            foreach (var p in shape)
            {
                float past = Mathf.Max(Mathf.Max(-p.x, p.x - width), Mathf.Max(-p.y, p.y - height));
                if (past > worst) { worst = past; worstShape = shape; }
            }
        if (worst > edge) findings.Add(new Finding { A = piece.Name, Depth = worst, At = Middle(worstShape), ShapeA = worstShape, ScreenHeight = Screen.height });
    }

    private static void Gather(Settings s)
    {
        if (rules == null) ReadRules();
        foreach (var piece in pieces.Values) { piece.Shapes.Clear(); piece.Empty = true; }
        measured.Clear();
        kinds.Clear();
        canvases.Clear();
        canvases.AddRange(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        float screenArea = Mathf.Max(1f, (float)Screen.width * Screen.height);
        foreach (var canvas in canvases)
        {
            if (!canvas.isRootCanvas || !canvas.isActiveAndEnabled) continue;
            // A world canvas is part of the scene; a camera canvas without its camera is drawn as an overlay.
            if (canvas.renderMode == RenderMode.WorldSpace || Kind(canvas.transform) == SkipKind) continue;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            graphics.Clear();
            canvas.GetComponentsInChildren(false, graphics);
            foreach (var graphic in graphics)
            {
                if (graphic == null || !graphic.enabled || graphic.canvasRenderer.cull || graphic is TMP_SubMeshUI) continue;
                var root = PieceOf(graphic.transform, canvas.transform);
                if (root == null) continue;
                if (!pieces.TryGetValue(root, out var piece))
                    pieces[root] = piece = new Piece { Root = root, Name = PathOf(root) };
                MeasureGraphic(graphic, camera, piece, s, screenArea);
            }
        }
        // Pieces whose transform has gone are dropped; the rest are measured if anything of them is drawn.
        var dead = new List<Transform>();
        foreach (var pair in pieces)
        {
            if (pair.Key == null) { dead.Add(pair.Key); continue; }
            if (!pair.Value.Empty) measured.Add(pair.Value);
        }
        foreach (var key in dead) pieces.Remove(key);
    }

    private static void MeasureGraphic(Graphic graphic, Camera camera, Piece piece, Settings s, float screenArea)
    {
        float alpha = graphic.canvasRenderer.GetInheritedAlpha();
        if (alpha * graphic.color.a < s.faint) return;
        var rect = graphic.rectTransform;
        rect.GetWorldCorners(corners);
        var box = new Vector2[4];
        for (int i = 0; i < 4; i++) box[i] = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
        if (Mathf.Abs(SignedArea(box)) > s.backdrop * screenArea) return;
        Masks(rect, camera);
        if (graphic is TMP_Text text) Letters(text, camera, piece, s, alpha);
        else Triangles(graphic, camera, piece, s, alpha);
    }

    // Each visible letter's box; a letter's own alpha (rich text) counts.
    private static void Letters(TMP_Text text, Camera camera, Piece piece, Settings s, float alpha)
    {
        var info = text.textInfo;
        if (info == null || info.characterInfo == null) return;
        var t = text.rectTransform;
        int count = Mathf.Min(info.characterCount, info.characterInfo.Length);
        for (int i = 0; i < count; i++)
        {
            var c = info.characterInfo[i];
            if (!c.isVisible || alpha * (c.color.a / 255f) < s.faint) continue;
            var shape = new[]
            {
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(c.bottomLeft)),
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(c.topLeft)),
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(c.topRight)),
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(c.bottomRight)),
            };
            Keep(piece, shape);
        }
    }

    // The graphic's triangles as the canvas builds them; triangles too faint to see are left out.
    private static void Triangles(Graphic graphic, Camera camera, Piece piece, Settings s, float alpha)
    {
        if (populate == null)
            populate = typeof(Graphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null);
        if (populate == null) return;
        var rect = graphic.rectTransform.rect;
        if (rect.width < 0f || rect.height < 0f) return;
        helper ??= new VertexHelper();
        helper.Clear();
        populateArgs[0] = helper;
        try
        {
            populate.Invoke(graphic, populateArgs);
            modifiers.Clear();
            graphic.GetComponents(typeof(IMeshModifier), modifiers);
            foreach (var modifier in modifiers)
                if (!(modifier is Behaviour behaviour) || behaviour.isActiveAndEnabled) ((IMeshModifier)modifier).ModifyMesh(helper);
        }
        catch (System.Exception)
        {
            // A graphic that cannot build its mesh outside a canvas rebuild is measured by its box.
            helper.Clear();
            Keep(piece, Box(graphic.rectTransform, camera));
            return;
        }
        stream.Clear();
        helper.GetUIVertexStream(stream);
        var t = graphic.rectTransform;
        for (int i = 0; i + 2 < stream.Count; i += 3)
        {
            float shown = Mathf.Max(stream[i].color.a, Mathf.Max(stream[i + 1].color.a, stream[i + 2].color.a)) / 255f;
            if (shown * alpha < s.faint) continue;
            var shape = new[]
            {
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(stream[i].position)),
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(stream[i + 1].position)),
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(stream[i + 2].position)),
            };
            if (Mathf.Abs(SignedArea(shape)) < .25f) continue;
            Keep(piece, shape);
        }
    }

    private static Vector2[] Box(RectTransform rect, Camera camera)
    {
        rect.GetWorldCorners(corners);
        var box = new Vector2[4];
        for (int i = 0; i < 4; i++) box[i] = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
        return box;
    }

    // The RectMask2D and Mask boxes above a graphic, up to its canvas, on the screen.
    private static void Masks(RectTransform from, Camera camera)
    {
        masks.Clear();
        for (Transform t = from; t != null; t = t.parent)
        {
            if (t.TryGetComponent(out RectMask2D rectMask) && rectMask.isActiveAndEnabled) masks.Add(Box(rectMask.rectTransform, camera));
            else if (t.TryGetComponent(out Mask mask) && mask.isActiveAndEnabled) masks.Add(Box(mask.rectTransform, camera));
            if (t.TryGetComponent(out Canvas canvas) && canvas.isRootCanvas) break;
        }
    }

    // A shape cut by the masks found for its graphic, kept if anything of it is left.
    private static void Keep(Piece piece, Vector2[] shape)
    {
        foreach (var mask in masks)
        {
            shape = Clip(shape, mask);
            if (shape == null) return;
        }
        piece.Add(shape);
    }
}
