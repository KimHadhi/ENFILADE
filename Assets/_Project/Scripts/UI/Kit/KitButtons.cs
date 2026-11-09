using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The menus' buttons: cut-corner plates with an icon, a thin bar, then the label. Primary is the pink one with
/// a dark label and ▶▶▶ marching at its right; Secondary is a dark one with a white icon and a pink bar; Go is
/// the near-white call to start, with stripes running across it. A small index number ("01") can sit in the top-right
/// corner like a rangefinder's mark. Each has KitHover's corner brackets and lean, and a press that darkens it. Icons
/// are white LAYERLAB Game Picto Pack pictograms copied to Resources/UI/Icons, tinted here.
/// </summary>
public static class KitButtons
{
    public enum Look { Primary, Secondary, Go }

    private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => icons.Clear();

    /// <summary>An icon from Resources/UI/Icons by name (Play, Room, Servers, Settings, Profile, Character, Map, Quit, Left, Right).</summary>
    public static Texture2D Icon(string name)
    {
        if (icons.TryGetValue(name, out var icon) && icon != null) return icon;
        return icons[name] = Resources.Load<Texture2D>("UI/Icons/" + name);
    }

    public static RawImage IconImage(Transform parent, string name, float size, Color tint)
    {
        var rect = FeelUI.Rect("Icon " + name, parent);
        rect.sizeDelta = new Vector2(size, size);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = Icon(name);
        image.color = tint;
        image.raycastTarget = false;
        image.enabled = image.texture != null;
        return image;
    }

    /// <summary>The fill colour of a look.</summary>
    public static Color FillOf(Look look) => look == Look.Primary ? KitStyle.Accent : look == Look.Go ? KitStyle.Ice : KitStyle.Card;

    /// <summary>
    /// A button at <paramref name="at"/> (from its parent's top left) of <paramref name="size"/>: the icon in a square at
    /// the left, a bar, the label. Returns its rect; <paramref name="label"/> gives the label for later changes.
    /// </summary>
    public static RectTransform Make(Transform parent, Vector2 at, Vector2 size, Look look, string icon, string text, float textSize,
        Action clicked, out TextMeshProUGUI label, string index = null)
    {
        var rect = KitStyle.TopLeft("Button " + text, parent, at, size);
        Color ink = look == Look.Secondary ? KitStyle.Text : KitStyle.Ink;
        var back = KitStyle.Cut(rect, FillOf(look));
        back.raycastTarget = true;
        if (look == Look.Go) Stripes(rect);

        float square = Mathf.Min(size.y, 92f);
        float x;
        if (!string.IsNullOrEmpty(icon))
        {
            var holder = KitStyle.TopLeft("Icon", rect, Vector2.zero, new Vector2(square, size.y));
            IconImage(holder, icon, square * .44f, look == Look.Secondary ? KitStyle.Ice : ink);
            x = square;
            // The thin bar between icon and label: pink on dark, ink on the coloured ones.
            float barHeight = size.y * .5f;
            KitStyle.Bar("Divider", rect, new Vector2(x, (size.y - barHeight) * .5f), new Vector2(KitStyle.AccentWidth, barHeight),
                look == Look.Secondary ? KitStyle.Accent : ink);
            x += KitStyle.AccentWidth + 18f;
        }
        else x = 28f;
        float chevrons = look == Look.Secondary ? 0f : size.y * .9f;
        var box = KitStyle.TopLeft("Label", rect, new Vector2(x, 0f), new Vector2(size.x - x - chevrons - 12f, size.y));
        label = KitStyle.Flat(box, text, textSize, TextAlignmentOptions.MidlineLeft, ink);
        if (chevrons > 0f)
        {
            var march = KitStyle.TopLeft("Chevrons", rect, new Vector2(size.x - chevrons - 10f, 0f), new Vector2(chevrons, size.y));
            march.gameObject.AddComponent<KitChevrons>().Build(ink, size.y * .22f);
        }
        if (!string.IsNullOrEmpty(index))
        {
            var mark = KitStyle.TopLeft("Index", rect, new Vector2(size.x - 64f, 5f), new Vector2(46f, 16f));
            KitStyle.Flat(mark, index, 12f, TextAlignmentOptions.TopRight, KitStyle.With(ink, .45f)).characterSpacing = 4f;
        }

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        var colours = button.colors;
        colours.normalColor = colours.highlightedColor = colours.selectedColor = Color.white;
        colours.pressedColor = new Color(.78f, .78f, .8f);
        colours.fadeDuration = .05f;
        button.colors = colours;
        if (clicked != null) button.onClick.AddListener(() => clicked());
        rect.gameObject.AddComponent<KitHover>();
        return rect;
    }

    /// <summary>A see-through pink lid over <paramref name="rect"/> for KitReveal.Card to drain, cut like the rect is.</summary>
    public static Image Lid(RectTransform rect)
    {
        var lid = KitStyle.Fill(KitStyle.Stretch("Flash", rect), KitStyle.Flash);
        if (rect.TryGetComponent(out Image under) && under.sprite == KitShapes.Chamfer) KitStyle.Shape(lid);
        return lid;
    }

    // Faint diagonal stripes running across a coloured button, cut to its shape.
    private static void Stripes(RectTransform rect)
    {
        var holder = KitStyle.Stretch("Stripes", rect);
        KitStyle.Cut(holder, Color.white);
        holder.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        float width = rect.sizeDelta.x, height = rect.sizeDelta.y;
        var run = FeelUI.Rect("Run", holder);
        run.anchorMin = run.anchorMax = new Vector2(0f, .5f);
        for (float x = -height - KitStripes.Spacing; x < width + height; x += KitStripes.Spacing)
        {
            var stripe = FeelUI.Rect("Stripe", run);
            stripe.anchorMin = stripe.anchorMax = new Vector2(.5f, .5f);
            stripe.anchoredPosition = new Vector2(x, 0f);
            stripe.sizeDelta = new Vector2(12f, height * 2.2f);
            stripe.localRotation = Quaternion.Euler(0f, 0f, -32f);
            KitStyle.Fill(stripe, new Color(1f, 1f, 1f, .16f));
        }
        run.gameObject.AddComponent<KitStripes>();
    }
}
