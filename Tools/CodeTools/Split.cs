using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// A big class is cut into partial files by subject: at the "// ---- Subject ----" lines its authors already drew, or at
/// line ranges given outright. Nothing is rewritten, members only change file. Fields that carry attributes and static
/// fields with a starting value stay in the main file, so what the inspector shows and the order statics are set in stay
/// as they were (C# leaves the order between the parts of a class open).
static class Split
{
    static readonly Regex Banner = new(@"^//\s*[-=]{3,}\s*(.+?)\s*[-=]{3,}\s*$", RegexOptions.Compiled);

    sealed class Part
    {
        public string Name, Title;
        public List<MemberDeclarationSyntax> Members = new();
        public List<SyntaxTrivia> Banners = new();
    }

    static string Pascal(string title)
    {
        var text = new StringBuilder();
        foreach (string word in Regex.Split(title, @"[^A-Za-z0-9]+"))
            if (word.Length > 0) text.Append(char.ToUpperInvariant(word[0])).Append(word.Substring(1));
        return text.ToString();
    }

    static SyntaxTrivia? BannerOf(MemberDeclarationSyntax member)
    {
        foreach (var trivia in member.GetLeadingTrivia())
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && Banner.IsMatch(trivia.ToString())) return trivia;
        return null;
    }

    static bool Stays(MemberDeclarationSyntax member)
    {
        if (member is not BaseFieldDeclarationSyntax field) return false;
        if (field.AttributeLists.Count > 0) return true;
        bool isStatic = field.Modifiers.Any(SyntaxKind.StaticKeyword), isConst = field.Modifiers.Any(SyntaxKind.ConstKeyword);
        return isStatic && !isConst && field.Declaration.Variables.Any(v => v.Initializer != null);
    }

    static int LineStart(string text, int at)
    {
        while (at > 0 && text[at - 1] != '\n') at--;
        return at;
    }

    static int LineEnd(string text, int at)
    {
        while (at < text.Length && text[at] != '\n') at++;
        return at < text.Length ? at + 1 : at;
    }

    public static void Run(string rel, string ranges, string names, int least, bool dry, string titles = null)
    {
        // what each part is about, for the line at the top of its file: "Name=words;Name=words"
        var about = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(titles))
            foreach (string pair in titles.Split(';')) { int eq = pair.IndexOf('='); if (eq > 0) about[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim(); }
        string text = Project.ReadText(rel, out bool bom);
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var tree = CSharpSyntaxTree.ParseText(text, Project.Parse(false), Project.Full(rel));
        var root = (CompilationUnitSyntax)tree.GetRoot();
        string fileName = Path.GetFileNameWithoutExtension(rel).Split('.')[0];
        var type = root.Members.OfType<TypeDeclarationSyntax>().FirstOrDefault(t => t.Identifier.ValueText == fileName)
                   ?? root.Members.OfType<TypeDeclarationSyntax>().OrderByDescending(t => t.Members.Count).First();
        var rename = new Dictionary<string, string>();
        // names: "Title=Name;Title=Name", or just "Name;Name;..." for the subject lines in the order they stand
        string[] inOrder = null;
        if (!string.IsNullOrEmpty(names))
        {
            if (names.Contains('=')) foreach (string pair in names.Split(';')) { var sides = pair.Split('='); rename[sides[0].Trim()] = sides[1].Trim(); }
            else inOrder = names.Split(';').Select(n => n.Trim()).ToArray();
        }
        int seen = 0;

        var parts = new List<Part>();
        if (!string.IsNullOrEmpty(ranges))
        {
            foreach (string spec in ranges.Split(','))
            {
                var sides = spec.Split(':');
                var lines = sides[1].Split('-');
                int from = int.Parse(lines[0]), to = int.Parse(lines[1]);
                var part = parts.FirstOrDefault(p => p.Name == sides[0].Trim());
                bool known = part != null;
                part ??= new Part { Name = sides[0].Trim(), Title = sides[0].Trim() };
                foreach (var member in type.Members)
                {
                    var span = tree.GetLineSpan(member.Span);
                    int first = span.StartLinePosition.Line + 1, last = span.EndLinePosition.Line + 1;
                    if (first >= from && last <= to) part.Members.Add(member);
                    else if (first <= to && last >= from) throw new Exception($"lines {from}-{to} cut through a member at {first}-{last}");
                }
                if (!known) parts.Add(part);
            }
        }
        else
        {
            Part current = null;
            foreach (var member in type.Members)
            {
                var banner = BannerOf(member);
                if (banner != null)
                {
                    string title = Banner.Match(banner.Value.ToString()).Groups[1].Value;
                    string name = inOrder != null ? (seen < inOrder.Length ? inOrder[seen] : throw new Exception("more subject lines than names")) 
                        : rename.TryGetValue(title, out string given) ? given : Pascal(title);
                    seen++;
                    current = parts.FirstOrDefault(p => p.Name == name);
                    if (current == null) parts.Add(current = new Part { Name = name, Title = title });
                    current.Banners.Add(banner.Value);
                }
                current?.Members.Add(member);
            }
        }

        var cuts = new List<Cut>();
        var made = new List<(string file, string body, int lines)>();
        var modifiers = type.Modifiers.Where(m => !m.IsKind(SyntaxKind.PartialKeyword)).Select(m => m.Text).ToList();
        string head = string.Join(" ", modifiers.Append("partial").Append(type.Keyword.Text)) + " " + type.Identifier.ValueText + (type.TypeParameterList?.ToString() ?? "");
        string usings = string.Concat(root.Usings.Select(u => u.ToString() + newline));
        foreach (var part in parts)
        {
            if (part.Name == "-" || part.Name.Length == 0) continue;
            var moving = part.Members.Where(m => !Stays(m)).ToList();
            var body = new StringBuilder();
            int lines = 0;
            foreach (var member in moving)
            {
                var (start, end) = Edits.WholeLines(member);
                if (member.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsDirective) || member.GetLeadingTrivia().Any(t => t.IsDirective))
                    throw new Exception($"{part.Name}: a member with a preprocessor line at {tree.GetLineSpan(member.Span).StartLinePosition.Line + 1}; cut this one by hand");
                string piece = text.Substring(start, end - start);
                // the subject line itself is not carried along: the file's name says it
                piece = string.Join(newline, piece.Split(new[] { newline }, StringSplitOptions.None).Where(line => !Banner.IsMatch(line.Trim())));
                if (body.Length > 0) body.Append(newline);
                body.Append(piece.TrimStart('\r', '\n'));
                lines += piece.Count(c => c == '\n');
            }
            if (lines < least) { Console.WriteLine($"  left in place: {part.Title} ({lines} lines)"); continue; }
            foreach (var member in moving)
            {
                var (start, end) = Edits.WholeLines(member);
                cuts.Add(new Cut { Start = start, End = end });
            }
            foreach (var banner in part.Banners)
            {
                int from = LineStart(text, banner.SpanStart), to = LineEnd(text, banner.Span.End);
                if (!cuts.Any(c => c.Start <= from && c.End >= to)) cuts.Add(new Cut { Start = from, End = to });
            }
            var file = new StringBuilder();
            file.Append(usings).Append(newline);
            file.Append("// ").Append(type.Identifier.ValueText).Append(": ").Append(about.TryGetValue(part.Name, out string words) ? words : part.Title).Append(". One part of the class; the rest is in ")
                .Append(fileName).Append(".cs and the files beside it.").Append(newline);
            file.Append(head).Append(newline).Append('{').Append(newline).Append(body.ToString().TrimEnd('\r', '\n')).Append(newline).Append('}').Append(newline);
            made.Add((Path.Combine(Path.GetDirectoryName(rel) ?? "", fileName + "." + part.Name + ".cs").Replace('\\', '/'), file.ToString(), lines));
        }
        if (made.Count == 0) { Console.WriteLine("SPLIT nothing to cut in " + rel); return; }
        if (!type.Modifiers.Any(SyntaxKind.PartialKeyword)) cuts.Add(new Cut { Start = type.Keyword.SpanStart, End = type.Keyword.SpanStart, With = "partial ", Tidy = false });

        int before = text.Count(c => c == '\n');
        Console.WriteLine($"SPLIT {(dry ? "(dry) " : "")}{rel} ({before} lines) -> main {before - made.Sum(m => m.lines)} + " + string.Join(", ", made.Select(m => Path.GetFileName(m.file) + " " + m.lines)));
        if (dry) return;
        foreach (var (file, body, _) in made)
        {
            string full = Project.Full(file);
            if (File.Exists(full)) throw new Exception("exists: " + file);
            File.WriteAllText(full + ".meta", "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
            Project.WriteNew(file, body, bom);
        }
        Project.WriteText(rel, Edits.Apply(text, cuts), bom);
    }
}
