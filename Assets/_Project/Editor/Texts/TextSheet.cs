using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Texts.csv as the 글자 편집기 (TextEditorWindow) sees it: each row's id, its words in every language column, its note
/// (#어디) and line. Loading changes nothing in the file. Saving writes only the cells that were changed, each onto the
/// file as it is on disk at that moment, so rows other sessions or tools added meanwhile stay, and a cell someone else
/// changed since it was loaded is left for the user to look at, not overwritten.
/// </summary>
internal sealed class TextSheet
{
    internal static string AssetPath => TablePaths.Of(Texts.TableName);
    private const string NoteColumn = "#어디";

    internal sealed class Row
    {
        public string Id = "";
        /// <summary>Which of the rows with this id it is (0 for the first): a repeated id is a mistake to show, not hide.</summary>
        public int Occurrence;
        /// <summary>The #--- heading it sits under in the file.</summary>
        public string Section = "";
        public int Line;
        /// <summary>The words per column (Columns' order) as the file had them when loaded, and as edited now.</summary>
        public string[] Saved, Words;
        /// <summary>Why the last save left this row's edit unwritten (someone else changed the cell), or null.</summary>
        public string Conflict;

        public bool Changed(int column) => column > 0 && column < Words.Length && Words[column] != Saved[column];
        public bool Dirty => Words.Where((_, column) => Changed(column)).Any();
    }

    internal sealed class SaveResult
    {
        public int Written, Conflicts;
        /// <summary>The file was held (Excel and the like) and the write waits for it (TableFile).</summary>
        public bool Waiting;
        /// <summary>Each changed row with its words before and after, for the labels already on screen (TextLiveSwap).</summary>
        public readonly List<(string id, string column, string before, string after)> Changes = new List<(string, string, string, string)>();
    }

    /// <summary>The header's columns: id first, then the languages, then notes (#…).</summary>
    internal readonly List<string> Columns = new List<string>();
    internal readonly List<Row> Rows = new List<Row>();
    /// <summary>The file's size and time when it was read, to notice a change made elsewhere.</summary>
    internal string Stamp { get; private set; } = "";

    internal int Column(string name) => Columns.FindIndex(c => string.Equals(c.Trim(), name, StringComparison.OrdinalIgnoreCase));
    internal int Korean => Column(Texts.Korean);
    internal int Note => Column(NoteColumn);

    /// <summary>The language columns (ko first as the file has it).</summary>
    internal IEnumerable<string> Languages => Columns.Skip(1).Where(c => c.Trim().Length > 0 && !c.Trim().StartsWith("#"));

    internal static TextSheet Load()
    {
        var sheet = new TextSheet();
        sheet.Read(TableFile.Read(AssetPath));
        sheet.Stamp = StampOf(AssetPath);
        return sheet;
    }

    /// <summary>
    /// Reads the file again (someone else saved it) and keeps this sheet's unsaved edits on the rows that are still there.
    /// An edited cell whose words changed on disk meanwhile is marked (Conflict) so the user decides.
    /// </summary>
    internal void Refresh() => Reread(true);

    // Reads the file again, keeping the unsaved edits (and their marks) on the rows still there; with flag, an edited cell
    // whose words changed on disk since is marked too.
    private void Reread(bool flag)
    {
        var edits = Rows.Where(r => r.Dirty).ToList();
        var oldColumns = Columns.ToList();
        Read(TableFile.Read(AssetPath));
        Stamp = StampOf(AssetPath);
        foreach (var edit in edits)
        {
            var row = Find(edit.Id, edit.Occurrence);
            if (row == null) continue;
            row.Conflict = edit.Conflict;
            for (int oldColumn = 1; oldColumn < oldColumns.Count; oldColumn++)
            {
                if (!edit.Changed(oldColumn)) continue;
                int column = Column(oldColumns[oldColumn]);
                if (column < 0) continue;
                row.Words[column] = edit.Words[oldColumn];
                if (flag && row.Saved[column] != edit.Saved[oldColumn] && row.Saved[column] != edit.Words[oldColumn])
                    row.Conflict = $"'{oldColumns[oldColumn]}' 칸이 다른 곳에서 바뀌었어요(지금 파일: \"{row.Saved[column]}\"). 고친 글자를 남겼으니 확인하고 저장하세요.";
            }
        }
    }

    /// <summary>Whether the file changed on disk since this sheet read it.</summary>
    internal bool ChangedOnDisk => StampOf(AssetPath) != Stamp;

    internal Row Find(string id, int occurrence = 0)
    {
        foreach (var row in Rows)
            if (row.Occurrence == occurrence && string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)) return row;
        return null;
    }

    private void Read(string text)
    {
        Columns.Clear();
        Rows.Clear();
        var records = CsvSpans.Parse(text);
        if (records.Count == 0) return;
        Columns.AddRange(records[0].Cells.Select(c => c.Value.Trim()));
        string section = "";
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int r = 1; r < records.Count; r++)
        {
            var record = records[r];
            string id = record[0].Trim();
            if (id.StartsWith("#"))
            {
                if (id.StartsWith("#---")) section = id.Trim('#', '-', ' ');
                continue;
            }
            if (id.Length == 0) continue;
            seen.TryGetValue(id, out int occurrence);
            seen[id] = occurrence + 1;
            var words = new string[Columns.Count];
            for (int c = 0; c < words.Length; c++) words[c] = record[c];
            Rows.Add(new Row
            {
                Id = id, Occurrence = occurrence, Section = section, Line = record.Line,
                Saved = words, Words = (string[])words.Clone()
            });
        }
    }

    /// <summary>
    /// Writes every changed cell onto the file as it is now (TableFile: in place, waiting if a program holds the file).
    /// A cell that changed on disk since it was loaded is left unwritten and its row marked; one that already says the
    /// new words counts as written.
    /// </summary>
    internal SaveResult Save()
    {
        var result = new SaveResult();
        string text = TableFile.Read(AssetPath);
        var records = CsvSpans.Parse(text);
        if (records.Count == 0) return result;
        var header = records[0].Cells.Select(c => c.Value.Trim()).ToList();
        // Which record each (id, occurrence) is now.
        var at = new Dictionary<string, CsvSpans.Record>(StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int r = 1; r < records.Count; r++)
        {
            string id = records[r][0].Trim();
            if (id.Length == 0 || id.StartsWith("#")) continue;
            seen.TryGetValue(id, out int occurrence);
            seen[id] = occurrence + 1;
            at[id + "\n" + occurrence] = records[r];
        }
        var edits = new Dictionary<CsvSpans.Record, Dictionary<int, string>>();
        var landed = new List<(Row row, int column)>();
        foreach (var row in Rows.Where(r => r.Dirty))
        {
            row.Conflict = null;
            if (!at.TryGetValue(row.Id + "\n" + row.Occurrence, out var record))
            {
                row.Conflict = "이 줄이 파일에서 사라졌어요(다른 곳에서 지웠거나 id를 바꿨어요). 고친 글자는 저장되지 않았어요.";
                result.Conflicts++;
                continue;
            }
            for (int column = 1; column < Columns.Count; column++)
            {
                if (!row.Changed(column)) continue;
                int onDisk = header.FindIndex(c => string.Equals(c, Columns[column], StringComparison.OrdinalIgnoreCase));
                if (onDisk < 0)
                {
                    row.Conflict = $"'{Columns[column]}' 열이 파일에서 사라졌어요.";
                    result.Conflicts++;
                    continue;
                }
                string now = record[onDisk];
                if (now == row.Words[column])
                {
                    landed.Add((row, column));
                    continue;
                }
                if (now != row.Saved[column])
                {
                    row.Conflict = $"'{Columns[column]}' 칸을 다른 곳에서 먼저 바꿨어요(지금 파일: \"{now}\"). 덮어쓰지 않았어요. 확인하고 다시 저장하세요.";
                    row.Saved[column] = now;
                    result.Conflicts++;
                    continue;
                }
                if (!edits.TryGetValue(record, out var cells)) edits[record] = cells = new Dictionary<int, string>();
                cells[onDisk] = row.Words[column];
                landed.Add((row, column));
                result.Changes.Add((row.Id, Columns[column], row.Saved[column], row.Words[column]));
            }
        }
        if (edits.Count > 0)
        {
            // Back to front, so each record's place in the text is still where the parse found it.
            foreach (var edit in edits.OrderByDescending(e => e.Key.Cells[0].Start))
                text = CsvSpans.SetCells(text, edit.Key, edit.Value);
            result.Waiting = !TableFile.Write(AssetPath, text, "글자 편집기의 고친 글자",
                "ENFILADE → 글자 → 글자 편집기를 열어 다시 저장해 주세요.");
        }
        foreach (var (row, column) in landed) row.Saved[column] = row.Words[column];
        result.Written = landed.Count;
        // Rows whose edit was not written keep it, marked.
        Reread(false);
        return result;
    }

    // ---- What a line must keep ----

    // A hole the code fills: {0}, {1:0.0}, {2,5}, and Perks.csv's {Row.name} numbers. {{ and }} are braces as words.
    private static readonly Regex HoleRegex = new Regex(@"\{\{|\}\}|\{([^{}:,]+)(?:[:,][^{}]*)?\}", RegexOptions.Compiled);

    /// <summary>The holes in the words, each once, by name ("0", "Assault.chargeSeconds").</summary>
    internal static SortedSet<string> Holes(string words)
    {
        var holes = new SortedSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(words)) return holes;
        foreach (Match match in HoleRegex.Matches(words))
            if (match.Groups[1].Success) holes.Add(match.Groups[1].Value.Trim());
        return holes;
    }

    // Braces left over once every hole and escaped pair is taken out: string.Format would throw on them.
    private static bool LoneBrace(string words) => HoleRegex.Replace(words ?? "", "").IndexOfAny(new[] { '{', '}' }) >= 0;

    private static readonly Regex KeyLabel = new Regex(@"\[[^\[\]]{1,12}\]", RegexOptions.Compiled);

    /// <summary>
    /// What would go wrong with the row's words as they are now, against what the file had (the code fills the holes the
    /// line had, and splits some lines at '|'), and between the languages. Errors break the line on screen; notes are
    /// worth a look.
    /// </summary>
    internal List<(bool error, string what)> Problems(Row row)
    {
        var problems = new List<(bool, string)>();
        int ko = Korean;
        if (ko < 0) return problems;
        string words = row.Words[ko] ?? "", saved = row.Saved[ko] ?? "";
        if (words.Trim().Length == 0)
            problems.Add((true, $"한국어 칸이 비었어요. 화면에 [{row.Id}]가 그대로 보여요."));
        foreach (int column in LanguageColumns())
        {
            string now = row.Words[column] ?? "", before = row.Saved[column] ?? "";
            string name = Columns[column];
            if (now.Length == 0) continue;
            var holes = Holes(now);
            if (row.Changed(column))
            {
                var had = Holes(before);
                var lost = had.Except(holes).ToList();
                var added = holes.Except(had).ToList();
                if (lost.Count > 0) problems.Add((true, $"{name}: 코드가 채우는 자리 {Join(lost)}가 빠졌어요. 그 값이 화면에 안 나와요."));
                if (added.Count > 0 && before.Length > 0)
                    problems.Add((true, $"{name}: 새 자리 {Join(added)}는 코드가 채우지 않아요. 줄이 통째로 깨져 보일 수 있어요."));
                // Only an option list needs its count: a body slider's two end words or a choice row's chips, one per
                // option of the model. A story's lines are free (Enter makes as many as the words need, Texts.Lines).
                int pipesBefore = before.Count(c => c == '|'), pipesNow = now.Count(c => c == '|');
                if (OptionList(row.Id) && pipesBefore != pipesNow && before.Length > 0)
                    problems.Add((true, $"{name}: | 로 나눈 고르기 칸이 {pipesBefore + 1}개에서 {pipesNow + 1}개가 됐어요. 이 줄은 모델의 고르기 수와 같아야 해요(왼쪽부터 차례로 짝지어요)."));
                var keysBefore = KeyLabel.Matches(before).Cast<Match>().Select(m => m.Value).Distinct().ToList();
                var keysGone = keysBefore.Where(k => now.IndexOf(k, StringComparison.Ordinal) < 0).ToList();
                if (keysGone.Count > 0)
                    problems.Add((false, $"{name}: 키 표시 {string.Join(" ", keysGone)}가 빠졌어요. 대괄호 안 기본 키는 플레이어가 고른 키로 바뀌어 보여요."));
                if (now != now.Trim()) problems.Add((false, $"{name}: 칸 앞뒤 공백은 읽을 때 지워져요."));
            }
            // Only a line with holes goes through string.Format, the one thing a lone brace breaks.
            if (holes.Count > 0 && LoneBrace(now)) problems.Add((true, $"{name}: 짝 없는 중괄호가 있어요. 글자로 쓰려면 {{{{ }}}}로 두 번 써요."));
            if (column != ko && words.Length > 0)
            {
                var koHoles = Holes(words);
                if (!koHoles.SetEquals(holes))
                    problems.Add((true, $"{name}: 자리 {Join(holes)}가 한국어의 {Join(koHoles)}와 달라요."));
            }
        }
        return problems;
    }

    // Rows whose '|' splits a fixed list of options, matched by position (BodyShapes: a slider's ends, a choice's chips).
    internal static bool OptionList(string id) => id.EndsWith(".choices", StringComparison.OrdinalIgnoreCase)
                                                  || id.EndsWith(".ends", StringComparison.OrdinalIgnoreCase);

    internal IEnumerable<int> LanguageColumns()
    {
        for (int c = 1; c < Columns.Count; c++)
            if (Columns[c].Length > 0 && !Columns[c].StartsWith("#")) yield return c;
    }

    private static string Join(IEnumerable<string> holes) => holes.Any() ? string.Join(" ", holes.Select(h => "{" + h + "}")) : "(없음)";

    // ---- Areas ----

    /// <summary>
    /// The 구역 filter: areas of the game and the id beginnings in each (the first word of an id). Ids of no area show
    /// under 기타 with their first word.
    /// </summary>
    internal static readonly (string name, string[] starts)[] Areas =
    {
        ("메뉴·방·기록", new[] { "menu", "servers", "room", "net", "loading", "profile", "records", "common", "team", "map" }),
        ("설정·키", new[] { "settings", "crosshair", "key" }),
        ("HUD·알림", new[] { "hud", "objective", "wave", "score", "special", "money", "life", "ff", "hold", "execution", "point", "shield", "rank", "chat", "pickup", "feed" }),
        ("옷장·체형", new[] { "wardrobe", "outfit", "part", "body" }),
        ("콜아웃", new[] { "callout" }),
        ("클래스·카드", new[] { "class", "classes", "perk" }),
        ("캠페인·타워", new[] { "campaign", "chapter", "mission", "tower" }),
        ("허브·결과", new[] { "hub", "results" }),
        ("총·투척물·상점", new[] { "gun", "gunrank", "gunkills", "weapons", "skin", "grenade", "shop" }),
        ("좀비", new[] { "zombie" }),
    };

    internal static string AreaOf(string id)
    {
        int dot = id.IndexOf('.');
        string first = dot < 0 ? id : id.Substring(0, dot);
        foreach (var (name, starts) in Areas)
            if (starts.Any(s => string.Equals(s, first, StringComparison.OrdinalIgnoreCase))) return name;
        return "기타";
    }

    // ---- File ----

    internal static string FullPath(string assetPath) => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", assetPath));

    private static string StampOf(string assetPath)
    {
        try
        {
            var info = new FileInfo(FullPath(assetPath));
            return info.Exists ? info.Length + "@" + info.LastWriteTimeUtc.Ticks : "-";
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return "";
        }
    }
}
