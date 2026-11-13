using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// Numbers about the shape of the code: sizes, long methods, and how often the scattered patterns occur.
static class Stats
{
    public static void Report()
    {
        var build = Project.Load(false);
        var text = new StringBuilder();
        var files = new List<(string rel, int lines)>();
        var methods = new List<(string where, string name, int lines)>();
        var types = new List<(string where, string name, int lines, int members)>();
        var calls = new Dictionary<string, int>();
        var callFiles = new Dictionary<string, HashSet<string>>();
        int staticMutable = 0, boots = 0, instances = 0;

        void Count(string what, string rel)
        {
            calls[what] = calls.GetValueOrDefault(what) + 1;
            if (!callFiles.TryGetValue(what, out var set)) callFiles[what] = set = new();
            set.Add(rel);
        }

        foreach (var compilation in build.All)
            foreach (var tree in compilation.SyntaxTrees)
            {
                string rel = Project.Rel(tree.FilePath);
                if (!Project.IsProject(rel)) continue;
                var root = tree.GetRoot();
                files.Add((rel, tree.GetText().Lines.Count));
                var model = compilation.GetSemanticModel(tree);
                foreach (var node in root.DescendantNodes())
                {
                    switch (node)
                    {
                        case BaseMethodDeclarationSyntax method:
                        {
                            var span = tree.GetLineSpan(method.Span);
                            string name = method is MethodDeclarationSyntax m ? m.Identifier.ValueText : method.Kind().ToString();
                            methods.Add((rel + ":" + (span.StartLinePosition.Line + 1), name, span.EndLinePosition.Line - span.StartLinePosition.Line + 1));
                            if (method.AttributeLists.ToString().Contains("RuntimeInitializeOnLoadMethod")) boots++;
                            break;
                        }
                        case TypeDeclarationSyntax type:
                        {
                            var span = tree.GetLineSpan(type.Span);
                            types.Add((rel, type.Identifier.ValueText, span.EndLinePosition.Line - span.StartLinePosition.Line + 1, type.Members.Count));
                            break;
                        }
                        case FieldDeclarationSyntax field when field.Modifiers.Any(SyntaxKind.StaticKeyword) && !field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
                                                              && !field.Modifiers.Any(SyntaxKind.ConstKeyword):
                            staticMutable += field.Declaration.Variables.Count;
                            break;
                        case PropertyDeclarationSyntax property when property.Identifier.ValueText == "Instance" && property.Modifiers.Any(SyntaxKind.StaticKeyword):
                            instances++;
                            break;
                        case InvocationExpressionSyntax invocation:
                        {
                            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called) break;
                            string owner = called.ContainingType?.Name ?? "";
                            string name = called.Name;
                            if (owner == "Resources" && name.StartsWith("Load")) Count("Resources." + name, rel);
                            else if (name.StartsWith("FindObject") || name.StartsWith("FindFirstObject") || name.StartsWith("FindAnyObject")) Count("Object." + name, rel);
                            else if (owner == "GameObject" && name is "Find" or "FindWithTag" or "FindGameObjectsWithTag") Count("GameObject." + name, rel);
                            else if (owner == "GameTables") Count("GameTables." + name, rel);
                            else if (owner == "TableCells") Count("TableCells." + name, rel);
                            else if (owner == "Texts") Count("Texts." + name, rel);
                            else if (owner == "Debug" && name.StartsWith("Log")) Count("Debug." + name, rel);
                            else if (owner == "PlayerPrefs") Count("PlayerPrefs." + name, rel);
                            else if (name is "AddComponent") Count("AddComponent", rel);
                            else if (owner == "Shader" && name is "Find" or "PropertyToID") Count("Shader." + name, rel);
                            else if (owner == "RuntimeSceneHierarchy") Count("RuntimeSceneHierarchy." + name, rel);
                            break;
                        }
                        case ObjectCreationExpressionSyntax creation:
                        {
                            string made = creation.Type.ToString();
                            if (made is "GameObject" or "Material" or "Mesh" or "Texture2D" or "RenderTexture") Count("new " + made, rel);
                            break;
                        }
                        case MemberAccessExpressionSyntax access when access.Name.Identifier.ValueText == "main" && access.Expression.ToString() == "Camera":
                            Count("Camera.main", rel);
                            break;
                    }
                }
            }

        text.AppendLine($"files {files.Count}, lines {files.Sum(f => f.lines)}  (runtime {files.Where(f => !Project.InEditorFolder(f.rel)).Sum(f => f.lines)}, editor {files.Where(f => Project.InEditorFolder(f.rel)).Sum(f => f.lines)})");
        text.AppendLine($"files over 1000 lines: {files.Count(f => f.lines > 1000)}, over 600: {files.Count(f => f.lines > 600)}, over 400: {files.Count(f => f.lines > 400)}, under 100: {files.Count(f => f.lines < 100)}");
        text.AppendLine($"methods {methods.Count}; over 200 lines: {methods.Count(m => m.lines > 200)}, over 100: {methods.Count(m => m.lines > 100)}, over 60: {methods.Count(m => m.lines > 60)}");
        text.AppendLine($"static fields that change: {staticMutable}; self-starting methods: {boots}; static Instance properties: {instances}");
        text.AppendLine();
        text.AppendLine("lines by top folder:");
        foreach (var group in files.GroupBy(f => string.Join("/", f.rel.Split('/').Skip(2).Take(2))).OrderByDescending(g => g.Sum(f => f.lines)))
            text.AppendLine($"  {group.Key,-28} {group.Count(),4} files {group.Sum(f => f.lines),7} lines");
        text.AppendLine();
        text.AppendLine("biggest files:");
        foreach (var f in files.OrderByDescending(f => f.lines).Take(70)) text.AppendLine($"  {f.lines,5}  {f.rel.Substring(16)}");
        text.AppendLine();
        text.AppendLine("longest methods:");
        foreach (var m in methods.OrderByDescending(m => m.lines).Take(60)) text.AppendLine($"  {m.lines,5}  {m.name,-34} {m.where.Substring(16)}");
        text.AppendLine();
        text.AppendLine("types with most members:");
        foreach (var t in types.OrderByDescending(t => t.members).Take(40)) text.AppendLine($"  {t.members,4} members {t.lines,5} lines  {t.name,-30} {t.where.Substring(16)}");
        text.AppendLine();
        text.AppendLine("calls:");
        foreach (var pair in calls.OrderByDescending(p => p.Value)) text.AppendLine($"  {pair.Value,5}  in {callFiles[pair.Key].Count,4} files  {pair.Key}");
        File.WriteAllText(Path.Combine(Project.Out, "stats.txt"), text.ToString());
        Console.WriteLine(text.ToString());
    }
}

/// Methods with the same structure once every own member, local and literal is replaced by a placeholder: families of
/// code that was written once and copied with other names.
static class Clones
{
    static string Shape(SemanticModel model, MethodDeclarationSyntax method, INamedTypeSymbol owner)
    {
        SyntaxNode body = (SyntaxNode)method.Body ?? method.ExpressionBody;
        if (body == null) return null;
        var names = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var text = new StringBuilder();
        text.Append(method.ParameterList.Parameters.Count).Append('|');
        foreach (var token in body.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.NumericLiteralToken)) { text.Append("# "); continue; }
            if (token.IsKind(SyntaxKind.StringLiteralToken)) { text.Append("S "); continue; }
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                ISymbol symbol = token.Parent is IdentifierNameSyntax name ? model.GetSymbolInfo(name).Symbol : model.GetDeclaredSymbol(token.Parent);
                bool local = symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol or ILabelSymbol || symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction };
                bool own = symbol != null && SymbolEqualityComparer.Default.Equals(symbol.ContainingType, owner) && symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol;
                if (local || own)
                {
                    if (!names.TryGetValue(symbol, out string alias)) names[symbol] = alias = (local ? "$" : "@") + names.Count;
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
        var groups = new Dictionary<string, List<(string where, string name, int lines, string owner)>>();
        foreach (var compilation in build.All)
            foreach (var tree in compilation.SyntaxTrees)
            {
                string rel = Project.Rel(tree.FilePath);
                if (!Project.IsProject(rel)) continue;
                var model = compilation.GetSemanticModel(tree);
                foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var span = tree.GetLineSpan(method.Span);
                    int lines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
                    if (lines < 5) continue;
                    var owner = model.GetDeclaredSymbol(method)?.ContainingType;
                    string shape = Shape(model, method, owner);
                    if (shape == null || shape.Length < 120) continue;
                    if (!groups.TryGetValue(shape, out var list)) groups[shape] = list = new();
                    list.Add((rel + ":" + (span.StartLinePosition.Line + 1), method.Identifier.ValueText, lines, owner?.Name ?? ""));
                }
            }
        var text = new StringBuilder();
        var clones = groups.Values.Where(g => g.Count > 1).OrderByDescending(g => (g.Count - 1) * g[0].lines).ToList();
        text.AppendLine($"clone families {clones.Count}, copies {clones.Sum(g => g.Count - 1)}, lines {clones.Sum(g => (g.Count - 1) * g[0].lines)}");
        foreach (var group in clones)
        {
            text.AppendLine($"x{group.Count}  {group[0].lines} lines  {string.Join("/", group.Select(g => g.name).Distinct())}");
            foreach (var g in group) text.AppendLine("      " + g.where.Substring(16) + "  " + g.owner + "." + g.name);
        }
        File.WriteAllText(Path.Combine(Project.Out, "clones.txt"), text.ToString());
        Console.WriteLine(text.ToString().Split('\n')[0]);
    }
}
