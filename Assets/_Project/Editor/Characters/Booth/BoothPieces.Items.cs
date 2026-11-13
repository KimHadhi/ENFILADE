using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// BoothPieces: Items. One part of the class; the rest is in BoothPieces.cs and the files beside it.
internal static partial class BoothPieces
{
    // The items the new pieces make: per slot, one item of all its pieces (each with the trim on it), and when they are of
    // different kinds each alone too. Id, slot, renderer names, name words.
    private static IEnumerable<(string Id, string Slot, List<string> Parts, string Words)> Items(List<Piece> pieces, string modelId,
        HashSet<string> model)
    {
        string title = BoothUnderwear.Title(modelId);
        var hosts = pieces.Where(p => !p.Named && !p.IsTrim && p.Slot != null)
            .Concat(pieces.Where(p => !p.Named && p.IsTrim && p.Host == null && p.Slot != null)).ToList();
        foreach (var group in hosts.GroupBy(p => p.Slot, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            List<string> PartsOf(IEnumerable<Piece> those) => those.SelectMany(host => new[] { host }.Concat(pieces.Where(t => t.Host == host)))
                .Select(p => p.Name).Distinct().ToList();
            var readable = members.Select(p => Readable(p.Name, model)).ToList();
            string together = string.Join(" · ", readable.Select(words => Regex.Replace(words, @"\s*\d+$", "")).Distinct());
            yield return (modelId + "-" + group.Key, group.Key, PartsOf(members), title + " " + together);
            int kinds = members.Select(p => KindOf(p)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (members.Count < 2 || kinds < members.Count) continue;
            for (int i = 0; i < members.Count; i++)
                yield return (modelId + "-" + group.Key + "-" + Slug(readable[i]), group.Key, PartsOf(new[] { members[i] }), title + " " + readable[i]);
        }
    }

    // What a piece is, to tell pieces of one slot apart: the words its slot was read from (stocking, socks, knee pad, leg
    // belt), so halves and variants of one thing stay one item (Skirt_Front and Skirt_Back, HomeShirt big and small,
    // Necklace1 to 4); all its words for a piece placed by the body. A plural reads as its singular.
    private static string KindOf(Piece piece)
    {
        var words = piece.Words;
        for (int i = 0; i + 1 < words.Count; i++)
            foreach (var (first, second, _) in SlotPairs)
                if (first.Contains(words[i]) && second.Contains(words[i + 1])) return words[i] + " " + Singular(words[i + 1]);
        foreach (var (_, slotWords) in SlotWords)
            foreach (string word in words)
                if (slotWords.Contains(word)) return Singular(word);
        return string.Join(" ", words.Where(word => word.Length > 2).Select(Singular));
    }

    private static string Singular(string word) => word.Length > 3 && word.EndsWith("s", StringComparison.Ordinal) ? word.Substring(0, word.Length - 1) : word;

    // The skin keys the character's own piece of the slot holds drawn in (CharacterParts.csv needs named Shrink or Kuikomi,
    // as SkinCover.Hides reads them) become the new item's fit on that character (OutfitFits.csv "item@skin"), so the skin
    // stays in under it as under the piece it takes the place of. SkinCover lets go of those it doesn't cover.
    private static void TakeOverKeys(BoothTable owns, BoothTable fits, string skin, string item, string slot)
    {
        if (owns == null || fits == null || string.IsNullOrEmpty(skin)) return;
        int skinColumn = owns.Column("skin"), slotColumn = owns.Column("slot"), needsColumn = owns.Column("needs");
        int shapesColumn = fits.Column("shapes");
        if (skinColumn < 0 || slotColumn < 0 || needsColumn < 0 || shapesColumn < 0) return;
        string id = item + OutfitLook.Separator + skin;
        if (fits.FindId(id) >= 0) return;
        var keys = owns.Rows
            .Where(row => owns.Cell(row, skinColumn).Equals(skin, StringComparison.OrdinalIgnoreCase)
                && owns.Cell(row, slotColumn).Equals(slot, StringComparison.OrdinalIgnoreCase))
            .SelectMany(row => TableCells.Parts(owns.Cell(row, needsColumn)))
            .Where(key => key.IndexOf("shrink", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("kuikomi", StringComparison.OrdinalIgnoreCase) >= 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count == 0) return;
        fits.Add(new Dictionary<string, string> { ["id"] = id, ["shapes"] = string.Join("|", keys) });
    }
}
