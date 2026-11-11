using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ultimate as a magazine on the portrait's left edge, from the user's round 4 note (2026-11-11): the neat place of
/// draft B, the motion of draft C, and draft A's magazine change for spending it.
/// <list type="bullet">
/// <item>One round is a tenth of what one use costs (UltimateCharge.RequiredCharge), so ten rounds is ready; room up to
/// the maximum holds the overflow above a pink line. Rounds stack from the bottom, lying on their side towards the
/// picture, with a tick per round on the left.</item>
/// <item>The next round draws its outline as the charge comes; complete, it is pushed in from the left with a white
/// flash and a few sparks.</item>
/// <item>Ready: the hatch over the magazine is slammed shut by a pink plate, beats, and a light runs up the rounds.</item>
/// <item>Spent (the charge drops by about one cost while ready, as the perk cards' TryConsume does): the plate slides
/// off, the spent rounds strip out of the top with their cases thrown left, the magazine drops away spinning, and a
/// fresh one slides up holding what was left over.</item>
/// </list>
/// One mesh, nothing over the picture. Game time, so a spend made while the perk cards pause the game plays out as the
/// game goes on. Made and placed by HudPortrait; numbers in Tables/UltMagazine.csv.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UltimateMagazine : MaskableGraphic, ITableBound
{
    public const string TableName = "UltMagazine", Row = "Magazine";
    private const int MaxSlots = 16;
    private const float Pad = 4f, HatchHeight = 14f, HatchSkew = 4f, CaseWidth = 9f, CaseHeight = 4.5f;

    [Tooltip("Width of the magazine beside the picture (px at 1080p).")]
    [SerializeField, Min(8f)] private float width = 24f;
    [Tooltip("Room between the magazine and the picture's black mat.")]
    [SerializeField, Min(0f)] private float gap = 2f;
    [Tooltip("Rounds that make one use of the ultimate.")]
    [SerializeField, Min(1)] private int roundsPerUse = 10;
    [SerializeField, Min(.01f)] private float seatSeconds = .22f;
    [Tooltip("How far left a completed round starts before it is pushed in.")]
    [SerializeField, Min(0f)] private float seatPush = 16f;
    [SerializeField, Min(.01f)] private float slamSeconds = .16f;
    [Tooltip("How long the spent rounds take to strip out of the top.")]
    [SerializeField, Min(.01f)] private float stripSeconds = .3f;
    [SerializeField, Min(.01f)] private float dropSeconds = .45f;
    [SerializeField, Min(0f)] private float dropFall = 120f;
    [Tooltip("Wait after the strip before the fresh magazine comes up.")]
    [SerializeField, Min(0f)] private float freshDelay = .2f;
    [SerializeField, Min(.01f)] private float freshSeconds = .35f;
    [SerializeField, Min(0f)] private float freshRise = 70f;
    [SerializeField, Min(.1f)] private float sweepSeconds = 1.25f;
    [SerializeField, Min(.1f)] private float caseSeconds = .55f;
    [SerializeField, Min(.1f)] private float beatsPerSecond = 1.1f;

    private static readonly Color Pink = new Color(1f, .31f, .47f), Ice = new Color(1f, .96f, .97f);
    private static readonly Color Night = new Color(.055f, .055f, .065f), Brass = new Color(.91f, .76f, .48f);

    private bool primed, ready;
    private int loaded, slots;
    private float part, lastCharge, readyTime = -99f, changeTime = -99f, now;
    private int changeFrom, changeTo;
    private readonly float[] seatTime = new float[MaxSlots];
    // Where drawing goes: a turn about a pivot, then an offset, and an opacity (the falling and the fresh magazine).
    private Vector2 offset, pivot;
    private float cos = 1f, sin, alpha = 1f;

    public float Width => width;
    public float Gap => gap;

    void ITableBound.OnTableApplied() => primed = false;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
        for (int i = 0; i < MaxSlots; i++) seatTime[i] = -99f;
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying) return;
        now = Time.time;
        var player = PlayerRoster.Local;
        var source = player != null ? player.Ultimate : null;
        if (source == null || source.RequiredCharge <= 0f)
        {
            if (slots != 0) { slots = 0; SetVerticesDirty(); }
            primed = false;
            return;
        }
        Read(source.CurrentCharge, source.RequiredCharge, source.MaximumCharge);
        // Only while something moves or stands lit; a still, unready magazine is drawn once.
        if (ready || Animating) SetVerticesDirty();
    }

    private void Read(float charge, float required, float maximum)
    {
        float roundSize = required / roundsPerUse;
        int newSlots = Mathf.Clamp(Mathf.CeilToInt(maximum / roundSize - .001f), roundsPerUse, MaxSlots);
        float rounds = charge / roundSize;
        int newLoaded = Mathf.Clamp(Mathf.FloorToInt(rounds + 1e-4f), 0, newSlots);
        float newPart = newLoaded >= newSlots ? 0f : Mathf.Clamp01(rounds - newLoaded);
        bool nowReady = newLoaded >= roundsPerUse;
        bool changed = !primed || newLoaded != loaded || newSlots != slots || Mathf.Abs(newPart - part) > .002f;
        if (!primed)
        {
            primed = true;
            for (int i = 0; i < MaxSlots; i++) seatTime[i] = -99f;
            readyTime = -99f;
        }
        else if (ready && charge < lastCharge - required * .9f)
        {
            // Spent: the magazine is changed; what is left over comes up in the fresh one, already seated.
            changeTime = now; changeFrom = loaded; changeTo = newLoaded;
            for (int i = 0; i < MaxSlots; i++) seatTime[i] = -99f;
            readyTime = nowReady ? now + stripSeconds + freshDelay + freshSeconds : -99f;
        }
        else
        {
            // New rounds go in one after another, a beat apart, when several come at once.
            for (int i = loaded; i < newLoaded && i < MaxSlots; i++) seatTime[i] = now + (i - loaded) * .05f;
            if (nowReady && !ready) readyTime = now + Mathf.Max(0, newLoaded - 1 - loaded) * .05f + seatSeconds * .5f;
        }
        ready = nowReady;
        loaded = newLoaded; part = newPart; slots = newSlots; lastCharge = charge;
        if (changed) SetVerticesDirty();
    }

    private float ChangeLength => stripSeconds + freshDelay + freshSeconds;

    private bool Animating
    {
        get
        {
            if (now - changeTime < Mathf.Max(ChangeLength, stripSeconds + dropSeconds) + caseSeconds) return true;
            for (int i = 0; i < loaded && i < MaxSlots; i++) if (now - seatTime[i] < seatSeconds) return true;
            return false;
        }
    }

    // ---------------------------------------------------------------- the mesh

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (slots <= 0 || r.width <= 0f || r.height <= 0f) return;
        float c = now - changeTime;
        int spent = changeFrom - changeTo;
        if (c >= 0f && c < Mathf.Max(ChangeLength, stripSeconds + dropSeconds))
        {
            // The old magazine: the spent rounds leave from the top, then it falls away turning.
            int stripped = c < stripSeconds ? Mathf.FloorToInt(spent * c / stripSeconds) : spent;
            float d = Mathf.Clamp01((c - stripSeconds) / dropSeconds);
            if (d < 1f)
            {
                Pose(new Vector2(0f, -(d * d * dropFall + d * 8f)), -35f * d * d, r.center, 1f - d);
                Magazine(mesh, r, changeFrom - stripped, 0f, false);
            }
            float f = Mathf.Clamp01((c - stripSeconds - freshDelay) / freshSeconds);
            if (f > 0f)
            {
                Pose(new Vector2(0f, -freshRise * (1f - Kick(f))), 0f, r.center, f);
                Magazine(mesh, r, changeTo, part, false);
            }
        }
        else
        {
            Pose(Vector2.zero, 0f, r.center, 1f);
            Magazine(mesh, r, loaded, part, true);
        }
        Pose(Vector2.zero, 0f, r.center, 1f);
        Hatch(mesh, r, c, spent);
        Cases(mesh, r, c, spent);
    }

    private void Magazine(VertexHelper mesh, Rect r, int count, float partial, bool live)
    {
        float pitch = (r.height - 2f * Pad) / slots, h = Mathf.Min(8f, pitch * .62f), length = r.width - 6f;
        Quad(mesh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin), With(Night, .96f));
        Frame(mesh, r, 1f, With(Ice, .28f));
        for (int i = 0; i < slots; i++)
        {
            float y = r.yMin + Pad + pitch * (i + .5f), tick = (i + 1) % 5 == 0 ? 8f : 4f;
            Line(mesh, new Vector2(r.xMin - tick - 1f, y), new Vector2(r.xMin - 1f, y), 1.4f, With(Ice, i < count ? .7f : .22f));
        }
        // The ready line between one use's rounds and the overflow.
        if (slots > roundsPerUse)
        {
            float y = r.yMin + Pad + pitch * roundsPerUse;
            Quad(mesh, new Vector2(r.xMin - 11f, y - .8f), new Vector2(r.xMin - 11f, y + .8f), new Vector2(r.xMax, y + .8f), new Vector2(r.xMax, y - .8f), Pink);
        }
        float x = r.xMin + 3f;
        for (int i = 0; i < slots; i++)
        {
            float y = r.yMin + Pad + pitch * (i + .5f);
            if (i < count)
            {
                float s = live && i < MaxSlots ? (now - seatTime[i]) / seatSeconds : 9f;
                bool seating = s >= 0f && s < 1f;
                if (s < 0f) continue; // queued behind the round before it
                float dx = seating ? -seatPush * (1f - Kick(s)) : 0f;
                Round(mesh, x + dx, y, length, h, seating && s < .3f ? Ice : Pink, Ice);
                if (seating) Sparks(mesh, new Vector2(x + dx - 2f, y), s, i);
            }
            else
            {
                Round(mesh, x, y, length, h, With(Ice, .06f), With(Ice, .09f));
                if (live && i == count && partial > .02f)
                {
                    Round(mesh, x, y, length, h, With(Pink, .3f * partial), With(Ice, .3f * partial));
                    Outline(mesh, x, y, length, h, partial, With(Ice, .9f));
                }
            }
        }
        if (live && ready && now >= readyTime)
        {
            float k = Mathf.Repeat((now - readyTime) / sweepSeconds, 1f), y = r.yMin + k * r.height;
            Quad(mesh, new Vector2(r.xMin + 2f, y - 1f), new Vector2(r.xMin + 2f, y + 1f), new Vector2(r.xMax - 2f, y + 1f), new Vector2(r.xMax - 2f, y - 1f), With(Ice, .5f * (1f - .5f * k)));
        }
    }

    // The hatch over the magazine: open (a dark plate with an empty nested diamond) until ready, then slammed shut by a pink plate.
    private void Hatch(VertexHelper mesh, Rect r, float c, int spent)
    {
        float x0 = r.xMin - 3f, y0 = r.yMax + 4f, w = r.width + 6f, cx = x0 + w * .5f + HatchSkew * .5f, cy = y0 + HatchHeight * .5f;
        Plate(mesh, x0, y0, w, With(Night, .96f));
        PlateOutline(mesh, x0, y0, w, 0f, 1.2f, With(Ice, .35f));
        Diamond(mesh, cx, cy, 4.5f, With(Ice, .45f), false);
        bool closed = ready && now >= readyTime;
        bool leaving = c >= 0f && c < .1f;
        if (closed || leaving)
        {
            float k = closed ? Kick(Mathf.Clamp01((now - readyTime) / slamSeconds)) : 1f - Mathf.Clamp01(c / .1f);
            float shift = -30f * (1f - k), a = closed ? 1f : k;
            Color plate = closed && now - readyTime < .05f ? Ice : Pink;
            Plate(mesh, x0 + shift, y0, w, With(plate, a));
            Diamond(mesh, cx + shift, cy, 4f, With(Night, a), true);
            if (closed)
            {
                float beat = UltimatePortraitGlow.Heartbeat(now - readyTime, beatsPerSecond);
                PlateOutline(mesh, x0, y0, w, 3f, 2f, With(Pink, .5f * beat));
            }
        }
        // Each spent round flashes the hatch as it goes.
        for (int i = 0; i < spent; i++)
        {
            float age = c - stripSeconds * (i + 1) / Mathf.Max(1, spent);
            if (age >= 0f && age < .07f) Plate(mesh, x0, y0, w, With(Ice, 1f - age / .07f));
        }
    }

    // The spent rounds' cases, thrown out of the hatch to the left.
    private void Cases(VertexHelper mesh, Rect r, float c, int spent)
    {
        Vector2 port = new Vector2(r.xMin - 3f, r.yMax + 4f + HatchHeight * .5f);
        for (int i = 0; i < spent; i++)
        {
            float k = (c - stripSeconds * (i + 1) / Mathf.Max(1, spent)) / caseSeconds;
            if (k < 0f || k >= 1f) continue;
            Vector2 at = port + new Vector2(-k * 60f * (.8f + .4f * Hash(i)), k * 48f - k * k * 90f);
            float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.6f, 1f, k));
            Spun(mesh, at, new Vector2(CaseWidth, CaseHeight) * .5f, k * 520f, With(Brass, a));
        }
    }

    // ---------------------------------------------------------------- shapes

    // A cartridge on its side, bullet towards the picture; x, y: its back end, centre line.
    private void Round(VertexHelper mesh, float x, float y, float length, float h, Color body, Color tip)
    {
        float m = length * .62f, hh = h * .5f;
        Quad(mesh, new Vector2(x, y - hh), new Vector2(x, y + hh), new Vector2(x + m, y + hh), new Vector2(x + m, y - hh), body);
        Fan(mesh, tip, new Vector2(x + m, y - hh + .5f), new Vector2(x + m, y + hh - .5f), new Vector2(x + length * .84f, y + hh - 1.1f),
            new Vector2(x + length, y), new Vector2(x + length * .84f, y - hh + 1.1f));
        Quad(mesh, new Vector2(x + 1.5f, y - hh), new Vector2(x + 1.5f, y + hh), new Vector2(x + 3f, y + hh), new Vector2(x + 3f, y - hh), With(Night, .45f * body.a));
    }

    // The next round's outline, drawn round from its back end as far as the charge has come.
    private void Outline(VertexHelper mesh, float x, float y, float length, float h, float share, Color tint)
    {
        float hh = h * .5f;
        Vector2[] p =
        {
            new Vector2(x, y - hh), new Vector2(x, y + hh), new Vector2(x + length * .84f, y + hh),
            new Vector2(x + length, y), new Vector2(x + length * .84f, y - hh), new Vector2(x, y - hh)
        };
        float total = 0f;
        for (int i = 1; i < p.Length; i++) total += Vector2.Distance(p[i - 1], p[i]);
        float left = total * Mathf.Clamp01(share);
        for (int i = 1; i < p.Length && left > 0f; i++)
        {
            float seg = Vector2.Distance(p[i - 1], p[i]);
            Vector2 end = seg <= left ? p[i] : Vector2.Lerp(p[i - 1], p[i], left / seg);
            Line(mesh, p[i - 1], end, 1.2f, tint);
            left -= seg;
        }
    }

    private void Sparks(VertexHelper mesh, Vector2 from, float k, int seed)
    {
        for (int i = 0; i < 3; i++)
        {
            float angle = Mathf.PI + (Hash(seed * 7 + i) - .5f) * 1.6f;
            float reach = 4f + (1f - Mathf.Pow(1f - k, 3f)) * (14f + Hash(seed * 7 + i + 3) * 12f), size = 4f * (1f - k) + 1.2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), at = from + dir * reach;
            Vector2 side = new Vector2(Mathf.Cos(angle + 1.6f), Mathf.Sin(angle + 1.6f));
            Fan(mesh, With(i % 2 == 0 ? Ice : Pink, 1f - k), at, at + dir * size * 1.8f, at + side * size * .6f);
        }
    }

    private void Plate(VertexHelper mesh, float x, float y, float w, Color tint)
        => Quad(mesh, new Vector2(x, y), new Vector2(x + HatchSkew, y + HatchHeight), new Vector2(x + w + HatchSkew, y + HatchHeight), new Vector2(x + w, y), tint);

    private void PlateOutline(VertexHelper mesh, float x, float y, float w, float grow, float thickness, Color tint)
    {
        if (tint.a <= 0f) return;
        Vector2 a = new Vector2(x - grow, y - grow), b = new Vector2(x + HatchSkew - grow, y + HatchHeight + grow);
        Vector2 cc = new Vector2(x + w + HatchSkew + grow, y + HatchHeight + grow), d = new Vector2(x + w + grow, y - grow);
        Line(mesh, a, b, thickness, tint); Line(mesh, b, cc, thickness, tint); Line(mesh, cc, d, thickness, tint); Line(mesh, d, a, thickness, tint);
    }

    // The HUD's nested diamond: an outline with a small filled one inside, or one solid diamond.
    private void Diamond(VertexHelper mesh, float x, float y, float radius, Color tint, bool solid)
    {
        Vector2 t = new Vector2(x, y + radius), rr = new Vector2(x + radius, y), b = new Vector2(x, y - radius), l = new Vector2(x - radius, y);
        if (solid) { Quad(mesh, l, t, rr, b, tint); return; }
        Line(mesh, l, t, 1.2f, tint); Line(mesh, t, rr, 1.2f, tint); Line(mesh, rr, b, 1.2f, tint); Line(mesh, b, l, 1.2f, tint);
        float s = radius * .45f;
        Quad(mesh, new Vector2(x - s, y), new Vector2(x, y + s), new Vector2(x + s, y), new Vector2(x, y - s), tint);
    }

    private void Frame(VertexHelper mesh, Rect r, float thickness, Color tint)
    {
        Vector2 a = new Vector2(r.xMin, r.yMin), b = new Vector2(r.xMin, r.yMax), c = new Vector2(r.xMax, r.yMax), d = new Vector2(r.xMax, r.yMin);
        Line(mesh, a, b, thickness, tint); Line(mesh, b, c, thickness, tint); Line(mesh, c, d, thickness, tint); Line(mesh, d, a, thickness, tint);
    }

    private void Line(VertexHelper mesh, Vector2 a, Vector2 b, float thickness, Color tint)
    {
        Vector2 along = b - a;
        if (along.sqrMagnitude < 1e-6f) return;
        Vector2 n = new Vector2(-along.y, along.x).normalized * (thickness * .5f);
        Quad(mesh, a - n, a + n, b + n, b - n, tint);
    }

    private void Spun(VertexHelper mesh, Vector2 centre, Vector2 half, float degrees, Color tint)
    {
        float rad = degrees * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        Vector2 Corner(float x, float y) => centre + new Vector2(x * c - y * s, x * s + y * c);
        Quad(mesh, Corner(-half.x, -half.y), Corner(-half.x, half.y), Corner(half.x, half.y), Corner(half.x, -half.y), tint);
    }

    // ---------------------------------------------------------------- vertices

    private void Pose(Vector2 move, float degrees, Vector2 about, float opacity)
    {
        offset = move; pivot = about; alpha = opacity;
        float rad = degrees * Mathf.Deg2Rad; cos = Mathf.Cos(rad); sin = Mathf.Sin(rad);
    }

    private Vector2 Place(Vector2 p)
    {
        Vector2 d = p - pivot;
        return pivot + new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos) + offset;
    }

    private Color32 Tint(Color tint)
    {
        Color c = tint * color;
        c.a *= alpha;
        return c;
    }

    private void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        if (tint.a * alpha <= .002f) return;
        int i = mesh.currentVertCount;
        Color32 v = Tint(tint);
        mesh.AddVert(Place(a), v, Vector2.zero);
        mesh.AddVert(Place(b), v, Vector2.zero);
        mesh.AddVert(Place(c), v, Vector2.zero);
        mesh.AddVert(Place(d), v, Vector2.zero);
        mesh.AddTriangle(i, i + 1, i + 2);
        mesh.AddTriangle(i + 2, i + 3, i);
    }

    // A convex polygon as a fan from its first point.
    private void Fan(VertexHelper mesh, Color tint, params Vector2[] points)
    {
        if (tint.a * alpha <= .002f || points.Length < 3) return;
        int i = mesh.currentVertCount;
        Color32 v = Tint(tint);
        foreach (var p in points) mesh.AddVert(Place(p), v, Vector2.zero);
        for (int k = 1; k < points.Length - 1; k++) mesh.AddTriangle(i, i + k, i + k + 1);
    }

    private static Color With(Color c, float a) { c.a = a; return c; }
    // Ease out past the end and back: the snap of something seating.
    private static float Kick(float t) { t = Mathf.Clamp01(t); float u = t - 1f; return 1f + 2.9f * u * u * u + 1.9f * u * u; }
    private static float Hash(int i) { float x = Mathf.Sin(i * 127.1f + 311.7f) * 43758.5453f; return x - Mathf.Floor(x); }
}
