using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MainMenuWardrobe: Right wing: the body page. One part of the class; the rest is in MainMenuWardrobe.cs and the files beside it.
public sealed partial class MainMenuWardrobe
{
    private void OpenBody()
    {
        openSlot = BodyPage;
        StopTrying();
        KitSound.Play(KitSound.Cue.Ping, .35f);
        Build();
    }

    private void PickTab(string tab)
    {
        bodyTab = tab;
        KitSound.Play(KitSound.Cue.Ping, .25f);
        Build();
    }

    // Every look the character's model can take, a tab at a time (body, face, eyes …), the camera close on what the tab
    // shapes. Each row is a slider or chips; the change shows on the character at once and is kept on letting go.
    private void BuildBody(RectTransform right, PlayerBodyProfile.Skin skin)
    {
        var options = BodyShapes.For(skin.Id);
        var tabs = BodyShapes.Groups.Where(group => options.Any(option => option.Group == group)).ToList();
        if (bodyTab == null || !tabs.Contains(bodyTab)) bodyTab = tabs.FirstOrDefault();
        if (preview != null) preview.Focus(FocusOf(bodyTab));
        var saved = OutfitLook.Saved(skin.Id);

        Chip(right, new Vector2(0f, 26f), 150f, "‹  " + Texts.Get("wardrobe.back"), false, ShowSlots);
        // 원래대로: every option back to the model as it came, shown while any is changed.
        bodyReset = Chip(right, new Vector2(RightWidth - 150f, 26f), 150f, Texts.Get("body.reset"), false, () => ResetBody(skin));
        bodyReset.gameObject.SetActive(BodyShapes.Changed(saved, skin.Id) > 0);
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", right, new Vector2(0f, 66f), new Vector2(RightWidth, 62f)), Texts.Get("body.title"), 50f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
        Grow(title.rectTransform, 0f);
        Note(right, new Vector2(4f, 128f), RightWidth, Texts.Get("body.where"));

        float tabWidth = Mathf.Floor((RightWidth - TabGap * (TabColumns - 1)) / TabColumns), y = 160f;
        for (int i = 0; i < tabs.Count; i++)
        {
            string tab = tabs[i];
            Chip(right, new Vector2(i % TabColumns * (tabWidth + TabGap), y + i / TabColumns * (TabHeight + TabGap)), tabWidth,
                Texts.Get("body.group." + tab), tab == bodyTab, tab == bodyTab ? null : (System.Action)(() => PickTab(tab)));
        }
        y += Mathf.CeilToInt(tabs.Count / (float)TabColumns) * (TabHeight + TabGap) + 8f;

        // The tab's rows in a list the wheel scrolls, from under the tabs to the page's foot: the eyes alone have two dozen.
        var list = ScrollView("Body Options", y);
        float at = 4f;
        foreach (var option in options)
            if (option.Group == bodyTab) at += BodyRow(list, skin, option, saved, at) + RowGap;
        Scroll(list, at);
    }

    // A list the wheel scrolls, in the right wing's place from <paramref name="top"/> under the page's top to its foot.
    // Rows go into the list returned, top down from 0; Scroll then sizes it once they are in and lets the wheel move it.
    private RectTransform ScrollView(string name, float top)
    {
        var view = FeelUI.Rect(name, content);
        view.anchorMin = new Vector2(1f, 0f);
        view.anchorMax = new Vector2(1f, 1f);
        view.pivot = new Vector2(1f, 1f);
        view.offsetMin = new Vector2(-(edge + RightWidth), ListFoot);
        view.offsetMax = new Vector2(-edge, -top);
        KitStyle.Fill(view, Color.clear).raycastTarget = true; // the wheel scrolls between the rows too
        view.gameObject.AddComponent<RectMask2D>();
        return KitStyle.TopLeft("List", view, Vector2.zero, new Vector2(RightWidth, 0f));
    }

    private static void Scroll(RectTransform list, float height)
    {
        list.sizeDelta = new Vector2(RightWidth, height);
        var view = (RectTransform)list.parent;
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = view;
        scroll.content = list;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = ScrollStep;
    }

    // Where the camera looks for a tab: close on the face for the face and ears, closer on the eyes for the eyes, brows and
    // lashes, head and chest for hair; the whole character for the body, the animal ears and tail, and the outfit.
    private static string FocusOf(string tab) => tab switch
    {
        "face" or "ears" => "face",
        "eyes" or "brows" => "eyes",
        "hair" => "hair",
        _ => null,
    };

    // One option as a row: its name (a pink dot before it while it is changed), then a slider or chips. Returns its height.
    private float BodyRow(RectTransform list, PlayerBodyProfile.Skin skin, BodyShapes.Option option, OutfitLook saved, float y)
    {
        float width = RightWidth - 8f;
        var row = KitStyle.TopLeft("Option " + option.Id, list, new Vector2(0f, y), new Vector2(width, 40f));
        var mark = KitStyle.Fill(KitStyle.TopLeft("Changed", row, new Vector2(2f, 15f), new Vector2(6f, 6f)), KitStyle.Accent);
        mark.enabled = BodyShapes.TryChosen(saved, option, out _);
        bodyMarks[option] = mark;
        var name = KitStyle.Flat(KitStyle.TopLeft("Name", row, new Vector2(14f, 4f), new Vector2(RowName - 18f, 28f)), option.Name, 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Text);
        name.fontStyle = FontStyles.Normal;
        name.overflowMode = FontRoles.ClippedOverflow(name);
        float own = BodyShapes.Own(skin, option), value = BodyShapes.Value(saved, skin, option);
        float height = option.Kind == BodyShapes.Kind.Choice ? ChoiceRow(row, skin, option, own, value) : SliderRow(row, skin, option, own, value);
        row.sizeDelta = new Vector2(width, height);
        Grow(row, 0f);
        return height;
    }

    // A slider: dragging shapes the character at once, letting go keeps it. A thin white tick on the track marks the
    // model's own value, and a pair names its two ends under it.
    private float SliderRow(RectTransform row, PlayerBodyProfile.Skin skin, BodyShapes.Option option, float own, float value)
    {
        float left = RowName, width = RightWidth - 8f - RowName - ValueWidth;
        // The tick goes in first, under the slider's handle, on the track as drawn (it starts KnobRoom in from either end).
        float place = Mathf.InverseLerp(option.Min, option.Max, own), room = SettingsWidgets.KnobRoom;
        KitStyle.Fill(KitStyle.TopLeft("Own", row, new Vector2(left + room + place * (width - room * 2f) - 1f, 8f), new Vector2(2f, 20f)),
            KitStyle.With(KitStyle.Ice, .6f));
        var number = KitStyle.Flat(KitStyle.TopLeft("Value", row, new Vector2(left + width + 6f, 4f), new Vector2(ValueWidth - 6f, 28f)),
            ValueText(option, value), 14f, TextAlignmentOptions.MidlineRight, KitStyle.Soft);
        number.fontStyle = FontStyles.Normal;
        float current = value;
        var slider = SettingsWidgets.Slider(row, option.Min, option.Max, value, moved =>
        {
            current = moved;
            number.text = ValueText(option, moved);
            if (preview != null) preview.Shape(option, moved);
        }, wholeNumbers: true);
        var rect = (RectTransform)slider.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -4f);
        rect.sizeDelta = new Vector2(width, 28f);
        PointerRelease.On(slider.gameObject, () => SaveShape(skin, option, current));
        var ends = option.Ends;
        if (ends == null) return 36f;
        Small(row, new Vector2(left, 31f), width * .5f, ends[0], TextAlignmentOptions.MidlineLeft);
        Small(row, new Vector2(left + width * .5f, 31f), width * .5f, ends[1], TextAlignmentOptions.MidlineRight);
        return 48f;
    }

    // Chips, four to a line (three for long words): a pick shows and is kept at once. The model's own one is underlined.
    private float ChoiceRow(RectTransform row, PlayerBodyProfile.Skin skin, BodyShapes.Option option, float own, float value)
    {
        var words = option.Choices;
        int columns = words.Max(word => word.Length) > 5 ? 3 : 4;
        float left = RowName, room = RightWidth - 8f - RowName;
        float chipWidth = Mathf.Floor((room - ChipGap * (columns - 1)) / columns);
        int mine = Mathf.RoundToInt(own);
        var backs = new Image[words.Length];
        var labels = new TextMeshProUGUI[words.Length];
        for (int i = 0; i < words.Length; i++)
        {
            int index = i;
            var chip = KitStyle.TopLeft("Choice " + i, row, new Vector2(left + i % columns * (chipWidth + ChipGap), 4f + i / columns * (OptionChip + 6f)),
                new Vector2(chipWidth, OptionChip));
            backs[i] = KitStyle.Fill(chip, Color.clear);
            KitSkew.On(backs[i], ChipSlant);
            backs[i].raycastTarget = true;
            labels[i] = KitStyle.Flat(KitStyle.Stretch("Text", chip), words[i], 13f, TextAlignmentOptions.Center, KitStyle.Text);
            labels[i].overflowMode = FontRoles.ClippedOverflow(labels[i]);
            if (i == mine)
                KitSkew.On(KitStyle.Fill(KitStyle.TopLeft("Own", chip, new Vector2(8f, OptionChip - 3f), new Vector2(chipWidth - 16f, 2f)),
                    KitStyle.With(KitStyle.Ice, .75f)), ChipSlant);
            var button = chip.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = backs[i];
            button.onClick.AddListener(() =>
            {
                if (preview != null) preview.Shape(option, index);
                SaveShape(skin, option, index);
                LightChips(backs, labels, index);
            });
            chip.gameObject.AddComponent<KitHover>();
        }
        LightChips(backs, labels, Mathf.RoundToInt(value));
        return 4f + Mathf.CeilToInt(words.Length / (float)columns) * (OptionChip + 6f);
    }

    // The picked chip solid pink, the others faint.
    private static void LightChips(Image[] backs, TextMeshProUGUI[] labels, int picked)
    {
        for (int i = 0; i < backs.Length; i++)
        {
            backs[i].color = i == picked ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .09f);
            labels[i].color = i == picked ? KitStyle.Ink : KitStyle.Text;
        }
    }

    // A pair's value with its side: "+40" toward its right end, "-40" toward its left.
    private static string ValueText(BodyShapes.Option option, float value)
    {
        int whole = Mathf.RoundToInt(value);
        return option.Kind == BodyShapes.Kind.Pair && whole > 0 ? "+" + whole : whole.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Small(RectTransform parent, Vector2 at, float width, string text, TextAlignmentOptions alignment)
    {
        var label = KitStyle.Flat(KitStyle.TopLeft("End", parent, at, new Vector2(width, 16f)), text, 11f, alignment, KitStyle.Soft);
        label.fontStyle = FontStyles.Normal;
    }

    // Keeps a body option as the character shows it now; one back at the model's own is written out of the outfit. The
    // character isn't stood up again (it shows this already) and the page isn't rebuilt: its marks follow.
    private void SaveShape(PlayerBodyProfile.Skin skin, BodyShapes.Option option, float value)
    {
        var look = OutfitLook.Saved(skin.Id);
        bool own = Mathf.RoundToInt(value) == Mathf.RoundToInt(BodyShapes.Own(skin, option));
        BodyShapes.Choose(look, option, own ? (float?)null : value);
        OutfitLook.Save(skin.Id, look);
        if (preview != null) preview.Kept(look);
        GameSettings.Apply();
        GameSettings.Save();
        KitSound.Play(KitSound.Cue.Buy, .25f);
        if (bodyMarks.TryGetValue(option, out var mark) && mark != null) mark.enabled = !own;
        int changed = BodyShapes.Changed(look, skin.Id);
        if (bodyReset != null) bodyReset.gameObject.SetActive(changed > 0);
        if (bodyCount != null) bodyCount.text = BodyCount(skin, look);
    }

    // 원래대로: every body option out of the outfit; the character is stood up again from its model, as it came.
    private void ResetBody(PlayerBodyProfile.Skin skin)
    {
        var look = OutfitLook.Saved(skin.Id);
        foreach (var option in BodyShapes.For(skin.Id)) BodyShapes.Choose(look, option, null);
        Save(skin, look);
    }

    // Puts the choice on: one thing per place, so a one-piece empties the bottom, and a bottom put on later takes the
    // one-piece off. A new piece starts in its first colour.
    private void Wear(PlayerBodyProfile.Skin skin, OutfitSlot slot, string item, string colour = "")
    {
        // What was being tried becomes the outfit: the preview lets go of the try and stands the saved one.
        wanted = null;
        wantedText = triedText = wantedSlot = wantedLine = null;
        wantedRemoves = false;
        if (preview != null) preview.DropTry();
        Save(skin, With(skin, slot, item, colour));
    }

    // The outfit as it would be with this choice in the slot, not saved.
    private static OutfitLook With(PlayerBodyProfile.Skin skin, OutfitSlot slot, string item, string colour = "")
    {
        var look = OutfitLook.Saved(skin.Id);
        if (OutfitCatalog.Item(item) is OutfitItem picked)
            foreach (string also in picked.AlsoSlots) look.Set(also, null);
        foreach (var other in OutfitCatalog.Slots)
            if (other != slot && look.TryGet(other.id, out var on) && OutfitCatalog.Item(on.Item) is OutfitItem onItem
                && onItem.AlsoSlots.Contains(slot.id, System.StringComparer.OrdinalIgnoreCase))
                look.Set(other.id, null);
        look.Set(slot.id, item, colour);
        return look;
    }
}
