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

    /// <summary>
    /// The look as Outfits.csv has its items now, so a look saved before an item moved or was cut up still wears the same:
    /// - an item in another slot now goes there: the Sera Yura bikini and bodysuit went from the vest and waist to the
    ///   underwear slots (2026-11-12). A pick already in that slot stays and the moved one goes;
    /// - an item the table no longer has, cut into items of their own (OutfitItem.was), becomes them, each in its own slot
    ///   in the same colour: the Banshaku shirt's stocking, thigh belt, choker and necklaces (2026-11-12). The old item
    ///   showed them over whatever was there, so each goes where nothing is picked or the character's own piece is taken
    ///   off (-), and an item picked there stays. The old id is gone once the look is saved again, so this happens once:
    ///   a piece taken off afterwards stays off.
    /// Itself when nothing changed.
    /// </summary>
    public OutfitLook Settled()
    {
        OutfitLook settled = null;
        List<(OutfitItem Piece, string Colour)> pieces = null;
        foreach (var pair in picks)
        {
            if (pair.Value.IsOff || pair.Key.StartsWith(BodyShapes.Prefix, StringComparison.Ordinal)) continue;
            var item = OutfitCatalog.Item(pair.Value.Item);
            if (item == null)
            {
                var split = OutfitCatalog.SplitFrom(pair.Value.Item);
                if (split.Count == 0) continue;
                settled ??= Parse(ToString());
                settled.picks.Remove(pair.Key);
                pieces ??= new List<(OutfitItem, string)>();
                foreach (var piece in split) pieces.Add((piece, pair.Value.Colour));
                continue;
            }
            if (string.Equals(item.slot, pair.Key, StringComparison.OrdinalIgnoreCase) || OutfitCatalog.Slot(item.slot) is not OutfitSlot slot) continue;
            settled ??= Parse(ToString());
            settled.picks.Remove(pair.Key);
            if (!settled.picks.ContainsKey(slot.id)) settled.picks[slot.id] = pair.Value;
        }
        if (pieces != null)
            foreach (var (piece, colour) in pieces)
            {
                if (OutfitCatalog.Slot(piece.slot) is not OutfitSlot slot) continue;
                if (settled.picks.TryGetValue(slot.id, out var there) && !there.IsOff) continue;
                settled.picks[slot.id] = new Pick(piece.id, colour);
            }
        return settled ?? this;
    }

    // A saved look as it reads now (Settled), written as saved when nothing moved.
    private static string Settle(string text)
    {
        var look = Parse(text);
        var settled = look.Settled();
        return settled == look ? text : settled.ToString();
    }

    /// <summary>The outfit this PC last put on the character (settings.json).</summary>
    public static OutfitLook Saved(string skinId) => Parse(SavedText(skinId));

    public static string SavedText(string skinId)
    {
        var saved = GameSettings.Current.outfits;
        if (saved == null || string.IsNullOrEmpty(skinId)) return "";
        string prefix = skinId + Separator;
        foreach (string entry in saved)
            if (entry != null && entry.StartsWith(prefix, StringComparison.Ordinal)) return Settle(entry.Substring(prefix.Length));
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
