using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// What a character wears in its slots, as text: "slot=item#colour;slot=item". Each character keeps its own in
/// settings.json (GameSettings.outfits, entries "skin@look"), so switching characters and back finds the last outfit
/// still on. The other PCs get it after the skin and its colour set (PlayerNetworkPose sends "skin#colour@look"), and a PC
/// that lacks one of the items shows that slot with the character's own piece. The item "-" takes the own piece off with
/// nothing in its place. Ids may not contain '@', ';', '=' or '#'.
/// </summary>
public sealed class OutfitLook
{
    /// <summary>Between a skin and its outfit, in settings entries and in what other PCs receive.</summary>
    public const char Separator = '@';
    /// <summary>The item that takes the character's own piece off.</summary>
    public const string Off = "-";
    private const char PickSeparator = ';', ItemSeparator = '=', ColourSeparator = '#';

    public readonly struct Pick
    {
        public readonly string Item, Colour;

        public Pick(string item, string colour)
        {
            Item = item ?? "";
            Colour = colour ?? "";
        }

        public bool IsOff => Item == Off;
    }

    // Sorted, so the same picks always make the same text and two looks compare as strings.
    private readonly SortedDictionary<string, Pick> picks = new SortedDictionary<string, Pick>(StringComparer.Ordinal);

    public bool IsEmpty => picks.Count == 0;

    /// <summary>Every entry's slot in order, body options ("shape:…", BodyShapes) among them.</summary>
    public IEnumerable<string> Slots => picks.Keys;

    public bool TryGet(string slot, out Pick pick) => picks.TryGetValue(slot ?? "", out pick);

    /// <summary>Puts <paramref name="item"/> in the slot; null or empty empties it, back to the character's own piece.</summary>
    public void Set(string slot, string item, string colour = "")
    {
        if (string.IsNullOrEmpty(slot)) return;
        if (string.IsNullOrEmpty(item)) picks.Remove(slot);
        else picks[slot] = new Pick(item, colour);
    }

    public static OutfitLook Parse(string text)
    {
        var look = new OutfitLook();
        if (string.IsNullOrEmpty(text)) return look;
        foreach (string entry in text.Split(PickSeparator))
        {
            int equals = entry.IndexOf(ItemSeparator);
            if (equals <= 0) continue;
            string slot = entry.Substring(0, equals).Trim(), worn = entry.Substring(equals + 1).Trim();
            int hash = worn.IndexOf(ColourSeparator);
            look.Set(slot, hash < 0 ? worn : worn.Substring(0, hash), hash < 0 ? "" : worn.Substring(hash + 1));
        }
        return look;
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var pair in picks)
        {
            if (text.Length > 0) text.Append(PickSeparator);
            text.Append(pair.Key).Append(ItemSeparator).Append(pair.Value.Item);
            if (pair.Value.Colour.Length > 0) text.Append(ColourSeparator).Append(pair.Value.Colour);
        }
        return text.ToString();
    }

    /// <summary>The same look written the one way <see cref="ToString"/> writes it.</summary>
    public static string Tidy(string text) => Parse(text).ToString();

    /// <summary>The outfit this PC last put on the character (settings.json).</summary>
    public static OutfitLook Saved(string skinId) => Parse(SavedText(skinId));

    public static string SavedText(string skinId)
    {
        var saved = GameSettings.Current.outfits;
        if (saved == null || string.IsNullOrEmpty(skinId)) return "";
        string prefix = skinId + Separator;
        foreach (string entry in saved)
            if (entry != null && entry.StartsWith(prefix, StringComparison.Ordinal)) return entry.Substring(prefix.Length);
        return "";
    }

    /// <summary>Makes <paramref name="look"/> the character's outfit in the settings; GameSettings.Apply tells the body.</summary>
    public static void Save(string skinId, OutfitLook look)
    {
        if (string.IsNullOrEmpty(skinId)) return;
        var saved = GameSettings.Current.outfits ??= new List<string>();
        string prefix = skinId + Separator;
        saved.RemoveAll(entry => entry == null || entry.StartsWith(prefix, StringComparison.Ordinal));
        string text = look != null ? look.ToString() : "";
        if (text.Length > 0) saved.Add(prefix + text);
    }

    /// <summary>A skin's look ("skin#colour") with its outfit after it, as the other PCs receive it.</summary>
    public static string Join(string skinLook, string outfit) =>
        string.IsNullOrEmpty(outfit) ? skinLook ?? "" : (skinLook ?? "") + Separator + outfit;

    /// <summary>The skin's part of what <see cref="Join"/> made. A look from a build without outfits is all skin.</summary>
    public static string SkinPart(string look)
    {
        look ??= "";
        int at = look.IndexOf(Separator);
        return at < 0 ? look : look.Substring(0, at);
    }

    /// <summary>The outfit's part of what <see cref="Join"/> made; empty when there is none.</summary>
    public static string OutfitPart(string look)
    {
        look ??= "";
        int at = look.IndexOf(Separator);
        return at < 0 ? "" : look.Substring(at + 1);
    }
}
