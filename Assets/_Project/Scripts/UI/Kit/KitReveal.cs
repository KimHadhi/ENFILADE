using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// How a menu panel opens and closes, after the user's reference video (a "BASE UPGRADE" panel). Opening, about 0.8 s:
/// the thin pink frame lines draw themselves along their length; the title slides in from the left behind a mask
/// (a number beside it counts up, KitReveal.Elapsed); a line sweeps down the panel; each card grows out from its left
/// edge one after another as a see-through pink block with its content already showing, and the pink then drains
/// away, card by card from the top, to the card's own dark colour. Closing, under 0.2 s: the whole panel slides off to
/// the left as it fades. Parts are registered once when the panel is built; Open replays them all. Unscaled time, so a
/// paused or slowed game doesn't slow the menus.
/// </summary>
[DisallowMultipleComponent]
public sealed class KitReveal : MonoBehaviour
{
    // A card grows over GrowSeconds, holds its pink FlashHold, then drains over DrainSeconds.
    public const float GrowSeconds = .16f, FlashHold = .12f, DrainSeconds = .3f, CloseSeconds = .18f, FadeInSeconds = .08f;

    private enum Kind { Line, Slide, Card, Sweep }

    private sealed class Part
    {
        public Kind Kind;
        public RectTransform Rect;
        public Image Flash;
        public Color Settled;
        public float At, Length;
        public Vector2 From, To;
    }

    private readonly List<Part> parts = new List<Part>();
    private RectTransform root;
    private CanvasGroup group;
    private Vector2 home;
    private float openedAt = float.NegativeInfinity, closingAt = float.NegativeInfinity, longest;
    private bool fades = true;
    private Action closed;

    /// <summary>Seconds since it last opened (unscaled).</summary>
    public float Elapsed => Time.unscaledTime - openedAt;
    public bool Closing => closed != null;

    private void Awake() => Ensure();

    // Set up on first use: a panel built while it (or a parent) is hidden gets no Awake until it shows.
    private void Ensure()
    {
        if (root != null && group != null) return;
        root = (RectTransform)transform;
        home = root.anchoredPosition;
        if (!TryGetComponent(out group)) group = gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>A frame line that draws along its length from its pivot (set the pivot at the end it grows from).</summary>
    public void Line(RectTransform line, bool vertical, float at, float length = .22f)
        => Add(new Part { Kind = Kind.Line, Rect = line, At = at, Length = length, To = vertical ? new Vector2(1f, 0f) : new Vector2(0f, 1f) });

    /// <summary>Something that slides in by <paramref name="by"/> (px) to where it is, inside a masked holder.</summary>
    public void Slide(RectTransform moving, Vector2 by, float at, float length = .24f)
        => Add(new Part { Kind = Kind.Slide, Rect = moving, At = at, Length = length, To = moving.anchoredPosition, From = moving.anchoredPosition - by });

    /// <summary>A card that grows from its left edge (pivot x 0) under a pink <paramref name="flash"/> that then drains.</summary>
    public void Card(RectTransform card, Image flash, float at)
        => Add(new Part { Kind = Kind.Card, Rect = card, Flash = flash, Settled = flash.color, At = at, Length = GrowSeconds + FlashHold + DrainSeconds });

    /// <summary>A line that sweeps from <paramref name="from"/> to <paramref name="to"/> (its anchored position) and is gone.</summary>
    public void Sweep(RectTransform line, Vector2 from, Vector2 to, float at, float length = .45f)
        => Add(new Part { Kind = Kind.Sweep, Rect = line, At = at, Length = length, From = from, To = to });

    private void Add(Part part)
    {
        parts.Add(part);
        longest = Mathf.Max(longest, part.At + part.Length);
        Apply(part, 0f);
    }

    /// <summary>Hidden and still until Open (for a panel that opens a moment after it is built).</summary>
    public void Hide()
    {
        Ensure();
        group.alpha = 0f;
        enabled = false;
    }

    public void Open() => Open(true);

    /// <summary>Plays the opening; with <paramref name="fade"/> false the panel itself shows at once and only its
    /// registered parts play (a panel built again in place, where a fade of the whole would blink).</summary>
    public void Open(bool fade)
    {
        Ensure();
        closed = null;
        fades = fade;
        openedAt = Time.unscaledTime;
        root.anchoredPosition = home;
        gameObject.SetActive(true);
        enabled = true;
        foreach (var part in parts) Apply(part, 0f);
        group.alpha = fade ? 0f : 1f;
    }

    /// <summary>Slides it away to the left; <paramref name="done"/> runs once it is gone.</summary>
    public void Close(Action done)
    {
        if (Closing) return;
        Ensure();
        closed = done ?? (() => { });
        closingAt = Time.unscaledTime;
        enabled = true;
    }

    private void Update()
    {
        Ensure();
        if (Closing)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - closingAt) / CloseSeconds);
            float eased = k * k;
            root.anchoredPosition = home - new Vector2((root.rect.width + 60f) * eased, 0f);
            group.alpha = 1f - eased;
            if (k < 1f) return;
            var done = closed;
            closed = null;
            enabled = false;
            gameObject.SetActive(false);
            root.anchoredPosition = home;
            done();
            return;
        }
        float t = Elapsed;
        group.alpha = fades ? Mathf.Clamp01(t / FadeInSeconds) : 1f;
        foreach (var part in parts) Apply(part, t);
        if (t > longest + .05f) enabled = false;
    }

    private static void Apply(Part part, float t)
    {
        if (part.Rect == null) return;
        float k = part.Length > 0f ? Mathf.Clamp01((t - part.At) / part.Length) : 1f;
        switch (part.Kind)
        {
            case Kind.Line:
                float drawn = EaseOut(k);
                part.Rect.localScale = new Vector3(part.To.x > 0f ? 1f : drawn, part.To.x > 0f ? drawn : 1f, 1f);
                break;
            case Kind.Slide:
                part.Rect.anchoredPosition = Vector2.LerpUnclamped(part.From, part.To, EaseOut(k));
                break;
            case Kind.Sweep:
                part.Rect.anchoredPosition = Vector2.Lerp(part.From, part.To, k);
                var line = part.Rect.GetComponent<Image>();
                if (line != null) { var c = line.color; c.a = k <= 0f || k >= 1f ? 0f : Mathf.Sin(k * Mathf.PI) * .9f; line.color = c; }
                break;
            case Kind.Card:
                float since = t - part.At;
                float grow = Mathf.Clamp01(since / GrowSeconds);
                part.Rect.localScale = new Vector3(since <= 0f ? 0f : EaseOut(grow), 1f, 1f);
                float drain = Mathf.Clamp01((since - GrowSeconds - FlashHold) / DrainSeconds);
                var flash = part.Settled;
                flash.a = part.Settled.a * (1f - drain);
                part.Flash.color = flash;
                break;
        }
    }

    public static float EaseOut(float k) => 1f - (1f - k) * (1f - k) * (1f - k);
}
