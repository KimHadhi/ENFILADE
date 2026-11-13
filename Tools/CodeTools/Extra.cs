using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// Methods whose bodies are the same once local names are numbered: copies that could be one.
static class Dupes
{
    static string Shape(SemanticModel model, BaseMethodDeclarationSyntax method)
    {
        SyntaxNode body = (SyntaxNode)method.Body ?? method.ExpressionBody;
        if (body == null) return null;
        var names = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var text = new StringBuilder();
        foreach (var parameter in method.ParameterList.Parameters)
            text.Append(parameter.Modifiers.ToString()).Append(' ').Append(parameter.Type?.ToString()).Append(',');
        text.Append("=>").Append((method as MethodDeclarationSyntax)?.ReturnType.ToString()).Append('|');
        foreach (var token in body.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                ISymbol symbol = token.Parent is IdentifierNameSyntax name ? model.GetSymbolInfo(name).Symbol : model.GetDeclaredSymbol(token.Parent);
                if (symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol or ILabelSymbol || symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction })
                {
                    if (!names.TryGetValue(symbol, out string alias)) names[symbol] = alias = "$" + names.Count;
                    text.Append(alias).Append(' ');
                    continue;
                }
            }
            text.Append(token.Text).Append(' ');
        }
        return text.ToString();
    }

    public static void Report()
    {
        var build = Project.Load(false);
        var groups = new Dictionary<string, List<(string where, string name, int lines, bool isStatic, string owner)>>();
        foreach (var compilation in build.All)
            foreach (var tree in compilation.SyntaxTrees)
            {
                string rel = Project.Rel(tree.FilePath);
                if (!Project.IsProject(rel)) continue;
                var model = compilation.GetSemanticModel(tree);
                foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    string shape = Shape(model, method);
                    if (shape == null || shape.Length < 90) continue;
                    var span = tree.GetLineSpan(method.Span);
                    if (!groups.TryGetValue(shape, out var list)) groups[shape] = list = new();
                    list.Add((rel + ":" + (span.StartLinePosition.Line + 1), method.Identifier.ValueText, span.EndLinePosition.Line - span.StartLinePosition.Line + 1,
                        method.Modifiers.Any(SyntaxKind.StaticKeyword), (method.Parent as BaseTypeDeclarationSyntax)?.Identifier.ValueText ?? ""));
                }
            }
        var text = new StringBuilder();
        var dupes = groups.Values.Where(g => g.Count > 1).OrderByDescending(g => (g.Count - 1) * g[0].lines).ToList();
        text.AppendLine($"duplicate groups {dupes.Count}, copies that could go {dupes.Sum(g => g.Count - 1)}, lines {dupes.Sum(g => (g.Count - 1) * g[0].lines)}");
        foreach (var group in dupes)
        {
            text.AppendLine($"x{group.Count}  {group[0].lines} lines  {string.Join("/", group.Select(g => g.name).Distinct())}  {(group.All(g => g.isStatic) ? "static" : "")}");
            foreach (var g in group) text.AppendLine("      " + g.where + "  " + g.owner + "." + g.name);
        }
        File.WriteAllText(Path.Combine(Project.Out, "dupes.txt"), text.ToString());
        Console.WriteLine(text.ToString().Split('\n')[0]);
    }
}

/// The members of the types in one file with their sizes, to decide how a big file is cut.
static class Outline
{
    public static void Print(string rel)
    {
        var root = CSharpSyntaxTree.ParseText(Project.ReadText(rel, out _), Project.Parse(false)).GetRoot();
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var whole = type.SyntaxTree.GetLineSpan(type.Span);
            Console.WriteLine($"{type.Keyword.Text} {type.Identifier.ValueText} : {type.BaseList?.Types.ToString() ?? ""}  [{whole.StartLinePosition.Line + 1}-{whole.EndLinePosition.Line + 1}]");
            foreach (var member in type.Members)
            {
                if (member is TypeDeclarationSyntax) continue;
                var span = member.SyntaxTree.GetLineSpan(member.Span);
                int lines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
                string name = member switch
                {
                    MethodDeclarationSyntax m => m.Identifier.ValueText + "(" + string.Join(",", m.ParameterList.Parameters.Select(p => p.Type?.ToString())) + ")",
                    PropertyDeclarationSyntax p => p.Identifier.ValueText,
                    BaseFieldDeclarationSyntax f => string.Join(",", f.Declaration.Variables.Select(v => v.Identifier.ValueText)),
                    ConstructorDeclarationSyntax => "ctor",
                    EnumDeclarationSyntax e => "enum " + e.Identifier.ValueText,
                    DelegateDeclarationSyntax d => "delegate " + d.Identifier.ValueText,
                    _ => member.Kind().ToString()
                };
                string kind = member switch { MethodDeclarationSyntax => "M", PropertyDeclarationSyntax => "P", BaseFieldDeclarationSyntax => "F", _ => "-" };
                Console.WriteLine($"  {span.StartLinePosition.Line + 1,5} {kind} {lines,4}  {name}");
            }
        }
    }
}

/// Using lines nothing in the file needs, in the editor and in a player build alike.
static class Usings
{
    public static void Run(bool dry)
    {
        Project.Documentation = DocumentationMode.Parse;
        var unused = new Dictionary<string, Dictionary<int, int>>();
        var seen = new Dictionary<string, int>();
        foreach (bool player in new[] { false, true })
        {
            var build = Project.Load(player);
            foreach (var compilation in build.All)
            {
                var hidden = compilation.GetDiagnostics().Where(d => d.Id == "CS8019" && d.Location.IsInSource);
                foreach (var d in hidden)
                {
                    string rel = Project.Rel(d.Location.SourceTree.FilePath);
                    if (!Project.IsProject(rel)) continue;
                    var node = d.Location.SourceTree.GetRoot().FindNode(d.Location.SourceSpan);
                    var line = node.FirstAncestorOrSelf<UsingDirectiveSyntax>();
                    if (line == null) continue;
                    if (!unused.TryGetValue(rel, out var map)) unused[rel] = map = new();
                    // from the word "using" on: what stands above it (a file's opening comment, a directive) stays
                    string key = rel + "@" + line.SpanStart;
                    seen[key] = seen.GetValueOrDefault(key) + 1;
                    map[line.SpanStart] = line.FullSpan.End;
                }
                foreach (var tree in compilation.SyntaxTrees)
                {
                    string rel = Project.Rel(tree.FilePath);
                    if (Project.IsProject(rel)) seen["#" + rel] = seen.GetValueOrDefault("#" + rel) + 1;
                }
            }
        }
        var byFile = new Dictionary<string, List<Cut>>();
        int count = 0;
        foreach (var pair in unused)
        {
            int configurations = seen.GetValueOrDefault("#" + pair.Key);
            foreach (var span in pair.Value)
            {
                if (seen.GetValueOrDefault(pair.Key + "@" + span.Key) < configurations) continue;
                if (!byFile.TryGetValue(pair.Key, out var cuts)) byFile[pair.Key] = cuts = new();
                cuts.Add(new Cut { Start = span.Key, End = span.Value, Tidy = false });
                count++;
            }
        }
        var (written, _) = Edits.Commit(byFile, dry);
        Console.WriteLine($"USINGS {(dry ? "(dry) " : "")}removed {count} in {written} files");
    }
}
