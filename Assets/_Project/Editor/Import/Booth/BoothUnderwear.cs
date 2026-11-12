using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Puts the underwear that comes with a BOOTH avatar or outfit into the underwear slots (OutfitSlot.under) as the import
/// registers it: every mesh named like underwear (bra, bikini, panty, underwear, lingerie, swimsuit, 下着, ブラ, ショーツ,
/// 水着, 속옷 …) that no row lists yet. A character's goes into CharacterParts.csv as its own underwear, an outfit's into
/// Outfits.csv as an item of its model. Named a top or a bottom (Bikini top, Underwear_Under) it goes there; else by where
/// it sits: above halfway from the hips to the chest it is a top, below a bottom, and an outfit's piece reaching from the
/// hips to the chest (a swimsuit, a bodysuit) is a top that takes the bottom too. Rows written by hand win: a mesh any row
/// lists is left as it is, and a row already in the slot gets the new meshes added. A character with no rows yet gets
/// none: its clothes aren't told from its body until it has them, so nothing could say what its underwear is under
/// (SkinCover, UnderwearFit). The user asked for it with the underwear slots (2026-11-12), so new avatars and outfits keep
/// every combination open without anyone sorting their underwear by hand.
/// </summary>
internal static class BoothUnderwear
{
    private const string PartsPath = "Assets/_Project/Resources/Tables/CharacterParts.csv";
    private const string ItemsPath = "Assets/_Project/Resources/Tables/Outfits.csv";
    private const string Top = "undertop", Bottom = "underbottom";
    private const string Redo = "Tools → Characters → BOOTH 다시 가져오기를 눌러 주세요.";

    // Words of underwear in a mesh's name, whole words: MAYO_Underwear_Bra, "Bikini top String", Sera_Yura_Bikini_Panty.
    private static readonly HashSet<string> UnderWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "bra", "bras", "brassiere", "bikini", "panty", "panties", "underwear", "lingerie", "swimsuit", "swimwear", "leotard", "thong" };
    // Japanese and Korean ones run on inside a longer name, so any part of it.
    private static readonly string[] UnderPieces = { "下着", "ブラ", "パンティ", "ショーツ", "ビキニ", "水着", "レオタード", "속옷", "브라", "팬티", "비키니", "수영복" };
    private static readonly HashSet<string> TopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "bra", "bras", "brassiere", "top", "upper", "chest", "breast", "bust" };
    private static readonly string[] TopPieces = { "ブラ", "브라" };
    private static readonly HashSet<string> BottomWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "panty", "panties", "thong", "bottom", "lower", "under" };
    private static readonly string[] BottomPieces = { "パンティ", "ショーツ", "팬티" };

    /// <summary>A character's prefab, or an outfit model's prefab for one character, as the import reads it.</summary>
    internal readonly struct Source
    {
        public readonly string Id;
        public readonly GameObject Prefab;
        // The character it is (a character) or is made for (an outfit): where its hips and chest are.
        public readonly GameObject Character;

        public Source(string id, GameObject prefab, GameObject character)
        {
            Id = id;
            Prefab = prefab;
            Character = character;
        }
    }

    /// <summary>
    /// Writes the rows for the underwear found in <paramref name="characters"/> and <paramref name="outfits"/>. False while
    /// a table write waits for its file (TableFile): it lands by itself.
    /// </summary>
    internal static bool Write(List<Source> characters, List<Source> outfits)
    {
        bool landed = true;
        try
        {
            landed &= WriteCharacters(characters);
            landed &= WriteOutfits(outfits);
        }
        catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning($"[BOOTH] 속옷 칸 줄을 쓰지 못했습니다({error.Message}). {Redo}");
            return false;
        }
        return landed;
    }

    // A character's own underwear, into its rows of the underwear slots.
    private static bool WriteCharacters(List<Source> characters)
    {
        if (characters.Count == 0) return true;
        var table = Table.Read(PartsPath);
        if (table == null) return true;
        int skinColumn = table.Column("skin"), slotColumn = table.Column("slot"), partsColumn = table.Column("parts");
        if (skinColumn < 0 || slotColumn < 0 || partsColumn < 0) return true;
        foreach (var source in characters)
        {
            var rows = table.Rows.Where(row => table.Cell(row, skinColumn).Equals(source.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (rows.Count == 0)
            {
                if (Underwear(source.Prefab).Any())
                    Debug.Log($"[BOOTH] {source.Id}: CharacterParts.csv에 이 캐릭터의 옷 줄이 없어 속옷을 속옷 칸에 넣지 않았습니다. 옷 줄을 적은 뒤 다시 가져오면 들어갑니다.");
                continue;
            }
            var listed = new HashSet<string>(rows.SelectMany(row => Split(table.Cell(row, partsColumn))), StringComparer.OrdinalIgnoreCase);
            var frame = Frame.Of(source.Character, source.Prefab);
            foreach (var renderer in Underwear(source.Prefab))
            {
                if (!listed.Add(renderer.name)) continue;
                // Its own one-piece goes in the top's row: a character's own piece takes no other slot.
                string slot = SlotOf(renderer, frame, out _);
                int row = Find(rows, r => table.Cell(r, slotColumn).Equals(slot, StringComparison.OrdinalIgnoreCase));
                if (row >= 0) table.Append(row, partsColumn, renderer.name);
                else
                {
                    rows.Add(table.Add(new Dictionary<string, string>
                    {
                        ["id"] = source.Id + "." + slot,
                        ["skin"] = source.Id,
                        ["slot"] = slot,
                        ["parts"] = renderer.name,
                    }));
                }
                Debug.Log($"[BOOTH] {source.Id}: '{renderer.name}'을 속옷 칸({slot})의 원래 속옷으로 넣었습니다.");
            }
        }
        return table.Save(PartsPath, "속옷 칸 줄");
    }

    // An outfit's underwear, as items of its model in the underwear slots.
    private static bool WriteOutfits(List<Source> outfits)
    {
        if (outfits.Count == 0) return true;
        var table = Table.Read(ItemsPath);
        if (table == null) return true;
        int idColumn = table.Column("id"), slotColumn = table.Column("slot"), alsoColumn = table.Column("also"),
            modelColumn = table.Column("model"), partsColumn = table.Column("parts");
        if (idColumn < 0 || slotColumn < 0 || modelColumn < 0 || partsColumn < 0) return true;
        string ModelOf(int row)
        {
            string model = table.Cell(row, modelColumn);
            return model.Length > 0 ? model : table.Cell(row, idColumn);
        }
        foreach (var source in outfits)
        {
            var named = new HashSet<string>(table.Rows.Where(row => ModelOf(row).Equals(source.Id, StringComparison.OrdinalIgnoreCase))
                .SelectMany(row => Split(table.Cell(row, partsColumn))), StringComparer.OrdinalIgnoreCase);
            var frame = Frame.Of(source.Character, source.Prefab);
            foreach (var renderer in Underwear(source.Prefab))
            {
                if (!named.Add(renderer.name)) continue;
                string slot = SlotOf(renderer, frame, out bool both);
                string id = source.Id + "-" + slot;
                int row = Find(table.Rows, r => table.Cell(r, idColumn).Equals(id, StringComparison.OrdinalIgnoreCase));
                for (int n = 2; row >= 0 && !ModelOf(row).Equals(source.Id, StringComparison.OrdinalIgnoreCase); n++)
                {
                    id = source.Id + "-" + slot + "-" + n;
                    row = Find(table.Rows, r => table.Cell(r, idColumn).Equals(id, StringComparison.OrdinalIgnoreCase));
                }
                if (row >= 0) table.Append(row, partsColumn, renderer.name);
                else
                {
                    table.Add(new Dictionary<string, string>
                    {
                        ["id"] = id,
                        ["slot"] = slot,
                        ["also"] = both && alsoColumn >= 0 ? Bottom : "",
                        ["name"] = Readable(renderer.name),
                        ["model"] = source.Id,
                        ["parts"] = renderer.name,
                    });
                }
                Debug.Log($"[BOOTH] {source.Id}: '{renderer.name}'을 속옷 칸({slot}) 옷 '{id}'으로 넣었습니다.");
            }
        }
        return table.Save(ItemsPath, "속옷 옷 줄");
    }

    // The prefab's meshes named like underwear.
    private static IEnumerable<Renderer> Underwear(GameObject prefab)
    {
        if (prefab == null) yield break;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            if ((renderer is SkinnedMeshRenderer || renderer is MeshRenderer) && Named(renderer.name, UnderWords, UnderPieces)) yield return renderer;
    }

    // Whole words split at anything but letters and at a small letter followed by a capital (UnderwearTop).
    private static readonly Regex Breaks = new Regex(@"[^\p{L}]+|(?<=\p{Ll})(?=\p{Lu})", RegexOptions.Compiled);

    private static bool Named(string name, HashSet<string> words, string[] pieces)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (string word in Breaks.Split(name))
            if (word.Length > 0 && words.Contains(word)) return true;
        foreach (string piece in pieces)
            if (name.IndexOf(piece, StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    // The underwear slot a mesh goes in: its name says, else where it sits; both when it reaches from the hips to the chest.
    private static string SlotOf(Renderer renderer, Frame frame, out bool both)
    {
        both = false;
        bool top = Named(renderer.name, TopWords, TopPieces), bottom = Named(renderer.name, BottomWords, BottomPieces);
        if (top != bottom) return top ? Top : Bottom;
        if (frame == null || !(MeshOf(renderer) is Mesh mesh)) return Top;
        var box = BoxOf(mesh.bounds, frame.ToCharacter * renderer.transform.localToWorldMatrix);
        both = box.min.y < frame.Hips + .02f && box.max.y > frame.Chest - .02f;
        return both || box.center.y > (frame.Hips + frame.Chest) * .5f ? Top : Bottom;
    }

    // A mesh's name without its maker's prefix, spaced (Banshaku Shirt_Bikini top → Bikini top).
    private static string Readable(string name)
    {
        int cut = name.LastIndexOf('_');
        string tail = cut >= 0 && cut < name.Length - 1 ? name.Substring(cut + 1) : name;
        return tail.Replace('_', ' ').Trim();
    }

    private static Mesh MeshOf(Renderer renderer) =>
        renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

    private static Bounds BoxOf(Bounds local, Matrix4x4 matrix)
    {
        var box = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            box.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
        return box;
    }

    private static IEnumerable<string> Split(string list) =>
        (list ?? "").Split('|').Select(entry => entry.Trim()).Where(entry => entry.Length > 0);

    // The first row that matches, or -1.
    private static int Find(IEnumerable<int> rows, Func<int, bool> match)
    {
        foreach (int row in rows)
            if (match(row)) return row;
        return -1;
    }

    // Heights of the character's hips and chest, and a prefab's space turned into the character's (both stand their
    // roots at one origin, as dressing aligns them).
    private sealed class Frame
    {
        public float Hips, Chest;
        public Matrix4x4 ToCharacter;

        public static Frame Of(GameObject character, GameObject prefab)
        {
            if (character == null || prefab == null) return null;
            var animator = character.GetComponentInChildren<Animator>(true);
            var avatar = animator != null ? animator.avatar : null;
            if (avatar == null || !avatar.isHuman) return null;
            string hipsName = null, chestName = null;
            foreach (var human in avatar.humanDescription.human)
            {
                if (human.humanName == "Hips") hipsName = human.boneName;
                else if (human.humanName == "Chest") chestName = human.boneName;
                else if (human.humanName == "UpperChest" && chestName == null) chestName = human.boneName;
            }
            var bones = character.GetComponentsInChildren<Transform>(true);
            var hips = bones.FirstOrDefault(bone => bone.name == hipsName);
            var chest = bones.FirstOrDefault(bone => bone.name == chestName);
            if (hips == null || chest == null) return null;
            var toCharacter = character.transform.worldToLocalMatrix;
            float hipsY = toCharacter.MultiplyPoint3x4(hips.position).y, chestY = toCharacter.MultiplyPoint3x4(chest.position).y;
            if (chestY <= hipsY) return null;
            return new Frame { Hips = hipsY, Chest = chestY, ToCharacter = prefab.transform.worldToLocalMatrix };
        }
    }

    // A table's lines as the file has them (rows of these tables never break across lines), changed in place.
    private sealed class Table
    {
        private string newline;
        private List<string> header;
        private readonly List<string> lines = new List<string>();
        private readonly List<List<string>> cells = new List<List<string>>();
        private bool changed;
        private string original;

        // Data rows: indexes into the lines (not the header, not # notes).
        public IEnumerable<int> Rows => Enumerable.Range(1, Math.Max(0, lines.Count - 1))
            .Where(i => cells[i] != null && cells[i].Count > 0 && cells[i][0].Trim().Length > 0 && cells[i][0].Trim()[0] != '#');

        public static Table Read(string path)
        {
            string text = TableFile.Read(path);
            if (string.IsNullOrEmpty(text)) return null;
            var table = new Table { original = text, newline = text.Contains("\r\n") ? "\r\n" : "\n" };
            foreach (string line in text.Split(new[] { table.newline }, StringSplitOptions.None))
            {
                table.lines.Add(line);
                table.cells.Add(line.Length == 0 ? null : CsvTable.ReadRecords(line).FirstOrDefault());
            }
            while (table.lines.Count > 1 && table.lines[table.lines.Count - 1].Length == 0)
            {
                table.lines.RemoveAt(table.lines.Count - 1);
                table.cells.RemoveAt(table.cells.Count - 1);
            }
            if (table.cells[0] == null) return null;
            table.header = table.cells[0].Select(cell => cell.Trim().TrimStart('﻿')).ToList();
            return table;
        }

        public int Column(string name) => header.IndexOf(name);

        public string Cell(int row, int column) =>
            column >= 0 && cells[row] != null && column < cells[row].Count ? cells[row][column].Trim() : "";

        // Adds a name to a row's list of renderers.
        public void Append(int row, int column, string name)
        {
            var record = cells[row].ToList();
            while (record.Count < header.Count) record.Add("");
            string list = record[column].Trim();
            record[column] = list.Length > 0 ? list + "|" + name : name;
            cells[row] = record;
            lines[row] = string.Join(",", record.Select(Quote));
            changed = true;
        }

        // A new row at the end, its cells by column; returns its index.
        public int Add(Dictionary<string, string> row)
        {
            var record = header.Select(column => row.TryGetValue(column, out var value) ? value ?? "" : "").ToList();
            cells.Add(record);
            lines.Add(string.Join(",", record.Select(Quote)));
            changed = true;
            return lines.Count - 1;
        }

        public bool Save(string path, string what)
        {
            if (!changed) return !TableFile.Waiting(path);
            var text = new StringBuilder();
            foreach (string line in lines) text.Append(line).Append(newline);
            if (text.ToString() == original) return !TableFile.Waiting(path);
            return TableFile.Write(path, text.ToString(), what, Redo);
        }

        private static string Quote(string cell) =>
            cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
    }
}
