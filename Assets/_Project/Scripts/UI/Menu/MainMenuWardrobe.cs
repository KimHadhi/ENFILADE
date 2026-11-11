using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title screen's 캐릭터 page: a wardrobe in the lobby's look, the chosen character standing free in the middle
/// (MainMenuPreview; a drag across it turns it). Left wing: the characters as picture cards (SkinPortraits) and the chosen
/// one's colour chips. Right wing: the outfit slots by body part (OutfitSlots.csv) as tiles saying what is worn there.
/// A tile opens its slot: the camera comes close on that part of the body and the wing lists what can go there (the
/// character's own piece, taking it off, every item made for this character), the worn item's colour chips under it.
/// Every pick is worn at once and saved. The user asked for an equipment window with slots by body part, the finer the
/// better, and found the old list running into the bottom bar (2026-11-11); the page stays between the bars.
/// Then (2026-11-11, "의상 슬롯 기능 부위 별로 장착한 옷 뭔지 보이게, 그리고 옷 선택 옵션에서도 투명하게 옷 미리
/// 보기가 있게끔"): a tile names what is on the slot, the character's own pieces too ("기본 · 스타킹"), and the pointer on a
/// tile marks those pieces on the body with a breathing pink rim; resting on a choice tries it on, the item see-through
/// (MainMenuPreview.Try, OutfitGhost), until the pointer leaves or the choice is picked.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuWardrobe : MonoBehaviour
{
    private const float LeftWidth = 520f, RightWidth = 560f, CardGap = 10f, ChipHeight = 30f, ChipGap = 8f, TileHeight = 50f,
        TileGap = 6f, PlateHeight = 60f, PlateGap = 10f, Slant = 12f, ChipSlant = 7f;
    private const int CardColumns = 5, ChipColumns = 4;
    private static readonly string[] Groups = { "head", "neck", "torso", "arms", "legs" };

    private RectTransform root, content;
    private MainMenuPreview preview;
    private KitReveal reveal;
    private float edge, growAt;
    private string openSlot;
    private bool growNext;
    // The choice the pointer rests on, waiting this long before it is tried on, so sweeping over the list doesn't stand
    // the character up again for every plate it crosses (s).
    private const float TryAfter = .12f;
    private OutfitLook wanted;
    private string wantedGhost, wantedText, triedText;
    private float wantedAt;

    public bool IsOpen => root != null && root.gameObject.activeSelf;
    /// <summary>One slot's list is up: Esc goes back to all the slots first.</summary>
    public bool ShowsSlot => IsOpen && openSlot != null;

    public static MainMenuWardrobe Make(RectTransform canvas, float edge, float top, float bottom, MainMenuPreview preview)
    {
        var root = FeelUI.Rect("Wardrobe", canvas);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(0f, bottom);
        root.offsetMax = new Vector2(0f, -top);
        var page = root.gameObject.AddComponent<MainMenuWardrobe>();
        page.root = root;
        page.edge = edge;
        page.preview = preview;
        root.gameObject.SetActive(false);
        return page;
    }

    /// <summary>All the slots, the whole character in view, everything growing in.</summary>
    public void Open()
    {
        openSlot = null;
        growNext = true;
        root.gameObject.SetActive(true);
        if (preview != null) preview.Focus(null);
        Build();
    }

    public void Close()
    {
        if (root == null) return;
        root.gameObject.SetActive(false);
        openSlot = null;
        StopTrying();
        if (preview != null) preview.Focus(null);
    }

    /// <summary>Back from one slot's list to all the slots.</summary>
    public void ShowSlots()
    {
        openSlot = null;
        StopTrying();
        if (preview != null) preview.Focus(null);
        Build();
    }

    private void OpenSlot(OutfitSlot slot)
    {
        openSlot = slot.id;
        StopTrying();
        if (preview != null) preview.Focus(slot.group);
        KitSound.Play(KitSound.Cue.Ping, .35f);
        Build();
    }

    // The page as the settings are now. Opening, it grows in; after a pick it is simply there again, so clicking through
    // the clothes doesn't replay the whole page.
    private void Build()
    {
        if (content != null) Destroy(content.gameObject);
        content = KitStyle.Stretch("Content", root);
        reveal = growNext ? content.gameObject.AddComponent<KitReveal>() : null;
        reveal?.Hide();
        growAt = .04f;
        var profile = PlayerBodyProfile.Load();
        var skins = profile == null ? new List<PlayerBodyProfile.Skin>() : profile.Skins.Where(s => s != null && s.Model != null).ToList();
        var skin = profile != null ? profile.CurrentSkin : null;
        if (skin == null || !skins.Contains(skin)) skin = skins.FirstOrDefault();
        BuildLeft(skins, skin);
        if (skin != null)
        {
            var right = FeelUI.Rect("Right", content);
            right.anchorMin = right.anchorMax = right.pivot = new Vector2(1f, 1f);
            right.anchoredPosition = new Vector2(-edge, 0f);
            right.sizeDelta = new Vector2(RightWidth, 0f);
            if (openSlot != null && OutfitCatalog.Slot(openSlot) is OutfitSlot slot) BuildSlot(right, skin, slot);
            else BuildSlots(right, skin);
        }
        reveal?.Open();
        growNext = false;
    }

    // ---- Left wing: who and which colours ----

    private void BuildLeft(List<PlayerBodyProfile.Skin> skins, PlayerBodyProfile.Skin chosen)
    {
        Title(content, edge, Texts.Get("menu.character.title"), 3f);
        Note(content, new Vector2(edge + 4f, 108f), LeftWidth + 160f, Texts.Get("wardrobe.where"));
        if (chosen == null)
        {
            Note(content, new Vector2(edge, 150f), LeftWidth, Texts.Get("settings.character.none"));
            return;
        }
        SkinPortraits.Ensure();
        float width = Mathf.Floor((LeftWidth - CardGap * (CardColumns - 1)) / CardColumns);
        float picture = width * SkinPortraits.Height / SkinPortraits.Width, height = picture + 28f;
        float y = 150f;
        for (int i = 0; i < skins.Count; i++)
            Card(skins[i], skins[i] == chosen, new Vector2(edge + i % CardColumns * (width + CardGap), y + i / CardColumns * (height + CardGap)),
                new Vector2(width, height), picture);
        y += Mathf.CeilToInt(skins.Count / (float)CardColumns) * (height + CardGap) + 8f;

        Note(content, new Vector2(edge, y), LeftWidth, Texts.Get("wardrobe.colour"));
        y += 28f;
        var worn = chosen.VariantOf(GameSettings.Current.characterVariant);
        var colours = chosen.Variants.Where(v => v != null).ToList();
        float chipWidth = Mathf.Floor((LeftWidth - ChipGap * (ChipColumns - 1)) / ChipColumns);
        if (colours.Count == 0) Chip(content, new Vector2(edge, y), chipWidth, Texts.Get("wardrobe.own"), true, null);
        for (int i = 0; i < colours.Count; i++)
        {
            var colour = colours[i];
            Chip(content, new Vector2(edge + i % ChipColumns * (chipWidth + ChipGap), y + i / ChipColumns * (ChipHeight + ChipGap)), chipWidth,
                colour.DisplayName, colour == worn, colour == worn ? null : (System.Action)(() => PickColour(colour)));
        }
        y += Mathf.Max(1, Mathf.CeilToInt(colours.Count / (float)ChipColumns)) * (ChipHeight + ChipGap) + 16f;

        // Everything back to how the character came, in one press, when anything is changed.
        if (!OutfitLook.Saved(chosen.Id).IsEmpty)
            Chip(content, new Vector2(edge, y), 180f, Texts.Get("wardrobe.reset"), false, () => Save(chosen, new OutfitLook()));
    }

    private void Card(PlayerBodyProfile.Skin skin, bool chosen, Vector2 at, Vector2 size, float picture)
    {
        var card = KitStyle.TopLeft("Card " + skin.Id, content, at, size);
        KitStyle.Fill(card, chosen ? KitStyle.Accent : Color.clear).raycastTarget = true; // its 2 px frame; clear still takes clicks
        var inside = KitStyle.Stretch("Inside", card);
        inside.offsetMin = new Vector2(2f, 2f);
        inside.offsetMax = new Vector2(-2f, -2f);
        var fill = KitStyle.Fill(inside, CharacterSkinView.CardFill);
        var frame = KitStyle.TopLeft("Picture", inside, new Vector2(2f, 2f), new Vector2(size.x - 8f, picture - 6f));
        PortraitImage.Add(frame, skin, skin.Variants.Count > 0 ? skin.Variants[0] : null);
        var name = KitStyle.Flat(KitStyle.TopLeft("Name", inside, new Vector2(0f, picture - 2f), new Vector2(size.x - 4f, 24f)), skin.DisplayName, 15f,
            TextAlignmentOptions.Center, chosen ? KitStyle.Text : KitStyle.Soft);
        name.overflowMode = CombatUIAssets.ClippedOverflow(name);
        Grow(card, 0f);
        if (chosen) return;
        var button = card.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = fill;
        button.onClick.AddListener(() => PickSkin(skin));
        card.gameObject.AddComponent<KitHover>();
    }

    private void PickSkin(PlayerBodyProfile.Skin skin)
    {
        var settings = GameSettings.Current;
        settings.characterSkin = skin.Id;
        var first = skin.Variants.FirstOrDefault(variant => variant != null);
        settings.characterVariant = first != null ? first.Id : "";
        // Another character may have nothing for the slot that was open.
        openSlot = null;
        StopTrying();
        if (preview != null) preview.Focus(null);
        Commit();
    }

    private void PickColour(SkinVariant colour)
    {
        GameSettings.Current.characterVariant = colour.Id;
        Commit();
    }

    // ---- Right wing: every slot ----

    private void BuildSlots(RectTransform right, PlayerBodyProfile.Skin skin)
    {
        Title(right, 0f, Texts.Get("wardrobe.title"), -3f);
        Note(right, new Vector2(4f, 108f), RightWidth, Texts.Get("wardrobe.slots.where"));
        var look = OutfitLook.Saved(skin.Id);
        float width = Mathf.Floor((RightWidth - TileGap) * .5f), y = 146f;
        foreach (string group in Groups)
        {
            var slots = OutfitCatalog.Slots.Where(slot => slot.group == group).ToList();
            if (slots.Count == 0) continue;
            Note(right, new Vector2(0f, y), 120f, Texts.Get("outfit.group." + group), 14f);
            KitStyle.Bar("Rule", right, new Vector2(64f, y + 11f), new Vector2(RightWidth - 64f, 1f), KitStyle.Faint);
            y += 22f;
            for (int i = 0; i < slots.Count; i++)
                Tile(right, skin, slots[i], look, new Vector2(i % 2 * (width + TileGap), y + i / 2 * (TileHeight + TileGap)), width);
            y += Mathf.CeilToInt(slots.Count / 2f) * (TileHeight + TileGap) + 8f;
        }
    }

    // One slot: its name, and what is worn there now (an item and its colour, the character's own piece, nothing).
    private void Tile(RectTransform right, PlayerBodyProfile.Skin skin, OutfitSlot slot, OutfitLook look, Vector2 at, float width)
    {
        var items = OutfitCatalog.ItemsFor(skin.Id, slot.id);
        bool own = OutfitCatalog.HasOwnPiece(skin.Id, slot.id);
        bool usable = items.Count > 0 || (own && slot.removable);
        look.TryGet(slot.id, out var pick);
        var item = items.FirstOrDefault(candidate => candidate.id == pick.Item);
        // The character's own pieces by name, so every tile says what is on it.
        string ownName = own ? OutfitCatalog.OwnName(skin.Id, slot.id) : "";
        string value = item != null ? item.DisplayName + ColourNote(skin, item, pick.Colour)
            : pick.IsOff ? Texts.Get("wardrobe.off.worn")
            : own && ownName.Length > 0 ? Texts.Format("wardrobe.own.named", ownName)
            : Texts.Get(own ? "wardrobe.own" : "wardrobe.none");

        var tile = KitStyle.TopLeft("Slot " + slot.id, right, at, new Vector2(width, TileHeight));
        var back = KitStyle.Fill(KitStyle.Stretch("Back", tile), item != null ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (item != null) Rim(tile, Slant);
        KitSkew.On(KitStyle.Bar("Bar", tile, Vector2.zero, new Vector2(3f, TileHeight), item != null ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .3f)), Slant);
        Note(tile, new Vector2(16f, 4f), width - 24f, slot.Name, 13f);
        var shown = KitStyle.Flat(KitStyle.TopLeft("Worn", tile, new Vector2(16f, 20f), new Vector2(width - 26f, 26f)), value, 18f,
            TextAlignmentOptions.MidlineLeft, item != null ? KitStyle.Text : KitStyle.With(KitStyle.Text, .7f));
        shown.fontStyle = FontStyles.Normal;
        shown.overflowMode = CombatUIAssets.ClippedOverflow(shown);
        Grow(tile, Slant);
        // The pointer on a tile marks its pieces on the body.
        WardrobeHover.On(tile.gameObject, () => { if (preview != null) preview.Highlight(slot.id); },
            () => { if (preview != null) preview.Highlight(null); });
        if (!usable)
        {
            tile.gameObject.AddComponent<CanvasGroup>().alpha = .35f;
            return;
        }
        var button = tile.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => OpenSlot(slot));
        tile.gameObject.AddComponent<KitHover>();
    }

    // ---- Right wing: one slot's choices ----

    private void BuildSlot(RectTransform right, PlayerBodyProfile.Skin skin, OutfitSlot slot)
    {
        Chip(right, new Vector2(0f, 26f), 150f, "‹  " + Texts.Get("wardrobe.back"), false, ShowSlots);
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", right, new Vector2(0f, 66f), new Vector2(RightWidth, 62f)), slot.Name, 50f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
        Grow(title.rectTransform, 0f);
        Note(right, new Vector2(4f, 128f), RightWidth, Texts.Get("outfit.group." + slot.group) + "  ·  " + Texts.Get("wardrobe.try"));

        var items = OutfitCatalog.ItemsFor(skin.Id, slot.id);
        bool own = OutfitCatalog.HasOwnPiece(skin.Id, slot.id);
        OutfitLook.Saved(skin.Id).TryGet(slot.id, out var pick);
        string worn = pick.IsOff && slot.removable && own ? OutfitLook.Off : items.Any(i => i.id == pick.Item) ? pick.Item : "";
        float y = 162f;
        int row = 0;
        void Option(string id, string name, string note)
        {
            var plate = Plate(right, new Vector2(row % 2 == 0 ? 0f : 10f, y), name, note, id == worn, (row % 2 == 0 ? 1f : -1f) * .6f,
                id == worn ? null : (System.Action)(() => Wear(skin, slot, id)));
            // Resting on a choice not worn tries it on: an item see-through, the own piece or bare as they are.
            if (id != worn)
                WardrobeHover.On(plate.gameObject, () => Want(With(skin, slot, id), id.Length > 0 && id != OutfitLook.Off ? id : null),
                    () => Want(null, null));
            y += PlateHeight + PlateGap;
            row++;
        }
        Option("", Texts.Get("wardrobe.own"), Texts.Get(own ? "wardrobe.own.note" : "wardrobe.none.note"));
        if (slot.removable && own) Option(OutfitLook.Off, Texts.Get("wardrobe.off"), Texts.Get("wardrobe.off.note"));
        foreach (var item in items)
        {
            var colours = OutfitCatalog.Model(item.id, skin.Id)?.Variants;
            int count = colours != null ? colours.Count(v => v != null) : 0;
            Option(item.id, item.DisplayName, count > 1 ? Texts.Format("wardrobe.colours", count) : "");
            if (item.id != worn || count < 2) continue;
            // The worn piece's colours, right under it.
            float chipWidth = Mathf.Floor((RightWidth - 20f - ChipGap * (ChipColumns - 1)) / ChipColumns);
            var sets = colours.Where(v => v != null).ToList();
            var current = OutfitCatalog.Model(item.id, skin.Id).VariantOf(pick.Colour);
            for (int i = 0; i < sets.Count; i++)
            {
                var set = sets[i];
                Chip(right, new Vector2(20f + i % ChipColumns * (chipWidth + ChipGap), y + i / ChipColumns * (ChipHeight + ChipGap)), chipWidth,
                    set.DisplayName, set == current, set == current ? null : (System.Action)(() => Wear(skin, slot, item.id, set.Id)));
            }
            y += Mathf.CeilToInt(sets.Count / (float)ChipColumns) * (ChipHeight + ChipGap) + 6f;
        }
    }

    // One choice of a slot as a slanted plate; the worn one dark with a pink edge and a 입음 chip.
    private RectTransform Plate(RectTransform right, Vector2 at, string name, string note, bool worn, float tilt, System.Action picked)
    {
        var plate = KitStyle.TopLeft("Choice " + name, right, at, new Vector2(RightWidth - 10f, PlateHeight));
        plate.localRotation = Quaternion.Euler(0f, 0f, tilt);
        var back = KitStyle.Fill(KitStyle.Stretch("Back", plate), worn ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (worn) Rim(plate, Slant);
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, PlateHeight), worn ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f)), Slant);
        var title = KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(22f, 6f), new Vector2(RightWidth - 150f, 30f)), name, 22f,
            TextAlignmentOptions.MidlineLeft, worn ? KitStyle.Text : KitStyle.With(KitStyle.Text, .85f));
        title.overflowMode = CombatUIAssets.ClippedOverflow(title);
        if (!string.IsNullOrEmpty(note)) Note(plate, new Vector2(24f, 36f), RightWidth - 150f, note, 13f);
        if (worn)
        {
            var mark = KitStyle.TopLeft("Worn", plate, new Vector2(RightWidth - 120f, (PlateHeight - 24f) * .5f), new Vector2(84f, 24f));
            KitSkew.On(KitStyle.Fill(mark, KitStyle.Accent), 6f);
            KitStyle.Flat(mark, Texts.Get("wardrobe.worn"), 13f, TextAlignmentOptions.Center, KitStyle.Ink);
        }
        Grow(plate, Slant);
        if (picked == null) return plate;
        var button = plate.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => picked());
        plate.gameObject.AddComponent<KitHover>();
        return plate;
    }

    // Puts the choice on: one thing per place, so a one-piece empties the bottom, and a bottom put on later takes the
    // one-piece off. A new piece starts in its first colour.
    private void Wear(PlayerBodyProfile.Skin skin, OutfitSlot slot, string item, string colour = "")
    {
        // What was being tried becomes the outfit: the preview lets go of the try and stands the saved one.
        wanted = null;
        wantedText = triedText = wantedGhost = null;
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

    // ---- Trying on: the choice under the pointer, after a short rest ----

    private void Want(OutfitLook look, string ghost)
    {
        wanted = look;
        wantedGhost = ghost;
        wantedText = look != null ? look + "|" + ghost : null;
        wantedAt = Time.unscaledTime + TryAfter;
    }

    private void Update()
    {
        if (preview == null || wantedText == triedText || Time.unscaledTime < wantedAt) return;
        triedText = wantedText;
        preview.Try(wanted, wantedGhost);
    }

    // Back to the saved outfit with nothing marked: leaving a slot's list, the page, or the character.
    private void StopTrying()
    {
        wanted = null;
        wantedText = triedText = wantedGhost = null;
        if (preview == null) return;
        preview.Try(null, null);
        preview.Highlight(null);
    }

    private void Save(PlayerBodyProfile.Skin skin, OutfitLook look)
    {
        OutfitLook.Save(skin.Id, look);
        Commit();
    }

    // Worn and kept at once, as the menu's other choices are: the preview and the body follow GameSettings.Changed.
    private void Commit()
    {
        GameSettings.Apply();
        GameSettings.Save();
        KitSound.Play(KitSound.Cue.Buy, .4f);
        Build();
    }

    // " · 검정" after an item worn in a colour other than its first.
    private static string ColourNote(PlayerBodyProfile.Skin skin, OutfitItem item, string colour)
    {
        var entry = OutfitCatalog.Model(item.id, skin.Id);
        if (entry == null || entry.Variants.Count < 2) return "";
        var set = entry.VariantOf(colour);
        return set != null && set != entry.Variants[0]
            ? "<color=#" + ColorUtility.ToHtmlStringRGBA(KitStyle.Soft) + ">  ·  " + set.DisplayName + "</color>" : "";
    }

    // ---- The kit's pieces as this page uses them ----

    private void Title(RectTransform parent, float x, string text, float tilt)
    {
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", parent, new Vector2(x, 26f), new Vector2(parent == content ? LeftWidth : RightWidth, 80f)),
            text, 64f, TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, tilt);
        Grow(title.rectTransform, 0f);
    }

    private static void Note(RectTransform parent, Vector2 at, float width, string text, float size = 16f)
    {
        var note = KitStyle.Flat(KitStyle.TopLeft("Note", parent, at, new Vector2(width, size + 6f)), text, size, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        note.fontStyle = FontStyles.Normal;
        note.overflowMode = CombatUIAssets.ClippedOverflow(note);
    }

    // A small slanted chip; the chosen one solid pink. Without an action it only shows.
    private void Chip(RectTransform parent, Vector2 at, float width, string text, bool on, System.Action picked)
    {
        var chip = KitStyle.TopLeft("Chip " + text, parent, at, new Vector2(width, ChipHeight));
        var back = KitStyle.Fill(chip, on ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .09f));
        KitSkew.On(back, ChipSlant);
        back.raycastTarget = picked != null;
        var label = KitStyle.Flat(KitStyle.Stretch("Text", chip), text, 14f, TextAlignmentOptions.Center, on ? KitStyle.Ink : KitStyle.Text);
        label.overflowMode = CombatUIAssets.ClippedOverflow(label);
        Grow(chip, ChipSlant);
        if (picked == null) return;
        var button = chip.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => picked());
        chip.gameObject.AddComponent<KitHover>();
    }

    // The pink 2 px edge of the worn piece, leaning as its plate does.
    private static void Rim(RectTransform plate, float slant)
    {
        var rim = KitStyle.Stretch("Edge", plate);
        rim.sizeDelta = new Vector2(2f, 2f);
        rim.SetAsFirstSibling();
        KitSkew.On(KitStyle.Fill(rim, KitStyle.With(KitStyle.Accent, .75f)), slant);
    }

    // Opening, each piece grows in under a pink lid a moment after the one before.
    private void Grow(RectTransform piece, float slant)
    {
        if (reveal == null) return;
        var lid = KitStyle.Fill(KitStyle.Stretch("Flash", piece), KitStyle.Flash);
        if (slant != 0f) KitSkew.On(lid, slant);
        reveal.Card(piece, lid, growAt);
        growAt += .015f;
    }
}
