using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MainMenuWardrobe: Left wing: who and which colours. One part of the class; the rest is in MainMenuWardrobe.cs and the files beside it.
public sealed partial class MainMenuWardrobe
{
    private void BuildLeft(List<PlayerBodyProfile.Skin> skins, PlayerBodyProfile.Skin chosen)
    {
        Title(content, edge, Texts.Get("menu.character.title"), 3f, preview != null ? preview.WhereLine : Texts.Get("wardrobe.where"),
            new Vector2(edge + 4f, 108f), LeftWidth + 160f);
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

        // Kept looks, every character's, worn again in a press (MainMenuWardrobe.Presets).
        PresetPlate(new Vector2(edge, y));
        y += BodyPlateHeight + 18f;

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
        name.overflowMode = FontRoles.ClippedOverflow(name);
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
        // Another character may have nothing for the slot that was open; the presets page stays.
        openSlot = openSlot == PresetPage ? PresetPage : null;
        StopTrying();
        if (preview != null) preview.Focus(null);
        Commit();
    }

    private void PickColour(SkinVariant colour)
    {
        GameSettings.Current.characterVariant = colour.Id;
        Commit();
    }
}
