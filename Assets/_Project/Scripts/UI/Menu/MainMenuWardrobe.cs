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
/// tile marks those pieces on the body with a breathing pink rim; resting on a choice tries it on (MainMenuPreview.Try,
/// OutfitGhost), until the pointer leaves or the choice is picked.
/// Then (2026-11-12): the see-through try read as something appearing, not a preview, a pretty piece looked plain that
/// way, and resting on 벗기 looked already taken off. A try is now worn as it really looks under a quick pink rim, inside
/// a pulsing pink viewfinder whose 미리 보기 plate says what a click would do; a removal keeps the piece on, fading. Every
/// plate and tile carries a see-through picture of its piece at its right end (OutfitIcons), and a slot changed from how
/// the character came is dark with a pink edge and a 바꿈 tag ("텍스트로만 돼 있어서 내가 뭘 바꿨고 뭘 입고 있는지
/// 직관적으로 눈에 안 띄어. 사진이 필요해").
/// A character whose model can be shaped (Mayo, Ichigo: BodyShapes.csv) has a 체형 plate under its colours. It opens the
/// body page in the right wing: every look the model offers, tab by tab (body, face, eyes, brows and lashes, ears, hair,
/// animal ears and tail, outfit details), as sliders and chips that shape the character at once, the camera close on
/// the face or eyes for their tabs (the user, 2026-11-12, asked for all of each character's own customization).
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
    private string wantedSlot, wantedText, triedText, wantedLine;
    private bool wantedRemoves;
    private float wantedAt;
    // The badge over the character while a choice is tried on, so a try never reads as worn (user, 2026-11-12: a try
    // didn't feel like a preview, and 벗기 looked already taken off), and what it says the click would do.
    private RectTransform badge;
    private CanvasGroup badgeGroup;
    private TextMeshProUGUI badgeLine;
    private string shownLine;
    // The viewfinder: how far inside the wings and the bars it stands, and its corner brackets (px).
    private const float FrameGap = 40f, FrameInset = 14f, BracketLength = 46f, BracketWidth = 4f;
    // Pictures (OutfitIcons) not taken yet when their plate or tile was made; they go in as they come.
    private readonly List<(RawImage image, System.Func<Texture> picture)> pictures = new List<(RawImage, System.Func<Texture>)>();
    // A picture's box: on a choice's plate and on a tile, at their right ends (px), and how see-through it lies.
    private const float PlatePicture = 58f, TilePicture = 44f, PictureAlpha = .9f;

    // The body page (BodyShapes) stands where a slot's list would, under this name in openSlot, on one of its tabs. A save
    // there rebuilds nothing (a slider may be held, the list scrolled): its marks, the 원래대로 chip and the plate's count
    // follow instead.
    private const string BodyPage = "#body";
    private string bodyTab;
    private TextMeshProUGUI bodyCount;
    private RectTransform bodyReset;
    private readonly Dictionary<BodyShapes.Option, Image> bodyMarks = new Dictionary<BodyShapes.Option, Image>();
    // The body plate, the tabs (four to a line), a row's name column, the gap under each row, a choice chip's height, a
    // slider's number at its right, where the list stops above the page's foot and how far one wheel step scrolls it (px).
    private const float BodyPlateHeight = 62f, TabHeight = 30f, TabGap = 6f, RowName = 176f, RowGap = 8f, OptionChip = 28f,
        ValueWidth = 46f, ListFoot = 16f, ScrollStep = 40f;
    private const int TabColumns = 4;
    // The open slot's choices scroll. A pick builds the page again and the list comes back where it was scrolled to;
    // opening a slot, or going back to all of them, starts it at the top.
    private RectTransform slotList;
    private string slotListFor;
    private float slotScroll;

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
        slotListFor = null;
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
        slotListFor = null;
        StopTrying();
        if (preview != null) preview.Focus(null);
        Build();
    }

    private void OpenSlot(OutfitSlot slot)
    {
        openSlot = slot.id;
        slotListFor = null;
        StopTrying();
        if (preview != null) preview.Focus(slot.group);
        KitSound.Play(KitSound.Cue.Ping, .35f);
        Build();
    }

    // The page as the settings are now. Opening, it grows in; after a pick it is simply there again, so clicking through
    // the clothes doesn't replay the whole page.
    private void OnEnable() => OutfitIcons.Taken += FillPictures;

    private void OnDisable() => OutfitIcons.Taken -= FillPictures;

    private void Build()
    {
        if (slotList != null) slotScroll = slotList.anchoredPosition.y;
        slotList = null;
        if (content != null) Destroy(content.gameObject);
        pictures.Clear();
        bodyMarks.Clear();
        bodyCount = null;
        bodyReset = null;
        content = KitStyle.Stretch("Content", root);
        reveal = growNext ? content.gameObject.AddComponent<KitReveal>() : null;
        reveal?.Hide();
        growAt = .04f;
        var profile = PlayerBodyProfile.Load();
        var skins = profile == null ? new List<PlayerBodyProfile.Skin>() : profile.Skins.Where(s => s != null && s.Model != null).ToList();
        var skin = profile != null ? profile.CurrentSkin : null;
        if (skin == null || !skins.Contains(skin)) skin = skins.FirstOrDefault();
        // A character whose model has no body options shows the slots instead.
        if (openSlot == BodyPage && (skin == null || BodyShapes.For(skin.Id).Count == 0)) openSlot = null;
        BuildLeft(skins, skin);
        if (skin != null)
        {
            var right = FeelUI.Rect("Right", content);
            right.anchorMin = right.anchorMax = right.pivot = new Vector2(1f, 1f);
            right.anchoredPosition = new Vector2(-edge, 0f);
            right.sizeDelta = new Vector2(RightWidth, 0f);
            if (openSlot == BodyPage) BuildBody(right, skin);
            else if (openSlot != null && OutfitCatalog.Slot(openSlot) is OutfitSlot slot) BuildSlot(right, skin, slot);
            else BuildSlots(right, skin);
        }
        BuildBadge();
        reveal?.Open();
        growNext = false;
    }

    // While a choice is tried on, a pink viewfinder stands around the middle of the page where the character is, as a
    // camera's would: its 미리 보기 plate at the top left, clear of the head, and under it what a click would do. Hidden
    // until a try. A plate over the head hid the very hat being tried.
    private void BuildBadge()
    {
        badge = KitStyle.Stretch("Preview Frame", content);
        badge.offsetMin = new Vector2(edge + LeftWidth + FrameGap, FrameInset);
        badge.offsetMax = new Vector2(-(edge + RightWidth + FrameGap), -FrameInset);
        badgeGroup = badge.gameObject.AddComponent<CanvasGroup>();
        badgeGroup.interactable = badgeGroup.blocksRaycasts = false;
        foreach (var corner in new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) })
            Bracket(badge, corner);
        var plate = KitStyle.TopLeft("Plate", badge, new Vector2(18f, 16f), new Vector2(190f, 36f));
        KitSkew.On(KitStyle.Fill(plate, KitStyle.Accent), Slant);
        KitStyle.Flat(plate, Texts.Get("wardrobe.preview"), 22f, TextAlignmentOptions.Center, KitStyle.Ink);
        badgeLine = KitStyle.Flat(KitStyle.TopLeft("Line", badge, new Vector2(22f, 58f), new Vector2(380f, 22f)), "", 15f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Text);
        badgeLine.fontStyle = FontStyles.Normal;
        shownLine = null;
        badge.gameObject.SetActive(false);
    }

    // One corner of the viewfinder: two pink bars meeting at it.
    private static void Bracket(RectTransform frame, Vector2 corner)
    {
        for (int i = 0; i < 2; i++)
        {
            var bar = FeelUI.Rect("Bracket", frame);
            bar.anchorMin = bar.anchorMax = bar.pivot = corner;
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = i == 0 ? new Vector2(BracketLength, BracketWidth) : new Vector2(BracketWidth, BracketLength);
            KitStyle.Fill(bar, KitStyle.Accent);
        }
    }

    // Pictures that have come in since their plates and tiles were made.
    private void FillPictures()
    {
        for (int i = pictures.Count - 1; i >= 0; i--)
        {
            var (image, picture) = pictures[i];
            var texture = image != null ? picture() : null;
            if (image != null && texture == null) continue;
            if (image != null)
            {
                image.texture = texture;
                image.enabled = true;
            }
            pictures.RemoveAt(i);
        }
    }

    // A picture laid see-through at the right of a plate or tile: shown at once when taken, else as soon as it is.
    private void Picture(RectTransform parent, Vector2 at, float size, System.Func<Texture> picture)
    {
        if (picture == null) return;
        var box = KitStyle.TopLeft("Picture", parent, at, new Vector2(size, size));
        var image = box.gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.color = KitStyle.With(Color.white, PictureAlpha);
        var texture = picture();
        image.texture = texture;
        image.enabled = texture != null;
        if (texture == null) pictures.Add((image, picture));
    }

    // ---- Left wing: who and which colours ----

    private void BuildLeft(List<PlayerBodyProfile.Skin> skins, PlayerBodyProfile.Skin chosen)
    {
        Title(content, edge, Texts.Get("menu.character.title"), 3f, Texts.Get("wardrobe.where"), new Vector2(edge + 4f, 108f), LeftWidth + 160f);
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

        // The way to the body page, for a character whose model can be shaped.
        if (BodyShapes.For(chosen.Id).Count > 0)
        {
            BodyPlate(chosen, new Vector2(edge, y));
            y += BodyPlateHeight + 18f;
        }

        // Every piece back to how the character came, in one press, when any is changed. The body options stay: the body
        // page has its own 원래대로.
        if (!BodyShapes.Clothes(OutfitLook.Saved(chosen.Id)).IsEmpty)
            Chip(content, new Vector2(edge, y), 180f, Texts.Get("wardrobe.reset"), false,
                () => Save(chosen, BodyShapes.Shapes(OutfitLook.Saved(chosen.Id))));
    }

    // The body page's plate: the character's own looks (BodyShapes.csv), how many there are and how many are changed.
    private void BodyPlate(PlayerBodyProfile.Skin skin, Vector2 at)
    {
        bool open = openSlot == BodyPage;
        var plate = KitStyle.TopLeft("Body", content, at, new Vector2(LeftWidth, BodyPlateHeight));
        var back = KitStyle.Fill(KitStyle.Stretch("Back", plate), open ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (open) Rim(plate, Slant);
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, BodyPlateHeight), KitStyle.Accent), Slant);
        KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(22f, 4f), new Vector2(260f, 32f)), Texts.Get("body.open"), 24f,
            TextAlignmentOptions.MidlineLeft);
        bodyCount = Note(plate, new Vector2(24f, 36f), LeftWidth - 90f, BodyCount(skin, OutfitLook.Saved(skin.Id)), 14f);
        KitStyle.Flat(KitStyle.TopLeft("Arrow", plate, new Vector2(LeftWidth - 52f, 0f), new Vector2(32f, BodyPlateHeight)), "›", 34f,
            TextAlignmentOptions.Center, KitStyle.Accent);
        Grow(plate, Slant);
        if (open) return;
        var button = plate.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(OpenBody);
        plate.gameObject.AddComponent<KitHover>();
    }

    private static string BodyCount(PlayerBodyProfile.Skin skin, OutfitLook look) =>
        Texts.Format("body.open.note", BodyShapes.For(skin.Id).Count, BodyShapes.Changed(look, skin.Id));

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
        Title(right, 0f, Texts.Get("wardrobe.title"), -3f, Texts.Get("wardrobe.slots.where"), new Vector2(4f, 108f), RightWidth);
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

        // Changed from how the character came (an item on, or its own piece off): dark with a pink edge and a 바꿈 tag, so
        // what was changed stands out among the tiles at a glance (user, 2026-11-12).
        bool changed = item != null || (pick.IsOff && own);
        var tile = KitStyle.TopLeft("Slot " + slot.id, right, at, new Vector2(width, TileHeight));
        var back = KitStyle.Fill(KitStyle.Stretch("Back", tile), changed ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (changed) Rim(tile, Slant);
        KitSkew.On(KitStyle.Bar("Bar", tile, Vector2.zero, new Vector2(3f, TileHeight), changed ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .3f)), Slant);
        // What is on the slot, as a picture at its right end: the item in its colour, or the character's own piece.
        var variant = skin.VariantOf(GameSettings.Current.characterVariant);
        if (item != null) Picture(tile, new Vector2(width - TilePicture - 8f, (TileHeight - TilePicture) * .5f), TilePicture,
            () => OutfitIcons.OfItem(skin, item, pick.Colour));
        else if (own && !pick.IsOff) Picture(tile, new Vector2(width - TilePicture - 8f, (TileHeight - TilePicture) * .5f), TilePicture,
            () => OutfitIcons.OfOwn(skin, slot.id, variant));
        float room = width - 26f - TilePicture - 8f;
        if (changed)
        {
            var tag = KitStyle.TopLeft("Changed", tile, new Vector2(width - TilePicture - 62f, 5f), new Vector2(44f, 15f));
            KitSkew.On(KitStyle.Fill(tag, KitStyle.Accent), 4f);
            KitStyle.Flat(tag, Texts.Get("wardrobe.changed"), 11f, TextAlignmentOptions.Center, KitStyle.Ink);
        }
        Note(tile, new Vector2(16f, 4f), room - (changed ? 52f : 0f), slot.Name, 13f);
        var shown = KitStyle.Flat(KitStyle.TopLeft("Worn", tile, new Vector2(16f, 20f), new Vector2(room, 26f)), value, 18f,
            TextAlignmentOptions.MidlineLeft, changed ? KitStyle.Text : KitStyle.With(KitStyle.Text, .7f));
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
        // The choices in a list the wheel scrolls, from under the note to the page's foot: one outfit brought sixty colour
        // sets (Sera Yura, 2026-11-12), and the worn piece's chips ran fifteen rows off the page.
        var list = ScrollView("Slot Choices", 162f);
        float y = 4f;
        int row = 0;
        var variant = skin.VariantOf(GameSettings.Current.characterVariant);
        var saved = OutfitLook.Saved(skin.Id);
        void Option(string id, string name, string note, System.Func<Texture> picture)
        {
            var plate = Plate(list, new Vector2(row % 2 == 0 ? 0f : 10f, y), name, note, id == worn, (row % 2 == 0 ? 1f : -1f) * .6f,
                id == worn ? null : (System.Action)(() => Wear(skin, slot, id)), picture);
            // Resting on a choice not worn tries it on as it would really look. Taking off (벗기, or back to nothing of
            // its own) keeps what is on and shows it fading, so a try of taking off never reads as already off.
            if (id != worn)
            {
                bool removes = id == OutfitLook.Off || (id.Length == 0 && !own);
                string line = Texts.Get(removes ? "wardrobe.preview.off" : id.Length == 0 ? "wardrobe.preview.own" : "wardrobe.preview.wear");
                WardrobeHover.On(plate.gameObject, () => Want(removes ? saved : With(skin, slot, id), slot.id, removes, line),
                    () => Want(null, null, false, null));
            }
            y += PlateHeight + PlateGap;
            row++;
        }
        Option("", Texts.Get("wardrobe.own"), Texts.Get(own ? "wardrobe.own.note" : "wardrobe.none.note"),
            own ? () => OutfitIcons.OfOwn(skin, slot.id, variant) : null);
        if (slot.removable && own) Option(OutfitLook.Off, Texts.Get("wardrobe.off"), Texts.Get("wardrobe.off.note"), null);
        foreach (var item in items)
        {
            // A chip per look the piece can take: an outfit's colour sets recolour all of it, so on one piece many look
            // alike (OutfitCatalog.ColoursOf).
            var sets = OutfitCatalog.ColoursOf(item, skin.Id);
            int count = sets.Count;
            string colour = item.id == worn ? pick.Colour : "";
            Option(item.id, item.DisplayName, count > 1 ? Texts.Format("wardrobe.colours", count) : "", () => OutfitIcons.OfItem(skin, item, colour));
            if (item.id != worn || count < 2) continue;
            // The worn piece's colours, right under it.
            float chipWidth = Mathf.Floor((RightWidth - 20f - ChipGap * (ChipColumns - 1)) / ChipColumns);
            var current = OutfitCatalog.ShownColour(item, skin.Id, pick.Colour);
            for (int i = 0; i < sets.Count; i++)
            {
                var set = sets[i];
                Chip(list, new Vector2(20f + i % ChipColumns * (chipWidth + ChipGap), y + i / ChipColumns * (ChipHeight + ChipGap)), chipWidth,
                    set.DisplayName, set == current, set == current ? null : (System.Action)(() => Wear(skin, slot, item.id, set.Id)));
            }
            y += Mathf.CeilToInt(sets.Count / (float)ChipColumns) * (ChipHeight + ChipGap) + 6f;
        }
        Scroll(list, y);
        // A pick rebuilds the page: the list comes back where it was scrolled to (Build).
        if (slotListFor == slot.id)
        {
            float room = Mathf.Max(0f, y - ((RectTransform)list.parent).rect.height);
            list.anchoredPosition = new Vector2(0f, Mathf.Clamp(slotScroll, 0f, room));
        }
        slotList = list;
        slotListFor = slot.id;
    }

    // One choice of a slot as a slanted plate, a picture of the piece at its right end; the worn one dark with a pink
    // edge and a 입음 chip.
    private RectTransform Plate(RectTransform right, Vector2 at, string name, string note, bool worn, float tilt, System.Action picked,
        System.Func<Texture> picture)
    {
        float width = RightWidth - 10f;
        var plate = KitStyle.TopLeft("Choice " + name, right, at, new Vector2(width, PlateHeight));
        plate.localRotation = Quaternion.Euler(0f, 0f, tilt);
        var back = KitStyle.Fill(KitStyle.Stretch("Back", plate), worn ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (worn) Rim(plate, Slant);
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, PlateHeight), worn ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f)), Slant);
        Picture(plate, new Vector2(width - PlatePicture - 14f, (PlateHeight - PlatePicture) * .5f), PlatePicture, picture);
        float room = width - PlatePicture - 150f;
        var title = KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(22f, 6f), new Vector2(room, 30f)), name, 22f,
            TextAlignmentOptions.MidlineLeft, worn ? KitStyle.Text : KitStyle.With(KitStyle.Text, .85f));
        title.overflowMode = CombatUIAssets.ClippedOverflow(title);
        if (!string.IsNullOrEmpty(note)) Note(plate, new Vector2(24f, 36f), room, note, 13f);
        if (worn)
        {
            var mark = KitStyle.TopLeft("Worn", plate, new Vector2(width - PlatePicture - 112f, (PlateHeight - 24f) * .5f), new Vector2(84f, 24f));
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

    // ---- Right wing: the body page ----

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
        name.overflowMode = CombatUIAssets.ClippedOverflow(name);
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
        // The tick goes in first, under the slider's handle (the handle runs 7 px inside each end of the track).
        float place = Mathf.InverseLerp(option.Min, option.Max, own);
        KitStyle.Fill(KitStyle.TopLeft("Own", row, new Vector2(left + 7f + place * (width - 14f) - 1f, 8f), new Vector2(2f, 20f)),
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
            labels[i].overflowMode = CombatUIAssets.ClippedOverflow(labels[i]);
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

    // ---- Trying on: the choice under the pointer, after a short rest ----

    // The choice under the pointer: the look it would make, its slot, whether it takes the slot's pieces off, and what
    // the badge says a click does.
    private void Want(OutfitLook look, string slot, bool removes, string line)
    {
        wanted = look;
        wantedSlot = slot;
        wantedRemoves = removes;
        wantedLine = line;
        wantedText = look != null ? look + "|" + slot + "|" + removes : null;
        wantedAt = Time.unscaledTime + TryAfter;
    }

    private void Update()
    {
        if (preview != null && wantedText != triedText && Time.unscaledTime >= wantedAt)
        {
            triedText = wantedText;
            preview.Try(wanted, wantedSlot, wantedRemoves);
        }
        ShowBadge();
    }

    // The badge is up while a try is on the character, saying what a click would do, gently beating.
    private void ShowBadge()
    {
        if (badge == null) return;
        bool trying = triedText != null;
        if (badge.gameObject.activeSelf != trying) badge.gameObject.SetActive(trying);
        if (!trying) return;
        if (shownLine != wantedLine)
        {
            shownLine = wantedLine;
            badgeLine.text = wantedLine ?? "";
        }
        badgeGroup.alpha = .82f + .18f * Mathf.Sin(Time.unscaledTime * 5f);
    }

    // Back to the saved outfit with nothing marked: leaving a slot's list, the page, or the character.
    private void StopTrying()
    {
        wanted = null;
        wantedText = triedText = wantedSlot = wantedLine = null;
        wantedRemoves = false;
        if (preview == null) return;
        preview.Try(null, null, false);
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

    // A page's big title, and the line under it, which shows only once the title's pink has drained.
    private void Title(RectTransform parent, float x, string text, float tilt, string under, Vector2 underAt, float underWidth)
    {
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", parent, new Vector2(x, 26f), new Vector2(parent == content ? LeftWidth : RightWidth, 80f)),
            text, 64f, TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, tilt);
        float at = growAt;
        Grow(title.rectTransform, 0f);
        var line = Note(parent, underAt, underWidth, under);
        if (reveal == null) return;
        // Shown at once, the line lay under the title's tilted pink while it opened (the user, 2026-11-12: "분홍색
        // 네모칸이랑 저 아래 글자랑 겹쳐서 … 딜레이 줘서"): it grows in from its left after, with no pink of its own.
        var clear = KitStyle.Fill(KitStyle.Stretch("Flash", line.rectTransform), Color.clear);
        reveal.Card(line.rectTransform, clear, at + KitReveal.GrowSeconds + KitReveal.FlashHold + KitReveal.DrainSeconds);
    }

    private static TextMeshProUGUI Note(RectTransform parent, Vector2 at, float width, string text, float size = 16f)
    {
        var note = KitStyle.Flat(KitStyle.TopLeft("Note", parent, at, new Vector2(width, size + 6f)), text, size, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        note.fontStyle = FontStyles.Normal;
        note.overflowMode = CombatUIAssets.ClippedOverflow(note);
        return note;
    }

    // A small slanted chip; the chosen one solid pink. Without an action it only shows.
    private RectTransform Chip(RectTransform parent, Vector2 at, float width, string text, bool on, System.Action picked)
    {
        var chip = KitStyle.TopLeft("Chip " + text, parent, at, new Vector2(width, ChipHeight));
        var back = KitStyle.Fill(chip, on ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .09f));
        KitSkew.On(back, ChipSlant);
        back.raycastTarget = picked != null;
        var label = KitStyle.Flat(KitStyle.Stretch("Text", chip), text, 14f, TextAlignmentOptions.Center, on ? KitStyle.Ink : KitStyle.Text);
        label.overflowMode = CombatUIAssets.ClippedOverflow(label);
        Grow(chip, ChipSlant);
        if (picked == null) return chip;
        var button = chip.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => picked());
        chip.gameObject.AddComponent<KitHover>();
        return chip;
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
