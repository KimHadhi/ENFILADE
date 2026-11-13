using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// Text edits on source files: cuts with optional replacement, applied from the end of a file backwards, the blank lines
/// around a cut tidied, files left without any declaration deleted together with their .meta.
sealed class Cut
{
    public int Start, End;
    public string With = "";
    public bool Tidy = true;
}

static class Edits
{
    static readonly Regex EmptyIf = new(@"^[ \t]*#if[^\r\n]*\r?\n(?:[ \t]*\r?\n)*[ \t]*#endif[^\r\n]*\r?\n", RegexOptions.Multiline | RegexOptions.Compiled);
    static readonly Regex EmptyRegion = new(@"^[ \t]*#region[^\r\n]*\r?\n(?:[ \t]*\r?\n)*[ \t]*#endregion[^\r\n]*\r?\n", RegexOptions.Multiline | RegexOptions.Compiled);

    public static CSharpParseOptions Options(bool player) => Project.Parse(player);

    /// A declaration with the comment block that belongs to it: from the line after the last blank line or directive above
    /// it to the end of its last line.
    public static (int start, int end) WholeLines(SyntaxNode node)
    {
        int keep = node.FullSpan.Start;
        bool content = false;
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.IsDirective || trivia.IsKind(SyntaxKind.DisabledTextTrivia)) { keep = trivia.FullSpan.End; content = false; continue; }
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                if (!content) keep = trivia.FullSpan.End;
                content = false;
            }
            else if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia)) content = true;
        }
        return (keep, node.FullSpan.End);
    }

    static bool BlankLineAt(string text, int at, out int next)
    {
        int i = at;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        if (i < text.Length && text[i] == '\r') i++;
        if (i < text.Length && text[i] == '\n') { next = i + 1; return true; }
        next = at;
        return false;
    }

    static bool BlankLineBefore(string text, int at, out int start)
    {
        // 'at' is the start of a line; the line before it runs from 'start' to 'at'.
        start = at;
        if (at == 0 || text[at - 1] != '\n') return false;
        int i = at - 1;
        if (i > 0 && text[i - 1] == '\r') i--;
        int end = i;
        while (i > 0 && text[i - 1] != '\n') i--;
        for (int k = i; k < end; k++)
            if (text[k] != ' ' && text[k] != '\t') return false;
        start = i;
        return true;
    }

    static char LastSolidBefore(string text, int at)
    {
        for (int i = at - 1; i >= 0; i--)
            if (!char.IsWhiteSpace(text[i])) return text[i];
        return '\0';
    }

    static char FirstSolidAt(string text, int at)
    {
        for (int i = at; i < text.Length; i++)
            if (!char.IsWhiteSpace(text[i])) return text[i];
        return '\0';
    }

    public static string Apply(string text, List<Cut> cuts)
    {
        cuts.Sort((a, b) => b.Start.CompareTo(a.Start));
        int limit = int.MaxValue;
        foreach (var cut in cuts)
        {
            if (cut.End > limit) continue;   // inside or across a later cut that was already made: leave it
            limit = cut.Start;
            text = text.Substring(0, cut.Start) + cut.With + text.Substring(cut.End);
            if (!cut.Tidy || cut.With.Length > 0) continue;
            int at = cut.Start;
            bool blankBefore = BlankLineBefore(text, at, out _);
            bool openBefore = LastSolidBefore(text, at) == '{';
            if (blankBefore || openBefore)
                while (BlankLineAt(text, at, out int next)) text = text.Remove(at, next - at);
            if (FirstSolidAt(text, at) == '}')
                while (BlankLineBefore(text, at, out int start)) { text = text.Remove(start, at - start); at = start; }
        }
        string before;
        do { before = text; text = EmptyRegion.Replace(EmptyIf.Replace(text, ""), ""); } while (text != before);
        return text;
    }

    public static bool HasDeclarations(string text)
    {
        foreach (bool player in new[] { false, true })
        {
            var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text, Options(player)).GetRoot();
            if (root.Members.Count > 0 || root.AttributeLists.Count > 0) return true;
        }
        return false;
    }

    /// Writes the edited files; a file left with no declaration is deleted with its .meta. Returns (written, deleted).
    public static (int written, int deleted) Commit(Dictionary<string, List<Cut>> byFile, bool dry)
    {
        int written = 0, deleted = 0;
        foreach (var pair in byFile.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            string text = Project.ReadText(pair.Key, out bool bom);
            string result = Apply(text, pair.Value);
            if (result == text) continue;
            if (!HasDeclarations(result))
            {
                deleted++;
                Console.WriteLine("  delete " + pair.Key);
                if (dry) continue;
                File.Delete(Project.Full(pair.Key));
                if (File.Exists(Project.Full(pair.Key) + ".meta")) File.Delete(Project.Full(pair.Key) + ".meta");
                continue;
            }
            written++;
            if (!dry) Project.WriteText(pair.Key, result, bom);
        }
        return (written, deleted);
    }
}

static class Kill
{
    static SyntaxNode Locate(SyntaxNode root, int start)
    {
        if (start >= root.FullSpan.End) return null;
        var token = root.FindToken(start);
        return token.Parent?.AncestorsAndSelf().FirstOrDefault(n => n.SpanStart == start && n is MemberDeclarationSyntax or VariableDeclaratorSyntax && n.SpanStart == start);
    }

    public static string Id(Candidate c) => c.File + "::" + (c.Owner.Length > 0 ? c.Owner + "." : "") + c.Name;

    public static void Run(HashSet<string> kinds, HashSet<string> skipFlags, HashSet<string> except, HashSet<string> only, bool dry)
    {
        var dead = Dead.Find(out _);
        var chosen = dead.Where(d => !d.Flags.Contains("inDeadType") && kinds.Contains(d.Kind) && !d.Flags.Any(f => skipFlags.Contains(f))
                                     && !except.Contains(Id(d)) && (only.Count == 0 || only.Contains(Id(d)))).ToList();
        var byFile = new Dictionary<string, List<Cut>>();
        var trees = new Dictionary<string, (SyntaxNode editor, SyntaxNode player)>();
        var fieldGroups = new Dictionary<(string file, int start), (BaseFieldDeclarationSyntax field, HashSet<int> dead)>();
        int lines = 0, missed = 0;

        foreach (var d in chosen)
        {
            foreach (var (file, start, _) in d.Spans)
            {
                if (!trees.TryGetValue(file, out var pair))
                {
                    string text = Project.ReadText(file, out _);
                    pair = (CSharpSyntaxTree.ParseText(text, Edits.Options(false)).GetRoot(), CSharpSyntaxTree.ParseText(text, Edits.Options(true)).GetRoot());
                    trees[file] = pair;
                }
                var node = Locate(pair.editor, start) ?? Locate(pair.player, start);
                if (node == null) { missed++; Console.WriteLine("  not found: " + Id(d)); continue; }
                if (!byFile.TryGetValue(file, out var cuts)) byFile[file] = cuts = new List<Cut>();
                if (node is VariableDeclaratorSyntax variable && variable.Parent?.Parent is BaseFieldDeclarationSyntax field)
                {
                    var groupKey = (file, field.SpanStart);
                    if (!fieldGroups.TryGetValue(groupKey, out var group)) fieldGroups[groupKey] = group = (field, new HashSet<int>());
                    group.dead.Add(field.Declaration.Variables.IndexOf(variable));
                    continue;
                }
                var (from, to) = Edits.WholeLines(node);
                cuts.Add(new Cut { Start = from, End = to });
            }
            lines += d.Lines;
        }
        foreach (var pair in fieldGroups)
        {
            var (field, deadIndices) = pair.Value;
            var variables = field.Declaration.Variables;
            var cuts = byFile[pair.Key.file];
            if (deadIndices.Count == variables.Count)
            {
                var (from, to) = Edits.WholeLines(field);
                cuts.Add(new Cut { Start = from, End = to });
                continue;
            }
            var kept = Enumerable.Range(0, variables.Count).Where(i => !deadIndices.Contains(i)).Select(i => variables[i].ToString());
            cuts.Add(new Cut { Start = variables[0].SpanStart, End = variables[variables.Count - 1].Span.End, With = string.Join(", ", kept), Tidy = false });
        }

        var (written, deleted) = Edits.Commit(byFile, dry);
        Console.WriteLine($"KILL {(dry ? "(dry) " : "")}declarations {chosen.Count}, lines about {lines}, files written {written}, files deleted {deleted}, not found {missed}");
        var summary = new StringBuilder();
        foreach (var d in chosen) summary.AppendLine($"{d.Kind}\t{Id(d)}\t{d.Lines}\t{string.Join(",", d.Flags.OrderBy(f => f))}");
        File.WriteAllText(Path.Combine(Project.Out, "killed.txt"), summary.ToString());
    }
}
