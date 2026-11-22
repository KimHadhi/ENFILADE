using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One key slot in the key window (<see cref="KeyBindingsView"/>): a box with the key's name. It answers the
/// pointer going down rather than a click, so the button that finishes a listen (a mouse button being bound) can't
/// start another on its release. Lit white while it listens; a short white bar at its left while its key isn't the
/// default; a quick white flash when a swap changes it, gone within half a second.
/// </summary>
[DisallowMultipleComponent]
public sealed class KeyBindingCell : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
{
    private const float FlashSeconds = .45f;

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
        cell.fill = HudMenu.Box(rect, HudMenu.Surface, true); // the box takes the pointer
        var markRect = SettingsWidgets.Rect("Changed", rect);
        markRect.anchorMin = Vector2.zero; markRect.anchorMax = new Vector2(0f, 1f); markRect.pivot = new Vector2(0f, .5f);
        markRect.sizeDelta = new Vector2(3f, -14f);
        markRect.anchoredPosition = new Vector2(8f, 0f);
        cell.mark = SettingsWidgets.Fill(markRect, HudMenu.Text);
        cell.mark.raycastTarget = false;
        cell.label = SettingsWidgets.Label(rect, "", 18f, HudMenu.Text, TextAlignmentOptions.Center);
        KitStyle.Stretch(cell.label.rectTransform);
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

    /// <summary>A swap or a reset changed this slot: it lights white and fades back at once.</summary>
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
            fill.color = HudMenu.Lit;
            label.color = HudMenu.Ink;
            label.fontStyle = FontStyles.Bold;
            mark.enabled = false;
            return;
        }
        fill.color = Color.Lerp(over ? HudMenu.Over : HudMenu.Surface, HudMenu.Lit, flash);
        label.color = empty ? HudMenu.Muted : flash > .5f ? HudMenu.Ink : HudMenu.Text;
        label.fontStyle = moved ? FontStyles.Bold : FontStyles.Normal;
        mark.enabled = moved;
    }
}
