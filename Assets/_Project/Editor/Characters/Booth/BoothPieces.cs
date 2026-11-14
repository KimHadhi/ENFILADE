using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Puts the pieces of a BOOTH outfit into the wardrobe as the import registers it (BoothImport): each mesh of an outfit
/// model that no row of Outfits.csv names yet becomes an item in the slot it is for, so an outfit of a shirt, stockings,
/// a thigh belt and necklaces arrives as things one can wear apart. The Banshaku shirt had them all in one item of the
/// top slot (the user, 2026-11-12: "왜 셔츠인데 아래에 스타킹이나 가터 벨트까지 있어? 따로 분리 돼 이거?").
/// What a piece is (its name, the piece it lies on as trim, where its skin hangs) is sorted by ModPieces, the rules the
/// mod exporter sorts a player's own outfit by too, so the game's outfits and a mod's are told apart the same way.
/// Here the result goes into the game's tables: a slot's pieces make one item; several of different kinds (a stocking
/// and socks) are each an item too, beside the set of them that is the maker's look. Underwear goes in first
/// (BoothUnderwear); a piece switched off in the prefab (the other choice of a VRChat toggle) and a copy of the
/// character's own body are left out, and a model with an item taking every piece no other names (parts empty) is
/// whole as it is. Names go in Texts.csv in the maker's words, for the 글자 편집기. A new piece in a slot where the
/// character wears its own takes over the skin keys that piece held drawn in (CharacterParts.csv needs, Shrink and
/// Kuikomi: OutfitFits.csv), so stockings worn in place of Mayo's keep her legs from poking through them, and
/// SkinCover lets go of what the new piece doesn't cover. Rows written by hand win: nothing a row names is sorted
/// again, so a wrong sort is fixed by editing its row.
/// </summary>
internal static class BoothPieces
{
    private static string ItemsPath => TablePaths.Of("Outfits");
    private static string FitsPath => TablePaths.Of("OutfitFits");
    private static string PartsPath => TablePaths.Of("CharacterParts");

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
            var body = ModPieces.Body.Of(source.Character);
            var model = new HashSet<string>(ModPieces.Words(source.Id), StringComparer.OrdinalIgnoreCase);
            // Underwear is the underwear slots' (BoothUnderwear), named or not, so it isn't measured here.
            var pieces = ModPieces.Measure(source.Prefab, body, named, model);
            int off = pieces.RemoveAll(piece => piece.Off), copies = pieces.RemoveAll(piece => piece.Copy);
            if (pieces.All(piece => piece.Named)) continue;
            int unknown = ModPieces.Sort(pieces, body, slot => OutfitCatalog.Slot(slot) is { under: false });
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
            foreach (var (id, slot, _, parts, words) in ModPieces.Items(pieces, source.Id, model, ModPieces.Title(source.Id)))
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
