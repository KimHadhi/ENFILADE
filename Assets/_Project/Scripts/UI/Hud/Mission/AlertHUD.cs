using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What just happened that the player should know of, said once at the top of the screen. The user's pick of the second
/// drafts (2026-11-10: "C랑 B가 좋아", after the first round's "A인데 C의 방향과 거리도 … 심플함은 추가하고 투머치함은
/// 간결하게"): a short dark band, slanted like the rest of the HUD, whose cap says the kind before the words are read
/// (running hazard stripes for danger, the nested diamond for the rest). It comes in from the side the thing is on, three
/// chevrons flowing off that end and the distance at the other (draft C); what is about the player themself has no side
/// and drops from above. A thin line under the words runs out as the band's time does. A danger with a place also
/// leaves a mark on it: the hazard square and the distance, sliding to the screen's edge and pointing the way when it is
/// out of view, and staying a while after the band has gone (draft B). The downed keep the mark they had
/// (TeammateMarkerHUD). Bands turn with a lean as the rest of the HUD does; marks stand where the world is. Built on
/// first use, on real time.
/// </summary>
[DisallowMultipleComponent]
public sealed class AlertHUD : MonoBehaviour
{
    public enum Kind { Danger, Team, Self }

    private const int MostBands = 3, MostMarks = 3, Chevrons = 3;
    // The band (px at 1080p): how far under the screen's top the first stands, its height and the gap to the next, its cap
    // and lean, the words' size and their room either side, the distance's room, and the least width.
    private const float Top = 168f, Height = 52f, Gap = 10f, Cap = 60f, Slant = 12f, TextSize = 27f, Pad = 18f, DistanceRoom = 96f,
        PlainEnd = 28f, LeastWidth = 300f;
    // Its life (real s): in, all of it, out. How far to the side it starts, or how far above with no side.
    private const float InSeconds = .2f, Life = 2.8f, OutSeconds = .3f, SlideFrom = 300f, DropFrom = 22f;
    // The chevrons off its end: how far apart, how fast they flow (px/s), their size and stroke.
    private const float ChevronStep = 18f, ChevronSpeed = 36f, ChevronWidth = 11f, ChevronHeight = 30f, ChevronStroke = 4f;
    // The mark: how far in from the screen's edge it stops, its square, its fade out (s), and how near it steps aside (m).
    private const float EdgeMargin = 76f, Glyph = 40f, MarkOutSeconds = .4f, HiddenWithin = 4f, ShownFrom = 6.5f;
    private static readonly Color Plate = new Color(.024f, .024f, .031f, .9f);
    private static AlertHUD instance;

    private sealed class Band
    {
        public RectTransform root, arrows;
        public CanvasGroup group;
        public Image plate, cap, line, flash;
        public HazardGraphic hazard;
        public DiamondGraphic diamond;
        public TextMeshProUGUI label, distance;
        public readonly ChevronGraphic[] chevrons = new ChevronGraphic[Chevrons];
        public Kind kind;
        public string what;
        public Transform where;
        public Vector3 place;
        public bool placed;
        public Color tint;
        public float age, width, y;
        // The side it came in from (-1 left, 1 right, 0 above), and the side its chevrons were last drawn for.
        public int side, drawnSide;
    }

    private sealed class Mark
    {
        public RectTransform root, pointer;
        public CanvasGroup group;
        public HazardGraphic hazard;
        public TextMeshProUGUI distance;
        public Transform where;
        public Func<bool> gone;
        public float age, life, height;
    }

    private readonly List<Band> shown = new List<Band>();
    private readonly Stack<Band> spare = new Stack<Band>();
    private readonly List<Mark> marks = new List<Mark>();
    private readonly Stack<Mark> spareMarks = new Stack<Mark>();
    private RectTransform canvas, bandRoot;

    /// <summary>
    /// Something dangerous: the band, and with a place and markSeconds, a mark on it for that long or until gone says so
    /// (it died) or it is no longer there.
    /// </summary>
    public static void Danger(string what, Transform where = null, float markSeconds = 0f, Func<bool> gone = null)
        => Show(Kind.Danger, what, where, KitStyle.Accent, markSeconds, gone);

    /// <summary>Something happened to a teammate; tint is theirs.</summary>
    public static void Team(string what, Transform where, Color tint) => Show(Kind.Team, what, where, tint, 0f, null);

    /// <summary>Something happened to the player themself.</summary>
    public static void Self(string what) => Show(Kind.Self, what, null, KitStyle.Ice, 0f, null);

    private static void Show(Kind kind, string what, Transform where, Color tint, float markSeconds, Func<bool> gone)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(what)) return;
        if (instance == null)
        {
            var host = new GameObject("Alerts", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = host.AddComponent<AlertHUD>();
        }
        instance.Add(kind, what, where, tint);
        if (where != null && markSeconds > 0f) instance.AddMark(where, markSeconds, gone);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelUI.SetupOverlay(gameObject, 27);
        canvas = (RectTransform)transform;
        bandRoot = FeelUI.Rect("Bands", canvas);
        bandRoot.anchorMin = bandRoot.anchorMax = new Vector2(.5f, 1f);
        bandRoot.anchoredPosition = new Vector2(0f, -Top);
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Add(Kind kind, string what, Transform where, Color tint)
    {
        // The same thing said again while it is still up starts over rather than stacking.
        Band band = null;
        foreach (var live in shown)
            if (live.kind == kind && live.where == where && live.what == what) { band = live; break; }
        if (band == null)
        {
            if (shown.Count >= MostBands) Retire(0);
            band = spare.Count > 0 ? spare.Pop() : BuildBand();
            shown.Add(band);
            band.y = -(shown.Count - 1) * (Height + Gap);
        }
        band.kind = kind; band.what = what; band.where = where; band.tint = tint;
        band.age = 0f;
        band.placed = where != null;
        if (band.placed) band.place = where.position;
        var view = Camera.main;
        band.side = band.placed && view != null ? SideOf(view, band.place) : 0;
        band.drawnSide = 0;
        // Shown before it is dressed: its words are measured, which a label that is switched off may not do.
        band.root.gameObject.SetActive(true);
        Dress(band);
        Layout(band, view);
    }

    private void Retire(int index)
    {
        var band = shown[index];
        shown.RemoveAt(index);
        band.root.gameObject.SetActive(false);
        band.where = null;
        spare.Push(band);
    }

    private void AddMark(Transform where, float seconds, Func<bool> gone)
    {
        Mark mark = null;
        foreach (var live in marks) if (live.where == where) { mark = live; break; }
        if (mark == null)
        {
            if (marks.Count >= MostMarks) RetireMark(0);
            mark = spareMarks.Count > 0 ? spareMarks.Pop() : BuildMark();
            marks.Add(mark);
        }
        mark.where = where; mark.gone = gone; mark.life = seconds; mark.age = 0f;
        // It floats over the head, however tall the thing stands.
        mark.height = (where.TryGetComponent(out CapsuleCollider capsule) ? capsule.height * where.lossyScale.y : 1.8f) + .35f;
        mark.root.gameObject.SetActive(true);
        Layout(mark, Camera.main);
    }

    private void RetireMark(int index)
    {
        var mark = marks[index];
        marks.RemoveAt(index);
        mark.root.gameObject.SetActive(false);
        mark.where = null; mark.gone = null;
        spareMarks.Push(mark);
    }

    private void LateUpdate()
    {
        if (shown.Count == 0 && marks.Count == 0) return;
        float dt = Time.unscaledDeltaTime;
        var view = Camera.main;
        bandRoot.localRotation = Quaternion.Euler(0f, 0f, HudCamera.LeanRoll);
        for (int i = shown.Count - 1; i >= 0; i--)
        {
            shown[i].age += dt;
            if (shown[i].age >= Life) Retire(i);
        }
        for (int i = 0; i < shown.Count; i++)
        {
            // One above it gone, it moves up into the room.
            shown[i].y = Mathf.Lerp(shown[i].y, -i * (Height + Gap), 1f - Mathf.Exp(-14f * dt));
            Layout(shown[i], view);
        }
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var mark = marks[i];
            mark.age += dt;
            bool over = mark.age >= mark.life || mark.where == null || !mark.where.gameObject.activeInHierarchy;
            // What it marks is gone: it goes with it, by its fade rather than at once.
            if (!over && mark.gone != null && mark.gone() && mark.age < mark.life - MarkOutSeconds) mark.age = mark.life - MarkOutSeconds;
            if (over) RetireMark(i);
            else Layout(mark, view);
        }
    }

    private static float Out(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }

    // Which side of the view a place is on: 1 right, -1 left.
    private static int SideOf(Camera view, Vector3 place) => Vector3.Dot(view.transform.right, place - view.transform.position) >= 0f ? 1 : -1;

    // What its kind wears, and its width for these words.
    private void Dress(Band band)
    {
        bool danger = band.kind == Kind.Danger, self = band.kind == Kind.Self;
        band.cap.color = self ? KitStyle.Ice : KitStyle.Accent;
        band.hazard.gameObject.SetActive(danger);
        band.diamond.gameObject.SetActive(!danger);
        band.label.text = band.what;
        band.label.color = danger ? KitStyle.Accent : KitStyle.Ice;
        band.line.color = danger ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .55f);
        band.flash.enabled = danger;
        band.distance.gameObject.SetActive(band.placed);
        band.arrows.gameObject.SetActive(band.placed);
        Color arrow = danger ? KitStyle.Accent : band.tint;
        foreach (var chevron in band.chevrons) chevron.color = arrow;

        float width = Mathf.Max(LeastWidth, Cap + Pad + band.label.preferredWidth + (band.placed ? DistanceRoom : PlainEnd));
        band.width = width;
        band.plate.rectTransform.sizeDelta = band.flash.rectTransform.sizeDelta = new Vector2(width, Height);
        band.cap.rectTransform.anchoredPosition = new Vector2(-width * .5f + Cap * .5f, 0f);
        band.label.rectTransform.anchoredPosition = new Vector2(-width * .5f + Cap + Pad, 0f);
        band.label.rectTransform.sizeDelta = new Vector2(width - Cap - Pad - (band.placed ? DistanceRoom : PlainEnd) + 8f, Height);
        band.distance.rectTransform.anchoredPosition = new Vector2(width * .5f - 18f, 0f);
        // Along the plate's bottom edge, which its lean carries left of the top one.
        band.line.rectTransform.anchoredPosition = new Vector2(-width * .5f + 1f, -Height * .5f + 2.5f);
    }

    private void Layout(Band band, Camera view)
    {
        float t = band.age, arrive = Out(t / InSeconds), leave = Mathf.Clamp01((t - (Life - OutSeconds)) / OutSeconds);
        band.group.alpha = arrive * (1f - leave);
        band.root.anchoredPosition = new Vector2(band.side * (1f - arrive) * SlideFrom, band.y + (band.side == 0 ? (1f - arrive) * DropFrom : 0f));
        if (band.hazard.gameObject.activeSelf) band.hazard.Offset = t * 60f;
        band.line.rectTransform.sizeDelta = new Vector2((band.width - 12f) * (1f - Mathf.Clamp01((t - InSeconds) / (Life - InSeconds - OutSeconds))), 3f);
        // A danger glints white as it lands.
        if (band.flash.enabled)
            band.flash.color = new Color(1f, 1f, 1f, .7f * (t < .04f ? t / .04f : t < .09f ? 1f : 1f - Mathf.Clamp01((t - .09f) / .19f)));
        if (!band.placed) return;

        // Where it is now: it may have moved, and the player turned. One that is gone keeps its last place.
        if (band.where != null) band.place = band.where.position;
        int side = view != null ? SideOf(view, band.place) : band.side == 0 ? 1 : band.side;
        if (view != null) band.distance.text = Mathf.RoundToInt(Vector3.Distance(view.transform.position, band.place)) + "m";
        if (side != band.drawnSide)
        {
            band.drawnSide = side;
            foreach (var chevron in band.chevrons) chevron.Configure(side > 0, ChevronStroke);
        }
        band.arrows.anchoredPosition = new Vector2(side * (band.width * .5f + 14f), 0f);
        float reach = Chevrons * ChevronStep;
        for (int i = 0; i < Chevrons; i++)
        {
            // Each flows out to where the next began and starts over, dimming as it goes: one stream off the band's end.
            float along = i * ChevronStep + Mathf.Repeat(t * ChevronSpeed, ChevronStep);
            var chevron = band.chevrons[i];
            chevron.rectTransform.anchoredPosition = new Vector2(side * along, 0f);
            var color = chevron.color;
            color.a = (1f - along / reach) * Mathf.Clamp01(along / 5f);
            chevron.color = color;
        }
    }

    private void Layout(Mark mark, Camera view)
    {
        if (view == null) { mark.group.alpha = 0f; return; }
        Vector3 place = mark.where.position + Vector3.up * mark.height;
        float distance = Vector3.Distance(view.transform.position, mark.where.position);
        Vector3 point = view.WorldToViewportPoint(place);
        Vector2 offset = new Vector2(point.x - .5f, point.y - .5f);
        // Behind the camera the projection mirrors, and the way to turn is the other side (as TeammateMarkerHUD has it).
        if (point.z < 0f) offset = -offset;
        Vector2 size = canvas.rect.size, half = size * .5f - new Vector2(EdgeMargin, EdgeMargin), position = Vector2.Scale(offset, size);
        float reach = Mathf.Max(Mathf.Abs(position.x) / half.x, Mathf.Abs(position.y) / half.y);
        bool away = point.z < 0f || reach > 1f;
        if (point.z < 0f && position.sqrMagnitude < 1f) position = Vector2.down * half.y;
        else if (away) position /= Mathf.Max(reach, .001f);
        mark.root.anchoredPosition = position;

        // Out of view it points the way from the screen's edge, the distance on its inner side; in view it sits over the
        // thing itself, fainter, the distance under it.
        mark.pointer.gameObject.SetActive(away);
        Vector2 outward = position.sqrMagnitude > 1f ? position.normalized : Vector2.down;
        if (away)
        {
            mark.pointer.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg);
            mark.pointer.anchoredPosition = outward * (Glyph * .5f + 12f + Mathf.Abs(Mathf.Sin(mark.age * 4f)) * 6f);
        }
        mark.distance.rectTransform.anchoredPosition = away ? -outward * (Glyph * .5f + 26f) : new Vector2(0f, -Glyph * .5f - 15f);
        mark.distance.text = Mathf.RoundToInt(distance) + "m";
        mark.hazard.Offset = mark.age * 36f;
        float arrive = Out(mark.age / .18f), leave = Mathf.Clamp01((mark.age - (mark.life - MarkOutSeconds)) / MarkOutSeconds);
        mark.root.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, arrive);
        // Up close the thing itself says where it is.
        mark.group.alpha = arrive * (1f - leave) * (away ? 1f : .7f) * Mathf.InverseLerp(HiddenWithin, ShownFrom, distance);
    }

    private Band BuildBand()
    {
        var band = new Band { root = FeelUI.Rect("Band", bandRoot) };
        band.group = band.root.gameObject.AddComponent<CanvasGroup>();
        band.group.blocksRaycasts = band.group.interactable = false;
        band.plate = Panel("Plate", band.root, Plate);
        KitSkew.On(band.plate, Slant);
        band.cap = Panel("Cap", band.root, KitStyle.Accent);
        band.cap.rectTransform.sizeDelta = new Vector2(Cap, Height);
        KitSkew.On(band.cap, Slant);
        band.hazard = HudShapes.Make<HazardGraphic>("Hazard", band.cap.transform, KitStyle.Ink);
        band.hazard.rectTransform.sizeDelta = new Vector2(Cap, Height);
        band.hazard.Stripe = 9f;
        band.hazard.Slant = Slant;
        band.diamond = HudShapes.Make<DiamondGraphic>("Diamond", band.cap.transform, KitStyle.Ink);
        band.diamond.rectTransform.sizeDelta = Vector2.one * 26f;

        band.label = Words("Words", band.root, TextSize, TextAlignmentOptions.MidlineLeft);
        band.label.rectTransform.pivot = new Vector2(0f, .5f);
        band.distance = Words("Distance", band.root, 23f, TextAlignmentOptions.MidlineRight);
        band.distance.rectTransform.pivot = new Vector2(1f, .5f);
        band.distance.rectTransform.sizeDelta = new Vector2(DistanceRoom, Height);
        band.distance.color = KitStyle.Ice;
        band.line = Panel("Time", band.root, KitStyle.Accent);
        band.line.rectTransform.pivot = new Vector2(0f, .5f);
        band.flash = Panel("Glint", band.root, Color.white);
        KitSkew.On(band.flash, Slant);

        band.arrows = FeelUI.Rect("Arrows", band.root);
        for (int i = 0; i < Chevrons; i++)
        {
            band.chevrons[i] = HudShapes.Make<ChevronGraphic>("Chevron " + i, band.arrows, KitStyle.Accent);
            band.chevrons[i].rectTransform.sizeDelta = new Vector2(ChevronWidth, ChevronHeight);
        }
        band.root.gameObject.SetActive(false);
        return band;
    }

    private Mark BuildMark()
    {
        var mark = new Mark { root = FeelUI.Rect("Mark", canvas) };
        mark.group = mark.root.gameObject.AddComponent<CanvasGroup>();
        mark.group.blocksRaycasts = mark.group.interactable = false;
        // The hazard square: the HUD's near-black, its pink stripes running.
        Panel("Square", mark.root, KitStyle.Ink).rectTransform.sizeDelta = Vector2.one * Glyph;
        mark.hazard = HudShapes.Make<HazardGraphic>("Hazard", mark.root, KitStyle.Accent);
        mark.hazard.rectTransform.sizeDelta = Vector2.one * (Glyph - 6f);
        mark.hazard.Stripe = 6f;
        mark.pointer = FeelUI.Rect("Pointer", mark.root);
        mark.pointer.sizeDelta = new Vector2(13f, 20f);
        var arrow = mark.pointer.gameObject.AddComponent<UITriangle>();
        arrow.raycastTarget = false;
        arrow.color = KitStyle.Accent;
        // Pointing along its own right, so turning it to the way out is one angle.
        arrow.SetCorners(new Vector2(0f, 1f), new Vector2(1f, .5f), new Vector2(0f, 0f));
        mark.distance = FeelUI.ScoreLabel("Distance", mark.root, 20f, TextAlignmentOptions.Center);
        mark.distance.rectTransform.sizeDelta = new Vector2(120f, 26f);
        mark.distance.color = KitStyle.Ice;
        mark.root.gameObject.SetActive(false);
        return mark;
    }

    // The band's type: the score font, flat on the plate (a shadow only muddies it there).
    private static TextMeshProUGUI Words(string name, Transform parent, float size, TextAlignmentOptions alignment)
    {
        var text = FeelUI.ScoreLabel(name, parent, size, alignment);
        if (text.font != null && text.font.material != null) text.fontSharedMaterial = text.font.material;
        text.fontStyle = FontStyles.Bold;
        return text;
    }

    private static Image Panel(string name, Transform parent, Color color)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = color;
        return image;
    }
}
