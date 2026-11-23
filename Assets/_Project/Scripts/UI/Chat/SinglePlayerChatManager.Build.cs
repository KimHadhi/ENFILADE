using TMPro;
using UnityEngine;
using UnityEngine.UI;

// SinglePlayerChatManager: its parts, made in code (GameHud; until 2026-11-23 they were GameHUD.prefab's, saved in that
// prefab's old pink). Only their structure: ChatFrame places the chat from Chat.csv and dresses every part, and each
// line is drawn in code (ChatMessageItem).
public partial class SinglePlayerChatManager
{
    private const string TextRoleName = "HudText", ChatRoleName = "Chat";

    /// <summary>The chat on the HUD's left side: the history that scrolls, the input box and its channel tab.</summary>
    internal static SinglePlayerChatManager Build(Transform side)
    {
        var chat = FeelUI.Rect("Chat", side);
        chat.anchorMin = chat.anchorMax = chat.pivot = new Vector2(0f, .5f);
        chat.sizeDelta = new Vector2(100f, 100f);
        var manager = chat.gameObject.AddComponent<SinglePlayerChatManager>();

        // The history: a scroll view, its viewport, the lines' stack and a thin scrollbar.
        var history = FeelUI.Rect("ScrollView", chat);
        history.sizeDelta = new Vector2(445f, 280f);
        GameHud.Image(history, Color.clear, true);
        var scroll = history.gameObject.AddComponent<ScrollRect>();
        var viewport = GameHud.Fill("Viewport", history);
        GameHud.Image(viewport, Color.clear, true); // the wheel works between lines too
        viewport.gameObject.AddComponent<RectMask2D>();
        var lines = FeelUI.Rect("Message Container", viewport);
        lines.anchorMin = new Vector2(0f, 1f);
        lines.anchorMax = Vector2.one;
        lines.pivot = new Vector2(0f, 1f);
        var stack = lines.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        lines.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var barRect = FeelUI.Rect("Scrollbar Vertical", history);
        GameHud.Image(barRect, Color.clear, true);
        var area = GameHud.Fill("Sliding Area", barRect);
        var handle = FeelUI.Rect("Handle", area);
        var grip = GameHud.Image(handle, Color.white, true);
        var bar = barRect.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle;
        bar.targetGraphic = grip;
        bar.direction = Scrollbar.Direction.BottomToTop;
        scroll.content = lines;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        // The input box: its text in a masked area, the hint while it is empty, the channel tab at its left.
        var fieldRect = FeelUI.Rect("ChatInputField", chat);
        fieldRect.sizeDelta = new Vector2(445f, 32f);
        var plate = GameHud.Image(fieldRect, Color.clear, true);
        var textArea = GameHud.Fill("Text Area", fieldRect);
        textArea.gameObject.AddComponent<RectMask2D>();
        var hint = GameHud.Text(GameHud.Fill("Placeholder", textArea), TextRoleName, 16f, TextAlignmentOptions.MidlineLeft, HudTone.Grey);
        hint.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var typed = GameHud.Text(GameHud.Fill("Text", textArea), TextRoleName, 16f, TextAlignmentOptions.MidlineLeft, HudTone.Text);
        typed.richText = false;
        var tab = FeelUI.Rect("InputChannelBadge", fieldRect);
        tab.anchorMin = Vector2.zero;
        tab.anchorMax = new Vector2(0f, 1f);
        tab.sizeDelta = new Vector2(100f, 0f);
        var tabPlate = GameHud.Image(tab, Color.clear, true);
        var button = tab.gameObject.AddComponent<Button>();
        button.targetGraphic = tabPlate;
        var iconRect = FeelUI.Rect("Icon", tab);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, .5f);
        iconRect.anchoredPosition = new Vector2(21f, 0f);
        iconRect.sizeDelta = new Vector2(14f, 14f);
        var icon = iconRect.gameObject.AddComponent<ChatChannelIcon>();
        icon.raycastTarget = false;
        var name = GameHud.Fill("Channel", tab);
        name.offsetMin = new Vector2(37f, 0f);
        name.offsetMax = new Vector2(-5f, 0f);
        var label = GameHud.Text(name, TextRoleName, 16f, TextAlignmentOptions.MidlineLeft, HudTone.Text);

        // TMP_InputField makes its caret and selection when it wakes with its text set; the chat wakes with the HUD.
        var field = fieldRect.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = plate;
        field.textViewport = textArea;
        field.textComponent = typed;
        field.placeholder = hint;
        field.fontAsset = typed.font;
        field.pointSize = typed.fontSize;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 4000;
        field.richText = false;
        field.onFocusSelectAll = true;
        field.resetOnDeActivation = true;
        field.restoreOriginalTextOnEscape = true;
        field.navigation = new Navigation { mode = Navigation.Mode.None };

        manager.chatInput = field;
        manager.scrollRect = scroll;
        manager.messageContainer = lines;
        manager.channelLabel = label;
        manager.channelIcon = icon;
        manager.channelButton = button;
        return manager;
    }
}
