using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The chat's box on the HUD's left plane, redone 2026-11-12 with its lines (ChatMessageItem). It puts the chat where
/// Chat.csv Place says, measured from the screen's bottom left like the teammate cards under it (the prefab measured
/// from the screen's middle, which pushed it off a 4:3 screen and onto the cards of a short one), keeps its top under
/// the mission bar's TAB row, and gives it its own small lean on the plane. It dresses GameHUD's prefab parts in the
/// HUD's black, white and one pink: the history's panel (only while typing) with a pink mark on its top edge, a thin
/// pink scrollbar, newest lines at the bottom right over the input box (the prefab hung them from the top), and the
/// input box near black with a pink underline, a channel tab in the channel's colour with its name in dark ink, and
/// TMP_InputField's own blinking caret in pink, 2 px wide.
/// </summary>
public sealed class ChatFrame
{
    // The fewest px of history kept on a short screen: three lines.
    private const float LeastHistory = 110f;

    private readonly RectTransform chat, history, field, content;
    private readonly ScrollRect scroll;
    private readonly TMP_InputField input;
    private readonly Image panel, fieldPlate, edgeMark, underline;
    private readonly Button tab;
    private RectTransform canvas;
    private Vector2 placedFor;
    private float placedTilt = float.NaN;
    private bool dirty = true;

    public ChatFrame(RectTransform chat, ScrollRect scroll, TMP_InputField input, Button tab)
    {
        this.chat = chat;
        this.scroll = scroll;
        this.input = input;
        this.tab = tab;
        if (scroll != null)
        {
            history = (RectTransform)scroll.transform;
            panel = scroll.GetComponent<Image>();
            content = scroll.content;
            edgeMark = Bar("Edge Mark", history);
            edgeMark.rectTransform.anchorMin = edgeMark.rectTransform.anchorMax = edgeMark.rectTransform.pivot = new Vector2(0f, 1f);
            // Behind the lines, over the panel.
            edgeMark.rectTransform.SetAsFirstSibling();
            edgeMark.enabled = false;
        }
        if (input != null)
        {
            field = (RectTransform)input.transform;
            fieldPlate = input.GetComponent<Image>();
            underline = Bar("Underline", field);
            var line = underline.rectTransform;
            line.anchorMin = Vector2.zero;
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(.5f, 0f);
        }
    }

    /// <summary>The colours, sizes and shapes from <paramref name="look"/>: at the start and when Chat.csv is saved.</summary>
    public void Dress(ChatLook look)
    {
        dirty = true;
        var open = look.Open;
        if (panel != null)
        {
            KitStyle.Shape(panel);
            panel.color = open.panel;
        }
        if (edgeMark != null)
        {
            edgeMark.color = open.edge;
            edgeMark.rectTransform.sizeDelta = new Vector2(open.edgeLength, 2f);
        }
        if (scroll != null)
        {
            scroll.scrollSensitivity = open.scroll;
            scroll.verticalScrollbarSpacing = 2f;
            DressScrollbar(scroll.verticalScrollbar, open);
        }
        if (content != null)
        {
            // Hung from the bottom: few lines sit just over the input box, and new ones push the old ones up.
            content.anchorMin = Vector2.zero;
            content.anchorMax = new Vector2(1f, 0f);
            content.pivot = new Vector2(.5f, 0f);
            if (content.TryGetComponent(out VerticalLayoutGroup stack))
            {
                stack.spacing = look.Line.gap;
                stack.childAlignment = TextAnchor.LowerLeft;
                stack.padding = new RectOffset(0, 0, 4, 2);
            }
        }
        if (input != null) DressInput(look);
    }

    /// <summary>
    /// Puts the chat in its place for this screen and the player's HUD tilt. Cheap when nothing changed; call it every
    /// frame, since a window can be resized at any time.
    /// </summary>
    public void Place(ChatLook look)
    {
        if (chat == null) return;
        if (canvas == null)
        {
            var root = chat.GetComponentInParent<Canvas>();
            if (root == null) return;
            canvas = (RectTransform)root.rootCanvas.transform;
        }
        Vector2 size = canvas.rect.size;
        float tilt = GameSettings.Current.hudTilt;
        if (!dirty && size == placedFor && tilt == placedTilt) return;
        dirty = false;
        placedFor = size;
        placedTilt = tilt;

        var place = look.Place;
        // The parent (GameHUD's Left Panel) is a small square on the canvas's middle that HudPlane turns about the screen's
        // left edge, so the canvas's bottom left lies half the canvas from its middle.
        var parent = chat.parent as RectTransform;
        Rect parentRect = parent != null ? parent.rect : new Rect(0f, 0f, 0f, 0f);
        float most = place.ceiling + size.y * .5f - place.bottom - place.inputHeight - place.gap;
        float historyHeight = Mathf.Clamp(most, LeastHistory, Mathf.Max(LeastHistory, place.height));
        chat.anchorMin = chat.anchorMax = chat.pivot = Vector2.zero;
        chat.anchoredPosition = new Vector2(-size.x * .5f - parentRect.xMin + place.left, -size.y * .5f - parentRect.yMin + place.bottom);
        chat.sizeDelta = new Vector2(place.width, place.inputHeight + place.gap + historyHeight);
        // Its own lean about its bottom left, as the teammate cards take theirs (TeamHealthPanel): a positive yaw sends the
        // right side away.
        chat.localRotation = Quaternion.Euler(0f, -place.yaw * tilt, place.roll * tilt);
        if (history != null)
        {
            history.anchorMin = Vector2.zero;
            history.anchorMax = new Vector2(1f, 0f);
            history.pivot = Vector2.zero;
            history.anchoredPosition = new Vector2(0f, place.inputHeight + place.gap);
            history.sizeDelta = new Vector2(0f, historyHeight);
        }
        if (field != null)
        {
            field.anchorMin = Vector2.zero;
            field.anchorMax = new Vector2(1f, 0f);
            field.pivot = Vector2.zero;
            field.anchoredPosition = Vector2.zero;
            field.sizeDelta = new Vector2(0f, place.inputHeight);
        }
    }

    /// <summary>The widest a line may be: the chat's width less the scrollbar's room.</summary>
    public static float LineWidth(ChatLook look) => look.Place.width - look.Open.scrollbar - 6f;

    /// <summary>The panel's pink mark shows with the panel, while typing.</summary>
    public void ShowBox(bool open)
    {
        if (edgeMark != null) edgeMark.enabled = open;
    }

    /// <summary>The channel tab in the channel's colour, its name and mark in dark ink on it.</summary>
    public void PaintTab(Color color, TMP_Text label, ChatChannelIcon icon)
    {
        if (tab != null && tab.TryGetComponent(out Image plate)) plate.color = color;
        if (label != null) label.color = KitStyle.Ink;
        if (icon != null) icon.color = KitStyle.Ink;
    }

    private void DressInput(ChatLook look)
    {
        var open = look.Open;
        if (fieldPlate != null)
        {
            KitStyle.Shape(fieldPlate);
            fieldPlate.color = open.input;
        }
        if (underline != null)
        {
            underline.color = open.edge;
            underline.rectTransform.sizeDelta = new Vector2(0f, open.underline);
        }
        input.customCaretColor = true;
        input.caretColor = open.caret;
        input.caretWidth = Mathf.Max(1, open.caretWidth);
        input.selectionColor = open.selection;
        if (input.textComponent != null)
        {
            input.textComponent.fontSize = look.Line.textSize;
            input.textComponent.color = look.Line.ink;
        }
        if (input.placeholder is TMP_Text hint)
        {
            hint.fontSize = look.Line.textSize - 2f;
            hint.color = open.placeholder;
        }
        // The words start past the channel tab.
        if (input.textViewport != null)
        {
            input.textViewport.offsetMin = new Vector2(open.tab + 10f, 4f);
            input.textViewport.offsetMax = new Vector2(-10f, -4f);
        }
        if (tab == null) return;
        var tabRect = (RectTransform)tab.transform;
        tabRect.anchorMin = Vector2.zero;
        tabRect.anchorMax = new Vector2(0f, 1f);
        tabRect.pivot = new Vector2(0f, .5f);
        tabRect.anchoredPosition = Vector2.zero;
        tabRect.sizeDelta = new Vector2(open.tab, 0f);
        if (tab.TryGetComponent(out Image plate)) KitStyle.Shape(plate);
        foreach (Transform child in tabRect)
        {
            // The prefab's thin accent line and divider belong to the old flat badge; the whole tab is coloured now.
            if (child.name == "Accent" || child.name == "Divider") child.gameObject.SetActive(false);
            if (child.TryGetComponent(out TMP_Text label))
            {
                label.fontSize = open.tabSize;
                label.fontStyle = FontStyles.Bold;
            }
        }
    }

    private static void DressScrollbar(Scrollbar bar, ChatLook.OpenRow open)
    {
        if (bar == null) return;
        var rect = (RectTransform)bar.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(1f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(open.scrollbar, -8f);
        if (bar.TryGetComponent(out Image track))
        {
            track.sprite = null;
            track.color = open.track;
        }
        var handle = bar.handleRect;
        if (handle == null) return;
        if (handle.parent is RectTransform area) area.offsetMin = area.offsetMax = Vector2.zero;
        handle.sizeDelta = Vector2.zero;
        handle.localScale = Vector3.one;
        if (handle.TryGetComponent(out Image grip))
        {
            grip.sprite = null;
            grip.type = Image.Type.Simple;
            grip.color = open.edge;
        }
    }

    private static Image Bar(string name, RectTransform parent)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.gameObject.layer = parent.gameObject.layer;
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }
}
