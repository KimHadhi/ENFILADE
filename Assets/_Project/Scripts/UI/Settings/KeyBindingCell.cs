using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One key slot in the key window (<see cref="KeyBindingsView"/>): a slanted plate with the key's name. It answers the
/// pointer going down rather than a click, so the button that finishes a listen (a mouse button being bound) can't
/// start another on its release. Pink while it listens; a short pink bar at its left while its key isn't the
/// default; a quick pink flash when a swap changes it, gone within half a second.
/// </summary>
[DisallowMultipleComponent]
public sealed class KeyBindingCell : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
{
    private const float Slant = 8f, FlashSeconds = .45f;
    private static readonly Color Hover = new Color(1f, 1f, 1f, .13f), Flashed = KitStyle.With(KitStyle.Accent, .8f);

    /// <summary>Which of its line's slots this is: 0 the first key, 1 the second.</summary>
    public int Which { get; private set; }
    /// <summary>The line this cell belongs to, as its window keeps it.</summary>
    public object Line { get; private set; }

    private Action<KeyBindingCell, PointerEventData.InputButton> pressed;
    private Image fill, mark;
    private TextMeshProUGUI label;
    private bool over, listening, moved, empty;
    private float flash;

    public static KeyBindingCell Build(Transform row, object line, int which, float width, float height,
        Action<KeyBindingCell, PointerEventData.InputButton> pressed)
    {
        var rect = SettingsWidgets.Rect("Key " + (which + 1), row);
        SettingsWidgets.Size(rect, width, height, 0f, 0f);
        var cell = rect.gameObject.AddComponent<KeyBindingCell>();
        cell.Which = which;
        cell.Line = line;
        cell.pressed = pressed;
        cell.fill = SettingsWidgets.Fill(rect, SettingsWidgets.Surface); // the plate takes the pointer
        KitSkew.On(cell.fill, Slant);
        var markRect = SettingsWidgets.Rect("Changed", rect);
        markRect.anchorMin = Vector2.zero; markRect.anchorMax = new Vector2(0f, 1f); markRect.pivot = new Vector2(0f, .5f);
        markRect.sizeDelta = new Vector2(4f, 0f);
        markRect.anchoredPosition = Vector2.zero;
        cell.mark = SettingsWidgets.Fill(markRect, KitStyle.Accent);
        cell.mark.raycastTarget = false;
        KitSkew.On(cell.mark, Slant);
        cell.label = SettingsWidgets.Label(rect, "", 19f, SettingsWidgets.Text, TextAlignmentOptions.Center);
        SettingsWidgets.Stretch(cell.label.rectTransform);
        cell.label.richText = false; // a key named "<" or "[" is the key, not a tag
        cell.Paint();
        return cell;
    }

    /// <summary>Shows the key (<paramref name="key"/> "" for none), whether it is the default and whether it is being listened for.</summary>
    public void Show(string key, bool isDefault, bool isListening, string listenWords)
    {
        listening = isListening;
        moved = !isDefault;
        empty = string.IsNullOrEmpty(key);
        label.text = listening ? listenWords : empty ? "—" : key;
        Paint();
    }

    /// <summary>A swap or a reset changed this slot: it lights pink and fades back at once.</summary>
    public void Flash()
    {
        flash = 1f;
        Paint();
    }

    public void OnPointerDown(PointerEventData eventData) => pressed?.Invoke(this, eventData.button);

    public void OnPointerEnter(PointerEventData eventData)
    {
        over = true;
        Paint();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        over = false;
        Paint();
    }

    private void Update()
    {
        if (flash <= 0f) return;
        flash = Mathf.Max(0f, flash - Time.unscaledDeltaTime / FlashSeconds);
        Paint();
    }

    private void Paint()
    {
        if (fill == null) return;
        if (listening)
        {
            fill.color = KitStyle.Accent;
            label.color = KitStyle.Ink;
            label.fontStyle = FontStyles.Bold;
            mark.enabled = false;
            return;
        }
        fill.color = Color.Lerp(over ? Hover : SettingsWidgets.Surface, Flashed, flash);
        label.color = empty ? SettingsWidgets.Muted : flash > .5f ? KitStyle.Ink : SettingsWidgets.Text;
        label.fontStyle = moved ? FontStyles.Bold : FontStyles.Normal;
        mark.enabled = moved;
    }
}
