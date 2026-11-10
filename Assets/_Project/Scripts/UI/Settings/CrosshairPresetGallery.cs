using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The crosshair tab's starting points: a tile per <see cref="CrosshairPresets"/> row, drawn live in the player's own
/// colours, and a share code row (copy, paste, or type a code in). Picking a tile or reading a code turns drawing on,
/// then <c>changed</c> rebuilds the tab around the new look. More than two rows of tiles scroll.
/// </summary>
public sealed class CrosshairPresetGallery
{
    private const int Columns = 6, RowsShown = 2;
    private const float TileHeight = 92f;
    private const float GlowSeconds = .3f;
    private static readonly Color Hover = new Color(1f, 1f, 1f, .14f), Pressed = new Color(1f, 1f, 1f, .2f),
        Picked = new Color(W.Accent.r, W.Accent.g, W.Accent.b, .24f), PickedHover = new Color(W.Accent.r, W.Accent.g, W.Accent.b, .34f),
        PickedGlow = new Color(W.Accent.r, W.Accent.g, W.Accent.b, .5f);
    private static CrosshairProfile[] tileProfiles = new CrosshairProfile[0];
    private static string carriedInfo; // a message that outlives the rebuild a successful read causes
    private static int glowTile = -1; // the tile just picked glows softly once as the tab rebuilds
    private readonly CrosshairKind kind;
    private readonly Action changed;
    private readonly TextMeshProUGUI info;
    private string shownCode;

    public CrosshairPresetGallery(Transform parent, CrosshairKind kind, Action changed)
    {
        this.kind = kind;
        this.changed = changed;
        var settings = PlayerCrosshairs.Settings(kind);
        var presets = CrosshairPresets.All;
        if (tileProfiles.Length < presets.Count) Array.Resize(ref tileProfiles, presets.Count);
        int picked = settings.custom ? CrosshairPresets.IndexOf(settings) : -1;

        var grid = (presets.Count + Columns - 1) / Columns > RowsShown ? W.ScrollArea(parent) : Grid(parent);
        RectTransform row = null;
        for (int i = 0; i < presets.Count; i++)
        {
            if (i % Columns == 0) row = Row(grid);
            Tile(row, i, settings, i == picked);
        }
        glowTile = -1;
        CodeRow(parent, settings);
        info = W.Label(parent, carriedInfo ?? Texts.Get(settings.custom ? "crosshair.presets.info.custom" : "crosshair.presets.info.design"),
            16f, W.Muted);
        W.Size(info, height: 24f);
        carriedInfo = null;
    }

    private static RectTransform Grid(Transform parent)
    {
        var grid = W.Rect("Presets", parent);
        var layout = grid.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        W.Size(grid, flexibleWidth: 1f, flexibleHeight: 0f);
        return grid;
    }

    private static RectTransform Row(Transform grid)
    {
        var row = W.Rect("Row", grid);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        W.Size(row, height: TileHeight, flexibleHeight: 0f);
        return row;
    }

    private void Tile(Transform row, int index, CrosshairSettings look, bool picked)
    {
        var preset = CrosshairPresets.All[index];
        var tile = W.Rect(preset.Name, row);
        var image = W.Fill(tile, Color.white); // the button's tint gives the colour
        var button = tile.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = colors.selectedColor = picked ? Picked : W.Surface;
        colors.highlightedColor = picked ? PickedHover : Hover;
        colors.pressedColor = Pressed;
        button.colors = colors;
        // Colours set after the button exists would fade the tile in from white over a tenth of a second; snap them.
        image.CrossFadeColor(colors.normalColor, 0f, true, true);
        if (index == glowTile)
        {
            image.canvasRenderer.SetColor(PickedGlow);
            image.CrossFadeColor(colors.normalColor, GlowSeconds, true, true);
        }
        button.onClick.AddListener(() => Pick(index, preset));

        var art = W.Rect("Art", tile);
        art.anchorMin = new Vector2(0f, .24f); art.anchorMax = Vector2.one;
        art.offsetMin = art.offsetMax = Vector2.zero;
        CrosshairPreview.Draw(art).ApplyProfile(TileProfile(index, preset, look));

        var name = W.Label(tile, preset.Name, 15f, picked ? W.Accent : W.Muted, TextAlignmentOptions.Center);
        name.rectTransform.anchorMin = Vector2.zero; name.rectTransform.anchorMax = new Vector2(1f, .24f);
        name.rectTransform.offsetMin = new Vector2(0f, 4f); name.rectTransform.offsetMax = Vector2.zero;
    }

    // One reused profile per tile, rewritten in the player's colours each time the tiles are built.
    private CrosshairProfile TileProfile(int index, CrosshairPresets.Preset preset, CrosshairSettings look)
    {
        ref var profile = ref tileProfiles[index];
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<CrosshairProfile>();
            profile.name = "Preset Tile " + index;
            profile.hideFlags = HideFlags.HideAndDontSave;
        }
        var shape = look.Clone();
        CrosshairPresets.Apply(preset, shape);
        shape.WriteTo(profile, PlayerCrosshairs.Design(kind));
        return profile;
    }

    private void CodeRow(Transform parent, CrosshairSettings settings)
    {
        var row = W.Row(parent, Texts.Get("crosshair.code"));
        shownCode = settings.custom ? CrosshairCode.Encode(settings) : string.Empty;
        var field = W.TextInput(row, shownCode, _ => { });
        // Above the field's own priority: the code's text width would otherwise squeeze the label and buttons.
        W.Size(field, width: 0f, height: 34f, flexibleWidth: 1f).layoutPriority = 2;
        field.characterLimit = 80;
        var hint = W.Label(field.textViewport, Texts.Format("crosshair.code.hint", CrosshairCode.Prefix), 16f, W.Muted);
        W.Stretch(hint.rectTransform);
        field.placeholder = hint;
        hint.enabled = shownCode.Length == 0; // set after the text, so the field has not hidden it on its own
        field.pointSize = 16f;
        field.onEndEdit.AddListener(text =>
        {
            if (text == shownCode) return;
            if (!Read(text, Texts.Format("crosshair.code.unreadable", CrosshairCode.Prefix)))
                field.SetTextWithoutNotify(shownCode);
        });

        var copy = W.ActionButton(row, Texts.Get("crosshair.code.copy"), false, () =>
        {
            GUIUtility.systemCopyBuffer = shownCode;
            info.text = Texts.Get("crosshair.code.copied");
        });
        W.Size(copy, 84f, 34f);
        if (!settings.custom)
        {
            copy.interactable = false; // the designed crosshair is an asset, not a drawing a code can carry
            copy.GetComponentInChildren<TextMeshProUGUI>().color = W.Muted;
        }
        W.Size(W.ActionButton(row, Texts.Get("crosshair.code.paste"), false,
            () => Read(GUIUtility.systemCopyBuffer, Texts.Get("crosshair.code.clipboard_empty"))), 106f, 34f);
    }

    private bool Read(string text, string failure)
    {
        if (!CrosshairCode.TryDecode(text, out var drawing))
        {
            info.text = failure;
            return false;
        }
        PlayerCrosshairs.Replace(kind, drawing);
        GameSettings.Apply();
        carriedInfo = Texts.Get("crosshair.code.loaded");
        changed();
        return true;
    }

    private void Pick(int index, CrosshairPresets.Preset preset)
    {
        var settings = PlayerCrosshairs.Settings(kind);
        CrosshairPresets.Apply(preset, settings);
        settings.custom = true;
        GameSettings.Apply();
        glowTile = index;
        changed();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        tileProfiles = new CrosshairProfile[0];
        carriedInfo = null;
        glowTile = -1;
    }
}
