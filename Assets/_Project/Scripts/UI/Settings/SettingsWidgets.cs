using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Code-built uGUI pieces for the Esc menu. Flat rectangles, so the menu needs no imported sprites.</summary>
public static class SettingsWidgets
{
    public static readonly Color Text = new Color(.93f, .93f, .94f), Muted = new Color(.6f, .63f, .68f),
        Accent = new Color(.95f, .63f, .27f), OnAccent = new Color(.1f, .08f, .06f),
        Track = new Color(1f, 1f, 1f, .14f), Surface = new Color(1f, 1f, 1f, .07f);

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

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public static LayoutElement Size(Component target, float width = -1f, float height = -1f, float flexibleWidth = -1f)
    {
        if (!target.TryGetComponent(out LayoutElement layout)) layout = target.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width; layout.preferredHeight = height; layout.flexibleWidth = flexibleWidth;
        return layout;
    }

    public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var label = Rect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = CombatUIAssets.ResolveFont(null);
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
        Size(row, height: 40f);
        Size(Label(row, name, 22f, Text), width: 210f);
        return row;
    }

    public static Slider Slider(Transform parent, float min, float max, float value, Action<float> changed)
    {
        var root = Rect("Slider", parent);
        Size(root, height: 28f, flexibleWidth: 1f);
        Fill(root, Color.clear); // the whole row height takes clicks, not just the thin track
        var track = Rect("Track", root);
        track.anchorMin = new Vector2(0f, .5f); track.anchorMax = new Vector2(1f, .5f); track.sizeDelta = new Vector2(0f, 6f);
        Fill(track, Track);
        var fillArea = Rect("Fill Area", root);
        fillArea.anchorMin = new Vector2(0f, .5f); fillArea.anchorMax = new Vector2(1f, .5f); fillArea.sizeDelta = new Vector2(0f, 6f);
        var fill = Rect("Fill", fillArea);
        fill.sizeDelta = Vector2.zero;
        Fill(fill, Accent);
        var handleArea = Rect("Handle Area", root);
        Stretch(handleArea);
        handleArea.offsetMin = new Vector2(7f, 0f); handleArea.offsetMax = new Vector2(-7f, 0f);
        var handle = Rect("Handle", handleArea);
        handle.sizeDelta = new Vector2(14f, 0f);
        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = Fill(handle, Text);
        slider.minValue = min; slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(v => changed(v));
        return slider;
    }

    /// <summary>Side-by-side options; the chosen one is filled with the accent colour.</summary>
    public static void Choice(Transform parent, string[] options, int selected, Action<int> changed)
    {
        var root = Rect("Choice", parent);
        Size(root, height: 34f, flexibleWidth: 1f);
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
            }
        }
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var option = Rect(options[i], root);
            var image = Fill(option, Surface);
            var label = Label(option, options[i], 19f, Muted, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            images.Add(image); labels.Add(label);
            var button = option.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { Select(index); changed(index); });
        }
        Select(selected);
    }

    /// <summary>A dark centred window sized to what it holds, hidden until shown, over a dimmed screen.</summary>
    public static RectTransform Window(Transform canvas, string name, float width, out RectTransform shade)
    {
        shade = Rect(name, canvas);
        Stretch(shade);
        Fill(shade, new Color(0f, 0f, 0f, .62f));
        var panel = Rect("Panel", shade);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(width, 0f);
        Fill(panel, new Color(.06f, .07f, .09f, .96f));
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(44, 44, 36, 36);
        layout.spacing = 10f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        shade.gameObject.SetActive(false);
        return panel;
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
        var viewport = Rect("Viewport", root);
        Stretch(viewport);
        viewport.offsetMin = new Vector2(10f, 0f); viewport.offsetMax = new Vector2(-10f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = Label(viewport, string.Empty, 19f, Text);
        Stretch(text.rectTransform);
        var input = root.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = viewport; input.textComponent = text; input.targetGraphic = image;
        input.fontAsset = text.font; input.pointSize = text.fontSize;
        input.SetTextWithoutNotify(value);
        input.onValueChanged.AddListener(v => changed(v));
        return input;
    }

    public static Button ActionButton(Transform parent, string text, bool primary, Action clicked)
    {
        var rect = Rect(text, parent);
        Size(rect, width: 180f, height: 48f);
        var image = Fill(rect, primary ? Accent : Surface);
        var label = Label(rect, text, 21f, primary ? OnAccent : Text, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => clicked());
        return button;
    }
}
