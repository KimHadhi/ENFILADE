using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// BoothPieces: Words. One part of the class; the rest is in BoothPieces.cs and the files beside it.
internal static partial class BoothPieces
{
    private static List<string> Words(string name) =>
        Breaks.Split(name ?? "").Where(word => word.Length > 0).Select(word => word.ToLowerInvariant()).ToList();

    // The words after the maker's prefix: the model's own words (Banshaku Shirt_, Sera_Yura_) and short tags (SM1_). A
    // lone "T" before a shirt stays (T-Shirts); all of them stay when nothing would be left.
    private static List<string> AfterPrefix(List<string> words, HashSet<string> model)
    {
        int cut = 0;
        while (cut < words.Count && (model.Contains(words[cut]) || words[cut].Length <= 2)
            && !(words[cut] == "t" && cut + 1 < words.Count && words[cut + 1].StartsWith("shirt", StringComparison.Ordinal)))
            cut++;
        return cut < words.Count ? words.GetRange(cut, words.Count - cut) : words;
    }

    // A piece's name in the maker's words without the maker's prefix: White Board_Knee pad → Knee pad,
    // SM1_Sailor_Jacket → Sailor Jacket, IchigoShirt_HomeShirt_big → HomeShirt big.
    private static string Readable(string name, HashSet<string> model)
    {
        var parts = name.Split('_').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
        int cut = 0;
        while (cut < parts.Count - 1)
        {
            var words = Words(parts[cut]);
            bool maker = words.Count == 0 || words.All(word => model.Contains(word)) || (parts[cut].Length <= 4 && parts[cut] == parts[cut].ToUpperInvariant());
            if (!maker) break;
            cut++;
        }
        string readable = string.Join(" ", parts.Skip(cut)).Trim();
        // Only a number left (Earring1_1 → 1): the whole name, spaced.
        return readable.Any(char.IsLetter) ? readable : name.Replace('_', ' ').Trim();
    }

    private static string Slug(string words)
    {
        string slug = Regex.Replace(words.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 0 ? slug : "piece";
    }
}
