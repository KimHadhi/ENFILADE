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
internal static class BoothPieces
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
                foreach (string part in Split(table.Cell(row, partsColumn)))
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
                    if (Split(table.Cell(row, partsColumn)).Contains(trim.Host.Name, StringComparer.OrdinalIgnoreCase))
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

    // ---- What each piece is ----

    // The model's renderers worth sorting, measured; those rows name already come along as hosts for trim.
    private static List<Piece> Measure(BoothUnderwear.Source source, Body body, Dictionary<string, string> named,
        HashSet<string> model, out int off, out int copies)
    {
        off = copies = 0;
        var pieces = new List<Piece>();
        var root = source.Prefab.transform;
        var toCharacter = root.worldToLocalMatrix;
        foreach (var renderer in source.Prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
            var mesh = MeshOf(renderer);
            // Underwear is the underwear slots' (BoothUnderwear), named or not.
            if (mesh == null || BoothUnderwear.IsUnderwear(renderer.name)) continue;
            bool isNamed = named.TryGetValue(renderer.name, out string slot);
            if (!isNamed && !Shown(renderer, root))
            {
                off++;
                continue;
            }
            if (!isNamed && body != null && body.Copies(renderer, mesh))
            {
                copies++;
                continue;
            }
            var piece = new Piece { Renderer = renderer, Name = renderer.name, Named = isNamed, Slot = isNamed ? slot : null };
            piece.Words = AfterPrefix(Words(renderer.name), model);
            var matrix = toCharacter * renderer.transform.localToWorldMatrix;
            if (mesh.isReadable)
            {
                var vertices = mesh.vertices;
                piece.Points = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) piece.Points[i] = matrix.MultiplyPoint3x4(vertices[i]);
                piece.Box = BoxOf(piece.Points);
            }
            else piece.Box = BoxOf(mesh.bounds, matrix);
            if (!isNamed && body != null)
            {
                piece.Regions = body.Regions(renderer, mesh);
                var middle = piece.Box.center - body.Head;
                piece.Front = middle.x * body.Forward.x + middle.z * body.Forward.z;
            }
            pieces.Add(piece);
        }
        return pieces;
    }

    // Each new piece's slot, or the piece it is trim on. Returns how many could not be placed (no word, no body to measure).
    private static int Sort(List<Piece> pieces, Body body)
    {
        int unknown = 0;
        foreach (var piece in pieces.Where(p => !p.Named))
        {
            piece.NameSlot = NameSlot(piece.Name, piece.Words);
            // Named only by trim words, or a slot word with a trim word ("Loose socks Side Ribbon"): trim, on a piece of
            // that slot when the name says one.
            piece.IsTrim = piece.NameSlot == Trim || piece.Words.Any(TrimWords.Contains);
            if (!piece.IsTrim) piece.Slot = piece.NameSlot ?? BodySlot(piece, body);
        }
        var hosts = pieces.Where(p => (p.Named || (!p.IsTrim && p.Slot != null)) && p.Points != null).ToList();
        foreach (var trim in pieces.Where(p => !p.Named && p.IsTrim))
        {
            string wanted = trim.NameSlot != Trim ? trim.NameSlot : null;
            Piece best = null;
            float bestShare = 0f;
            if (trim.Points != null && trim.Points.Length > 0)
                foreach (var host in hosts)
                {
                    if (host == trim || (wanted != null && host.Slot != wanted)) continue;
                    float share = Lies(trim, host);
                    if (share > bestShare)
                    {
                        best = host;
                        bestShare = share;
                    }
                }
            if (best != null && bestShare >= HostShare)
            {
                trim.Host = best;
                trim.Slot = best.Slot;
            }
            else trim.Slot = wanted ?? BodySlot(trim, body);
        }
        foreach (var piece in pieces.Where(p => !p.Named && !(p.Host != null && p.Host.Named)))
            if (piece.Slot == null || OutfitCatalog.Slot(piece.Slot) == null || OutfitCatalog.Slot(piece.Slot).under)
            {
                piece.Slot = null;
                unknown++;
            }
        return unknown;
    }

    // A slot from the piece's name, Trim for trim alone, or null.
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

    // Where the piece's skin hangs, for a piece its name doesn't place; null without a body to measure against.
    private static string BodySlot(Piece piece, Body body)
    {
        if (body == null || piece.Regions == null || piece.Regions.Count == 0) return null;
        float Share(params string[] regions) => regions.Sum(region => piece.Regions.TryGetValue(region, out float weight) ? weight : 0f);
        var box = piece.Box;
        if (Share("head") >= .5f)
        {
            if (piece.Front > FaceFront) return box.center.y > body.Eyes - .02f ? "eyes" : "mask";
            return box.max.y > body.Head.y + .15f ? "helmet" : "hairpin";
        }
        if (Share("foot") >= .6f) return "shoes";
        if (Share("hand", "forearm") >= .6f && Share("hand") >= .2f) return "gloves";
        if (Share("upperarm", "forearm") >= .6f) return "wrist";
        if (Share("foot", "shin", "thigh") >= .6f && box.min.y < body.Ankle + .05f) return "legs";
        if (Share("thigh", "shin") >= .6f) return "thigh";
        if (Share("neck") >= .4f || (Share("neck", "chest") >= .8f && box.min.y > body.Chest + .05f)) return "neck";
        if (Share("hips", "thigh") >= .6f) return box.size.y > .1f ? "bottom" : "waist";
        if (Share("upperarm") >= .15f) return "top";
        if (Share("chest", "belly") >= .6f) return "vest";
        return "top";
    }

    // The share of the trim's vertices (a sample) with one of the host's within Reach.
    private static float Lies(Piece trim, Piece host)
    {
        host.Cells ??= Grid(host.Points);
        int step = Mathf.Max(1, trim.Points.Length / TrimSamples), tried = 0, near = 0;
        for (int i = 0; i < trim.Points.Length; i += step)
        {
            tried++;
            if (Near(host, trim.Points[i])) near++;
        }
        return tried > 0 ? near / (float)tried : 0f;
    }

    private static Dictionary<Vector3Int, List<int>> Grid(Vector3[] points)
    {
        var cells = new Dictionary<Vector3Int, List<int>>();
        for (int i = 0; i < points.Length; i++)
        {
            var key = Cell(points[i]);
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
            list.Add(i);
        }
        return cells;
    }

    private static Vector3Int Cell(Vector3 point) =>
        new Vector3Int(Mathf.FloorToInt(point.x / Reach), Mathf.FloorToInt(point.y / Reach), Mathf.FloorToInt(point.z / Reach));

    private static bool Near(Piece host, Vector3 point)
    {
        var key = Cell(point);
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                    if (host.Cells.TryGetValue(new Vector3Int(key.x + x, key.y + y, key.z + z), out var list))
                        foreach (int i in list)
                            if ((host.Points[i] - point).sqrMagnitude <= Reach * Reach) return true;
        return false;
    }

    // ---- Items ----

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
            .SelectMany(row => Split(owns.Cell(row, needsColumn)))
            .Where(key => key.IndexOf("shrink", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("kuikomi", StringComparison.OrdinalIgnoreCase) >= 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count == 0) return;
        fits.Add(new Dictionary<string, string> { ["id"] = id, ["shapes"] = string.Join("|", keys) });
    }

    // ---- Words ----

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

    private static IEnumerable<string> Split(string list) =>
        (list ?? "").Split('|').Select(entry => entry.Trim()).Where(entry => entry.Length > 0);

    // ---- Meshes ----

    private static Mesh MeshOf(Renderer renderer) =>
        renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

    // Drawn as the prefab comes: enabled, and itself and every parent up to the prefab's root active.
    private static bool Shown(Renderer renderer, Transform root)
    {
        if (!renderer.enabled) return false;
        for (var at = renderer.transform; at != null; at = at.parent)
        {
            if (!at.gameObject.activeSelf) return false;
            if (at == root) break;
        }
        return true;
    }

    private static Bounds BoxOf(Vector3[] points)
    {
        if (points.Length == 0) return new Bounds();
        var box = new Bounds(points[0], Vector3.zero);
        foreach (var point in points) box.Encapsulate(point);
        return box;
    }

    private static Bounds BoxOf(Bounds local, Matrix4x4 matrix)
    {
        var box = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            box.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
        return box;
    }

    // The character an outfit is made for: which of its bones is which part of the body, where its head, eyes, chest and
    // ankles are (in its own space, where the outfit's pieces stand as made), and its meshes, to know a copy of them.
    private sealed class Body
    {
        public Vector3 Head, Forward;
        public float Eyes, Chest, Ankle;
        private readonly Dictionary<string, string> human = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> own = new Dictionary<string, int>(StringComparer.Ordinal);

        public static Body Of(GameObject character)
        {
            if (character == null) return null;
            var animator = character.GetComponentInChildren<Animator>(true);
            var avatar = animator != null ? animator.avatar : null;
            if (avatar == null || !avatar.isHuman) return null;
            var body = new Body();
            var bonesOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var human in avatar.humanDescription.human)
            {
                body.human[human.boneName] = human.humanName;
                bonesOf[human.humanName] = human.boneName;
            }
            var toCharacter = character.transform.worldToLocalMatrix;
            var transforms = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var bone in character.GetComponentsInChildren<Transform>(true))
                if (!transforms.ContainsKey(bone.name)) transforms[bone.name] = bone;
            Vector3? At(string humanName) => bonesOf.TryGetValue(humanName, out string bone) && transforms.TryGetValue(bone, out var at)
                ? toCharacter.MultiplyPoint3x4(at.position) : (Vector3?)null;
            var head = At("Head");
            var foot = At("LeftFoot") ?? At("RightFoot");
            var chest = At("UpperChest") ?? At("Chest");
            if (head == null || foot == null || chest == null) return null;
            body.Head = head.Value;
            body.Ankle = foot.Value.y;
            body.Chest = chest.Value.y;
            var left = At("LeftEye");
            var right = At("RightEye");
            var eyes = left != null && right != null ? (left.Value + right.Value) * .5f : (Vector3?)null;
            body.Eyes = eyes?.y ?? body.Head.y + .04f;
            var forward = eyes != null ? eyes.Value - body.Head : Vector3.forward;
            forward.y = 0f;
            body.Forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
                if (MeshOf(renderer) is Mesh mesh && !body.own.ContainsKey(renderer.name)) body.own[renderer.name] = mesh.vertexCount;
            return body;
        }

        /// <summary>A mesh of the character's own carried in the outfit's file (the reference body): same name, same size.</summary>
        public bool Copies(Renderer renderer, Mesh mesh) => own.TryGetValue(renderer.name, out int count) && count == mesh.vertexCount;

        /// <summary>The share of the piece's skin weight on each part of the body (head, neck, chest, thigh …).</summary>
        public Dictionary<string, float> Regions(Renderer renderer, Mesh mesh)
        {
            var shares = new Dictionary<string, float>(StringComparer.Ordinal);
            if (renderer is SkinnedMeshRenderer skinned && mesh.isReadable && skinned.bones.Length > 0)
            {
                var bones = skinned.bones;
                var regions = new string[bones.Length];
                for (int i = 0; i < bones.Length; i++) regions[i] = RegionOf(bones[i]) ?? "other";
                float total = 0f;
                foreach (var weight in mesh.GetAllBoneWeights())
                {
                    if (weight.boneIndex < 0 || weight.boneIndex >= regions.Length || weight.weight <= 0f) continue;
                    shares.TryGetValue(regions[weight.boneIndex], out float sum);
                    shares[regions[weight.boneIndex]] = sum + weight.weight;
                    total += weight.weight;
                }
                if (total > 0f)
                {
                    foreach (string region in shares.Keys.ToList()) shares[region] /= total;
                    return shares;
                }
                shares.Clear();
            }
            // Unskinned or unreadable: the body part it hangs from.
            shares[RegionOf(renderer is SkinnedMeshRenderer root && root.rootBone != null ? root.rootBone : renderer.transform) ?? "other"] = 1f;
            return shares;
        }

        // The part of the body a bone is: its own humanoid name, or its nearest humanoid parent's (Breast_L is the chest).
        private string RegionOf(Transform bone)
        {
            for (var at = bone; at != null; at = at.parent)
                if (human.TryGetValue(at.name, out string humanName)) return Region(humanName);
            return null;
        }

        private static string Region(string humanName)
        {
            switch (humanName)
            {
                case "Head": case "Jaw": case "LeftEye": case "RightEye": return "head";
                case "Neck": return "neck";
                case "Chest": case "UpperChest": case "LeftShoulder": case "RightShoulder": return "chest";
                case "Spine": return "belly";
                case "Hips": return "hips";
                case "LeftUpperArm": case "RightUpperArm": return "upperarm";
                case "LeftLowerArm": case "RightLowerArm": return "forearm";
                case "LeftHand": case "RightHand": return "hand";
                case "LeftUpperLeg": case "RightUpperLeg": return "thigh";
                case "LeftLowerLeg": case "RightLowerLeg": return "shin";
                case "LeftFoot": case "RightFoot": case "LeftToes": case "RightToes": return "foot";
            }
            // Fingers ("Left Thumb Proximal") are the hand.
            return humanName.IndexOf("Thumb", StringComparison.Ordinal) >= 0 || humanName.IndexOf("Index", StringComparison.Ordinal) >= 0
                || humanName.IndexOf("Middle", StringComparison.Ordinal) >= 0 || humanName.IndexOf("Ring", StringComparison.Ordinal) >= 0
                || humanName.IndexOf("Little", StringComparison.Ordinal) >= 0 ? "hand" : "other";
        }
    }
}
