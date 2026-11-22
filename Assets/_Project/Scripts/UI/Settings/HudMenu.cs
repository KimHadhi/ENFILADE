using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The settings' pieces in the HUD's look (the user, 2026-11-22: "설정창도 리디자인해서 시인성, 가독성, 통일성 좋게"): the
/// HUD's neutral blacks in upright rounded boxes, white words, the chosen thing lit white with black words as the HUD's
/// selector lights its box, the HUD's face. Rows stand in cards under a heading, a name wide enough for its words, a switch
/// for on and off, a bar like the HUD's for a number. SettingsWidgets hands over to these under a HudMenuHost.
/// <para>Solid greys rather than see-through white: the project blends in linear, where a faint white over black comes out
/// far brighter than it was picked.</para>
/// </summary>
public static class HudMenu
{
    public static readonly Color Text = new Color(.96f, .96f, .96f), Muted = new Color(.66f, .66f, .67f), Lit = new Color(.93f, .93f, .93f),
        LitOver = Color.white, Ink = new Color(.04f, .04f, .045f), Ground = new Color(.045f, .045f, .05f, .985f), Side = new Color(.028f, .028f, .032f),
        Card = new Color(.083f, .083f, .09f), RowOver = new Color(.125f, .125f, .135f), Surface = new Color(.16f, .16f, .17f),
        Over = new Color(.24f, .24f, .25f), Track = new Color(.27f, .27f, .28f), Field = new Color(.03f, .03f, .035f), Line = new Color(.19f, .19f, .2f);
    public const float Radius = 6f;
    private const float KnobWide = 8f, KnobTall = 22f, SliderTall = 30f, Bar = 6f;

    /// <summary>The HUD's face (its font option), Hangul from the fallbacks.</summary>
    public static TMP_FontAsset Font
    {
        get
        {
            var font = HudTone.Font;
            return font != null ? font : FontRoles.Font(FontRoles.Label);
        }
    }

    public static Image Box(RectTransform rect, Color colour, bool takesClicks = false, float radius = Radius)
    {
        var image = HudTone.Rounded(rect, colour, radius);
        image.raycastTarget = takesClicks;
        return image;
    }

    /// <summary>A window's ground with a hairline round it.</summary>
    public static void Panel(RectTransform panel)
    {
        Box(panel, Line, true, 10f);
        var fill = W.Rect("Fill", panel);
        KitStyle.Stretch(fill);
        fill.offsetMin = new Vector2(1f, 1f);
        fill.offsetMax = new Vector2(-1f, -1f);
        Box(fill, Ground, false, 9f);
        fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        fill.SetAsFirstSibling();
    }

    /// <summary>A row: its name at the left, then whatever control the caller adds; it brightens under the pointer.</summary>
    public static RectTransform Row(HudMenuHost host, Transform parent, string name)
    {
        var row = W.Rect(name, parent);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 0, 0);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        W.Size(row, height: host.RowHeight, flexibleHeight: 0f);
        HudHover.On(Box(row, Color.clear, true), Clear(RowOver), RowOver);
        var label = W.Label(row, name, 19f, Text);
        label.overflowMode = TextOverflowModes.Ellipsis;
        W.Size(label, width: host.LabelWidth, height: host.RowHeight);
        return row;
    }

    /// <summary>A heading over the rows of a card.</summary>
    public static TextMeshProUGUI Header(Transform parent, string text)
    {
        var row = W.Rect("Header", parent);
        W.Size(row, height: 42f, flexibleHeight: 0f);
        var mark = W.Rect("Mark", row);
        mark.anchorMin = mark.anchorMax = mark.pivot = new Vector2(0f, .5f);
        mark.anchoredPosition = new Vector2(14f, 0f);
        mark.sizeDelta = new Vector2(3f, 16f);
        W.Fill(mark, Text).raycastTarget = false;
        var label = W.Label(row, text, 19f, Text);
        KitStyle.Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(26f, 0f);
        label.fontStyle = FontStyles.Bold;
        return label;
    }

    /// <summary>A card the rows of one kind stand in.</summary>
    public static RectTransform NewCard(Transform parent)
    {
        var card = W.Rect("Card", parent);
        Box(card, Card);
        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 6, 8);
        layout.spacing = 2f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return card;
    }

    /// <summary>A quiet line of explanation under the rows above it.</summary>
    public static TextMeshProUGUI Note(Transform parent, string text)
    {
        var holder = W.Rect("Note", parent);
        var layout = holder.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 2, 6);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var label = W.Label(holder, text, 16f, Muted);
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    /// <summary>The HUD's bar as a slider: track, white fill and knob one span, as SettingsWidgets.Slider lays them.</summary>
    public static Slider Slider(Transform parent, float min, float max, float value, Action<float> changed, bool wholeNumbers)
    {
        var root = W.Rect("Slider", parent);
        W.Size(root, height: SliderTall, flexibleWidth: 1f);
        W.Fill(root, Color.clear); // the whole row height takes clicks, not just the thin track
        var track = W.Rect("Track", root);
        track.anchorMin = new Vector2(0f, .5f);
        track.anchorMax = new Vector2(1f, .5f);
        track.sizeDelta = new Vector2(-W.KnobRoom * 2f, Bar);
        Box(track, Track, false, Bar * .5f);
        var fillArea = W.Rect("Fill Area", root);
        fillArea.anchorMin = new Vector2(0f, .5f);
        fillArea.anchorMax = new Vector2(1f, .5f);
        fillArea.sizeDelta = new Vector2(-W.KnobRoom * 2f, Bar);
        var fill = W.Rect("Fill", fillArea);
        fill.sizeDelta = Vector2.zero;
        Box(fill, Lit, false, Bar * .5f);
        var handleArea = W.Rect("Handle Area", root);
        KitStyle.Stretch(handleArea);
        handleArea.offsetMin = new Vector2(W.KnobRoom, 0f);
        handleArea.offsetMax = new Vector2(-W.KnobRoom, 0f);
        var handle = W.Rect("Handle", handleArea);
        handle.sizeDelta = new Vector2(KnobWide, KnobTall - SliderTall);
        var slider = root.gameObject.AddComponent<KitSlider>();
        var grip = Box(handle, LitOver, true, 3f);
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = grip;
        // Range and rounding go first: changing them later re-sets the value and calls the listener.
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = wholeNumbers;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(v => changed(v));
        return slider;
    }

    /// <summary>An on/off switch: off dark with its knob at the left, on lit with its knob at the right, the word beside it.
    /// <paramref name="show"/> sets it without calling <paramref name="changed"/>.</summary>
    public static RectTransform Switch(Transform parent, bool value, Action<bool> changed, out Action<bool> show)
    {
        var root = W.Rect("Switch", parent);
        W.Size(root, width: 118f, height: 34f, flexibleWidth: 0f);
        var hit = W.Fill(root, Color.clear);
        var track = W.Rect("Track", root);
        track.anchorMin = track.anchorMax = track.pivot = new Vector2(0f, .5f);
        track.sizeDelta = new Vector2(58f, 28f);
        var ground = Box(track, Surface);
        var knob = W.Rect("Knob", track);
        knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(0f, .5f);
        knob.sizeDelta = new Vector2(24f, 20f);
        var cap = Box(knob, Muted, false, 4f);
        var word = W.Label(root, "", 18f, Muted);
        KitStyle.Stretch(word.rectTransform);
        word.rectTransform.offsetMin = new Vector2(70f, 0f);
        string off = Texts.Get("settings.off"), on = Texts.Get("settings.on");
        bool now = value;
        void Paint()
        {
            ground.color = now ? Lit : Surface;
            cap.color = now ? Ink : Muted;
            knob.anchoredPosition = new Vector2(now ? 30f : 4f, 0f);
            word.text = now ? on : off;
            word.color = now ? Text : Muted;
        }
        var button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.onClick.AddListener(() =>
        {
            now = !now;
            Paint();
            changed(now);
        });
        Paint();
        show = to =>
        {
            now = to;
            Paint();
        };
        return root;
    }

    /// <summary>Options side by side in one dark box, the chosen one lit white with black words.</summary>
    public static RectTransform Choice(Transform parent, string[] options, int selected, Action<int> changed, out Action<int> show)
    {
        var root = W.Rect("Choice", parent);
        W.Size(root, height: 36f, flexibleWidth: 1f, flexibleHeight: 0f);
        Box(root, Field);
        var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(3, 3, 3, 3);
        layout.spacing = 3f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        var plates = new List<HudHover>();
        var labels = new List<TextMeshProUGUI>();
        void Select(int index)
        {
            for (int i = 0; i < plates.Count; i++)
            {
                bool on = i == index;
                if (on) plates[i].Set(Lit, LitOver);
                else plates[i].Set(Clear(Over), Over);
                labels[i].color = on ? Ink : Muted;
                labels[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            }
        }
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var option = W.Rect(options[i], root);
            var image = Box(option, Color.clear, true, 4f);
            var label = W.Label(option, options[i], 18f, Muted, TextAlignmentOptions.Center);
            KitStyle.Stretch(label.rectTransform);
            labels.Add(label);
            var button = option.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            plates.Add(HudHover.On(image, Color.clear, Over));
            button.onClick.AddListener(() =>
            {
                Select(index);
                changed(index);
            });
        }
        Select(selected);
        show = Select;
        return root;
    }

    public static Image Arrow(Transform parent, string glyph, Action clicked)
    {
        var rect = W.Rect(glyph == "<" ? "Back" : "Forward", parent);
        W.Size(rect, width: 44f, height: 36f);
        var image = Box(rect, Surface, true);
        var label = W.Label(rect, glyph, 20f, Text, TextAlignmentOptions.Center);
        label.richText = false; // a lone "<" is the glyph, not a tag
        KitStyle.Stretch(label.rectTransform);
        var button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;
        HudHover.On(image, Surface, Over);
        button.onClick.AddListener(() => clicked());
        return image;
    }

    public static Button ActionButton(Transform parent, string text, bool primary, Action clicked)
    {
        var rect = W.Rect(text, parent);
        W.Size(rect, width: 180f, height: 46f);
        var image = Box(rect, primary ? Lit : Surface, true);
        var label = W.Label(rect, text, 19f, primary ? Ink : Text, TextAlignmentOptions.Center);
        KitStyle.Stretch(label.rectTransform);
        label.fontStyle = primary ? FontStyles.Bold : FontStyles.Normal;
        var button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;
        HudHover.On(image, primary ? Lit : Surface, primary ? LitOver : Over);
        button.onClick.AddListener(() => clicked());
        return button;
    }

    /// <summary>The colour with nothing of it showing (so a box fades from and to it without a grey edge).</summary>
    public static Color Clear(Color colour) => new Color(colour.r, colour.g, colour.b, 0f);

    /// <summary>One of a list down the side (a settings tab, a part of the HUD): lit white while it is the chosen one.</summary>
    public sealed class Entry
    {
        private readonly HudHover hover;
        private readonly TextMeshProUGUI label;

        public RectTransform Rect { get; }

        public Entry(RectTransform rect, string name, float size, Action pressed, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            Rect = rect;
            var image = Box(rect, Color.clear, true);
            label = W.Label(rect, name, size, Text, alignment);
            KitStyle.Stretch(label.rectTransform);
            if (alignment == TextAlignmentOptions.MidlineLeft) label.rectTransform.offsetMin = new Vector2(16f, 0f);
            label.rectTransform.offsetMax = new Vector2(alignment == TextAlignmentOptions.MidlineLeft ? -8f : 0f, 0f);
            label.overflowMode = TextOverflowModes.Ellipsis;
            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            button.onClick.AddListener(() => pressed());
            hover = HudHover.On(image, Color.clear, Over);
            Show(false);
        }

        /// <summary><paramref name="faint"/>: the thing it names is switched off.</summary>
        public void Show(bool chosen, bool faint = false)
        {
            if (chosen) hover.Set(Lit, LitOver);
            else hover.Set(Clear(Over), Over);
            label.color = chosen ? Ink : faint ? Muted : Text;
            label.fontStyle = chosen ? FontStyles.Bold : FontStyles.Normal;
        }
    }
}
