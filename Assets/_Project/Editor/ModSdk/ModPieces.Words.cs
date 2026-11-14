// dev-text: file (the words makers give their meshes, and the slots' names in the exporter's window; nothing here is shown to players)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// ModPieces: the words. One part of the class; the rest is in ModPieces.cs and the files beside it.
public static partial class ModPieces
{
    /// <summary>
    /// The wardrobe's slots in the game's order, each with what the game calls it (OutfitSlots.csv and the names in
    /// Texts.csv, written down here because the package runs where the game's tables aren't). The game's own project
    /// checks this list against its table whenever it writes the package out (ModSdkPackage).
    /// </summary>
    public static readonly (string Id, string Label)[] Slots =
    {
        ("hair", "헤어"), ("helmet", "모자·헬멧"), ("hairpin", "머리 장식"), ("eyes", "안경·고글"), ("mask", "마스크"), ("ears", "귀걸이"),
        ("neck", "초커·스카프"), ("necklace", "목걸이"), ("top", "상의"), ("outer", "겉옷"), ("vest", "조끼"), ("back", "가방·등"),
        ("waist", "벨트·허리"), (UnderTop, "속옷 상의"), ("gloves", "장갑"), ("wrist", "팔찌·시계"), ("ring", "반지"), ("bottom", "하의"),
        ("legs", "양말·스타킹"), ("thigh", "다리 장식"), ("shoes", "신발"), ("tail", "꼬리"), (UnderBottom, "속옷 하의"),
    };

    /// <summary>The underwear slots: worn on the skin under everything else, the character's own unless an item is picked.</summary>
    public const string UnderTop = "undertop", UnderBottom = "underbottom";
    private const string Trim = "trim";

    /// <summary>Whether the id is one of the wardrobe's slots.</summary>
    public static bool IsSlot(string id) => Slots.Any(slot => string.Equals(slot.Id, id, StringComparison.OrdinalIgnoreCase));

    public static bool IsUnder(string id) =>
        string.Equals(id, UnderTop, StringComparison.OrdinalIgnoreCase) || string.Equals(id, UnderBottom, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the rules may sort a mesh into the slot by themselves: one of the wardrobe's, and not an underwear
    /// slot, which only a mesh named like underwear goes in.
    /// </summary>
    public static bool Sortable(string id) => IsSlot(id) && !IsUnder(id);

    /// <summary>What the game calls a slot, or the id itself for one this list doesn't know.</summary>
    public static string LabelOf(string id)
    {
        foreach (var (slot, label) in Slots)
            if (string.Equals(slot, id, StringComparison.OrdinalIgnoreCase)) return label;
        return id ?? "";
    }

    // Slots by whole words of a piece's name, tried in this order: a thigh belt before a belt, a necklace before a neck.
    private static readonly (string Slot, string[] Words)[] SlotWords =
    {
        ("thigh", new[] { "garter", "garters", "thighbelt", "legbelt", "kneepad", "kneepads", "legring", "thighring", "kneeguard", "kneeguards" }),
        ("necklace", new[] { "necklace", "necklaces", "pendant", "pendants", "locket" }),
        ("neck", new[] { "choker", "chokers", "scarf", "muffler", "neckerchief", "necktie", "bowtie", "neckcollar", "neckband", "neckwear" }),
        ("ears", new[] { "earring", "earrings", "piercing", "piercings", "earcuff" }),
        ("eyes", new[] { "glasses", "goggles", "eyepatch", "monocle", "sunglasses", "eyewear" }),
        ("mask", new[] { "mask", "masks", "respirator" }),
        ("helmet", new[] { "hat", "hats", "cap", "caps", "beret", "helmet", "hood", "beanie", "crown" }),
        ("hairpin", new[] { "hairpin", "hairclip", "headband", "hairband", "tiara", "headdress", "kemomimi" }),
        ("hair", new[] { "hair", "bangs", "ponytail", "twintail", "twintails", "braid", "ahoge" }),
        ("gloves", new[] { "glove", "gloves", "mitten", "mittens", "gauntlet", "gauntlets" }),
        ("wrist", new[] { "bracelet", "bracelets", "wristband", "armband", "armlet", "watch", "bangle" }),
        ("ring", new[] { "ring", "rings" }),
        ("shoes", new[] { "shoe", "shoes", "boot", "boots", "loafer", "loafers", "heel", "heels", "sandal", "sandals", "sneaker", "sneakers", "pumps", "slipper", "slippers" }),
        ("legs", new[] { "stocking", "stockings", "sock", "socks", "tights", "legwarmer", "legwarmers", "kneesocks", "kneehigh", "pantyhose", "leggings", "overknee" }),
        ("bottom", new[] { "skirt", "skirts", "shorts", "pants", "trousers", "jeans", "bloomers", "culottes" }),
        ("waist", new[] { "belt", "belts", "holster", "pouch", "pouches", "waistbag" }),
        ("back", new[] { "bag", "backpack", "wing", "wings", "cape", "cloak", "mantle", "rucksack" }),
        ("vest", new[] { "vest", "harness", "corset", "bodice" }),
        ("outer", new[] { "jacket", "coat", "cardigan", "parka", "hoodie", "blazer", "outer", "windbreaker", "collar", "shrug", "bolero" }),
        ("top", new[] { "shirt", "shirts", "tshirt", "tshirts", "blouse", "sweater", "knit", "camisole", "tanktop", "dress", "onepiece", "top", "tops", "uniform" }),
        ("tail", new[] { "tail", "tails", "shippo", "kemoshippo" }),
    };
    // Two words in a row that name a slot (Leg belt, Knee pad, Arm belt, Neck collar, T-Shirts); a null slot is trim (Name tag).
    private static readonly (string[] First, string[] Second, string Slot)[] SlotPairs =
    {
        (new[] { "thigh", "leg", "knee" }, new[] { "belt", "band", "ring", "pad", "pads", "strap", "garter", "holster", "guard", "guards", "ribbon" }, "thigh"),
        (new[] { "arm", "wrist", "upperarm" }, new[] { "belt", "band", "ring", "strap", "ribbon" }, "wrist"),
        (new[] { "neck" }, new[] { "collar", "ribbon", "tie", "band", "belt" }, "neck"),
        (new[] { "hair", "head" }, new[] { "pin", "clip", "band", "ornament", "accessory", "ribbon", "flower" }, "hairpin"),
        (new[] { "t" }, new[] { "shirt", "shirts" }, "top"),
        (new[] { "tank" }, new[] { "top" }, "top"),
        (new[] { "name", "id" }, new[] { "tag", "card" }, null),
    };
    // Japanese and Korean words run on inside a longer name, so any part of it.
    private static readonly (string Slot, string[] Pieces)[] SlotPieces =
    {
        ("thigh", new[] { "ガーター", "太もも", "ニーパッド", "가터", "허벅지" }),
        ("necklace", new[] { "ネックレス", "ペンダント", "목걸이" }),
        ("neck", new[] { "チョーカー", "マフラー", "スカーフ", "초커", "스카프" }),
        ("ears", new[] { "イヤリング", "ピアス", "귀걸이" }),
        ("eyes", new[] { "メガネ", "眼鏡", "ゴーグル", "안경" }),
        ("helmet", new[] { "帽子", "ベレー", "모자" }),
        ("gloves", new[] { "手袋", "グローブ", "장갑" }),
        ("shoes", new[] { "靴", "ブーツ", "ローファー", "신발", "구두", "부츠" }),
        ("legs", new[] { "靴下", "ソックス", "ストッキング", "タイツ", "스타킹", "양말", "타이츠" }),
        ("bottom", new[] { "スカート", "パンツ", "ズボン", "치마", "바지" }),
        ("outer", new[] { "ジャケット", "コート", "パーカー", "재킷", "코트" }),
        ("top", new[] { "シャツ", "ブラウス", "ワンピース", "셔츠", "블라우스", "원피스" }),
        ("tail", new[] { "しっぽ", "尻尾", "꼬리" }),
    };
    // Words of trim, sewn or pinned on another piece.
    private static readonly HashSet<string> TrimWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ribbon", "ribbons", "bow", "bows", "tie", "ties", "knot", "tag", "lace", "frill", "frills", "button", "buttons", "string", "strings",
        "strap", "straps", "tip", "cross", "side", "front", "back", "sleeve", "sleeves", "chain", "chains", "badge", "patch", "emblem",
        "logo", "tassel", "fringe", "pin", "pins", "cord", "decoration", "deco", "acc", "accessory",
    };
    // A top that is a bottom too: a character's own one-piece is listed under both slots (ModAvatarFile.Part).
    private static readonly HashSet<string> OnePieceWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dress", "dresses", "onepiece" };
    private static readonly string[] OnePiecePieces = { "ワンピース", "ドレス", "원피스", "드레스" };

    // Words of underwear in a mesh's name, whole words: MAYO_Underwear_Bra, "Bikini top String", Sera_Yura_Bikini_Panty.
    private static readonly HashSet<string> UnderWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "bra", "bras", "brassiere", "bikini", "panty", "panties", "underwear", "lingerie", "swimsuit", "swimwear", "leotard", "thong", "bodysuit" };
    // Japanese and Korean ones run on inside a longer name, so any part of it.
    private static readonly string[] UnderPieces = { "下着", "ブラ", "パンティ", "ショーツ", "ビキニ", "水着", "レオタード", "속옷", "브라", "팬티", "비키니", "수영복" };
    private static readonly HashSet<string> TopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "bra", "bras", "brassiere", "top", "upper", "chest", "breast", "bust" };
    private static readonly string[] TopPieces = { "ブラ", "브라" };
    private static readonly HashSet<string> BottomWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "panty", "panties", "thong", "bottom", "lower", "under" };
    private static readonly string[] BottomPieces = { "パンティ", "ショーツ", "팬티" };

    // Words split at anything but letters and at a small letter before a capital (NeckCollar, IchigoShirt).
    private static readonly Regex Breaks = new Regex(@"[^\p{L}]+|(?<=\p{Ll})(?=\p{Lu})", RegexOptions.Compiled);

    /// <summary>A name's words, lower case: split at anything but letters and at a small letter before a capital.</summary>
    public static List<string> Words(string name) =>
        Breaks.Split(name ?? "").Where(word => word.Length > 0).Select(word => word.ToLowerInvariant()).ToList();

    /// <summary>
    /// A mesh's words as the rules read them (<see cref="Piece.Words"/>): its name's words after the maker's prefix
    /// (<paramref name="model"/>: the model's own name in words). For whoever changes that prefix after measuring.
    /// </summary>
    public static List<string> PieceWords(string name, ICollection<string> model) => AfterPrefix(Words(name), model);

    // The words after the maker's prefix: the model's own words (Banshaku Shirt_, Sera_Yura_) and short tags (SM1_). A
    // lone "T" before a shirt stays (T-Shirts); all of them stay when nothing would be left.
    private static List<string> AfterPrefix(List<string> words, ICollection<string> model)
    {
        int cut = 0;
        while (cut < words.Count && (model.Contains(words[cut]) || words[cut].Length <= 2)
            && !(words[cut] == "t" && cut + 1 < words.Count && words[cut + 1].StartsWith("shirt", StringComparison.Ordinal)))
            cut++;
        return cut < words.Count ? words.GetRange(cut, words.Count - cut) : words;
    }

    /// <summary>
    /// The slot a piece's name says (<paramref name="words"/>: its words after the maker's prefix), "trim" for a name of
    /// trim alone, or null when the name says nothing.
    /// </summary>
    private static string NameSlot(string name, List<string> words)
    {
        for (int i = 0; i + 1 < words.Count; i++)
            foreach (var (first, second, slot) in SlotPairs)
                if (first.Contains(words[i]) && second.Contains(words[i + 1])) return slot ?? Trim;
        foreach (var (slot, slotWords) in SlotWords)
            if (words.Any(word => slotWords.Contains(word))) return slot;
        foreach (var (slot, pieces) in SlotPieces)
            if (pieces.Any(piece => name.IndexOf(piece, StringComparison.Ordinal) >= 0)) return slot;
        // Words run together (Thighbelt, Kneesocks): a long slot word inside them.
        string glued = string.Concat(words);
        foreach (var (slot, slotWords) in SlotWords)
            if (slotWords.Any(word => word.Length >= 5 && glued.IndexOf(word, StringComparison.Ordinal) >= 0)) return slot;
        if (words.Count > 0 && words.All(word => TrimWords.Contains(word) || word.Length <= 2)) return Trim;
        return null;
    }

    /// <summary>
    /// A piece's name in the maker's words without the maker's prefix: White Board_Knee pad → Knee pad,
    /// SM1_Sailor_Jacket → Sailor Jacket, IchigoShirt_HomeShirt_big → HomeShirt big.
    /// </summary>
    public static string Readable(string name, ICollection<string> model)
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

    /// <summary>Words as part of an id: lower-case English letters and digits, anything else a -; "piece" when nothing is left.</summary>
    public static string Slug(string words)
    {
        string slug = Regex.Replace((words ?? "").ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 0 ? slug : "piece";
    }

    /// <summary>A model id as words: white-board → White Board.</summary>
    public static string Title(string model) =>
        string.Join(" ", (model ?? "").Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1)));

    /// <summary>Whether a mesh is named like underwear (bra, bikini, panty, 下着 …): the underwear slots take it.</summary>
    public static bool IsUnderwear(string name) => Named(name, UnderWords, UnderPieces);

    /// <summary>Whether a mesh is named a one-piece (a dress): a top that fills the bottom too.</summary>
    public static bool IsOnePiece(string name) => Named(name, OnePieceWords, OnePiecePieces);

    private static bool Named(string name, HashSet<string> words, string[] pieces)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (string word in Breaks.Split(name))
            if (word.Length > 0 && words.Contains(word)) return true;
        foreach (string piece in pieces)
            if (name.IndexOf(piece, StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    // What a piece is, to tell pieces of one slot apart: the words its slot was read from (stocking, socks, knee pad, leg
    // belt), so halves and variants of one thing stay one item (Skirt_Front and Skirt_Back, HomeShirt big and small,
    // Necklace1 to 4); all its words for a piece placed by the body. A plural reads as its singular.
    private static string KindOf(List<string> words)
    {
        for (int i = 0; i + 1 < words.Count; i++)
            foreach (var (first, second, _) in SlotPairs)
                if (first.Contains(words[i]) && second.Contains(words[i + 1])) return words[i] + " " + Singular(words[i + 1]);
        foreach (var (_, slotWords) in SlotWords)
            foreach (string word in words)
                if (slotWords.Contains(word)) return Singular(word);
        return string.Join(" ", words.Where(word => word.Length > 2).Select(Singular));
    }

    private static string Singular(string word) => word.Length > 3 && word.EndsWith("s", StringComparison.Ordinal) ? word.Substring(0, word.Length - 1) : word;
}
