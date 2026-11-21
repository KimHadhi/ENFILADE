using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// HUD Mk II's words: Hitmarker Normal (the MkHud text role, Fonts.csv; Hangul falls back to Pretendard) with the addon's
/// faint dark outline (draw.SimpleTextOutlined, width 2, black at 20/255; MkHud.csv Text.outline*), placed the way the
/// addon places text: at a point, aligned left, centre or right and top, middle or bottom of the GMod font's cell. A label
/// in the small label size that holds Hangul draws at Text.hangul px instead (7.68 px Hangul cannot be read; the draft
/// the user picked did the same). Also the addon's helpers: letters spaced apart (InsertSpaceBetweenChars) and the
/// decoding scramble of the pickup rows (random symbols until each letter's turn).
/// </summary>
public static class MkText
{
    private static readonly StringBuilder built = new StringBuilder(64);

    /// <summary>
    /// A label in <paramref name="frame"/>. <paramref name="across"/>: 0 left, .5 centre, 1 right of the point;
    /// <paramref name="down"/>: 0 top, .5 middle, 1 bottom of the cell.
    /// </summary>
    public static TextMeshProUGUI Make(string name, RectTransform frame, MkLook look, float across, float down)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(frame, false);
        rect.gameObject.layer = frame.gameObject.layer;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(across, 1f - down);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        FontRoles.Apply(text, FontRoles.MkHud);
        var outlined = CombatUIAssets.Outlined(text.font, new Color(0f, 0f, 0f, look.Text.outlineAlpha), look.Text.outline);
        if (outlined != null) text.fontSharedMaterial = outlined;
        text.alignment = Alignment(across, down);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = false;
        text.raycastTarget = false;
        text.text = "";
        return text;
    }

    private static TextAlignmentOptions Alignment(float across, float down)
    {
        if (down < .25f) return across < .25f ? TextAlignmentOptions.TopLeft : across > .75f ? TextAlignmentOptions.TopRight : TextAlignmentOptions.Top;
        if (down > .75f) return across < .25f ? TextAlignmentOptions.BottomLeft : across > .75f ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.Bottom;
        return across < .25f ? TextAlignmentOptions.Left : across > .75f ? TextAlignmentOptions.Right : TextAlignmentOptions.Center;
    }

    /// <summary>The label's size: a GMod font size (a cell height) as its em, its box a cell high.</summary>
    public static void Size(TMP_Text text, MkLook look, float gmodSize)
    {
        float em = look.Em(gmodSize);
        if (!Mathf.Approximately(text.fontSize, em)) text.fontSize = em;
        var rect = text.rectTransform;
        var box = new Vector2(1400f, Mathf.Max(1f, gmodSize));
        if (rect.sizeDelta != box) rect.sizeDelta = box;
    }

    /// <summary>The label at a point of its frame (px from the top left, y down).</summary>
    public static void At(TMP_Text text, float x, float y)
    {
        var at = new Vector2(x, -y);
        if (text.rectTransform.anchoredPosition != at) text.rectTransform.anchoredPosition = at;
    }

    /// <summary>Words that change only when they differ, so a label rebuilds no more than it must.</summary>
    public static void Say(TMP_Text text, string words)
    {
        words ??= "";
        if (text.text != words) text.text = words;
    }

    /// <summary>
    /// A label in the addon's small label font (Text.label): its words and size, Hangul at Text.hangul px so it can be read.
    /// </summary>
    public static void Label(TMP_Text text, MkLook look, string words)
    {
        Say(text, words);
        if (HasHangul(words))
        {
            float px = look.Text.hangul;
            if (!Mathf.Approximately(text.fontSize, px)) text.fontSize = px;
            var box = new Vector2(1400f, px * look.Text.em);
            if (text.rectTransform.sizeDelta != box) text.rectTransform.sizeDelta = box;
        }
        else Size(text, look, look.Text.label);
    }

    public static bool HasHangul(string words)
    {
        if (string.IsNullOrEmpty(words)) return false;
        foreach (char c in words)
            if ((c >= '가' && c <= '힣') || (c >= '㄰' && c <= '㆏') || (c >= 'ᄀ' && c <= 'ᇿ')) return true;
        return false;
    }

    /// <summary>The addon's InsertSpaceBetweenChars: "MX16" → "M X 1 6".</summary>
    public static string Spaced(string words)
    {
        if (string.IsNullOrEmpty(words) || words.Length <= 1) return words ?? "";
        built.Clear();
        for (int i = 0; i < words.Length; i++)
        {
            if (i > 0) built.Append(' ');
            built.Append(words[i]);
        }
        return built.ToString();
    }

    /// <summary>
    /// The decoding scramble: letter i shows once <paramref name="progress"/> reaches (i + 1) / length, a random one of
    /// <paramref name="chars"/> until then; spaces stay spaces. Drawn anew every frame, as the addon does.
    /// </summary>
    public static string Scramble(string words, float progress, string chars)
    {
        if (string.IsNullOrEmpty(words) || progress >= 1f || string.IsNullOrEmpty(chars)) return words ?? "";
        built.Clear();
        int length = words.Length;
        for (int i = 0; i < length; i++)
        {
            char c = words[i];
            built.Append(c == ' ' || progress >= (i + 1f) / length ? c : chars[Random.Range(0, chars.Length)]);
        }
        return built.ToString();
    }

    /// <summary>A colour with its alpha times <paramref name="alpha"/>.</summary>
    public static Color Fade(Color colour, float alpha)
    {
        colour.a *= alpha;
        return colour;
    }

    /// <summary>The addon's LerpColor: a step of the way there each frame (rate per second).</summary>
    public static Color Follow(Color from, Color to, float rate, float dt) => Color.Lerp(from, to, Mathf.Clamp01(rate * dt));

    /// <summary>The addon's Lerp: a step of the way there each frame (rate per second).</summary>
    public static float Follow(float from, float to, float rate, float dt) => Mathf.Lerp(from, to, Mathf.Clamp01(rate * dt));
}
