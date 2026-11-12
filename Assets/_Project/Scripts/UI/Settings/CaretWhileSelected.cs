using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a typed box's blinking caret in sight while text in it is selected (user, 2026-11-12: "텍스트 치려하면 깜빡이는 |
/// 바가 없어서 아직도 내가 칠 수 있는 상태인 건가? 헷갈리는 거 같아"). TMP draws its caret only when nothing is selected,
/// so a box that selects all of its text when clicked (every settings number box, so typing replaces the number) showed no
/// caret from the click until the first key, the moment the player most wants to see one. This draws the same bar, in the
/// field's caret colour, width and blink rate, at the end of a whole-text selection (where Windows boxes keep it after
/// Ctrl+A) or at the moving end of any other, and steps aside as soon as TMP's own caret is back. Every box
/// SettingsWidgets.TextInput builds has one.
/// </summary>
[DisallowMultipleComponent]
public sealed class CaretWhileSelected : MonoBehaviour
{
    private TMP_InputField field;
    private TMP_Text text;
    private RectTransform bar;
    private Image paint;
    private int shownAnchor = -1, shownFocus = -1;
    private float since;

    public static CaretWhileSelected On(TMP_InputField input)
    {
        if (input == null || input.textComponent == null) return null;
        var caret = input.TryGetComponent(out CaretWhileSelected found) ? found : input.gameObject.AddComponent<CaretWhileSelected>();
        caret.field = input;
        caret.text = input.textComponent;
        if (caret.bar == null)
        {
            // Under the text: TMP's glyph positions are in its space, it slides the text to keep the selection's end in
            // the box, and the box's mask cuts what is outside, all of which the bar then shares.
            caret.bar = SettingsWidgets.Rect("Selection Caret", caret.text.transform);
            caret.bar.anchorMin = caret.bar.anchorMax = caret.text.rectTransform.pivot;
            caret.bar.pivot = Vector2.zero;
            caret.paint = caret.bar.gameObject.AddComponent<Image>();
            caret.paint.raycastTarget = false;
            caret.paint.enabled = false;
        }
        return caret;
    }

    private void LateUpdate()
    {
        if (field == null || text == null || !field.isFocused || field.readOnly) { Hide(); return; }
        int anchor = field.selectionStringAnchorPosition, focus = field.selectionStringFocusPosition;
        if (anchor == focus) { Hide(); return; } // nothing selected: TMP's own caret is showing
        if (anchor != shownAnchor || focus != shownFocus)
        {
            // A new or changed selection starts lit, the way TMP restarts its blink on every key.
            shownAnchor = anchor;
            shownFocus = focus;
            since = Time.unscaledTime;
        }
        Show(Lit() && Place(anchor, focus));
    }

    private void OnDisable() => Hide();

    private void Hide()
    {
        shownAnchor = shownFocus = -1;
        Show(false);
    }

    private void Show(bool on)
    {
        if (paint != null && paint.enabled != on) paint.enabled = on;
    }

    // TMP's blink: lit for the first half of each 1/rate seconds, timed from when the selection last changed.
    private bool Lit()
    {
        float rate = field.caretBlinkRate;
        if (rate <= 0f) return true;
        float period = 1f / rate;
        return (Time.unscaledTime - since) % period < period * .5f;
    }

    // Stands the bar where TMP_InputField.GenerateCaret would stand its caret for that end of the selection: before the
    // first letter of a line, else just after the letter before, as tall as that letter's line and as wide as TMP's.
    private bool Place(int anchor, int focus)
    {
        // The glyphs are those of the last layout. When the text changed this frame (a number box swaps its readout,
        // "80%", for the bare number as it takes focus) they would put the bar by letters already gone: lay it out now.
        if (text.havePropertiesChanged) text.ForceMeshUpdate();
        var info = text.textInfo;
        if (info == null || info.characterCount <= 0 || info.lineInfo == null) return false;
        int length = field.text.Length;
        bool whole = Mathf.Min(anchor, focus) == 0 && Mathf.Max(anchor, focus) >= length;
        // TMP's select-all leaves the moving end at the start; the end of the text is where the player's eye is.
        int at = CharacterAt(info, whole ? length : focus);
        TMP_CharacterInfo letter;
        float x;
        int line = info.characterInfo[Mathf.Min(at, info.characterCount - 1)].lineNumber;
        if (at < info.characterCount && line >= 0 && line < info.lineInfo.Length && at == info.lineInfo[line].firstCharacterIndex)
        {
            letter = info.characterInfo[at];
            x = letter.origin;
        }
        else
        {
            if (at <= 0) return false;
            letter = info.characterInfo[at - 1];
            x = letter.xAdvance;
        }
        float height = letter.ascender - letter.descender;
        float bottom = text.verticalAlignment == VerticalAlignmentOptions.Geometry ? -height * .5f : letter.descender;
        var face = text.font != null ? text.font.faceInfo : default;
        float scale = face.pointSize > 0f ? text.fontSize / face.pointSize * face.scale : 1f;
        float width = Mathf.Max(field.caretWidth * face.lineHeight * scale * .05f, 1f);
        var position = new Vector2(x, bottom);
        var size = new Vector2(width, height);
        if (bar.anchoredPosition != position) bar.anchoredPosition = position;
        if (bar.sizeDelta != size) bar.sizeDelta = size;
        paint.color = field.caretColor;
        return height > 0f;
    }

    // TMP_InputField.GetCaretPositionFromStringIndex: the first letter at or past a place in the string. The text TMP
    // shows ends in a zero-width space, so even the end of the string has a letter.
    private static int CharacterAt(TMP_TextInfo info, int stringIndex)
    {
        for (int i = 0; i < info.characterCount; i++)
            if (info.characterInfo[i].index >= stringIndex) return i;
        return info.characterCount;
    }
}
