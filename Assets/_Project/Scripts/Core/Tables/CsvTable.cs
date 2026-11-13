using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// One parsed balance table: row id → (field name → raw cell text). Two layouts are accepted:
/// a row table whose first column is the row id and whose other headers are field names, and a key table with the
/// header "key,value" whose keys read "Row.field". Rows or headers starting with '#' and empty cells are skipped.
/// Row ids and field names do not mind upper or lower case, so a mod's column "Damage" lands on "damage".
/// What looks like a slip of the hand (a row written twice, a cell cut in two by a comma without quotes) is kept in
/// <see cref="Problems"/> for whoever loads the table to say out loud; the table is read as well as it can be anyway.
/// </summary>
public sealed class CsvTable
{
    private readonly Dictionary<string, Dictionary<string, string>> rows =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> problems = new List<string>();

    public IEnumerable<string> RowIds => rows.Keys;

    /// <summary>What looked wrong in the text, a line each, in words for the person who wrote the table.</summary>
    public IReadOnlyList<string> Problems => problems;

    private CsvTable() { }

    public bool TryGetRow(string id, out IReadOnlyDictionary<string, string> row)
    {
        bool found = rows.TryGetValue(id ?? "", out var cells);
        row = cells;
        return found;
    }

    public static CsvTable Parse(string text)
    {
        var table = new CsvTable();
        var records = ReadRecords(text ?? "");
        if (records.Count == 0) return table;
        var header = records[0];
        bool keyLayout = header.Count > 1 && IsHeader(header[0], "key") && IsHeader(header[1], "value");
        if (!keyLayout) table.CheckHeader(header);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int r = 1; r < records.Count; r++)
        {
            var record = records[r];
            string first = record[0].Trim();
            if (first.Length == 0 || first[0] == '#') continue;
            // Cells past the last header belong to no column. The usual cause is a comma inside a cell that was not
            // put in quotes: the cell ends at the comma and the rest slides one column along.
            for (int c = header.Count; c < record.Count; c++)
                if (record[c].Trim().Length > 0)
                {
                    table.problems.Add($"'{first}' 줄에 칸이 {record.Count}개인데 열은 {header.Count}개입니다. 쉼표가 든 칸은 \"큰따옴표\"로 감싸야 합니다(안 그러면 쉼표에서 잘립니다)."); // dev-text
                    break;
                }
            if (!seen.Add(first))
                table.problems.Add(keyLayout ? $"'{first}'가 두 번 있습니다. 아래 줄의 값을 씁니다." : $"'{first}' 줄이 두 번 있습니다. 아래 줄의 칸이 위 줄을 덮습니다."); // dev-text
            if (keyLayout)
            {
                if (record.Count < 2) continue;
                int dot = first.IndexOf('.');
                table.Set(dot < 0 ? "" : first.Substring(0, dot), dot < 0 ? first : first.Substring(dot + 1), record[1]);
                continue;
            }
            for (int c = 1; c < record.Count && c < header.Count; c++)
            {
                string field = header[c].Trim();
                if (field.Length > 0 && field[0] != '#') table.Set(first, field, record[c]);
            }
        }
        return table;
    }

    /// <summary>
    /// Lays another parse of the same table over this one, as a mod's table goes over the game's (GameMods): a row
    /// with an id this table has takes the other's filled cells and keeps the rest, a new id adds a row after the
    /// ones here. An empty cell never blanks a filled one (Set skips empty cells in both).
    /// </summary>
    public void Overlay(CsvTable other)
    {
        if (other == null) return;
        foreach (var row in other.rows)
        {
            if (!rows.TryGetValue(row.Key, out var cells)) rows[row.Key] = cells = NewCells();
            foreach (var cell in row.Value) cells[cell.Key] = cell.Value;
        }
    }

    private void Set(string id, string field, string raw)
    {
        raw = raw.Trim();
        if (field.Length == 0 || raw.Length == 0) return;
        if (!rows.TryGetValue(id, out var cells)) rows[id] = cells = NewCells();
        cells[field] = raw;
    }

    private static Dictionary<string, string> NewCells() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Two columns of one name: the later one would silently win every row.
    private void CheckHeader(List<string> header)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int c = 1; c < header.Count; c++)
        {
            string field = header[c].Trim();
            if (field.Length > 0 && field[0] != '#' && !names.Add(field))
                problems.Add($"'{field}' 열이 두 번 있습니다. 뒤쪽 열의 값을 씁니다."); // dev-text
        }
    }

    private static bool IsHeader(string cell, string expected) => string.Equals(cell.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every record of a CSV text, cells as written, '#' columns and rows included (the inspector shows Surfaces.csv's
    /// notes). RFC 4180: comma separators, quoted cells with "" escapes and line breaks. Excel's UTF-8 byte order mark is
    /// ignored.
    /// </summary>
    public static List<List<string>> ReadRecords(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false, any = false;
        int start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c != '"') cell.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = false;
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; any = true; break;
                case ',': record.Add(cell.ToString()); cell.Clear(); any = true; break;
                case '\r': break;
                case '\n': EndRecord(); break;
                default: cell.Append(c); any = true; break;
            }
        }
        EndRecord();
        return records;

        void EndRecord()
        {
            if (any) { record.Add(cell.ToString()); records.Add(record); }
            record = new List<string>(); cell.Clear(); any = false;
        }
    }
}
