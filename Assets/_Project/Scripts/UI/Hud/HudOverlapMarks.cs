using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The outlines HudOverlapCheck draws over the screen around what it found: the two pieces' overlapping shapes in red,
/// a piece's part past the screen's edge in orange. Its own overlay canvas over everything, in screen pixels, never
/// measured itself (HudOverlap.csv skips it). ENFILADE → HUD → 겹침 검사 draws them for its pictures; while watching in
/// Play they show only when Hud.csv Overlap.draw is on.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HudOverlapMarks : MaskableGraphic
{
    public const string HostName = "HUD Overlap Marks";
    private const float Thickness = 2f;
    private static readonly Color Overlap = new Color(1f, .16f, .2f, .95f), OffScreen = new Color(1f, .62f, .1f, .95f);
    private static HudOverlapMarks instance;

    private readonly List<Vector2[]> overlapping = new List<Vector2[]>(), outside = new List<Vector2[]>();
    private float until;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>Outlines these shapes (screen px) for <paramref name="seconds"/>; replaces what showed before.</summary>
    public static void Show(List<Vector2[]> overlaps, List<Vector2[]> offScreen, float seconds)
    {
        var marks = Get();
        marks.overlapping.Clear();
        marks.outside.Clear();
        if (overlaps != null) marks.overlapping.AddRange(overlaps);
        if (offScreen != null) marks.outside.AddRange(offScreen);
        marks.until = Time.unscaledTime + Mathf.Max(0f, seconds);
        marks.gameObject.SetActive(true);
        marks.SetVerticesDirty();
    }

    public static void Clear()
    {
        if (instance == null) return;
        instance.overlapping.Clear();
        instance.outside.Clear();
        instance.SetVerticesDirty();
    }

    private static HudOverlapMarks Get()
    {
        if (instance != null) return instance;
        var host = new GameObject(HostName, typeof(RectTransform), typeof(Canvas));
        RuntimeSceneHierarchy.Keep(host, "Dev");
        var canvas = host.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        var rect = new GameObject("Outlines", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(host.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        instance = rect.gameObject.AddComponent<HudOverlapMarks>();
        instance.raycastTarget = false;
        return instance;
    }

    private void Update()
    {
        if (until > 0f && Time.unscaledTime >= until && (overlapping.Count > 0 || outside.Count > 0))
        {
            overlapping.Clear();
            outside.Clear();
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        // An overlay canvas without a scaler is in screen pixels, its origin at the screen's centre.
        var shift = new Vector2(Screen.width * .5f, Screen.height * .5f);
        foreach (var shape in overlapping) Outline(mesh, shape, shift, Overlap);
        foreach (var shape in outside) Outline(mesh, shape, shift, OffScreen);
    }

    private static void Outline(VertexHelper mesh, Vector2[] shape, Vector2 shift, Color color)
    {
        if (shape == null || shape.Length < 2) return;
        for (int i = 0; i < shape.Length; i++)
        {
            Vector2 a = shape[i] - shift, b = shape[(i + 1) % shape.Length] - shift;
            Vector2 along = b - a;
            if (along.sqrMagnitude < .01f) continue;
            Vector2 side = new Vector2(-along.y, along.x).normalized * (Thickness * .5f);
            int start = mesh.currentVertCount;
            mesh.AddVert(a - side, color, Vector2.zero);
            mesh.AddVert(a + side, color, Vector2.zero);
            mesh.AddVert(b + side, color, Vector2.zero);
            mesh.AddVert(b - side, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
