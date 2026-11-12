using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// How a settings value box (SettingsWidgets.NumberBox, the crosshair colour box) shows that it is being typed in (user,
/// 2026-11-12: "숫자 입력 중인지 잘 모르겠음 뭐 티가 안 나서"). While it has the keyboard the box turns near-black inside
/// a pink frame over a thicker pink underline, its text goes white and bold over a pink selection, a pink caret blinks
/// after it, and the row's name turns pink, the way a chosen settings tab is dark with a pink edge. It all switches at
/// once, on and off, leaving nothing to fade. Pointed at, the box brightens a little so it reads as a thing to click.
/// </summary>
[DisallowMultipleComponent]
public sealed class TypingLook : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float Frame = 2f, Line = 2f, LineTyping = 3f;
    private static readonly Color Typing = KitStyle.With(KitStyle.Night, .96f), Hover = new Color(1f, 1f, 1f, .11f),
        LineHover = new Color(1f, 1f, 1f, .4f);

    private TMP_InputField field;
    private Image fill, line;
    private GameObject frame;
    private TMP_Text text, rowName;
    private Color rowNameColor;
    private bool over, painted, shownTyping, shownOver;

    /// <summary>
    /// Dresses <paramref name="input"/> (made by SettingsWidgets.TextInput: a fill, a "Line" under it, the text in a
    /// viewport). <paramref name="name"/> is the row's name label, pink while typing; null leaves it alone.
    /// </summary>
    public static TypingLook On(TMP_InputField input, TMP_Text name)
    {
        if (input == null) return null;
        var look = input.TryGetComponent(out TypingLook found) ? found : input.gameObject.AddComponent<TypingLook>();
        look.Init(input, name);
        return look;
    }

    private void Init(TMP_InputField input, TMP_Text name)
    {
        field = input;
        fill = input.targetGraphic as Image;
        var lineRect = input.transform.Find("Line");
        line = lineRect != null ? lineRect.GetComponent<Image>() : null;
        text = input.textComponent;
        rowName = name;
        if (rowName != null) rowNameColor = rowName.color;
        input.customCaretColor = true;
        input.caretColor = KitStyle.Accent;
        input.caretWidth = 3;
        input.selectionColor = KitStyle.With(KitStyle.Accent, .5f);
        if (frame == null) frame = BuildFrame((RectTransform)input.transform);
        painted = false;
        Paint(false, false);
    }

    // Four pink lines just inside the box's edge, over everything in it; off unless typing.
    private static GameObject BuildFrame(RectTransform box)
    {
        var root = SettingsWidgets.Rect("Typing Frame", box);
        SettingsWidgets.Stretch(root);
        Edge(root, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, Frame));
        Edge(root, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, Frame));
        Edge(root, Vector2.zero, new Vector2(0f, 1f), new Vector2(Frame, 0f));
        Edge(root, new Vector2(1f, 0f), Vector2.one, new Vector2(Frame, 0f));
        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    private static void Edge(RectTransform parent, Vector2 min, Vector2 max, Vector2 size)
    {
        var edge = SettingsWidgets.Rect("Edge", parent);
        edge.anchorMin = min; edge.anchorMax = max;
        edge.pivot = new Vector2(min.x == max.x ? min.x : .5f, min.y == max.y ? min.y : .5f);
        edge.sizeDelta = size;
        edge.anchoredPosition = Vector2.zero;
        SettingsWidgets.Fill(edge, KitStyle.Accent).raycastTarget = false;
    }

    public void OnPointerEnter(PointerEventData eventData) => over = true;
    public void OnPointerExit(PointerEventData eventData) => over = false;

    // Polled rather than told: a box loses the keyboard by Enter, Esc, a click elsewhere or its window closing, and
    // isFocused is the one thing true in all of them.
    private void LateUpdate()
    {
        if (field == null) return;
        bool typing = field.isFocused;
        if (painted && typing == shownTyping && over == shownOver) return;
        Paint(typing, over);
    }

    private void OnDisable()
    {
        over = false;
        if (field != null) Paint(false, false);
    }

    private void Paint(bool typing, bool hover)
    {
        painted = true;
        shownTyping = typing;
        shownOver = hover;
        if (fill != null) fill.color = typing ? Typing : hover ? Hover : SettingsWidgets.Surface;
        if (frame != null) frame.SetActive(typing);
        if (line != null)
        {
            line.color = typing ? KitStyle.Accent : hover ? LineHover : SettingsWidgets.Track;
            line.rectTransform.sizeDelta = new Vector2(0f, typing ? LineTyping : Line);
        }
        if (text != null)
        {
            text.color = typing || hover ? SettingsWidgets.Text : SettingsWidgets.Muted;
            text.fontStyle = typing ? FontStyles.Bold : FontStyles.Normal;
        }
        if (rowName != null) rowName.color = typing ? KitStyle.Accent : rowNameColor;
    }
}
