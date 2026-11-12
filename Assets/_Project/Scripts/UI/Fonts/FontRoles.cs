using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Text roles: what a label is for, not which file draws it. Code asks for a role (Font, MaterialOf, Apply), a prefab or
/// scene text carries a TextRole, and Resources/Tables/Core/Fonts.csv gives each role a face and an optional material preset
/// by key (FontFaces holds the files). Changing every label of a role is one cell of that table.
/// The roles were cut from what the game drew with before them (2026-11-12), so the look stayed: Label = the old GunFeel
/// score font (Pretendard ExtraBold: menus, kit labels, kill feed), Prompt and Damage = the old Feedback prompt font
/// (S-Core Dream 6), HudText and Chat = the HUD and chat prefabs' S-Core Dream 5 copies, HudBold = their Pretendard Bold
/// copies, Shield = the shield number's outlined S-Core Dream 6 copy, Alarm = the objective banner's Pretendard Medium
/// copy, Ammo = the ammo display (it drew in TMP's sample font LiberationSans until then), Body = whatever asks for no
/// font (TMP's default font).
/// </summary>
public static class FontRoles
{
    public const string TableName = "Fonts";

    /// <summary>Menus and HUD labels built in code: KitStyle, SettingsWidgets, FeelUI.ScoreLabel, kill feed, score board.</summary>
    public const string Label = "Label";
    /// <summary>Prompts and notices: FeelUI.Label, execution, ladder, downed and rank-up prompts, enemy names.</summary>
    public const string Prompt = "Prompt";
    /// <summary>Damage numbers.</summary>
    public const string Damage = "Damage";
    /// <summary>The ammo display (AmmoHUD).</summary>
    public const string Ammo = "Ammo";
    /// <summary>HUD prefab text: weapon names, slot numbers, the interaction prompt, the chat channel.</summary>
    public const string HudText = "HudText";
    /// <summary>HUD prefab bold text: health numbers, objective titles, the TAB hint.</summary>
    public const string HudBold = "HudBold";
    /// <summary>The shield number, outlined.</summary>
    public const string Shield = "Shield";
    /// <summary>The objective banner.</summary>
    public const string Alarm = "Alarm";
    /// <summary>Chat lines.</summary>
    public const string Chat = "Chat";
    /// <summary>Text that asks for no font: TMP's default font, which TMP Settings must name too.</summary>
    public const string Body = "Body";

    // Fields of a role's rows in Fonts.csv ("Label.face").
    public const string FaceField = "face", PresetField = "preset", WarmField = "warm", PrefabsField = "prefabs";

    private static readonly HashSet<string> reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => reported.Clear();

    /// <summary>Every role of Fonts.csv (the rows with a face), in table order.</summary>
    public static IEnumerable<string> Roles
    {
        get
        {
            foreach (var id in GameTables.RowIds(TableName))
                if (GameTables.TryGetRow(TableName, id, out var row) && row.ContainsKey(FaceField)) yield return id;
        }
    }

    /// <summary>The key of the face a role draws with (Fonts.csv), or null.</summary>
    public static string FaceKey(string role) => Field(role, FaceField);

    /// <summary>The key of a role's material preset, or null when it draws with its face's own material.</summary>
    public static string PresetKey(string role) => Field(role, PresetField);

    /// <summary>Which characters FontWarmup draws ahead for a role's face: texts, digits, latin or none.</summary>
    public static string WarmSet(string role) => Field(role, WarmField) ?? "none";

    /// <summary>Path fragments of prefabs whose texts take this role when two roles share a face (ENFILADE → 글꼴).</summary>
    public static string PrefabHint(string role) => Field(role, PrefabsField);

    /// <summary>The face a role draws with. A role or face the tables lack is reported once and draws in TMP's default font.</summary>
    public static TMP_FontAsset Font(string role)
    {
        string key = FaceKey(role);
        var faces = FontFaces.Instance;
        var font = faces != null ? faces.Font(key) : null;
        if (font != null) return font;
        Report(key == null ? $"Fonts.csv에 '{role}' 역할의 face 줄이 없습니다. TMP 기본 글꼴로 그립니다."
            : faces == null ? $"Resources/{FontFaces.ResourcePath}.asset이 없어 '{role}' 역할을 TMP 기본 글꼴로 그립니다."
            : $"FontFaces에 '{key}' 글꼴이 없어 '{role}' 역할을 TMP 기본 글꼴로 그립니다.");
        return TMP_Settings.defaultFontAsset;
    }

    /// <summary>The material a role draws with: its preset, else its face's own material.</summary>
    public static Material MaterialOf(string role)
    {
        string preset = PresetKey(role);
        if (!string.IsNullOrEmpty(preset))
        {
            var faces = FontFaces.Instance;
            var material = faces != null ? faces.PresetMaterial(preset) : null;
            if (material != null) return material;
            Report($"FontFaces에 '{preset}' 재질 프리셋이 없어 '{role}' 역할을 글꼴 기본 재질로 그립니다.");
        }
        var font = Font(role);
        return font != null ? font.material : null;
    }

    /// <summary>Draw a text in its role's face and material. A script that styles the text further does so after this.</summary>
    public static void Apply(TMP_Text text, string role)
    {
        if (text == null) return;
        var font = Font(role);
        if (font == null) return;
        if (text.font != font) text.font = font;
        var material = MaterialOf(role);
        if (material != null && text.fontSharedMaterial != material) text.fontSharedMaterial = material;
    }

    /// <summary>
    /// Ellipsis when TMP will find "…" for this text's font and style, otherwise Truncate (TMP would switch to Truncate
    /// itself, with a console warning per text). It asks the places TMP's GetEllipsisSpecialCharacter asks, in its order
    /// and with the text's style: the font, the font's fallbacks, TMP Settings' fallbacks, the default font. TMP looks
    /// "…" up in a font's own glyphs only for upright regular text; bold, italic or any other weight goes to the font's
    /// weight table alone and, unlike ordinary letters, gets no second try with the regular glyphs. Every face of the game
    /// has "…", but their weight tables stay empty on purpose (a filled Bold slot would swap all bold text from
    /// synthesized bold to that typeface), so bold text finds it in the global fallback "PRETENDARD Weights SDF", whose
    /// table points at the real weights. Before that fallback (2026-11-12) every bold label lost its "…" and a text that
    /// turned bold after this call warned when it drew. Call after setting the font and style; a later style change is
    /// fine while that fallback is in TMP Settings.
    /// </summary>
    public static TextOverflowModes ClippedOverflow(TMP_Text text)
    {
        if (text == null || text.font == null) return TextOverflowModes.Truncate;
        const uint ellipsis = 0x2026;
        TMP_FontAsset font = text.font;
        FontStyles style = text.fontStyle;
        FontWeight weight = (style & FontStyles.Bold) == FontStyles.Bold ? FontWeight.Bold : text.fontWeight;
        TMP_FontAsset fallbackDefault = TMP_Settings.defaultFontAsset;
        bool found = TMP_FontAssetUtilities.GetCharacterFromFontAsset(ellipsis, font, false, style, weight, out _) != null
            || FoundIn(font.fallbackFontAssetTable) || FoundIn(TMP_Settings.fallbackFontAssets)
            || (fallbackDefault != null
                && TMP_FontAssetUtilities.GetCharacterFromFontAsset(ellipsis, fallbackDefault, true, style, weight, out _) != null);
        return found ? TextOverflowModes.Ellipsis : TextOverflowModes.Truncate;

        bool FoundIn(List<TMP_FontAsset> fonts) => fonts != null && fonts.Count > 0
            && TMP_FontAssetUtilities.GetCharacterFromFontAssets(ellipsis, font, fonts, true, style, weight, out _) != null;
    }

    private static string Field(string role, string field)
        => !string.IsNullOrEmpty(role) && GameTables.TryGetRow(TableName, role, out var row) && row.TryGetValue(field, out var value)
            ? value : null;

    // A broken row or a missing face is a setup slip worth one line, not one per label.
    private static void Report(string message)
    {
        if (reported.Add(message)) Debug.LogWarning("[Fonts] " + message);
    }
}
