using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Sorts the pieces of a BOOTH outfit into the wardrobe as the import registers it (BoothImport): each mesh of an outfit
/// model that no row of Outfits.csv names yet becomes an item in the slot it is for, so an outfit of a shirt, stockings,
/// a thigh belt and necklaces arrives as things one can wear apart. The Banshaku shirt had them all in one item of the
/// top slot (the user, 2026-11-12: "왜 셔츠인데 아래에 스타킹이나 가터 벨트까지 있어? 따로 분리 돼 이거?"). What a piece is:
/// - its name first: the words makers give pieces (Stocking, Choker, Necklace, Glasses, ストッキング, 스타킹 …), whole
///   words after the maker's prefix (Banshaku Shirt_, SM1_), word pairs (Leg belt, Knee pad, T-Shirts) and words run
///   together (Thighbelt);
/// - trim, named only so (a ribbon, tie, knot, tag), goes with the piece it lies on, the one with most of its vertices
///   within 2 cm: the sailor jacket's sleeve ribbons and front tie, a sock's ribbon, the name tag on a jacket;
/// - else where its skin hangs: its bone weights by body region, and for a head piece how far it stands before the face
///   (glasses at the eyes, a toast in the mouth is a mask).
/// A slot's pieces make one item; several of different kinds (a stocking and socks) are each an item too, beside the set
/// of them that is the maker's look. Underwear goes in first (BoothUnderwear); a piece switched off in the prefab (the
/// other choice of a VRChat toggle) and a copy of the character's own body are left out, and a model with an item taking
/// every piece no other names (parts empty) is whole as it is. Names go in Texts.csv in the maker's words, for the
/// 글자 편집기. A new piece in a slot where the character wears its own takes over the skin keys that piece held drawn in
/// (CharacterParts.csv needs, Shrink and Kuikomi: OutfitFits.csv), so stockings worn in place of Mayo's keep her legs from
/// poking through them, and SkinCover lets go of what the new piece doesn't cover. Rows written by hand win: nothing a
/// row names is sorted again, so a wrong sort is fixed by editing its row.
/// The rules were checked on every outfit the game has (Sailor Maiden, White Board, Sera Yura, Banshaku Shirt, the
/// Ichigo shirt, the mirrored side locks, the Ene earrings) against the rows written by hand, 2026-11-12.
/// </summary>
internal static partial class BoothPieces
{
    private static string ItemsPath => TablePaths.Of("Outfits");
    private static string FitsPath => TablePaths.Of("OutfitFits");
    private static string PartsPath => TablePaths.Of("CharacterParts");
    // Trim lies on the piece that has one of its vertices within Reach (m) of at least HostShare of the trim's.
    private const float Reach = .02f, HostShare = .5f;
    // A head piece whose middle stands this far (m) before the head bone is on the face: glasses 5.5-6.1 cm, Sera Yura's
    // toast 9-10 cm and its jam 5.8-6.7 cm, an earring 1 cm, a beret behind it (Mayo and Ichigo, 2026-11-12).
    private const float FaceFront = .045f;
    // How many of a trim piece's vertices are tried against the pieces it may lie on.
    private const int TrimSamples = 400;
    private const string Trim = "trim";

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
    // Words split at anything but letters and at a small letter before a capital (NeckCollar, IchigoShirt).
    private static readonly Regex Breaks = new Regex(@"[^\p{L}]+|(?<=\p{Ll})(?=\p{Lu})", RegexOptions.Compiled);

    private sealed class Piece
    {
        public Renderer Renderer;
        public string Name, Slot, NameSlot;
        public List<string> Words;
        // Already in a row: never sorted again, only a host for new trim.
        public bool Named;
        public bool IsTrim;
        public Piece Host;
        public Dictionary<string, float> Regions;
        public Bounds Box;
        public float Front;
        // Its vertices as made, in the character's space (null when its mesh can't be read), and a grid of them.
        public Vector3[] Points;
        public Dictionary<Vector3Int, List<int>> Cells;
    }

    /// <summary>
    /// Writes the items for the new pieces of these outfits. False while a table write waits for its file (TableFile): it
    /// lands by itself.
    /// </summary>
    internal static bool Write(List<BoothUnderwear.Source> outfits)
    {
        if (outfits.Count == 0) return true;
        try
        {
            return WriteItems(outfits);
        }
        catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning($"[BOOTH] 옷 조각 줄을 쓰지 못했습니다({error.Message}). {BoothTable.Redo}");
            return false;
        }
    }

    private static bool WriteItems(List<BoothUnderwear.Source> outfits)
    {
        var table = BoothTable.Read(ItemsPath);
        if (table == null) return true;
        int idColumn = table.Column("id"), slotColumn = table.Column("slot"), nameColumn = table.Column("name"),
            modelColumn = table.Column("model"), partsColumn = table.Column("parts");
        if (idColumn < 0 || slotColumn < 0 || modelColumn < 0 || partsColumn < 0) return true;
        string ModelOf(int row)
        {
            string model = table.Cell(row, modelColumn);
            return model.Length > 0 ? model : table.Cell(row, idColumn);
        }
        var owns = BoothTable.Read(PartsPath);
        var fits = BoothTable.Read(FitsPath);
        var names = new List<(string Id, string Words, string Note)>();
        foreach (var source in outfits)
        {
            if (source.Prefab == null) continue;
            var rows = table.Rows.Where(row => ModelOf(row).Equals(source.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            // An item naming no parts takes every piece no other names: the model is whole as it is (the Ene earrings).
            if (rows.Any(row => table.Cell(row, partsColumn).Length == 0)) continue;
            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (int row in rows)
                foreach (string part in TableCells.Parts(table.Cell(row, partsColumn)))
                    if (!named.ContainsKey(part)) named[part] = table.Cell(row, slotColumn);
            var body = Body.Of(source.Character);
            var model = new HashSet<string>(Words(source.Id), StringComparer.OrdinalIgnoreCase);
            var pieces = Measure(source, body, named, model, out int off, out int copies);
            if (pieces.All(piece => piece.Named)) continue;
            int unknown = Sort(pieces, body);
            var written = new List<string>();
            // Trim on a piece a row names already joins that row.
            foreach (var trim in pieces.Where(piece => !piece.Named && piece.Host != null && piece.Host.Named))
            {
                foreach (int row in rows)
                    if (TableCells.Parts(table.Cell(row, partsColumn)).Contains(trim.Host.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        table.Append(row, partsColumn, trim.Name);
                        written.Add(table.Cell(row, idColumn) + " +" + trim.Name);
                    }
            }
            foreach (var (id, slot, parts, words) in Items(pieces, source.Id, model))
            {
                string unique = id;
                int at = table.FindId(unique);
                for (int n = 2; at >= 0 && !ModelOf(at).Equals(source.Id, StringComparison.OrdinalIgnoreCase); n++)
                    at = table.FindId(unique = id + "-" + n);
                if (at >= 0)
                {
                    // A row of this model by that id, from an import before: its new pieces join it.
                    foreach (string part in parts) table.Append(at, partsColumn, part);
                    written.Add(unique + " +" + string.Join("|", parts));
                    continue;
                }
                table.Add(new Dictionary<string, string>
                {
                    ["id"] = unique,
                    ["slot"] = slot,
                    ["name"] = nameColumn >= 0 ? BoothUnderwear.NameId(unique) : "",
                    ["model"] = source.Id,
                    ["parts"] = string.Join("|", parts),
                });
                names.Add((BoothUnderwear.NameId(unique), words, BoothUnderwear.NameNote(unique)));
                written.Add(unique + "(" + slot + ")");
                TakeOverKeys(owns, fits, source.Skin, unique, slot);
            }
            if (written.Count == 0) continue;
            string left = (off > 0 ? $" 프리팹에서 꺼져 있던 조각 {off}개(VRChat 토글의 다른 쪽)는 넣지 않았습니다." : "")
                + (unknown > 0 ? $" 칸을 알 수 없는 조각 {unknown}개는 넣지 않았습니다(Outfits.csv에 줄을 써 주세요)." : "")
                + (copies > 0 ? $" 캐릭터 몸의 사본 {copies}개는 뺐습니다." : "");
            Debug.Log($"[BOOTH] {source.Id}: 옷 조각을 Outfits.csv에 넣었습니다 — {string.Join(", ", written)}. 이름은 ENFILADE → 글자 → 글자 편집기에서 고쳐요.{left}");
        }
        // The names first: a row is never left pointing at an id Texts.csv lacks.
        bool titled = BoothTable.AddTexts(names);
        bool keyed = fits == null || fits.Save(FitsPath, "옷 맞춤 줄");
        return table.Save(ItemsPath, "옷 조각 줄") && titled && keyed;
    }
}
