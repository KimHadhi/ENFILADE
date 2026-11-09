using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The menus' look. Started from the user's references (2026-11-09: a Roblox shooter's main menu for the layout, a
/// "BASE UPGRADE" panel video for the motion) and then given ENFILADE's own identity: "enfilade" is fire along the
/// length of a line, so the menus are drawn like a rangefinder (rails, scale ticks, bearings, cut corners). Colours are
/// the HUD's (draft A, the user's pick on 2026-11-09): near-black, white, and one pink, the same pink as the HUD's accent
/// (HudTilt.bridgeAccent) and the same near-white as its band. The member names stay as they were (Accent, Ice …) so
/// other menus built on the kit (shop, gun customize) follow without changes. The motion is KitReveal, KitHover,
/// KitScramble, KitPulse, KitRail. Built in code like the rest of the HUD (FeelUI), in the score font.
/// </summary>
public static class KitStyle
{
    /// <summary>The one pink: main buttons, frame lines, what is chosen (the HUD's accent).</summary>
    public static readonly Color Accent = new Color(1f, .31f, .47f);
    /// <summary>The second colour, a near-white with a breath of pink (the HUD's band): captions, data, the start button.</summary>
    public static readonly Color Ice = new Color(1f, .96f, .97f);
    /// <summary>The near-black behind everything (the menu camera's clear colour).</summary>
    public static readonly Color Night = new Color(.055f, .055f, .065f);
    /// <summary>The see-through pink a card is filled with as it opens, before it settles to Card.</summary>
    public static readonly Color Flash = new Color(1f, .31f, .47f, .5f);
    public static readonly Color Panel = new Color(.07f, .07f, .08f, .94f);
    public static readonly Color Card = new Color(.14f, .14f, .16f, .9f);
    public static readonly Color Line = new Color(1f, .31f, .47f, .85f);
    public static readonly Color Text = new Color(.97f, .97f, .97f);
    public static readonly Color Soft = new Color(1f, 1f, 1f, .55f);
    public static readonly Color Faint = new Color(1f, 1f, 1f, .2f);
    public static readonly Color Ink = new Color(.055f, .055f, .065f);
    public static readonly Color Red = new Color(1f, .3f, .28f);
    public static readonly Color Cyan = new Color(.38f, .88f, 1f);
    public static readonly Color Green = new Color(.3f, .86f, .52f);
    /// <summary>Thin frame lines and accent bars (px).</summary>
    public const float LineWidth = 2f, AccentWidth = 4f;

    /// <summary>The colour at another alpha.</summary>
    public static Color With(Color color, float alpha) { color.a = alpha; return color; }

    /// <summary>A rect hanging from its parent's top left: <paramref name="at"/> is how far right and down (px).</summary>
    public static RectTransform TopLeft(string name, Transform parent, Vector2 at, Vector2 size)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(at.x, -at.y);
        rect.sizeDelta = size;
        return rect;
    }

    /// <summary>A rect filling its parent.</summary>
    public static RectTransform Stretch(string name, Transform parent)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    public static Image Fill(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>A fill with two opposite corners cut off (top right, bottom left): the menus' card and button shape.</summary>
    public static Image Cut(RectTransform rect, Color color)
    {
        var image = Fill(rect, color);
        Shape(image);
        return image;
    }

    /// <summary>Gives an image the cut-corner shape.</summary>
    public static void Shape(Image image)
    {
        image.sprite = KitShapes.Chamfer;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
    }

    /// <summary>A label filling <paramref name="rect"/>, white and bold in the score font, with its soft shadow.</summary>
    public static TextMeshProUGUI Label(RectTransform rect, string text, float size, TextAlignmentOptions alignment, Color? color = null)
    {
        var label = FeelUI.ScoreLabel("Text", rect, size, alignment);
        var box = label.rectTransform;
        box.anchorMin = Vector2.zero;
        box.anchorMax = Vector2.one;
        box.offsetMin = box.offsetMax = Vector2.zero;
        label.text = text;
        label.color = color ?? Text;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>Like Label but flat (no shadow): for big type, coloured buttons and the fine print, where a shadow muddies it.</summary>
    public static TextMeshProUGUI Flat(RectTransform rect, string text, float size, TextAlignmentOptions alignment, Color? color = null)
    {
        var label = Label(rect, text, size, alignment, color);
        if (label.font != null && label.font.material != null) label.fontSharedMaterial = label.font.material;
        return label;
    }

    /// <summary>A thin line along <paramref name="rect"/>, which is the line's own box.</summary>
    public static Image Bar(string name, Transform parent, Vector2 at, Vector2 size, Color color)
        => Fill(TopLeft(name, parent, at, size), color);

    /// <summary>A rangefinder scale: <paramref name="count"/> short ticks <paramref name="step"/> apart, every <paramref name="major"/>th one longer.</summary>
    public static RectTransform Ticks(string name, Transform parent, Vector2 at, int count, float step, int major, float height, Color color)
    {
        var scale = TopLeft(name, parent, at, new Vector2(step * (count - 1) + 1f, height));
        for (int i = 0; i < count; i++)
        {
            bool big = major > 0 && i % major == 0;
            float tall = big ? height : height * .45f;
            Bar("Tick", scale, new Vector2(i * step, 0f), new Vector2(1f, tall), big ? color : With(color, color.a * .6f));
        }
        return scale;
    }
}
