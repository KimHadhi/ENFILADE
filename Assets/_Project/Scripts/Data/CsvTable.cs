using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// One parsed balance table: row id → (field name → raw cell text). Two layouts are accepted:
/// a row table whose first column is the row id and whose other headers are field names, and a key table with the
/// header "key,value" whose keys read "Row.field". Rows or headers starting with '#' and empty cells are skipped.
/// </summary>
public sealed class CsvTable
{
    private readonly Dictionary<string, Dictionary<string, string>> rows =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    public string Name { get; }
    public IEnumerable<string> RowIds => rows.Keys;

    private CsvTable(string name) { Name = name; }

    public bool TryGetRow(string id, out IReadOnlyDictionary<string, string> row)
    {
        bool found = rows.TryGetValue(id ?? "", out var cells);
        row = cells;
        return found;
    }

    public static CsvTable Parse(string text, string name)
    {
        var table = new CsvTable(name);
        var records = ReadRecords(text);
        if (records.Count == 0) return table;
        var header = records[0];
        bool keyLayout = header.Count > 1 && IsHeader(header[0], "key") && IsHeader(header[1], "value");
        for (int r = 1; r < records.Count; r++)
        {
            var record = records[r];
            string first = record[0].Trim();
            if (first.Length == 0 || first[0] == '#') continue;
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

    private void Set(string id, string field, string raw)
    {
        raw = raw.Trim();
        if (field.Length == 0 || raw.Length == 0) return;
        if (!rows.TryGetValue(id, out var cells)) rows[id] = cells = new Dictionary<string, string>(StringComparer.Ordinal);
        cells[field] = raw;
    }

    private static bool IsHeader(string cell, string expected) => string.Equals(cell.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    // RFC 4180: comma separators, quoted cells with "" escapes and line breaks. Excel's UTF-8 byte order mark is ignored.
    private static List<List<string>> ReadRecords(string text)
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
