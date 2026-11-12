using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// The health bar's round 4 draft A (the user's pick on 2026-11-11), laid over AdaptiveHealthHUD's bar and loss trail:
/// <list type="bullet">
/// <item>what a hit takes breaks off its cells as pieces that flash white, kick up and fall spinning; the cells left
/// flash white for a moment and the number rolls down to the new value;</item>
/// <item>a shield hit chips off what it took the same way, in the shield's colour (a break is ShieldShatter's);</item>
/// <item>at low health the fill runs with dark stripes, the number turns pink and beats, and a striped "위험" tag sits
/// past the bar's end;</item>
/// <item>healing lights the head of the fill and floats "+N" past the bar's end (the shield's in its colour).</item>
/// </list>
/// Play only, real time; everything is made once with the HUD so a hit creates nothing. Numbers: Tables/HealthBar.csv.
/// </summary>
public sealed class HealthBarFlair
{
    public const string TableName = "HealthBar", Row = "Bar";
    private const int PieceCount = 14;
    private const float WhiteSeconds = .05f, TagGap = 14f, TagGlyphWidth = 24f, TagGlyphHeight = 18f;

    [Serializable]
    public sealed class Settings
    {
        public float lowFraction = .25f;
        public float pieceSeconds = .55f;
        public float pieceGravity = 900f;
        public float pieceLift = 60f;
        public float pieceDrift = 40f;
        public float pieceSpin = 160f;
        public float hitFlashSeconds = .06f;
        public float rollSeconds = .22f;
        public float floatSeconds = 1f;
        public float floatRise = 26f;
        public float glintSeconds = .5f;
        public float stripePeriod = 9f;
        public float stripeScroll = 26f;
        public float beatsPerSecond = 1.1f;
    }

    private sealed class Piece
    {
        public SlantedPiece Image;
        public Vector2 Position, Velocity;
        public float Angle, Spin, Age = float.MaxValue, Life = 1f;
        public Color Tint;
    }

    private sealed class Floater
    {
        public TMP_Text Label;
        public float Age = float.MaxValue, Amount, Lift;
    }

    public readonly Settings Config = new Settings();
    private readonly RectTransform root;
    private readonly Piece[] pieces = new Piece[PieceCount];
    private readonly Floater healthFloat, shieldFloat;
    private readonly RectTransform dangerTag;
    private readonly SlantedHealthGraphic dangerGlyph;
    private readonly TMP_Text dangerLabel;
    private readonly Graphic number;
    private readonly Color numberColor;
    private readonly List<Vector2> runs = new List<Vector2>();
    private static readonly Color DangerInk = new Color(.055f, .055f, .065f, .5f);
    private int nextPiece;
    private float clock, flashLeft, healthGlint, shieldGlint, rollAge = float.MaxValue, rollFrom, rollTo;
    private bool danger;
    private Vector2 barEnd;

    /// <param name="root">The bar's own root (AdaptiveHealthHUD's visuals); the pieces, floats and tag live under it.</param>
    /// <param name="number">The health number, which turns pink at low health; its font is used for the floats.</param>
    public HealthBarFlair(RectTransform root, Graphic number)
    {
        this.root = root;
        this.number = number;
        numberColor = number != null ? number.color : Color.white;
        for (int i = 0; i < PieceCount; i++)
        {
            var rect = Rect("Health Piece " + i, root, Vector2.one * .5f);
            var image = rect.gameObject.AddComponent<SlantedPiece>();
            image.raycastTarget = false;
            image.enabled = false;
            pieces[i] = new Piece { Image = image };
        }
        healthFloat = new Floater { Label = Label("Health Float", 24f) };
        shieldFloat = new Floater { Label = Label("Health Float (Shield)", 20f), Lift = 24f };

        dangerTag = Rect("Danger Tag", root, new Vector2(0f, .5f));
        dangerTag.sizeDelta = new Vector2(TagGlyphWidth, TagGlyphHeight);
        // The tag's own parts hang from its left middle.
        var glyphRect = Rect("Glyph", dangerTag, new Vector2(0f, .5f));
        glyphRect.anchorMin = glyphRect.anchorMax = new Vector2(0f, .5f);
        glyphRect.sizeDelta = new Vector2(TagGlyphWidth, TagGlyphHeight);
        dangerGlyph = glyphRect.gameObject.AddComponent<SlantedHealthGraphic>();
        dangerGlyph.Configure(1f, 1f, 1d, false, 5f, 0f, KitStyle.Accent, Color.clear, Color.clear);
        dangerLabel = Label("Label", 17f, dangerTag);
        dangerLabel.rectTransform.anchorMin = dangerLabel.rectTransform.anchorMax = new Vector2(0f, .5f);
        dangerLabel.rectTransform.anchoredPosition = new Vector2(TagGlyphWidth + 8f, 0f);
        dangerLabel.text = Texts.Get("hud.danger");
        dangerLabel.color = KitStyle.Accent;
        dangerLabel.enabled = true;
        dangerTag.gameObject.SetActive(false);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 pivot)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = pivot;
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return rect;
    }

    // A label in the health number's own font when it is TextMesh Pro, else the HUD's score font.
    private TMP_Text Label(string name, float size, Transform parent = null)
    {
        var text = FeelUI.ScoreLabel(name, parent != null ? parent : root, size, TextAlignmentOptions.MidlineLeft);
        if (number is TMP_Text source && source.font != null) { text.font = source.font; text.fontSharedMaterial = source.fontSharedMaterial; }
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0f, .5f);
        rect.sizeDelta = new Vector2(140f, size * 1.4f);
        rect.gameObject.layer = (parent != null ? parent : root).gameObject.layer;
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        text.raycastTarget = false;
        text.enabled = false;
        return text;
    }

    /// <summary>Where the bar ends (its right end, half way up), in the root's units: the floats and tag sit past it.</summary>
    public void Place(float width, float height)
    {
        barEnd = new Vector2(width + TagGap, height * .5f);
        dangerTag.anchoredPosition = barEnd;
    }

    /// <summary>Health was lost: between low and high on the health graphic.</summary>
    public void Hit(SlantedHealthGraphic graphic, float low, float high, Color tint)
    {
        Knock(graphic, low, high, tint, 1f);
        flashLeft = Mathf.Max(0f, Config.hitFlashSeconds);
        rollFrom = Rolling ? Shown : high;
        rollTo = low;
        rollAge = 0f;
    }

    /// <summary>Shield was lost without breaking: chips from between low and high on the shield graphic.</summary>
    public void Chip(SlantedHealthGraphic graphic, float low, float high, Color tint) => Knock(graphic, low, high, tint, .6f);

    /// <summary>Something was given back: health, or shield.</summary>
    public void Healed(float amount, bool shield)
    {
        var floater = shield ? shieldFloat : healthFloat;
        // Gains close together (a medkit's ticks, two pickups) add up in one float.
        floater.Amount = floater.Age < .3f ? floater.Amount + amount : amount;
        floater.Age = 0f;
        floater.Label.text = "+" + Mathf.RoundToInt(floater.Amount);
        floater.Label.color = shield ? new Color(.65f, .95f, 1f) : KitStyle.Accent;
        floater.Label.enabled = true;
        if (shield) shieldGlint = Config.glintSeconds; else healthGlint = Config.glintSeconds;
    }

    private void Knock(SlantedHealthGraphic graphic, float low, float high, Color tint, float strength)
    {
        if (graphic == null || !graphic.isActiveAndEnabled) return;
        graphic.Span(low, high, runs, out float shear);
        var rect = graphic.rectTransform;
        // The graphic sits under the root with its pivot at its lower left (AdaptiveHealthHUD.MakeRect).
        Vector2 origin = rect.anchoredPosition;
        float height = rect.rect.height;
        foreach (var run in runs)
        {
            var piece = pieces[nextPiece];
            nextPiece = (nextPiece + 1) % PieceCount;
            float width = run.y - run.x;
            piece.Position = origin + new Vector2((run.x + run.y) * .5f + shear * .5f, height * .5f);
            piece.Velocity = new Vector2(Config.pieceDrift * Random.Range(.6f, 1.2f), Config.pieceLift * strength * Random.Range(.8f, 1.2f));
            piece.Spin = Config.pieceSpin * Random.Range(.8f, 1.4f) * (Random.value < .5f ? -1f : 1f);
            piece.Angle = 0f;
            piece.Age = 0f;
            piece.Life = Mathf.Max(.05f, Config.pieceSeconds);
            piece.Tint = tint;
            piece.Image.rectTransform.sizeDelta = new Vector2(width, height);
            piece.Image.Shear = shear * Mathf.Clamp01(width / Mathf.Max(1f, height));
            piece.Image.enabled = true;
        }
    }

    public void Tick(float dt, float health, float maximum)
    {
        clock += dt;
        foreach (var piece in pieces)
        {
            if (piece.Age >= piece.Life) continue;
            piece.Age += dt;
            if (piece.Age >= piece.Life) { piece.Image.enabled = false; continue; }
            piece.Velocity.y -= Config.pieceGravity * dt;
            piece.Position += piece.Velocity * dt;
            piece.Angle += piece.Spin * dt;
            var rect = piece.Image.rectTransform;
            rect.anchoredPosition = piece.Position;
            rect.localRotation = Quaternion.Euler(0f, 0f, piece.Angle);
            float k = piece.Age / piece.Life;
            Color tint = Color.Lerp(Color.white, piece.Tint, Mathf.Clamp01(piece.Age / WhiteSeconds));
            tint.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1f, k));
            piece.Image.color = tint;
        }
        flashLeft = Mathf.Max(0f, flashLeft - dt);
        healthGlint = Mathf.Max(0f, healthGlint - dt);
        shieldGlint = Mathf.Max(0f, shieldGlint - dt);
        if (rollAge < Config.rollSeconds) rollAge += dt;
        Float(healthFloat, dt);
        Float(shieldFloat, dt);

        bool low = maximum > 0f && health > 0f && health <= maximum * Config.lowFraction;
        if (low != danger)
        {
            danger = low;
            dangerTag.gameObject.SetActive(low);
            if (!low && number != null) number.color = numberColor;
        }
        if (!danger) return;
        float beat = UltimateMagazine.Heartbeat(clock, Config.beatsPerSecond);
        if (number != null) number.color = Color.Lerp(KitStyle.Accent, Color.white, beat * .6f);
        float pulse = .65f + .35f * Mathf.Sin(clock * 9f);
        dangerGlyph.color = new Color(1f, 1f, 1f, pulse);
        dangerGlyph.SetDanger(new Color(.055f, .055f, .065f, .85f), 6f, clock * 20f);
        Color label = KitStyle.Accent; label.a = pulse;
        dangerLabel.color = label;
    }

    private void Float(Floater floater, float dt)
    {
        if (floater.Age >= Config.floatSeconds) return;
        floater.Age += dt;
        if (floater.Age >= Config.floatSeconds) { floater.Label.enabled = false; return; }
        float k = floater.Age / Mathf.Max(.01f, Config.floatSeconds);
        floater.Label.rectTransform.anchoredPosition = barEnd + new Vector2(0f, floater.Lift + Config.floatRise * k + (danger ? 22f : 0f));
        Color tint = floater.Label.color; tint.a = 1f - k * k;
        floater.Label.color = tint;
    }

    /// <summary>Anything of the flair's drawn on the bar itself this frame (flash, glint, stripes, the rolling number).</summary>
    public bool TouchesBar => flashLeft > 0f || healthGlint > 0f || shieldGlint > 0f || danger || Rolling;
    public bool Rolling => rollAge < Config.rollSeconds;
    public float Shown => Mathf.Lerp(rollFrom, rollTo, 1f - Mathf.Pow(1f - Mathf.Clamp01(rollAge / Mathf.Max(.01f, Config.rollSeconds)), 3f));
    public Color Fill(Color baseColor) => flashLeft > 0f ? Color.white : baseColor;
    public Color DangerStripes => danger ? DangerInk : Color.clear;
    public float StripePeriod => Mathf.Max(2f, Config.stripePeriod);
    public float StripeOffset => clock * Config.stripeScroll;
    public Color HealthGlint => Glint(healthGlint);
    public Color ShieldGlint => Glint(shieldGlint);
    private Color Glint(float left) => left > 0f ? new Color(1f, 1f, 1f, Mathf.Clamp01(left / Mathf.Max(.01f, Config.glintSeconds))) : Color.clear;

    /// <summary>Puts the number's own colour back (the HUD is going away or being rebuilt).</summary>
    public void Restore()
    {
        if (number != null) number.color = numberColor;
    }
}
