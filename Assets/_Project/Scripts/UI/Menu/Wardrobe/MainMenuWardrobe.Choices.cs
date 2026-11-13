using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MainMenuWardrobe: Right wing: one slot's choices. One part of the class; the rest is in MainMenuWardrobe.cs and the files beside it.
public sealed partial class MainMenuWardrobe
{
    private void BuildSlot(RectTransform right, PlayerBodyProfile.Skin skin, OutfitSlot slot)
    {
        Chip(right, new Vector2(0f, 26f), 150f, "‹  " + Texts.Get("wardrobe.back"), false, ShowSlots);
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", right, new Vector2(0f, 66f), new Vector2(RightWidth, 62f)), slot.Name, 50f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
        Grow(title.rectTransform, 0f);
        // 사진 다시 찍기 (the user, 2026-11-12: "미리보기 사진 바꾸기 기능 넣어서 내가 직접 인게임에서 … 각도나 위치 조절해서
        // 찍어서"): on, a choice's plate opens the shooting window for its piece (OutfitShootView).
        Chip(right, new Vector2(RightWidth - 170f, 26f), 170f, Texts.Get("wardrobe.shoot"), shooting, ToggleShoot);
        bool tries = preview != null && preview.CanTry && !shooting;
        Note(right, new Vector2(4f, 128f), RightWidth, Texts.Get("outfit.group." + slot.group) + "  ·  "
            + Texts.Get(shooting ? "wardrobe.shoot.on" : tries ? "wardrobe.try" : "wardrobe.click"));

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
        void Option(string id, string name, string note, System.Func<Texture> picture, OutfitItem piece = null, string colour = "")
        {
            var pressed = id == worn ? null : (System.Action)(() => Wear(skin, slot, id));
            // Taking pictures again, a choice with a picture opens the shooting window for its piece (the worn one too) in
            // the colour its plate shows; one with none (벗기, nothing of its own) does nothing.
            if (shooting) pressed = picture == null ? null : (System.Action)(() => OpenShoot(skin, piece, slot.id, colour, name));
            var plate = Plate(list, new Vector2(row % 2 == 0 ? 0f : 10f, y), name, note, id == worn, (row % 2 == 0 ? 1f : -1f) * .6f,
                pressed, picture);
            // Resting on a choice not worn tries it on as it would really look. Taking off (벗기, or back to nothing of
            // its own) keeps what is on and shows it fading, so a try of taking off never reads as already off. A stage
            // that can't try (the hub's mirror) wears the choice on the click alone.
            if (id != worn && tries)
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
            own ? () => OutfitIcons.OfOwn(skin, slot.id, variant) : null, null, variant != null ? variant.Id : "");
        if (slot.removable && own) Option(OutfitLook.Off, Texts.Get("wardrobe.off"), Texts.Get("wardrobe.off.note"), null);
        foreach (var item in items)
        {
            // A chip per look the piece can take: an outfit's colour sets recolour all of it, so on one piece many look
            // alike (OutfitCatalog.ColoursOf).
            var sets = OutfitCatalog.ColoursOf(item, skin.Id);
            int count = sets.Count;
            string colour = item.id == worn ? pick.Colour : "";
            Option(item.id, item.DisplayName, count > 1 ? Texts.Format("wardrobe.colours", count) : "", () => OutfitIcons.OfItem(skin, item, colour),
                item, colour);
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

    // One choice of a slot as a slanted plate, the piece's picture big over its right part (cut by the plate's lean); the
    // worn one dark with a pink edge and a 입음 chip. The words and the chip keep left of the picture.
    private RectTransform Plate(RectTransform right, Vector2 at, string name, string note, bool worn, float tilt, System.Action picked,
        System.Func<Texture> picture)
    {
        float width = PlateWidth;
        var plate = KitStyle.TopLeft("Choice " + name, right, at, new Vector2(width, PlateHeight));
        plate.localRotation = Quaternion.Euler(0f, 0f, tilt);
        var back = KitStyle.Fill(KitStyle.Stretch("Back", plate), worn ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (worn) Rim(plate, Slant);
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, PlateHeight), worn ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f)), Slant);
        var image = Picture(plate, OutfitPictureLook.Where.Plate, picture);
        float clear = Mathf.Clamp(OutfitPictureLook.LeftOf(new Vector2(width, PlateHeight), OutfitPictureLook.Of(OutfitPictureLook.Where.Plate)),
            200f, width - 20f);
        // Taking pictures again, the part of the plate its picture shows sits in the viewfinder's pink corners: what a
        // press would take again.
        if (image != null && shooting && picked != null)
        {
            var window = KitStyle.TopLeft("Window", plate, new Vector2(clear + 4f, 4f), new Vector2(width - clear - 14f, PlateHeight - 8f));
            foreach (var corner in Corners) Bracket(window, corner, 14f, 2f);
        }
        float tagAt = clear - WornGap - WornTag, room = (worn ? tagAt : clear) - 22f - 10f;
        var title = KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(22f, 10f), new Vector2(room, 30f)), name, 22f,
            TextAlignmentOptions.MidlineLeft, worn ? KitStyle.Text : KitStyle.With(KitStyle.Text, .85f));
        title.overflowMode = FontRoles.ClippedOverflow(title);
        if (!string.IsNullOrEmpty(note)) Note(plate, new Vector2(24f, 42f), room, note, 13f);
        if (worn)
        {
            var mark = KitStyle.TopLeft("Worn", plate, new Vector2(tagAt, (PlateHeight - 24f) * .5f), new Vector2(WornTag, 24f));
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
}
