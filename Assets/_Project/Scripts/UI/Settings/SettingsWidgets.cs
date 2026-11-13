using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Code-built uGUI pieces for the Esc menu and the title screen's windows, in the menus' kit look (KitStyle, 2026-11-09):
/// the HUD's near-black, white and one pink, slanted buttons and choices, thin sliders with a slanted handle, windows
/// with cut corners and a pink mark. No imported sprites.
/// </summary>
public static class SettingsWidgets
{
    public static readonly Color Text = KitStyle.Text, Muted = new Color(.66f, .68f, .74f), Accent = KitStyle.Accent, OnAccent = KitStyle.Ink,
        Track = new Color(1f, 1f, 1f, .14f), Surface = new Color(1f, 1f, 1f, .07f);
    // How far buttons and choices lean (px).
    private const float Slant = 10f;
    /// <summary>How far in from either end of a slider's row its track and the knob's range start (px). The wardrobe's
    /// body sliders place the model's own value on the track by it.</summary>
    public const float KnobRoom = 7f;

    /// <summary>The menus' font: the Label text role (Fonts.csv), as the lobby's kit labels use.</summary>
    public static TMP_FontAsset Font() => FontRoles.Font(FontRoles.Label);

    public static RectTransform Rect(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    public static Image Fill(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    public static LayoutElement Size(Component target, float width = -1f, float height = -1f, float flexibleWidth = -1f,
        float flexibleHeight = -1f)
    {
        if (!target.TryGetComponent(out LayoutElement layout)) layout = target.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width; layout.preferredHeight = height;
        layout.flexibleWidth = flexibleWidth; layout.flexibleHeight = flexibleHeight;
        return layout;
    }

    /// <summary>Clears a window's contents (Window), keeping its own fill and mark.</summary>
    public static void ClearContent(Transform panel)
    {
        foreach (Transform child in panel)
        {
            if (child.TryGetComponent(out LayoutElement layout) && layout.ignoreLayout) continue;
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }

    /// <summary>Takes every child out of the layout now and destroys it at the end of the frame.</summary>
    public static void Clear(Transform parent)
    {
        foreach (Transform child in parent)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }

    public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var label = Rect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Font();
        label.text = text; label.fontSize = size; label.color = color; label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>A labelled row: name on the left, then whatever control the caller adds.</summary>
    public static RectTransform Row(Transform parent, string name)
    {
        var row = Rect(name, parent);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 16f; layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
        Size(row, height: 40f, flexibleHeight: 0f); // a force-expanding group would claim spare height otherwise
        Size(Label(row, name, 22f, Text), width: 210f);
        return row;
    }

    /// <summary>
    /// A thin slider with a slanted knob (KitSlider: the knob follows the pointer laid onto the track as drawn). The
    /// track, its pink fill and the knob's range are one span, <see cref="KnobRoom"/> in from either end of the row, so the
    /// knob's middle, the fill's end and the value's place on the track are the same point at every value; the track and
    /// the fill used to run the whole row while the knob stopped 7 px short of either end, and that much apart they read
    /// as the knob drifting off the cursor (user, 2026-11-12). The whole row height still takes the pointer.
    /// </summary>
    public static Slider Slider(Transform parent, float min, float max, float value, Action<float> changed,
        bool wholeNumbers = false)
    {
        var root = Rect("Slider", parent);
        Size(root, height: 28f, flexibleWidth: 1f);
        Fill(root, Color.clear); // the whole row height takes clicks, not just the thin track
        var track = Rect("Track", root);
        track.anchorMin = new Vector2(0f, .5f); track.anchorMax = new Vector2(1f, .5f); track.sizeDelta = new Vector2(-KnobRoom * 2f, 4f);
        Fill(track, Track);
        var fillArea = Rect("Fill Area", root);
        fillArea.anchorMin = new Vector2(0f, .5f); fillArea.anchorMax = new Vector2(1f, .5f);
        fillArea.sizeDelta = new Vector2(-KnobRoom * 2f, 4f);
        var fill = Rect("Fill", fillArea);
        fill.sizeDelta = Vector2.zero;
        Fill(fill, Accent);
        var handleArea = Rect("Handle Area", root);
        KitStyle.Stretch(handleArea);
        handleArea.offsetMin = new Vector2(KnobRoom, 0f); handleArea.offsetMax = new Vector2(-KnobRoom, 0f);
        var handle = Rect("Handle", handleArea);
        handle.sizeDelta = new Vector2(6f, -4f);
        var slider = root.gameObject.AddComponent<KitSlider>();
        var grip = Fill(handle, Text);
        KitSkew.On(grip, 6f);
        slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = grip;
        // Range and rounding go first: changing them later re-sets the value and calls the listener.
        slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = wholeNumbers;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(v => changed(v));
        return slider;
    }

    /// <summary>Side-by-side options; the chosen one is filled with the accent colour.</summary>
    public static RectTransform Choice(Transform parent, string[] options, int selected, Action<int> changed) =>
        Choice(parent, options, selected, changed, out _);

    /// <summary>
    /// The same, handing back <paramref name="show"/>, which lights another option without calling
    /// <paramref name="changed"/> (-1 lights none): a number typed in the row's box may match a preset or none.
    /// </summary>
    public static RectTransform Choice(Transform parent, string[] options, int selected, Action<int> changed, out Action<int> show)
    {
        var root = Rect("Choice", parent);
        Size(root, height: 34f, flexibleWidth: 1f, flexibleHeight: 0f);
        var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        var images = new List<Image>();
        var labels = new List<TextMeshProUGUI>();
        void Select(int index)
        {
            for (int i = 0; i < images.Count; i++)
            {
                images[i].color = i == index ? Accent : Surface;
                labels[i].color = i == index ? OnAccent : Muted;
                labels[i].fontStyle = i == index ? FontStyles.Bold : FontStyles.Normal;
            }
        }
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var option = Rect(options[i], root);
            var image = Fill(option, Surface);
            KitSkew.On(image, Slant * .8f);
            var label = Label(option, options[i], 19f, Muted, TextAlignmentOptions.Center);
            KitStyle.Stretch(label.rectTransform);
            images.Add(image); labels.Add(label);
            var button = option.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { Select(index); changed(index); });
        }
        Select(selected);
        show = Select;
        return root;
    }

    /// <summary>
    /// One option at a time between a back and a forward arrow, for lists too long to lay side by side (resolutions,
    /// monitors). Stops at either end.
    /// </summary>
    public static RectTransform Stepper(Transform parent, string[] options, int selected, Action<int> changed)
    {
        var root = Rect("Stepper", parent);
        Size(root, height: 34f, flexibleWidth: 1f, flexibleHeight: 0f);
        var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
        int index = options.Length > 0 ? Mathf.Clamp(selected, 0, options.Length - 1) : 0;
        TextMeshProUGUI value = null;
        Image back = null, forward = null;
        void Show()
        {
            value.text = options.Length > 0 ? options[index] : "";
            back.color = index > 0 ? Surface : Track * new Color(1f, 1f, 1f, .4f);
            forward.color = index < options.Length - 1 ? Surface : Track * new Color(1f, 1f, 1f, .4f);
        }
        void Step(int by)
        {
            int next = Mathf.Clamp(index + by, 0, Mathf.Max(0, options.Length - 1));
            if (next == index) return;
            index = next;
            Show();
            changed(index);
        }
        back = Arrow(root, "<", () => Step(-1));
        var box = Rect("Value", root);
        Size(box, flexibleWidth: 1f);
        KitSkew.On(Fill(box, Surface), Slant * .8f);
        value = Label(box, "", 19f, Text, TextAlignmentOptions.Center);
        KitStyle.Stretch(value.rectTransform);
        forward = Arrow(root, ">", () => Step(1));
        Show();
        return root;
    }

    private static Image Arrow(Transform parent, string glyph, Action clicked)
    {
        var rect = Rect(glyph == "<" ? "Back" : "Forward", parent);
        Size(rect, width: 44f);
        var image = Fill(rect, Surface);
        KitSkew.On(image, Slant * .8f);
        var label = Label(rect, glyph, 21f, Text, TextAlignmentOptions.Center);
        label.richText = false; // a lone "<" is the glyph, not a tag
        KitStyle.Stretch(label.rectTransform);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => clicked());
        return image;
    }

    /// <summary>A vertical scroll view taking the rest of its layout parent. Rows go into the returned content.</summary>
    public static RectTransform ScrollArea(Transform parent)
    {
        var root = Rect("Scroll", parent);
        Size(root, flexibleWidth: 1f, flexibleHeight: 1f);
        var viewport = Rect("Viewport", root);
        KitStyle.Stretch(viewport);
        viewport.offsetMax = new Vector2(-16f, 0f); // room for the bar
        viewport.gameObject.AddComponent<RectMask2D>();
        Fill(viewport, Color.clear); // the wheel also works between rows
        var content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1f);
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var bar = Rect("Scrollbar", root);
        bar.anchorMin = new Vector2(1f, 0f); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(1f, .5f);
        bar.sizeDelta = new Vector2(4f, 0f);
        Fill(bar, Track);
        var slidingArea = Rect("Sliding Area", bar);
        KitStyle.Stretch(slidingArea);
        var handle = Rect("Handle", slidingArea);
        handle.sizeDelta = Vector2.zero;
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle; scrollbar.targetGraphic = Fill(handle, Muted);
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.inertia = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
    }

    /// <summary>A dark centred window sized to what it holds, hidden until shown, over a dimmed screen.</summary>
    public static RectTransform Window(Transform canvas, string name, float width, out RectTransform shade)
    {
        shade = Rect(name, canvas);
        KitStyle.Stretch(shade);
        Fill(shade, new Color(0f, 0f, 0f, .72f));
        var panel = Rect("Panel", shade);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(width, 0f);
        Panel(panel);
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(44, 44, 36, 36);
        layout.spacing = 10f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        shade.gameObject.SetActive(false);
        return panel;
    }

    /// <summary>
    /// Dresses a window's panel: dark with cut corners, a faint edge and a short pink mark at its top left. The fill and
    /// the mark are children kept out of the layout (and out of ClearContent).
    /// </summary>
    public static void Panel(RectTransform panel)
    {
        KitStyle.Cut(panel, new Color(1f, 1f, 1f, .12f));
        var fill = Rect("Fill", panel);
        KitStyle.Stretch(fill);
        fill.offsetMin = new Vector2(1f, 1f); fill.offsetMax = new Vector2(-1f, -1f);
        KitStyle.Cut(fill, KitStyle.Panel);
        fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        fill.SetAsFirstSibling();
        var mark = Rect("Mark", panel);
        mark.anchorMin = mark.anchorMax = mark.pivot = new Vector2(0f, 1f);
        mark.anchoredPosition = Vector2.zero;
        mark.sizeDelta = new Vector2(140f, 3f);
        KitStyle.Fill(mark, Accent);
        mark.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
    }

    /// <summary>A row of buttons aligned to the right.</summary>
    public static RectTransform ButtonRow(Transform parent)
    {
        var buttons = Rect("Buttons", parent);
        var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 12f; row.childAlignment = TextAnchor.MiddleRight;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        row.padding = new RectOffset(0, 0, 14, 0);
        return buttons;
    }

    public static TMP_InputField TextInput(Transform parent, string value, Action<string> changed)
    {
        var root = Rect("Input", parent);
        Size(root, height: 34f, flexibleWidth: 1f);
        var image = Fill(root, Surface);
        var line = Rect("Line", root);
        line.anchorMin = Vector2.zero; line.anchorMax = new Vector2(1f, 0f); line.pivot = new Vector2(.5f, 0f);
        line.sizeDelta = new Vector2(0f, 2f);
        Fill(line, Track).raycastTarget = false;
        var viewport = Rect("Viewport", root);
        KitStyle.Stretch(viewport);
        viewport.offsetMin = new Vector2(10f, 0f); viewport.offsetMax = new Vector2(-10f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = Label(viewport, string.Empty, 19f, Text);
        KitStyle.Stretch(text.rectTransform);
        var input = root.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = viewport; input.textComponent = text; input.targetGraphic = image;
        input.fontAsset = text.font; input.pointSize = text.fontSize;
        // TMP_InputField makes the renderer that draws its caret and its selection in OnEnable, and only once it has its
        // text component. AddComponent on a live object runs OnEnable before that is set, so no box built here ever showed
        // a caret or a selection (user, 2026-11-12: "깜빡이는 | 바가 없어서 아직도 내가 칠 수 있는 상태인 건가?"). Waking it
        // again now makes them; under a parent not yet active, OnEnable is still to come and finds the text set.
        input.enabled = false;
        input.enabled = true;
        // TMP's own caret is dark grey, which vanished on these dark fields.
        input.customCaretColor = true; input.caretColor = Text; input.caretWidth = 2;
        input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, .45f);
        CaretWhileSelected.On(input);
        input.SetTextWithoutNotify(value);
        input.onValueChanged.AddListener(v => changed(v));
        return input;
    }

    /// <summary>
    /// A setting's value at the end of its row, as a box to type in (user, 2026-11-11: "슬라이더 말고 직접 숫자 칸에
    /// 입력"). It reads like the readout it replaces (<paramref name="shown"/>: "80%", "끔") until clicked, then holds the
    /// bare number (<paramref name="editing"/>: "80") with all of it selected, so typing replaces it. Enter or clicking
    /// away hands the number read to <paramref name="typed"/>, which clamps and applies it; Esc keeps the old value. Either
    /// way the box reads <paramref name="shown"/> again after. While it is typed in it looks it (<see cref="TypingLook"/>:
    /// dark inside a pink frame, a pink caret, the row's name pink). The first child of <paramref name="parent"/> is taken
    /// for the row's name when it is a label (a <see cref="Row"/>).
    /// </summary>
    public static TMP_InputField NumberBox(Transform parent, Func<string> shown, Func<string> editing, Action<float> typed)
    {
        var field = TextInput(parent, shown(), _ => { });
        Size(field, width: 104f, height: 30f, flexibleWidth: 0f);
        field.characterLimit = 12;
        field.onFocusSelectAll = true;
        var text = (TextMeshProUGUI)field.textComponent;
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.fontSize = field.pointSize = 20f;
        // TMP draws the caret just past the last letter, and right-aligned text puts that on the viewport's edge, where
        // the viewport's mask would cut it away: this much room keeps it in sight.
        text.rectTransform.offsetMax = new Vector2(-CaretRoom, 0f);
        TypingLook.On(field, parent.childCount > 0 ? parent.GetChild(0).GetComponent<TMP_Text>() : null);
        // Before the field takes focus, so the bare number is what gets selected and what Esc goes back to.
        field.onSelect.AddListener(_ => field.SetTextWithoutNotify(editing()));
        field.onEndEdit.AddListener(value =>
        {
            if (TryReadNumber(value, out float number)) typed(number);
            field.SetTextWithoutNotify(shown());
        });
        return field;
    }

    // Room right of a right-aligned number for the caret, which TMP draws past the last letter (px).
    private const float CaretRoom = 6f;

    /// <summary>The bare number a box shows while it is typed in: up to three decimals, none when it is whole.</summary>
    public static string Bare(float value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads a typed number leniently: units and words around it ("80%", "1.5x") are dropped, a comma counts as the decimal
    /// point and full-width digits from an IME as plain ones. False when no number is left.
    /// </summary>
    public static bool TryReadNumber(string text, out float number)
    {
        number = 0f;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var clean = new System.Text.StringBuilder();
        foreach (char raw in text)
        {
            char c = raw >= '０' && raw <= '９' ? (char)('0' + (raw - '０')) : raw == '．' || raw == ',' ? '.' : raw;
            if ((c >= '0' && c <= '9') || c == '.' || (c == '-' && clean.Length == 0)) clean.Append(c);
        }
        return float.TryParse(clean.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
            out number) && !float.IsNaN(number) && !float.IsInfinity(number);
    }

    public static Button ActionButton(Transform parent, string text, bool primary, Action clicked)
    {
        var rect = Rect(text, parent);
        Size(rect, width: 180f, height: 48f);
        var image = Fill(rect, primary ? Accent : Surface);
        KitSkew.On(image, Slant);
        var label = Label(rect, text, 21f, primary ? OnAccent : Text, TextAlignmentOptions.Center);
        KitStyle.Stretch(label.rectTransform);
        label.fontStyle = primary ? FontStyles.Bold : FontStyles.Normal;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => clicked());
        return button;
    }
}
