using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// A table's lines as the file has them (rows of these tables never break across lines), changed in place and written
/// back through TableFile only when something changed, so rows written by hand keep their words and their order. The
/// BOOTH import writes CharacterParts.csv, Outfits.csv, OutfitFits.csv and Texts.csv with it (BoothUnderwear,
/// BoothPieces).
/// </summary>
internal sealed class BoothTable
{
    internal const string Redo = "Tools → Characters → BOOTH 다시 가져오기를 눌러 주세요.";
    private const string TextsPath = "Assets/_Project/Resources/Tables/Texts.csv";

    private string newline;
    private List<string> header;
    private readonly List<string> lines = new List<string>();
    private readonly List<List<string>> cells = new List<List<string>>();
    private bool changed;
    private string original;

    /// <summary>Data rows: indexes into the lines (not the header, not # notes).</summary>
    public IEnumerable<int> Rows => Enumerable.Range(1, Math.Max(0, lines.Count - 1))
        .Where(i => cells[i] != null && cells[i].Count > 0 && cells[i][0].Trim().Length > 0 && cells[i][0].Trim()[0] != '#');

    /// <summary>The table as TableFile has it (a write still waiting, else the file); null when it is empty.</summary>
    public static BoothTable Read(string path)
    {
        string text = TableFile.Read(path);
        if (string.IsNullOrEmpty(text)) return null;
        var table = new BoothTable { original = text, newline = text.Contains("\r\n") ? "\r\n" : "\n" };
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
        column >= 0 && row >= 0 && row < cells.Count && cells[row] != null && column < cells[row].Count ? cells[row][column].Trim() : "";

    /// <summary>The first data row that matches, or -1.</summary>
    public int Find(Func<int, bool> match)
    {
        foreach (int row in Rows)
            if (match(row)) return row;
        return -1;
    }

    /// <summary>The data row whose first cell is this id, or -1.</summary>
    public int FindId(string id) => Find(row => Cell(row, 0).Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a name to a row's list ("a|b") in that column.</summary>
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

    /// <summary>A new row at the end, its cells by column; returns its index.</summary>
    public int Add(Dictionary<string, string> row) => Insert(lines.Count - 1, row);

    /// <summary>A new row right after line <paramref name="after"/>, its cells by column; returns its index.</summary>
    public int Insert(int after, Dictionary<string, string> row)
    {
        var record = header.Select(column => row.TryGetValue(column, out var value) ? value ?? "" : "").ToList();
        int at = Math.Max(1, Math.Min(after + 1, lines.Count));
        cells.Insert(at, record);
        lines.Insert(at, string.Join(",", record.Select(Quote)));
        changed = true;
        return at;
    }

    /// <summary>False while the write waits for its file (TableFile): it lands by itself.</summary>
    public bool Save(string path, string what)
    {
        if (!changed) return !TableFile.Waiting(path);
        var text = new StringBuilder();
        foreach (string line in lines) text.Append(line).Append(newline);
        if (text.ToString() == original) return !TableFile.Waiting(path);
        return TableFile.Write(path, text.ToString(), what, Redo);
    }

    /// <summary>
    /// Puts the words of new rows' names into Texts.csv, where the 글자 편집기 edits them (tables hold Texts ids, words
    /// live in Texts.csv: 2026-11-12). An id Texts.csv has already keeps its words. Each goes after the last row whose id
    /// begins as its own does (a new outfit's name beside the other outfit names), else at the end. False while the write
    /// waits for its file.
    /// </summary>
    internal static bool AddTexts(IEnumerable<(string Id, string Words, string Note)> texts)
    {
        var table = Read(TextsPath);
        if (table == null) return true;
        int korean = table.Column("ko"), note = table.Column("#어디");
        if (korean < 0) return true;
        foreach (var (id, words, where) in texts)
        {
            if (table.FindId(id) >= 0) continue;
            int cut = id.IndexOf('.');
            string family = cut > 0 ? id.Substring(0, cut + 1) : id;
            int after = -1;
            foreach (int row in table.Rows)
            {
                string other = table.Cell(row, 0);
                if (other.StartsWith(family, StringComparison.OrdinalIgnoreCase) && other.EndsWith(".name", StringComparison.OrdinalIgnoreCase)) after = row;
            }
            var cellsOf = new Dictionary<string, string> { [table.header[0]] = id, [table.header[korean]] = words };
            if (note >= 0) cellsOf[table.header[note]] = where;
            if (after >= 0) table.Insert(after, cellsOf);
            else table.Add(cellsOf);
        }
        return table.Save(TextsPath, "이름 글자");
    }

    private static string Quote(string cell) =>
        cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
}
