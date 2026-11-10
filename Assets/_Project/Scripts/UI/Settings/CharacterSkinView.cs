using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The character screen, as in the card mockup the user picked on 2026-11-05: character cards on top, the chosen
/// character's colour cards under them, and the chosen look large beside them. The Esc menu's character tab shows that
/// stage as a picture; the main menu leaves it out, since its 3D preview stands beside the screen (CharacterScreenView).
/// Picking writes characterSkin and characterVariant in <see cref="GameSettings"/>; another character starts on its
/// first colour set. Pictures come from <see cref="SkinPortraits"/>.
/// </summary>
public sealed class CharacterSkinView
{
    /// <summary>The card colour. Opaque, because the pictures are taken on it and have to meet it without a seam.</summary>
    public static readonly Color CardFill = new Color(.12f, .13f, .155f);
    private const float Gap = 12f, Border = 2f, Pad = 4f, StageShare = .33f;
    private const int CharacterColumns = 3, ColourColumns = 4;
    private static readonly float Aspect = (float)SkinPortraits.Width / SkinPortraits.Height;
    private readonly RectTransform root;
    private readonly float width;
    private readonly bool stage;
    private readonly Action changed;

    /// <param name="root">A vertical layout the screen fills.</param>
    /// <param name="width">The width it has, so the cards can be sized to the pictures.</param>
    /// <param name="stage">Show the chosen look large on the right.</param>
    /// <param name="changed">Called after a pick has been applied.</param>
    public CharacterSkinView(RectTransform root, float width, bool stage, Action changed = null)
    {
        this.root = root;
        this.width = width;
        this.stage = stage;
        this.changed = changed;
        Build();
    }

    private void Build()
    {
        W.Clear(root);
        var profile = PlayerBodyProfile.Load();
        var skins = profile == null
            ? Array.Empty<PlayerBodyProfile.Skin>()
            : profile.Skins.Where(skin => skin != null && skin.Model != null).ToArray();
        if (skins.Length == 0)
        {
            W.Size(W.Label(root, Texts.Get("settings.character.none"), 20f, W.Muted), height: 40f);
            return;
        }
        // Started straight in a level, no main menu has taken the pictures yet.
        SkinPortraits.Ensure();
        var chosen = Array.IndexOf(skins, profile.CurrentSkin) >= 0 ? profile.CurrentSkin : skins[0];
        var worn = chosen.VariantOf(GameSettings.Current.characterVariant);

        var body = W.Rect("Character", root);
        var layout = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = Gap * 2f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        W.Size(body, flexibleWidth: 1f, flexibleHeight: 0f);
        float stageWidth = stage ? Mathf.Round(width * StageShare) : 0f;
        float cardsWidth = stage ? width - stageWidth - layout.spacing : width;

        var cards = Column(body, cardsWidth);
        var characters = Grid(cards, cardsWidth, CharacterColumns, 20f);
        foreach (var skin in skins)
        {
            var pick = skin;
            // A character's card shows its first colour set, whichever one is worn.
            var first = skin.Variants.Count > 0 ? skin.Variants[0] : null;
            Card(characters, skin.DisplayName, 20f, skin, first, skin == chosen, () => { if (pick != chosen) PickSkin(pick); });
        }

        int count = chosen.Variants.Count(variant => variant != null);
        W.Size(W.Label(cards, count > 1 ? Texts.Format("settings.character.colors", count) : Texts.Get("settings.character.colors.one"), 18f,
            W.Muted), height: 34f);
        var colours = Grid(cards, cardsWidth, ColourColumns, 17f);
        if (count == 0) Card(colours, Texts.Get("settings.character.color.default"), 17f, chosen, null, true, null);
        foreach (var variant in chosen.Variants)
        {
            if (variant == null) continue;
            var pick = variant;
            Card(colours, variant.ShortName, 17f, chosen, variant, variant == worn, () => { if (pick != worn) PickVariant(pick); });
        }

        if (stage) Stage(body, stageWidth, chosen, worn);
    }

    private void PickSkin(PlayerBodyProfile.Skin skin)
    {
        var settings = GameSettings.Current;
        settings.characterSkin = skin.Id;
        var first = skin.Variants.FirstOrDefault(variant => variant != null);
        settings.characterVariant = first != null ? first.Id : "";
        Commit();
    }

    private void PickVariant(SkinVariant variant)
    {
        GameSettings.Current.characterVariant = variant.Id;
        Commit();
    }

    private void Commit()
    {
        GameSettings.Apply();
        changed?.Invoke();
        Build();
    }

    private static RectTransform Column(Transform parent, float width)
    {
        var column = W.Rect("Column", parent);
        var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        W.Size(column, width, flexibleWidth: 0f, flexibleHeight: 0f);
        return column;
    }

    private static float LabelStrip(float labelSize) => labelSize + 14f;

    // Cells sized so a picture fills a card's width at its own aspect, with the name under it.
    private static RectTransform Grid(Transform parent, float width, int columns, float labelSize)
    {
        var grid = W.Rect("Cards", parent);
        var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        float cell = Mathf.Floor((width - Gap * (columns - 1)) / columns);
        float picture = (cell - (Border + Pad) * 2f) / Aspect;
        layout.cellSize = new Vector2(cell, Border * 2f + Pad + picture + LabelStrip(labelSize));
        layout.spacing = new Vector2(Gap, Gap);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = columns;
        return grid;
    }

    private static void Card(Transform grid, string name, float labelSize, PlayerBodyProfile.Skin skin, SkinVariant variant,
        bool on, Action clicked)
    {
        var card = W.Rect(name, grid);
        W.Fill(card, on ? W.Accent : Color.clear); // the chosen card's 2 px frame; clear still takes clicks
        var inside = W.Rect("Inside", card);
        W.Stretch(inside);
        inside.offsetMin = new Vector2(Border, Border);
        inside.offsetMax = new Vector2(-Border, -Border);
        var fill = W.Fill(inside, CardFill);

        float strip = LabelStrip(labelSize);
        var area = W.Rect("Picture Area", inside);
        W.Stretch(area);
        area.offsetMin = new Vector2(Pad, strip);
        area.offsetMax = new Vector2(-Pad, -Pad);
        var picture = W.Rect("Picture", area);
        var fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = Aspect;
        PortraitImage.Add(picture, skin, variant);

        var label = W.Label(inside, name, labelSize, on ? W.Text : W.Muted, TextAlignmentOptions.Center);
        label.overflowMode = CombatUIAssets.ClippedOverflow(label);
        var rect = label.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(1f, 0f); rect.pivot = new Vector2(.5f, 0f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(-Pad * 2f, strip);

        if (clicked == null) return;
        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.onClick.AddListener(() => clicked());
    }

    private static void Stage(Transform parent, float width, PlayerBodyProfile.Skin skin, SkinVariant variant)
    {
        var column = Column(parent, width);
        var frame = W.Rect("Stage", column);
        W.Size(frame, width, width / Aspect, 0f, 0f);
        W.Fill(frame, CardFill);
        var picture = W.Rect("Picture", frame);
        W.Stretch(picture);
        PortraitImage.Add(picture, skin, variant);
        string name = variant != null ? skin.DisplayName + " · " + variant.DisplayName : skin.DisplayName;
        var label = W.Label(column, name, 19f, W.Text, TextAlignmentOptions.Center);
        label.overflowMode = CombatUIAssets.ClippedOverflow(label);
        W.Size(label, width, 30f);
    }
}
