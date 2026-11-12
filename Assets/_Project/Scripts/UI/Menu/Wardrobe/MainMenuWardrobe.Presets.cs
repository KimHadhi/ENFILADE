using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The wardrobe's presets page (OutfitPresets; the user, 2026-11-13: "옷장 내가 지금까지 옷 입혀서 꾸민 커스텀 프리셋으로
/// 만들어서 저장할 수 있게 해 줘"). A 프리셋 plate in the left wing, under the colours and the body plate, opens it in the
/// right wing. At the top, a name to type and 지금 모습 저장 (Enter too). Under them, every kept look as a slanted plate:
/// its portrait card at the left (the character dressed so, OutfitStudio), its name and its character, the one worn now
/// dark with a pink edge and a 입음 tag. A plate pressed wears its look, its character and colour set too. Its chips
/// overwrite it with the look as it is now, rename it, or delete it; overwriting and deleting ask twice, as the shooting
/// window's 기본값으로 does. The same page opens at the hub's mirror (HubDressing), where a name being typed keeps the
/// mirror's own keys off (Typing).
/// </summary>
public sealed partial class MainMenuWardrobe
{
    private const string PresetPage = "#presets";
    // A preset's plate, its portrait card, the save row, a plate's chips and their gap (px), and the wait for a second
    // press (s).
    private const float PresetHeight = 104f, PortraitWidth = 62f, PortraitHeight = 93f, SaveRow = 34f, PresetChip = 96f, PresetChipGap = 6f,
        AskSeconds = 3f;
    private const string AskOverwrite = "overwrite", AskDelete = "delete";

    private TMP_InputField presetName, renameField;
    private string typedName = "", renaming, asking, askingWhat, presetLine;
    private float askUntil, presetScroll;
    private int typingFrame = -10;
    private bool presetListKept;
    private KitScroll presetList;
    // Something a typing box asked for, done once the box has let go of its key (TickPresets): it must not be pulled
    // down while it is still handling Enter or Esc.
    private System.Action soon;

    /// <summary>A preset's name is being typed (or was this frame): Esc, F and the turning keys are its.</summary>
    public bool Typing => Time.frameCount - typingFrame <= 1;

    // ---- The left wing's way in ----

    // The plate that opens the presets page: how many there are.
    private void PresetPlate(Vector2 at)
    {
        bool open = openSlot == PresetPage;
        var plate = KitStyle.TopLeft("Presets", content, at, new Vector2(LeftWidth, BodyPlateHeight));
        var back = KitStyle.Fill(KitStyle.Stretch("Back", plate), open ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (open) Rim(plate, Slant);
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, BodyPlateHeight), KitStyle.Accent), Slant);
        KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(22f, 4f), new Vector2(260f, 32f)), Texts.Get("wardrobe.presets"), 24f,
            TextAlignmentOptions.MidlineLeft);
        int count = OutfitPresets.All.Count;
        Note(plate, new Vector2(24f, 36f), LeftWidth - 90f,
            count > 0 ? Texts.Format("wardrobe.presets.count", count) : Texts.Get("wardrobe.presets.none"), 14f);
        KitStyle.Flat(KitStyle.TopLeft("Arrow", plate, new Vector2(LeftWidth - 52f, 0f), new Vector2(32f, BodyPlateHeight)), "›", 34f,
            TextAlignmentOptions.Center, KitStyle.Accent);
        Grow(plate, Slant);
        if (open) return;
        var button = plate.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(OpenPresets);
        plate.gameObject.AddComponent<KitHover>();
    }

    private void OpenPresets()
    {
        openSlot = PresetPage;
        slotListFor = null;
        shooting = false;
        renaming = asking = askingWhat = presetLine = null;
        presetListKept = false;
        StopTrying();
        if (preview != null) preview.Focus(null);
        KitSound.Play(KitSound.Cue.Ping, .35f);
        Build();
    }

    // Before the page is built again: where its list was scrolled to, and no boxes from the page that goes.
    private void KeepPresetScroll()
    {
        if (presetList != null)
        {
            presetScroll = presetList.Offset;
            presetListKept = true;
        }
        presetList = null;
        presetName = renameField = null;
    }

    // ---- The page ----

    private void BuildPresets(RectTransform right, PlayerBodyProfile.Skin skin)
    {
        Chip(right, new Vector2(0f, 26f), 150f, "‹  " + Texts.Get("wardrobe.back"), false, LeavePresets);
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", right, new Vector2(0f, 66f), new Vector2(RightWidth, 62f)),
            Texts.Get("wardrobe.presets.title"), 50f, TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
        Grow(title.rectTransform, 0f);
        // What just happened (kept, worn, a slot emptied, a second press awaited), else what the page is for.
        var line = Note(right, new Vector2(4f, 128f), RightWidth, presetLine ?? Texts.Get("wardrobe.presets.where"));
        if (presetLine != null) line.color = KitStyle.Accent;

        // A name (empty: the character's name and a number) and 지금 모습 저장; Enter in the box saves too.
        float saveWidth = 170f, y = 158f;
        presetName = NameField(right, new Vector2(0f, y), RightWidth - saveWidth - 10f, typedName, Texts.Get("wardrobe.presets.name"));
        presetName.onValueChanged.AddListener(text => typedName = text);
        presetName.onSubmit.AddListener(_ => soon = () =>
        {
            if (openSlot == PresetPage) SaveNew(skin);
        });
        Chip(right, new Vector2(RightWidth - saveWidth, y + 2f), saveWidth, Texts.Get("wardrobe.presets.save"), true, () => SaveNew(skin));

        // The kept looks under the save row down to the page's foot, in the kit's list that scrolls once they outgrow it
        // (KitScroll, as the slots' grid): slanted and leaning plates aren't cut at its sides, and only what shows takes
        // clicks. Built again after a press, it comes back where it was scrolled to; a preset just kept is at the top.
        float top = y + SaveRow + 14f;
        var list = KitScroll.Make("Presets", content, new Vector2(1f, 0f), Vector2.one, new Vector2(-(edge + RightWidth), ListFoot),
            new Vector2(-edge, -top));
        var rows = list.Rows;
        float at = 0f;
        var presets = OutfitPresets.All;
        if (presets.Count == 0)
        {
            Note(rows, new Vector2(4f, at), RightWidth - 8f, Texts.Get("wardrobe.presets.empty"));
            at += 30f;
        }
        for (int i = 0; i < presets.Count; i++)
        {
            PresetRow(rows, presets[i], skin, new Vector2(i % 2 == 0 ? 0f : 10f, at), (i % 2 == 0 ? 1f : -1f) * .6f);
            at += PresetHeight + PlateGap;
        }
        list.Fit(at);
        list.Offset = presetListKept ? presetScroll : 0f;
        presetList = list;
        presetListKept = true;
        if (renameField != null)
        {
            renameField.Select();
            renameField.ActivateInputField();
        }
    }

    // One kept look: its portrait card, its name (a box while renamed), its character, the chips along its foot.
    private void PresetRow(RectTransform list, OutfitPreset preset, PlayerBodyProfile.Skin shown, Vector2 at, float tilt)
    {
        var skin = OutfitPresets.SkinOf(preset);
        bool worn = OutfitPresets.IsWorn(preset), naming = renaming == preset.id;
        float width = PlateWidth;
        var plate = KitStyle.TopLeft("Preset " + preset.id, list, at, new Vector2(width, PresetHeight));
        plate.localRotation = Quaternion.Euler(0f, 0f, tilt);
        var back = KitStyle.Fill(KitStyle.Stretch("Back", plate), worn ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (worn) Rim(plate, Slant);
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, PresetHeight), worn ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f)), Slant);

        // The portrait: the character dressed as the preset, standing, on the character cards' own dark fill.
        var card = KitStyle.TopLeft("Portrait", plate, new Vector2(20f, (PresetHeight - PortraitHeight) * .5f), new Vector2(PortraitWidth, PortraitHeight));
        KitStyle.Fill(card, CharacterSkinView.CardFill).raycastTarget = false;
        var portrait = OutfitPresets.Portrait(preset);
        if (portrait != null)
        {
            var picture = KitStyle.Stretch("Picture", card).gameObject.AddComponent<RawImage>();
            picture.texture = portrait;
            picture.raycastTarget = false;
        }

        float textAt = 20f + PortraitWidth + 16f, room = width - textAt - 24f;
        if (naming)
        {
            renameField = NameField(plate, new Vector2(textAt, 8f), room, preset.name, Texts.Get("wardrobe.presets.rename.hint"));
            var field = renameField;
            // Enter or clicking away keeps the new name; Esc keeps the old one. A box the page has already built anew
            // (Destroy lets it go at the frame's end, as if clicked away) is no longer the one being renamed in.
            field.onEndEdit.AddListener(text =>
            {
                if (field != renameField) return;
                bool cancelled = field.wasCanceled;
                soon = () => FinishRename(preset, text, cancelled);
            });
        }
        else
        {
            var name = KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(textAt, 10f), new Vector2(room - (worn ? WornTag + WornGap : 0f), 30f)),
                preset.name, 22f, TextAlignmentOptions.MidlineLeft, worn ? KitStyle.Text : KitStyle.With(KitStyle.Text, .85f));
            name.overflowMode = CombatUIAssets.ClippedOverflow(name);
            if (worn)
            {
                var mark = KitStyle.TopLeft("Worn", plate, new Vector2(textAt + room - WornTag, 13f), new Vector2(WornTag, 24f));
                KitSkew.On(KitStyle.Fill(mark, KitStyle.Accent), 6f);
                KitStyle.Flat(mark, Texts.Get("wardrobe.worn"), 13f, TextAlignmentOptions.Center, KitStyle.Ink);
            }
        }
        string who = skin != null ? skin.DisplayName + ColourOf(skin, preset) : Texts.Format("wardrobe.presets.gone", preset.skin);
        Note(plate, new Vector2(textAt + 2f, 44f), room, who, 13f);

        // 덮어쓰기 · 이름 바꾸기 · 지우기 along the plate's foot at its right; the one awaiting its second press pink.
        float chipY = PresetHeight - ChipHeight - 8f, chipX = width - 22f - (PresetChip * 3f + PresetChipGap * 2f);
        bool askedOver = asking == preset.id && askingWhat == AskOverwrite, askedDelete = asking == preset.id && askingWhat == AskDelete;
        Chip(plate, new Vector2(chipX, chipY), PresetChip,
            Texts.Get(askedOver ? "wardrobe.presets.again" : "wardrobe.presets.overwrite"), askedOver, () => OverwritePreset(preset, shown));
        Chip(plate, new Vector2(chipX + PresetChip + PresetChipGap, chipY), PresetChip, Texts.Get("wardrobe.presets.rename"), naming,
            naming ? null : (System.Action)(() => StartRename(preset)));
        Chip(plate, new Vector2(chipX + (PresetChip + PresetChipGap) * 2f, chipY), PresetChip,
            Texts.Get(askedDelete ? "wardrobe.presets.again" : "wardrobe.presets.delete"), askedDelete, () => DeletePreset(preset));
        Grow(plate, Slant);
        // Being renamed, or its character gone, it isn't worn by a press.
        if (naming || skin == null) return;
        var button = plate.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => WearPreset(preset));
        plate.gameObject.AddComponent<KitHover>();
    }

    // " · 검정" after the character's name, for a character with more than one colour set.
    private static string ColourOf(PlayerBodyProfile.Skin skin, OutfitPreset preset)
    {
        if (skin.Variants.Count < 2) return "";
        var set = skin.VariantOf(preset.colour);
        return set != null ? "  ·  " + set.DisplayName : "";
    }

    // A box to type a preset's name in, the settings' own (a caret that shows, a pink selection), set where the page has
    // it, with a hint while it is empty.
    private static TMP_InputField NameField(RectTransform parent, Vector2 at, float width, string value, string hint)
    {
        var field = SettingsWidgets.TextInput(parent, value ?? "", _ => { });
        var rect = (RectTransform)field.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(at.x, -at.y);
        rect.sizeDelta = new Vector2(width, SaveRow);
        field.characterLimit = OutfitPresets.MostName;
        field.lineType = TMP_InputField.LineType.SingleLine;
        if (field.textViewport != null)
        {
            var placeholder = KitStyle.Flat(KitStyle.Stretch("Hint", field.textViewport), hint, 16f, TextAlignmentOptions.MidlineLeft, KitStyle.Faint);
            placeholder.fontStyle = FontStyles.Normal;
            placeholder.raycastTarget = false;
            field.placeholder = placeholder;
        }
        return field;
    }

    // ---- What the page does ----

    private void WearPreset(OutfitPreset preset)
    {
        renaming = asking = askingWhat = null;
        if (!OutfitPresets.Wear(preset, out int emptied))
        {
            presetLine = Texts.Format("wardrobe.presets.gone", preset.skin);
            KitSound.Play(KitSound.Cue.Refuse, .4f);
            Build();
            return;
        }
        presetLine = emptied > 0 ? Texts.Format("wardrobe.presets.emptied", preset.name, emptied) : Texts.Format("wardrobe.presets.on", preset.name);
        StopTrying();
        if (preview != null) preview.Focus(null);
        // Applied and kept as any pick is: the body, the title screen's character and the other PCs follow.
        Commit();
    }

    private void SaveNew(PlayerBodyProfile.Skin skin)
    {
        if (skin == null) return;
        renaming = asking = askingWhat = null;
        var preset = OutfitPresets.Add(typedName, skin);
        typedName = "";
        presetLine = preset != null ? Texts.Format("wardrobe.presets.saved", preset.name) : null;
        // The new one is at the top: the list starts there.
        presetListKept = false;
        presetScroll = 0f;
        KitSound.Play(KitSound.Cue.Buy, .45f);
        Build();
    }

    private void OverwritePreset(OutfitPreset preset, PlayerBodyProfile.Skin skin)
    {
        if (skin == null || !Asked(preset, AskOverwrite, "wardrobe.presets.overwrite.ask")) return;
        OutfitPresets.Overwrite(preset, skin);
        presetLine = Texts.Format("wardrobe.presets.overwritten", preset.name);
        presetListKept = false;
        presetScroll = 0f;
        KitSound.Play(KitSound.Cue.Buy, .45f);
        Build();
    }

    private void DeletePreset(OutfitPreset preset)
    {
        if (!Asked(preset, AskDelete, "wardrobe.presets.delete.ask")) return;
        OutfitPresets.Remove(preset);
        presetLine = Texts.Format("wardrobe.presets.deleted", preset.name);
        KitSound.Play(KitSound.Cue.Ping, .3f);
        Build();
    }

    // Overwriting and deleting ask once more: the first press turns the chip pink and says what a second press within
    // AskSeconds does (true: this was the second).
    private bool Asked(OutfitPreset preset, string what, string question)
    {
        if (asking == preset.id && askingWhat == what && Time.unscaledTime <= askUntil)
        {
            asking = askingWhat = null;
            return true;
        }
        renaming = null;
        asking = preset.id;
        askingWhat = what;
        askUntil = Time.unscaledTime + AskSeconds;
        presetLine = Texts.Format(question, preset.name);
        KitSound.Play(KitSound.Cue.Ping, .3f);
        Build();
        return false;
    }

    private void StartRename(OutfitPreset preset)
    {
        renaming = preset.id;
        asking = askingWhat = null;
        presetLine = null;
        KitSound.Play(KitSound.Cue.Ping, .3f);
        Build();
    }

    // The box let go: Enter, or a click anywhere else (another plate pressed, ‹ 옷장, the page closed), keeps the name
    // typed; Esc keeps the old one.
    private void FinishRename(OutfitPreset preset, string text, bool cancelled)
    {
        if (renaming == preset.id) renaming = null;
        if (!cancelled) OutfitPresets.Rename(preset, text);
        if (IsOpen && openSlot == PresetPage) Build();
    }

    // ‹ 옷장: straight back to the slots, whatever was being typed or asked.
    private void LeavePresets()
    {
        renaming = asking = askingWhat = presetLine = null;
        typingFrame = -10;
        ShowSlots();
    }

    // Esc on the presets page while a name is typed, a box is open or a second press is awaited: that goes, the page stays
    // (true). TMP's box may have let go of the key already, so a box typed in this frame or the last counts.
    private bool LetGoOfPreset()
    {
        if (openSlot != PresetPage || (!Typing && renaming == null && asking == null)) return false;
        renaming = asking = askingWhat = presetLine = null;
        typingFrame = -10;
        // Esc keeps the old name: let go of here (this Update may come before TMP has seen the Esc), the box is no longer
        // the one being renamed in, so its end of edit is ignored.
        renameField = null;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        Build();
        return true;
    }

    // Once a frame (Update): typing marked, what a box asked for done, a second press that didn't come forgotten.
    private void TickPresets()
    {
        if ((presetName != null && presetName.isFocused) || (renameField != null && renameField.isFocused)) typingFrame = Time.frameCount;
        if (soon != null)
        {
            var now = soon;
            soon = null;
            if (IsOpen) now();
        }
        if (asking != null && Time.unscaledTime > askUntil)
        {
            asking = askingWhat = presetLine = null;
            if (IsOpen && openSlot == PresetPage) Build();
        }
    }
}
