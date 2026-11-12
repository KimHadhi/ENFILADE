using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// The 하드코딩 검사 (Tools → 글자 → 하드코딩 검사, the 글자 편집기's 검사 tab) and the editor's "쓰는 곳". Every word a
/// player reads belongs in Texts.csv (Docs/MaintenanceGuide.md, 화면 문구), so this finds the ones that slipped past it:
/// Korean written straight into runtime code (sorted into words that may reach the screen and developer words: console
/// answers, logs, inspector tips, object names), English words put on a label in code, prefab and scene texts in Korean
/// with no TextKey, data table cells holding words instead of a Texts id, ids code or tables ask for that the table lacks
/// (they show as [id]), and broken rows of Texts.csv itself.
/// </summary>
internal static class TextScan
{
    internal enum Kind
    {
        /// <summary>Korean (or a word on a label) in runtime code that may reach the screen: belongs in Texts.csv.</summary>
        CodeWords,
        /// <summary>Korean in runtime code only developers see: console answers, logs, inspector tips, object names.</summary>
        DeveloperWords,
        /// <summary>A Korean text in a prefab or scene with no TextKey: shown as written, out of the editor's reach.</summary>
        PrefabWords,
        /// <summary>Words in a data table's cell instead of a Texts id: they show, but the editor can't see them.</summary>
        TableWords,
        /// <summary>An id code or a table asks for that Texts.csv lacks: the screen shows [id].</summary>
        MissingId,
        /// <summary>A Texts.csv row that is broken, repeated or empty.</summary>
        BrokenRow
    }

    internal sealed class Finding
    {
        public Kind Kind;
        /// <summary>The asset path, and the line in it (1 is the first; 0 when there is none).</summary>
        public string Path;
        public int Line;
        public string What;
        /// <summary>For developer words: why they may stay in code.</summary>
        public string Why = "";
    }

    private const string ScriptsFolder = "Assets/_Project/Scripts";
    private const string TablesFolder = "Assets/_Project/Resources/Tables";
    private static readonly string[] SceneFolders = { "Assets/_Project/Prefabs", "Assets/_Project/Scenes", "Assets/_Project/Resources" };

    /// <summary>
    /// Code only developers read, by path (a folder ends with '/'), optionally one method and one literal of it. A line
    /// can be let off on its own with a "// dev-text" comment.
    /// </summary>
    private static readonly (string path, string method, string literal, string why)[] Developer =
    {
        ("UI/Chatting/PlayerConsoleCommands.cs", null, null, "개발자 콘솔"),
        ("UI/Chatting/CombatTestCommands.cs", null, null, "개발자 콘솔"),
        ("UI/Chatting/DevConsole.cs", null, null, "개발자 콘솔"),
        ("UI/Chatting/GameCommands.cs", null, null, "개발자 콘솔"),
        ("Player/Classes/CardCommands.cs", null, null, "개발자 콘솔"),
        ("Debug/", null, null, "개발용 시험 도구"),
        ("Audio/SoundPreview.cs", null, null, "개발자 콘솔 /sound"),
        ("Loading/PsoWarmup.cs", null, null, "개발자 콘솔 /pso"),
        ("Network/NetworkStatsHUD.cs", null, null, "개발자 콘솔 /net"),
        ("Network/InternetRoom.cs", null, null, "연결 방식 설명: 콘솔 /net에만 나온다"),
        ("Network/GameSession.cs", null, "직접 연결(LAN·주소)", "연결 방식 설명: 콘솔 /net에만 나온다"),
        ("UI/Hud/HudOverlapCheck.cs", null, null, "HUD 겹침 검사 보고서"),
        ("Hub/ResultsMannequin.cs", null, null, "콘솔 /results 대답"),
        ("Hub/SteelTarget.cs", null, null, "과녁 조각의 오브젝트 이름"),
        ("Settings/KeyBindings.cs", null, null, "Texts의 [좌클릭] 같은 키 표시를 알아보는 짝"),
        ("Data/", null, null, "표 경고"),
        ("Combat/Waves/WaveManager.cs", null, null, "콘솔 /wave 대답(LastError)"),
        ("Enemies/ZombieBody.Hitboxes.cs", "ToggleHitboxes", null, "콘솔 /hitbox"),
        ("UI/Chatting/SinglePlayerChatManager.cs", "LoadMap", null, "콘솔 /map"),
        ("UI/Chatting/ChatMessageItem.cs", null, "가", "줄 높이를 재는 글자"),
    };

    // A console handler's answers (/hub, /results, /ff, /director …): methods named so.
    private static readonly Regex ConsoleMethod = new Regex(@"^(Command|\w*Commands?|Report|\w*Report)$");
    private static readonly Regex LogCall = new Regex(@"Debug\.(Log|LogWarning|LogError|LogFormat|LogException|Assert)|\bWarn\(|\bReport\(|LogWarning\(|LogError\(|throw new|Exception\(");
    private static readonly Regex ObjectName = new Regex(@"new GameObject\(\s*\$?@?""|\.name\s*=\s*\$?@?""|\bname:\s*\$?@?""");
    private static readonly Regex Register = new Regex(@"\.Register\(");
    private static readonly Regex MethodLine = new Regex(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|internal|protected|static|override|virtual|sealed|async|unsafe|new|extern|partial)\s+)+[\w<>\[\],.?() ]*?\s(\w+)\s*\(");
    // English set on a label in code ("RELOADING", "COUNTER"): two letters or more in a .text = "…" or SetText("…").
    private static readonly Regex EnglishLabel = new Regex(@"(\.text\s*=\s*|\.SetText\()\$?@?""([^""]*[A-Za-z]{2,}[^""]*)""");
    private static readonly Regex Hangul = new Regex(@"[가-힣ㄱ-ㆎ]");
    // Texts.Get("id"), Texts.Format("id", …), Texts.Has("id") with a whole id written in code.
    private static readonly Regex AskedId = new Regex(@"Texts\.(?:Get|Format|Has)\(\s*""([^""\s]+)""");

    internal static List<Finding> Run(TextSheet sheet)
    {
        var findings = new List<Finding>();
        var ids = new HashSet<string>(sheet.Rows.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        ScanCode(findings, ids);
        ScanTables(findings, ids);
        ScanPrefabs(findings);
        ScanSheet(findings, sheet);
        return findings;
    }

    // ---- Code ----

    private static void ScanCode(List<Finding> findings, HashSet<string> ids)
    {
        foreach (string file in Directory.GetFiles(ScriptsFolder, "*.cs", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            if (path.Contains("/Editor/")) continue;
            string relative = path.Substring(ScriptsFolder.Length + 1);
            string source = File.ReadAllText(path);
            var lines = source.Split('\n');
            bool wholeFileOff = lines.Take(40).Any(l => l.Contains("// dev-text: file"));
            var methods = MethodsByLine(lines);
            foreach (var literal in Literals(source))
            {
                string line = literal.Line - 1 < lines.Length ? lines[literal.Line - 1] : "";
                foreach (Match asked in AskedId.Matches(line))
                    if (!ids.Contains(asked.Groups[1].Value) && asked.Groups[1].Value == literal.Text)
                        findings.Add(new Finding { Kind = Kind.MissingId, Path = path, Line = literal.Line, What = $"Texts.csv에 없는 id \"{literal.Text}\": 화면에 [{literal.Text}]로 보여요" });
                if (!Hangul.IsMatch(literal.Text)) continue;
                string why = WhyDeveloper(relative, methods[Math.Min(literal.Line - 1, methods.Length - 1)], literal, line, wholeFileOff);
                findings.Add(new Finding
                {
                    Kind = why == null ? Kind.CodeWords : Kind.DeveloperWords, Path = path, Line = literal.Line,
                    What = "\"" + Short(literal.Text) + "\"", Why = why ?? ""
                });
            }
            for (int i = 0; i < lines.Length; i++)
            {
                var english = EnglishLabel.Match(lines[i]);
                if (!english.Success || lines[i].Contains("dev-text")) continue;
                findings.Add(new Finding { Kind = Kind.CodeWords, Path = path, Line = i + 1, What = $"라벨에 바로 쓴 영어 \"{Short(english.Groups[2].Value)}\"" });
            }
        }
    }

    private static string WhyDeveloper(string relative, string method, (int Line, string Text, bool InAttribute) literal, string line, bool wholeFileOff)
    {
        if (wholeFileOff || line.Contains("// dev-text")) return "// dev-text 표시";
        if (literal.InAttribute) return "인스펙터 설명";
        if (LogCall.IsMatch(line)) return "로그·경고";
        if (ObjectName.IsMatch(line)) return "오브젝트 이름";
        if (Register.IsMatch(line)) return "콘솔 명령 설명";
        if (method != null && ConsoleMethod.IsMatch(method)) return $"콘솔 대답({method})";
        foreach (var (path, onlyMethod, onlyLiteral, why) in Developer)
        {
            bool inPath = path.EndsWith("/") ? relative.StartsWith(path, StringComparison.OrdinalIgnoreCase)
                : string.Equals(relative, path, StringComparison.OrdinalIgnoreCase);
            if (!inPath) continue;
            if (onlyMethod != null && onlyMethod != method) continue;
            if (onlyLiteral != null && onlyLiteral != literal.Text) continue;
            return why;
        }
        return null;
    }

    // The method each line sits in, as far as a declaration line tells (enough to know a console handler).
    private static string[] MethodsByLine(string[] lines)
    {
        var methods = new string[Math.Max(1, lines.Length)];
        string current = null;
        for (int i = 0; i < lines.Length; i++)
        {
            var match = MethodLine.Match(lines[i]);
            if (match.Success && !lines[i].Contains(" new ") && !lines[i].TrimEnd().EndsWith(";")) current = match.Groups[1].Value;
            methods[i] = current;
        }
        return methods;
    }

    /// <summary>
    /// Every string literal of a C# source outside comments, with its line and whether it sits in an attribute
    /// ([Tooltip("…")]). Plain, verbatim (@"…") and interpolated ($"…", the holes skipped) strings.
    /// </summary>
    internal static List<(int Line, string Text, bool InAttribute)> Literals(string source)
    {
        var found = new List<(int, string, bool)>();
        var brackets = new Stack<bool>(); // per open '[': whether it opened an attribute
        char previous = ';';
        int line = 1, i = 0, n = source.Length;
        while (i < n)
        {
            char c = source[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                while (i < n && source[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? n : end + 2;
                for (int k = i; k < end; k++) if (source[k] == '\n') line++;
                i = end;
                continue;
            }
            if (c == '\'')
            {
                i++;
                while (i < n && source[i] != '\'') { if (source[i] == '\\') i++; i++; }
                i++;
                previous = '\'';
                continue;
            }
            if (c == '"' || ((c == '$' || c == '@') && i + 1 < n && (source[i + 1] == '"' || source[i + 1] == '$' || source[i + 1] == '@')))
            {
                bool verbatim = false, interpolated = false;
                while (i < n && (source[i] == '$' || source[i] == '@'))
                {
                    verbatim |= source[i] == '@';
                    interpolated |= source[i] == '$';
                    i++;
                }
                if (i >= n || source[i] != '"') continue;
                i++;
                int startLine = line;
                var text = new StringBuilder();
                while (i < n)
                {
                    char s = source[i];
                    if (s == '\n') line++;
                    if (verbatim)
                    {
                        if (s == '"')
                        {
                            if (i + 1 < n && source[i + 1] == '"') { text.Append('"'); i += 2; continue; }
                            break;
                        }
                    }
                    else if (s == '\\' && i + 1 < n) { text.Append(source[i + 1] == 'n' ? '\n' : source[i + 1]); i += 2; continue; }
                    if (interpolated && s == '{')
                    {
                        if (i + 1 < n && source[i + 1] == '{') { text.Append('{'); i += 2; continue; }
                        int depth = 1;
                        i++;
                        while (i < n && depth > 0)
                        {
                            if (source[i] == '{') depth++;
                            else if (source[i] == '}') depth--;
                            else if (source[i] == '\n') line++;
                            else if (source[i] == '"')
                            {
                                i++;
                                while (i < n && source[i] != '"') { if (source[i] == '\\') i++; i++; }
                            }
                            i++;
                        }
                        text.Append("{…}");
                        continue;
                    }
                    if (s == '"') break;
                    text.Append(s);
                    i++;
                }
                i++;
                found.Add((startLine, text.ToString(), brackets.Contains(true)));
                previous = '"';
                continue;
            }
            if (c == '[') brackets.Push(previous == ';' || previous == '{' || previous == '}' || previous == ']');
            else if (c == ']' && brackets.Count > 0) brackets.Pop();
            previous = c;
            i++;
        }
        return found;
    }

    // ---- Tables ----

    // Columns that are notes for whoever edits the table, never shown: they keep their words.
    private static bool NoteColumn(string column) => column.StartsWith("#") || string.Equals(column, "note", StringComparison.OrdinalIgnoreCase);

    private static void ScanTables(List<Finding> findings, HashSet<string> ids)
    {
        foreach (string file in Directory.GetFiles(TablesFolder, "*.csv"))
        {
            string path = file.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(path);
            if (name == Texts.TableName) continue;
            var records = CsvSpans.Parse(TableFile.Read(path));
            if (records.Count == 0) continue;
            var header = records[0].Cells.Select(c => c.Value.Trim()).ToList();
            bool keyLayout = header.Count > 1 && header[0] == "key" && header[1] == "value";
            foreach (var record in records.Skip(1))
            {
                string id = record[0].Trim();
                if (id.Length == 0 || id.StartsWith("#")) continue;
                if (Hangul.IsMatch(id) && !keyLayout)
                    findings.Add(new Finding { Kind = Kind.TableWords, Path = path, Line = record.Line, What = $"{name}.csv: 줄 id가 한국어 \"{Short(id)}\": 화면 이름이면 name 칸에 Texts id를 두고 id는 영어로" });
                for (int c = 1; c < record.Cells.Count && c < header.Count; c++)
                {
                    string column = keyLayout ? id : header[c];
                    if (NoteColumn(header[c]) || keyLayout && c > 1) continue;
                    string cell = record[c].Trim();
                    if (cell.Length == 0) continue;
                    if (Hangul.IsMatch(cell))
                        findings.Add(new Finding { Kind = Kind.TableWords, Path = path, Line = record.Line, What = $"{name}.csv [{id}] {column}: 글자를 칸에 바로 씀 \"{Short(cell)}\"" });
                    else if (LooksLikeTextId(cell) && !ids.Contains(cell))
                        findings.Add(new Finding { Kind = Kind.MissingId, Path = path, Line = record.Line, What = $"{name}.csv [{id}] {column}: Texts.csv에 없는 id \"{cell}\"" });
                }
            }
        }
    }

    // The id beginnings data tables point at (Texts.csv rows made for table words, 2026-11-12): another cell that
    // merely has dots (a step id, a sound key) is no Texts id.
    private static readonly string[] TableIdStarts = { "perk.", "class.", "chapter.", "mission.", "map.", "menu.ticker.", "pickup.", "shop.", "outfit.", "part.", "feed.flair.", "crosshair.preset.", "callout.line.", "moment." };

    private static bool LooksLikeTextId(string cell)
        => cell.IndexOf(' ') < 0 && TableIdStarts.Any(s => cell.StartsWith(s, StringComparison.OrdinalIgnoreCase));

    // ---- Prefabs and scenes ----

    // A serialized TMP text (m_text: "…", Korean escaped as \uXXXX by Unity's YAML writer).
    private static readonly Regex SerializedText = new Regex(@"^\s+m_text: ""((?:[^""\\]|\\.)*)""", RegexOptions.Multiline);

    private static void ScanPrefabs(List<Finding> findings)
    {
        foreach (string folder in SceneFolders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (string file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".prefab") && !file.EndsWith(".unity")) continue;
                string path = file.Replace('\\', '/');
                string text = File.ReadAllText(path);
                if (text.IndexOf("m_text:", StringComparison.Ordinal) < 0) continue;
                foreach (Match match in SerializedText.Matches(text))
                {
                    string words = Unescape(match.Groups[1].Value);
                    if (!Hangul.IsMatch(words)) continue;
                    // A TextKey on the same object writes it from Texts.csv when it shows.
                    if (HasTextKey(text, match.Index)) continue;
                    int line = 1 + text.Take(match.Index).Count(ch => ch == '\n');
                    findings.Add(new Finding { Kind = Kind.PrefabWords, Path = path, Line = line, What = $"\"{Short(words)}\" (코드가 실행 중에 덮어쓰면 괜찮아요)" });
                }
            }
        }
    }

    // Whether the GameObject owning the text component at this place has a TextKey.
    private static bool HasTextKey(string yaml, int at)
    {
        int start = yaml.LastIndexOf("--- !u!", at, StringComparison.Ordinal);
        if (start < 0) return false;
        var owner = Regex.Match(yaml.Substring(start, Math.Min(600, yaml.Length - start)), @"m_GameObject: \{fileID: (-?\d+)\}");
        if (!owner.Success) return false;
        string go = owner.Groups[1].Value;
        return Regex.IsMatch(yaml, @"m_GameObject: \{fileID: " + go + @"\}[^-]*?m_EditorClassIdentifier: Assembly-CSharp::TextKey");
    }

    // Unity's YAML strings: \uXXXX, \xXX, \n, \", \\.
    private static string Unescape(string raw)
    {
        var text = new StringBuilder();
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\' || i + 1 >= raw.Length) { text.Append(raw[i]); continue; }
            char e = raw[++i];
            if (e == 'u' && i + 4 < raw.Length) { text.Append((char)Convert.ToInt32(raw.Substring(i + 1, 4), 16)); i += 4; }
            else if (e == 'x' && i + 2 < raw.Length) { text.Append((char)Convert.ToInt32(raw.Substring(i + 1, 2), 16)); i += 2; }
            else text.Append(e == 'n' ? '\n' : e);
        }
        return text.ToString();
    }

    // ---- Texts.csv itself ----

    private static void ScanSheet(List<Finding> findings, TextSheet sheet)
    {
        int ko = sheet.Korean;
        foreach (var row in sheet.Rows)
        {
            if (row.Id.IndexOf(' ') >= 0 || Hangul.IsMatch(row.Id))
                findings.Add(new Finding { Kind = Kind.BrokenRow, Path = TextSheet.AssetPath, Line = row.Line, What = $"id가 문장인 줄 \"{Short(row.Id)}\": 위 줄의 칸 안에 진짜 줄바꿈이 들어가 줄이 둘로 쪼개졌어요. 줄바꿈은 \\n으로 써요" });
            else if (row.Occurrence > 0)
                findings.Add(new Finding { Kind = Kind.BrokenRow, Path = TextSheet.AssetPath, Line = row.Line, What = $"같은 id \"{row.Id}\"가 또 있어요: 게임은 칸마다 뒤의 줄을 써요" });
            else if (ko >= 0 && row.Saved[ko].Trim().Length == 0)
                findings.Add(new Finding { Kind = Kind.BrokenRow, Path = TextSheet.AssetPath, Line = row.Line, What = $"\"{row.Id}\"의 한국어가 비었어요: 화면에 [{row.Id}]가 보여요" });
        }
    }

    // ---- Where each id is used ----

    /// <summary>
    /// Where each Texts id is asked for: code lines that name it whole ("Scripts/UI/PerkCardScreen.cs:363"), code that
    /// builds it from a beginning ("zombie." + kind: listed as such) and table cells pointing at it ("Perks.csv
    /// gun_head · name").
    /// </summary>
    internal static Dictionary<string, List<(string path, int line, string what)>> Usage(TextSheet sheet)
    {
        var usage = new Dictionary<string, List<(string, int, string)>>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(sheet.Rows.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        var starts = new Dictionary<string, List<(string, int)>>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles("Assets/_Project", "*.cs", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            string source = File.ReadAllText(path);
            foreach (var literal in Literals(source))
            {
                string text = literal.Text;
                if (text.Length < 3 || text.IndexOf(' ') >= 0) continue;
                if (ids.Contains(text)) Add(usage, text, (path, literal.Line, ""));
                else if (text.EndsWith(".") && text.IndexOf('.') > 0)
                {
                    if (!starts.TryGetValue(text, out var list)) starts[text] = list = new List<(string, int)>();
                    list.Add((path, literal.Line));
                }
            }
        }
        foreach (string id in ids)
            foreach (var start in starts.Where(s => id.StartsWith(s.Key, StringComparison.OrdinalIgnoreCase)))
                foreach (var (path, line) in start.Value)
                    Add(usage, id, (path, line, $"\"{start.Key}\" + … 로 만든 id"));
        foreach (string file in Directory.GetFiles(TablesFolder, "*.csv"))
        {
            string path = file.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(path);
            if (name == Texts.TableName) continue;
            var records = CsvSpans.Parse(TableFile.Read(path));
            if (records.Count == 0) continue;
            var header = records[0].Cells.Select(c => c.Value.Trim()).ToList();
            foreach (var record in records.Skip(1))
                for (int c = 1; c < record.Cells.Count && c < header.Count; c++)
                {
                    string cell = record[c].Trim();
                    if (cell.Length > 0 && cell.IndexOf(' ') < 0 && ids.Contains(cell))
                        Add(usage, cell, (path, record.Line, $"{name}.csv {record[0].Trim()} · {header[c]}"));
                }
        }
        return usage;
    }

    private static void Add(Dictionary<string, List<(string, int, string)>> usage, string id, (string, int, string) place)
    {
        if (!usage.TryGetValue(id, out var list)) usage[id] = list = new List<(string, int, string)>();
        if (list.Count < 40) list.Add(place);
    }

    private static string Short(string text)
    {
        text = text.Replace("\n", "\\n");
        return text.Length <= 70 ? text : text.Substring(0, 70) + "…";
    }

    /// <summary>The findings as plain text, for copying.</summary>
    internal static string Report(List<Finding> findings)
    {
        var text = new StringBuilder();
        foreach (var group in findings.GroupBy(f => f.Kind))
        {
            text.AppendLine($"== {Title(group.Key)} ({group.Count()})");
            foreach (var finding in group)
                text.AppendLine($"{finding.Path}:{finding.Line}  {finding.What}{(finding.Why.Length > 0 ? "  [" + finding.Why + "]" : "")}");
            text.AppendLine();
        }
        return text.ToString();
    }

    internal static string Title(Kind kind) => kind switch
    {
        Kind.CodeWords => "코드에 바로 쓴 화면 글자 (Texts.csv로 옮길 것)",
        Kind.PrefabWords => "프리팹·씬에 바로 쓴 글자 (TextKey가 없음)",
        Kind.TableWords => "표 칸에 바로 쓴 글자 (화면엔 나오지만 글자 편집기 밖)",
        Kind.MissingId => "Texts.csv에 없는 id (화면에 [id]로 보임)",
        Kind.BrokenRow => "Texts.csv의 깨진 줄",
        _ => "개발용 글자 (콘솔·로그·인스펙터·오브젝트 이름: 코드에 둬도 됨)"
    };
}
