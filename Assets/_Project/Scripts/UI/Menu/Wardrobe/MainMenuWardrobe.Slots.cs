using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MainMenuWardrobe: Right wing: every slot. One part of the class; the rest is in MainMenuWardrobe.cs and the files beside it.
public sealed partial class MainMenuWardrobe
{
    private void BuildSlots(RectTransform right, PlayerBodyProfile.Skin skin)
    {
        Title(right, 0f, Texts.Get("wardrobe.title"), -3f, Texts.Get("wardrobe.slots.where"), new Vector2(4f, 108f), RightWidth);
        var look = OutfitLook.Saved(skin.Id);
        // The slots under the heading, in a list that scrolls once they outgrow the page (KitScroll): the 23 slots ran
        // past the page's foot on a short 21:9 screen (2026-11-13). It keeps its place while the page is built again.
        slotsGrid = KitScroll.Make("Slots", content, new Vector2(1f, 0f), Vector2.one, new Vector2(-(edge + RightWidth), ListFoot),
            new Vector2(-edge, -146f));
        var grid = slotsGrid.Rows;
        float width = TileWidth, y = 0f;
        foreach (string group in Groups)
        {
            var slots = OutfitCatalog.Slots.Where(slot => slot.group == group).ToList();
            if (slots.Count == 0) continue;
            Note(grid, new Vector2(0f, y), 120f, Texts.Get("outfit.group." + group), 14f);
            KitStyle.Bar("Rule", grid, new Vector2(64f, y + 11f), new Vector2(RightWidth - 64f, 1f), KitStyle.Faint);
            y += 22f;
            for (int i = 0; i < slots.Count; i++)
                Tile(grid, skin, slots[i], look, new Vector2(i % 2 * (width + TileGap), y + i / 2 * (TileHeight + TileGap)), width);
            y += Mathf.CeilToInt(slots.Count / 2f) * (TileHeight + TileGap) + 8f;
        }
        slotsGrid.Fit(y);
        slotsGrid.Offset = slotsOffset;
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
        // What is on the slot, as its picture over the tile's right part: the item in its colour, or the character's own
        // piece. The words keep the room they had: the picture's left side fades in under their end (Tile.fade).
        var variant = skin.VariantOf(GameSettings.Current.characterVariant);
        if (item != null) Picture(tile, OutfitPictureLook.Where.Tile, () => OutfitIcons.OfItem(skin, item, pick.Colour));
        else if (own && !pick.IsOff) Picture(tile, OutfitPictureLook.Where.Tile, () => OutfitIcons.OfOwn(skin, slot.id, variant));
        float room = width - 84f;
        if (changed)
        {
            var tag = KitStyle.TopLeft("Changed", tile, new Vector2(width - 112f, 5f), new Vector2(44f, 15f));
            KitSkew.On(KitStyle.Fill(tag, KitStyle.Accent), 4f);
            KitStyle.Flat(tag, Texts.Get("wardrobe.changed"), 11f, TextAlignmentOptions.Center, KitStyle.Ink);
        }
        Note(tile, new Vector2(16f, 4f), room - (changed ? 52f : 0f), slot.Name, 13f);
        var shown = KitStyle.Flat(KitStyle.TopLeft("Worn", tile, new Vector2(16f, 20f), new Vector2(room, 26f)), value, 18f,
            TextAlignmentOptions.MidlineLeft, changed ? KitStyle.Text : KitStyle.With(KitStyle.Text, .7f));
        shown.fontStyle = FontStyles.Normal;
        shown.overflowMode = FontRoles.ClippedOverflow(shown);
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
}
