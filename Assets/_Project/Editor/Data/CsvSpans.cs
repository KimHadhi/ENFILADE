using System.Collections.Generic;
using System.Text;

/// <summary>
/// A table's records with where each cell sits in the file's text, so an editor tool can change one cell and leave every
/// other byte as it was (its quotes, line endings, the rows other people are writing at the same time). Reads exactly as
/// <see cref="CsvTable.ReadRecords"/> does: commas, quoted cells with "" and line breaks, '\r' outside quotes dropped,
/// Excel's byte order mark skipped, blank lines making no record.
/// </summary>
internal static class CsvSpans
{
    /// <summary>One cell: its words, and its raw text's place in the file [Start, End), quotes included.</summary>
    internal struct Cell
    {
        public int Start, End;
        public string Value;
    }

    /// <summary>One record: its cells, where it ends (the end of its last cell) and the file line it starts on (1 is the first).</summary>
    internal sealed class Record
    {
        public readonly List<Cell> Cells = new List<Cell>();
        public int Line;
        public int End => Cells.Count > 0 ? Cells[Cells.Count - 1].End : 0;

        public string this[int column] => column < Cells.Count ? Cells[column].Value : "";
    }

    internal static List<Record> Parse(string text)
    {
        var records = new List<Record>();
        var record = new Record();
        var value = new StringBuilder();
        bool quoted = false, any = false;
        int start = text.Length > 0 && text[0] == (char)0xFEFF ? 1 : 0;
        int cellStart = start, line = 1, recordLine = 1;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n') line++;
            if (quoted)
            {
                if (c != '"') value.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = false;
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; any = true; break;
                case ',': EndCell(i); cellStart = i + 1; any = true; break;
                case '\r': break;
                case '\n': EndRecord(i); cellStart = i + 1; recordLine = line; break;
                default: value.Append(c); any = true; break;
            }
        }
        EndRecord(text.Length);
        return records;

        void EndCell(int at)
        {
            // A '\r' before the line's end is no part of the cell (outside quotes it is dropped on reading too).
            int end = at;
            while (end > cellStart && text[end - 1] == '\r') end--;
            record.Cells.Add(new Cell { Start = cellStart, End = end, Value = value.ToString() });
            value.Clear();
        }

        void EndRecord(int at)
        {
            if (any)
            {
                EndCell(at);
                record.Line = recordLine;
                records.Add(record);
            }
            record = new Record();
            value.Clear();
            any = false;
        }
    }

    /// <summary>The cell as it is written in a file: in quotes when it holds a comma, a quote or a line break.</summary>
    internal static string Quote(string value)
    {
        value ??= "";
        bool needs = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        return needs ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    /// <summary>
    /// The text with some cells of one record set (column → words): the record is written again from its own raw cells,
    /// the set ones quoted as needed, empty cells added when it was short of a column. Nothing outside it changes. Records
    /// edited one after another go back to front, so each record's place is still where the parse found it.
    /// </summary>
    internal static string SetCells(string text, Record record, IReadOnlyDictionary<int, string> values)
    {
        int last = record.Cells.Count - 1;
        foreach (int column in values.Keys) last = System.Math.Max(last, column);
        var built = new StringBuilder();
        for (int column = 0; column <= last; column++)
        {
            if (column > 0) built.Append(',');
            if (values.TryGetValue(column, out string value)) built.Append(Quote(value));
            else if (column < record.Cells.Count) built.Append(text, record.Cells[column].Start, record.Cells[column].End - record.Cells[column].Start);
        }
        int start = record.Cells[0].Start;
        return text.Substring(0, start) + built + text.Substring(record.End);
    }
}
