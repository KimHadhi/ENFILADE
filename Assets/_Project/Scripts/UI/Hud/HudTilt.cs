using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One HUD element's own lean on top of its plane, Persona-style: the portrait, the health number, its cells and the
/// ultimate bar each sit at their own angle and depth, so they read as separate pieces. A lean never pushes one piece into
/// another: every frame the pieces on one plane are measured by what they draw, and wherever the leans bring two closer
/// than they sit flat (or than <see cref="gap"/>), both slide apart by the difference along the side they meet on. What
/// opens up between a piece and the one under it can carry a thin tinted band (<see cref="bridge"/>, HudBridge), so the
/// health number stays tied to its bar however the two lean. HudTilt.csv has a row per element, named after its GameObject; HudRig adds one for each row it finds under GameHUD.
/// The turn and depth are scaled by the player's Settings → HUD 기울기 (0 lays the element flat on its plane).
/// </summary>
[DisallowMultipleComponent]
public sealed class HudTilt : MonoBehaviour, ITableBound
{
    public const string TableName = "HudTilt";
    private const int Passes = 4;

    [Tooltip("Turn about the vertical axis (°). Positive sends the right side away.")]
    [SerializeField] private float yaw;
    [Tooltip("Turn about the horizontal axis (°). Positive sends the top away.")]
    [SerializeField] private float pitch;
    [Tooltip("Turn within the screen (°). Positive is anticlockwise: the right side rises.")]
    [SerializeField] private float roll;
    [Tooltip("Distance from the plane (px at 1080p). Negative comes toward the player.")]
    [SerializeField] private float depth;
    [Tooltip("Slide along the plane (px at 1080p).")]
    [SerializeField] private float x, y;
    [Tooltip("Least room (px at 1080p) kept between this piece and the others once they lean. Pieces that sit closer " +
        "when flat keep that spacing, and pieces drawn over each other by design overlap no further.")]
    [SerializeField] private float gap = 4f;
    [Tooltip("Children left out of this piece's outline, by GameObject name, separated by '/'; a trailing * matches the " +
        "start of a name. For effects drawn past the piece (flames, flying shards) that may cover a neighbour.")]
    [SerializeField] private string ignore = "";
    [Tooltip("The piece just below this one, by GameObject name: a thin band in bridgeColor runs through the gap down to " +
        "it (HudBridge), Persona-style, tying two leaning pieces together. Empty for none.")]
    [SerializeField] private string bridge = "";
    [Tooltip("The band's colour: near white with only a breath of the bar's colour, so the accent at its end stays the one pink.")]
    [SerializeField] private Color bridgeColor = new Color(1f, .96f, .97f);
    [Tooltip("The band's accent: the small triangle at its wide end and the shard just past it (HudBridge). Best the " +
        "colour of the piece below.")]
    [SerializeField] private Color bridgeAccent = new Color(1f, .31f, .47f);
    [Tooltip("Least clear space (px at 1080p) between the band and each of the two pieces: it never hugs them, and thins " +
        "to a point where the gap is narrower than twice this.")]
    [SerializeField] private float bridgeGap = 5f;
    [Tooltip("Thickest the band gets (px at 1080p): it runs along the middle of the gap, tapering where the gap is " +
        "narrow, instead of filling it. 0 fills the whole gap.")]
    [SerializeField] private float bridgeWidth = 6f;

    private static readonly List<HudTilt> live = new List<HudTilt>();
    private static readonly List<Graphic> graphics = new List<Graphic>();
    private static readonly List<MonoBehaviour> behaviours = new List<MonoBehaviour>();
    private static int solvedFrame = -1;

    private RectTransform rect;
    private Transform plane;
    private Vector3 home;
    private Quaternion homeRotation, turn;
    private Matrix4x4 toPlane;
    private Rect outline;
    private bool hasOutline;
    private Vector2 push;
    private string parsedIgnore;
    private string[] ignored = Array.Empty<string>();
    // The outline's corners on the plane: flat, and leaning before any slide.
    private readonly Vector2[] flat = new Vector2[4], leaning = new Vector2[4];
    private HudBridge wedge;
    private HudTilt below;

    public static void Attach(RectTransform element)
    {
        if (!element.TryGetComponent(out HudTilt tilt)) tilt = element.gameObject.AddComponent<HudTilt>();
        GameTables.Bind(TableName, element.name, tilt);
    }

    public void OnTableApplied() => parsedIgnore = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        live.Clear();
        solvedFrame = -1;
    }

    private void OnEnable()
    {
        if (rect == null)
        {
            rect = (RectTransform)transform;
            home = rect.localPosition;
            homeRotation = rect.localRotation;
        }
        if (!live.Contains(this)) live.Add(this);
    }

    private void OnDisable()
    {
        live.Remove(this);
        HideWedge();
    }

    // Once a frame for all pieces together, after the HUD scripts have sized what they draw.
    private void LateUpdate()
    {
        if (solvedFrame == Time.frameCount) return;
        solvedFrame = Time.frameCount;
        float tilt = GameSettings.Current.hudTilt;
        foreach (var piece in live) piece.Prepare(tilt);
        for (int pass = 0; pass < Passes; pass++)
        {
            bool slid = false;
            for (int i = 0; i < live.Count; i++)
                for (int j = i + 1; j < live.Count; j++)
                    slid |= KeepApart(live[i], live[j]);
            if (!slid) break;
        }
        foreach (var piece in live) piece.Place(tilt);
        foreach (var piece in live) piece.Bridge();
    }

    private void Prepare(float tilt)
    {
        turn = homeRotation * Quaternion.Euler(pitch * tilt, -yaw * tilt, roll * tilt);
        push = Vector2.zero;
        if (!ReferenceEquals(parsedIgnore, ignore))
        {
            parsedIgnore = ignore;
            ignored = string.IsNullOrWhiteSpace(ignore)
                ? Array.Empty<string>()
                : Array.ConvertAll(ignore.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries), entry => entry.Trim());
        }
        if (plane == null) plane = FindPlane();
        hasOutline = plane != null && rect.parent != null && Measure(out outline);
        if (!hasOutline) return;
        toPlane = plane.worldToLocalMatrix * rect.parent.localToWorldMatrix;
        var at = home + new Vector3(x, y, 0f);
        Corners(Matrix4x4.TRS(at, homeRotation, rect.localScale), flat);
        Corners(Matrix4x4.TRS(at, turn, rect.localScale), leaning);
    }

    private void Place(float tilt)
    {
        Vector3 slide = hasOutline ? toPlane.inverse.MultiplyVector(push) : Vector3.zero;
        rect.localRotation = turn;
        rect.localPosition = home + new Vector3(x, y, depth * tilt) + slide;
    }

    // Fills the gap between this piece's underside and the top of the piece named in bridge, straight down on the plane
    // across this piece's width, kept bridgeGap clear of both; where that leaves no room there is nothing to fill.
    private void Bridge()
    {
        if (string.IsNullOrEmpty(bridge)) { HideWedge(); return; }
        if (below == null || below.name != bridge || !below.isActiveAndEnabled)
            below = live.Find(piece => piece != this && piece.name == bridge);
        if (below == null || !hasOutline || !below.hasOutline || below.plane != plane) { HideWedge(); return; }
        if (wedge == null) wedge = HudBridge.Create(CommonParent(rect, below.rect));
        wedge.Paint(bridgeColor, bridgeAccent);

        Vector3 p0 = OnPlane(rect, outline.xMin, outline.yMin), p1 = OnPlane(rect, outline.xMax, outline.yMin);
        Vector3 t0 = OnPlane(below.rect, below.outline.xMin, below.outline.yMax);
        Vector3 t1 = OnPlane(below.rect, below.outline.xMax, below.outline.yMax);
        Vector3 q0 = Under(p0, t0, t1), q1 = Under(p1, t0, t1);
        float clear = Mathf.Max(0f, bridgeGap);
        p0.y -= clear; p1.y -= clear; q0.y += clear; q1.y += clear;
        float h0 = p0.y - q0.y, h1 = p1.y - q1.y;
        if (h0 <= 0f && h1 <= 0f) { HideWedge(); return; }
        if (h0 <= 0f || h1 <= 0f)
        {
            // Room on one side only: the other end closes where the edges meet.
            var meet = Vector3.Lerp(p0, p1, h0 / (h0 - h1));
            if (h0 > 0f) p1 = q1 = meet;
            else p0 = q0 = meet;
        }
        if (bridgeWidth > 0f)
        {
            Narrow(ref p0, ref q0);
            Narrow(ref p1, ref q1);
        }
        wedge.SetEdges(ToWedge(p0), ToWedge(p1), ToWedge(q0), ToWedge(q1));
    }

    // One end of the band: centred in the gap there, no thicker than bridgeWidth.
    private void Narrow(ref Vector3 top, ref Vector3 bottom)
    {
        var middle = (top + bottom) * .5f;
        float half = Mathf.Clamp(top.y - bottom.y, 0f, bridgeWidth) * .5f;
        top = middle + Vector3.up * half;
        bottom = middle - Vector3.up * half;
    }

    private void HideWedge()
    {
        if (wedge != null) wedge.Hide();
    }

    private Vector3 ToWedge(Vector3 onPlane) => wedge.rectTransform.InverseTransformPoint(plane.TransformPoint(onPlane));

    private Vector3 OnPlane(RectTransform piece, float px, float py) =>
        plane.InverseTransformPoint(piece.TransformPoint(new Vector3(px, py, 0f)));

    // The point of the edge a-b straight below (or above) point on the plane.
    private static Vector3 Under(Vector3 point, Vector3 a, Vector3 b)
    {
        float run = b.x - a.x;
        return Mathf.Abs(run) < 1e-3f ? a : Vector3.LerpUnclamped(a, b, (point.x - a.x) / run);
    }

    private static Transform CommonParent(Transform a, Transform b)
    {
        for (var parent = a.parent; parent != null; parent = parent.parent)
            if (b.IsChildOf(parent)) return parent;
        return a.parent;
    }

    // The side of the curved HUD the piece lies on; pieces are kept apart only against others on the same one.
    private Transform FindPlane()
    {
        var side = GetComponentInParent<HudPlane>();
        if (side != null) return side.transform;
        var canvas = GetComponentInParent<Canvas>();
        return canvas != null ? canvas.rootCanvas.transform : null;
    }

    private void Corners(Matrix4x4 local, Vector2[] into)
    {
        var m = toPlane * local;
        into[0] = m.MultiplyPoint3x4(new Vector3(outline.xMin, outline.yMin));
        into[1] = m.MultiplyPoint3x4(new Vector3(outline.xMax, outline.yMin));
        into[2] = m.MultiplyPoint3x4(new Vector3(outline.xMax, outline.yMax));
        into[3] = m.MultiplyPoint3x4(new Vector3(outline.xMin, outline.yMax));
    }

    // Slides the two apart if leaning brought them closer than allowed. Returns whether they moved.
    private static bool KeepApart(HudTilt a, HudTilt b)
    {
        if (!a.hasOutline || !b.hasOutline || a.plane != b.plane) return false;
        if (a.rect.IsChildOf(b.rect) || b.rect.IsChildOf(a.rect)) return false;
        float allowed = Mathf.Min(Separation(a.flat, Vector2.zero, b.flat, Vector2.zero, out _), Mathf.Max(a.gap, b.gap));
        float now = Separation(a.leaning, a.push, b.leaning, b.push, out Vector2 away);
        if (now >= allowed - .01f) return false;
        Vector2 slide = away * ((allowed - now) * .5f);
        a.push -= slide;
        b.push += slide;
        return true;
    }

    // How far apart two outlines are along the axis that parts them best (negative: how deep they overlap along the one
    // that frees them soonest), with that axis pointing from a to b. Sliding b along it by d widens the result by d.
    private static float Separation(Vector2[] a, Vector2 aShift, Vector2[] b, Vector2 bShift, out Vector2 away)
    {
        float best = float.MinValue;
        away = Vector2.zero;
        for (int shape = 0; shape < 2; shape++)
        {
            var corners = shape == 0 ? a : b;
            for (int e = 0; e < 2; e++)
            {
                Vector2 edge = corners[e + 1] - corners[e];
                var axis = new Vector2(-edge.y, edge.x);
                float length = axis.magnitude;
                if (length < 1e-4f) continue;
                axis /= length;
                Project(a, aShift, axis, out float aMin, out float aMax);
                Project(b, bShift, axis, out float bMin, out float bMax);
                float ahead = bMin - aMax, behind = aMin - bMax;
                float here = Mathf.Max(ahead, behind);
                if (here <= best) continue;
                best = here;
                away = ahead >= behind ? axis : -axis;
            }
        }
        return away == Vector2.zero ? float.MaxValue : best;
    }

    private static void Project(Vector2[] corners, Vector2 shift, Vector2 axis, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        foreach (var corner in corners)
        {
            float along = Vector2.Dot(corner + shift, axis);
            min = Mathf.Min(min, along);
            max = Mathf.Max(max, along);
        }
    }

    // What the piece draws, as a box in its own space: every visible graphic under it, text by its letters, anything
    // inside a mask cut to the mask, less the children named in ignore.
    private bool Measure(out Rect box)
    {
        var toLocal = rect.worldToLocalMatrix;
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        rect.GetComponentsInChildren(false, graphics);
        foreach (var graphic in graphics)
        {
            // A text's extra meshes are inside its letters; a wedge is drawn from the outlines, not part of them.
            if (!graphic.enabled || graphic is TMP_SubMeshUI || graphic is HudBridge) continue;
            if (graphic.color.a * graphic.canvasRenderer.GetAlpha() <= .01f) continue;
            if (!Drawn(graphic, toLocal, out Rect part)) continue;
            xMin = Mathf.Min(xMin, part.xMin); yMin = Mathf.Min(yMin, part.yMin);
            xMax = Mathf.Max(xMax, part.xMax); yMax = Mathf.Max(yMax, part.yMax);
        }
        graphics.Clear();
        box = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return xMax > xMin && yMax > yMin;
    }

    private bool Drawn(Graphic graphic, Matrix4x4 toLocal, out Rect part)
    {
        part = default;
        var own = graphic.rectTransform;
        if (HidesItself(own)) return false;
        Rect area = own.rect;
        if (graphic is TMP_Text text && !Letters(text, out area)) return false;
        part = Box(own, area, toLocal);
        for (Transform up = own; up != null && up != rect; up = up.parent)
        {
            if (IsIgnored(up.name)) return false;
            if (up != own && up is RectTransform holder && IsMask(holder) && !Cut(ref part, Box(holder, holder.rect, toLocal)))
                return false;
        }
        return true;
    }

    // The letters standing on their baseline. A text's own bounds reach down to the font's descender line, below the
    // figures, so spacing measured from them (the wedge's bridgeGap) would come out wider than set.
    private static bool Letters(TMP_Text text, out Rect area)
    {
        area = default;
        var info = text.textInfo;
        if (info?.characterInfo == null) return false;
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        int count = Mathf.Min(info.characterCount, info.characterInfo.Length);
        for (int i = 0; i < count; i++)
        {
            var letter = info.characterInfo[i];
            if (!letter.isVisible) continue;
            xMin = Mathf.Min(xMin, letter.bottomLeft.x); xMax = Mathf.Max(xMax, letter.topRight.x);
            yMin = Mathf.Min(yMin, letter.baseLine); yMax = Mathf.Max(yMax, letter.topRight.y);
        }
        if (xMax <= xMin || yMax <= yMin) return false;
        area = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }

    private bool IsIgnored(string name)
    {
        foreach (string entry in ignored)
        {
            if (entry.EndsWith("*") ? name.StartsWith(entry.Substring(0, entry.Length - 1), StringComparison.Ordinal)
                : name == entry)
                return true;
        }
        return false;
    }

    // A mask that does not show its own graphic only cuts what is under it.
    private static bool HidesItself(Transform t) =>
        (t.TryGetComponent(out Mask mask) && !mask.showMaskGraphic) || HasSoftMask(t);

    private static bool IsMask(Transform t) =>
        t.TryGetComponent(out Mask _) || t.TryGetComponent(out RectMask2D _) || HasSoftMask(t);

    // The soft mask package (Coffee) is found by name so this does not depend on it.
    private static bool HasSoftMask(Transform t)
    {
        t.GetComponents(behaviours);
        bool found = false;
        foreach (var behaviour in behaviours)
            if (behaviour != null && behaviour.GetType().Name == "SoftMask") { found = true; break; }
        behaviours.Clear();
        return found;
    }

    private static Rect Box(RectTransform t, Rect area, Matrix4x4 toLocal)
    {
        var m = toLocal * t.localToWorldMatrix;
        Vector2 a = m.MultiplyPoint3x4(new Vector3(area.xMin, area.yMin)), b = m.MultiplyPoint3x4(new Vector3(area.xMax, area.yMin));
        Vector2 c = m.MultiplyPoint3x4(new Vector3(area.xMax, area.yMax)), d = m.MultiplyPoint3x4(new Vector3(area.xMin, area.yMax));
        return Rect.MinMaxRect(Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)), Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)),
            Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)), Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)));
    }

    private static bool Cut(ref Rect part, Rect mask)
    {
        float xMin = Mathf.Max(part.xMin, mask.xMin), yMin = Mathf.Max(part.yMin, mask.yMin);
        float xMax = Mathf.Min(part.xMax, mask.xMax), yMax = Mathf.Min(part.yMax, mask.yMax);
        if (xMax <= xMin || yMax <= yMin) return false;
        part = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }
}
