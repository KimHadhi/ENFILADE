using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// A field or an automatic property that is only ever written keeps nothing anyone uses. Where every write is a plain
/// statement of its own ("total = 0f;", "PlayCount++;") with nothing but plain values on its right, the statements go;
/// the member is then unused and "kill" takes it. The rest (a write inside an if without braces, a call on the right,
/// an initialiser, a struct laid out for the engine, a field a file is saved from) is listed and left alone.
/// Reads the list "dead" wrote to Logs/CodeTools/dead.txt, so run that first.
static class WriteOnly
{
    static readonly Regex Line = new(@"^\s+(?<file>Assets/\S+):(?<line>\d+) (?<kind>field|property) (?<name>\S+) writes=\d+ (?<flags>\S+)", RegexOptions.Compiled);

    static bool Plain(ExpressionSyntax e) => e switch
    {
        LiteralExpressionSyntax => true,
        IdentifierNameSyntax => true,
        DefaultExpressionSyntax => true,
        MemberAccessExpressionSyntax access => Plain(access.Expression),
        PrefixUnaryExpressionSyntax unary => !unary.IsKind(SyntaxKind.PreIncrementExpression) && !unary.IsKind(SyntaxKind.PreDecrementExpression) && Plain(unary.Operand),
        ParenthesizedExpressionSyntax inner => Plain(inner.Expression),
        BinaryExpressionSyntax both => Plain(both.Left) && Plain(both.Right),
        ConditionalExpressionSyntax pick => Plain(pick.Condition) && Plain(pick.WhenTrue) && Plain(pick.WhenFalse),
        CastExpressionSyntax cast => Plain(cast.Expression),
        ThisExpressionSyntax => true,
        _ => false,
    };

    public static void Run(bool dry)
    {
        string report = Path.Combine(Project.Out, "dead.txt");
        var wanted = new List<(string file, int line, string kind, string name)>();
        bool inList = false;
        foreach (string raw in File.ReadAllLines(report))
        {
            if (raw.StartsWith("WRITE-ONLY")) { inList = true; continue; }
            if (!inList) continue;
            var match = Line.Match(raw);
            if (!match.Success) continue;
            string flags = match.Groups["flags"].Value;
            if (flags.Contains("serialized") || flags.Contains("structField") || flags.Contains("reflect")) continue;
            wanted.Add((match.Groups["file"].Value, int.Parse(match.Groups["line"].Value), match.Groups["kind"].Value, match.Groups["name"].Value));
        }

        var build = Project.Load(false);
        var symbols = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var left = new List<string>();
        foreach (var compilation in build.All)
            foreach (var tree in compilation.SyntaxTrees)
            {
                string rel = Project.Rel(tree.FilePath);
                var here = wanted.Where(w => w.file == rel).ToList();
                if (here.Count == 0) continue;
                var model = compilation.GetSemanticModel(tree);
                var text = tree.GetText();
                foreach (var want in here)
                {
                    string member = want.name.Substring(want.name.LastIndexOf('.') + 1);
                    ISymbol symbol = null;
                    foreach (var node in tree.GetRoot().DescendantNodes())
                    {
                        if (text.Lines.GetLineFromPosition(node.SpanStart).LineNumber + 1 != want.line && !(node is PropertyDeclarationSyntax p0 && text.Lines.GetLineFromPosition(p0.Identifier.SpanStart).LineNumber + 1 == want.line)) continue;
                        if (node is VariableDeclaratorSyntax field && field.Identifier.ValueText == member && field.Parent?.Parent is FieldDeclarationSyntax)
                        {
                            if (field.Initializer != null && !Plain(field.Initializer.Value)) { left.Add(want.name + ": its initialiser does work"); symbol = null; break; }
                            symbol = model.GetDeclaredSymbol(field);
                            break;
                        }
                        if (node is PropertyDeclarationSyntax property && property.Identifier.ValueText == member)
                        {
                            bool automatic = property.AccessorList != null && property.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null) && property.ExpressionBody == null;
                            if (!automatic) { left.Add(want.name + ": a property with a setter that does work (only its value is unread)"); break; }
                            if (property.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword) || m.IsKind(SyntaxKind.VirtualKeyword) || m.IsKind(SyntaxKind.AbstractKeyword))
                                || property.ExplicitInterfaceSpecifier != null) { left.Add(want.name + ": part of a contract"); break; }
                            symbol = model.GetDeclaredSymbol(property);
                            if (symbol is IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 }) symbol = null;
                            break;
                        }
                    }
                    if (symbol != null && symbol.ContainingType.AllInterfaces.SelectMany(i => i.GetMembers(symbol.Name)).Any()) { left.Add(want.name + ": an interface asks for it"); continue; }
                    if (symbol != null) symbols[symbol] = want.name;
                }
            }

        // every use of each, and whether all of them are statements that can simply go
        var cuts = new Dictionary<ISymbol, List<(string rel, int start, int end)>>(SymbolEqualityComparer.Default);
        var refused = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var names = new HashSet<string>(symbols.Keys.Select(s => s.Name));
        foreach (var compilation in build.All)
            foreach (var tree in compilation.SyntaxTrees)
            {
                string rel = Project.Rel(tree.FilePath);
                if (!Project.IsProject(rel)) continue;
                SemanticModel model = null;
                foreach (var name in tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (!names.Contains(name.Identifier.ValueText)) continue;
                    model ??= compilation.GetSemanticModel(tree);
                    var symbol = model.GetSymbolInfo(name).Symbol?.OriginalDefinition;
                    if (symbol == null || !symbols.ContainsKey(symbol)) continue;
                    ExpressionSyntax target = name;
                    while (target.Parent is MemberAccessExpressionSyntax access && access.Name == target) target = access;
                    string why = null;
                    StatementSyntax statement = null;
                    switch (target.Parent)
                    {
                        case AssignmentExpressionSyntax set when set.Left == target && set.Parent is ExpressionStatementSyntax line:
                            if (!Plain(set.Right)) why = "a call or more on the right of a write";
                            else if (target is MemberAccessExpressionSyntax through && !Plain(through.Expression)) why = "written through a call";
                            statement = line;
                            break;
                        case PostfixUnaryExpressionSyntax step when step.Parent is ExpressionStatementSyntax line: statement = line; break;
                        case PrefixUnaryExpressionSyntax step when step.Parent is ExpressionStatementSyntax line && (step.IsKind(SyntaxKind.PreIncrementExpression) || step.IsKind(SyntaxKind.PreDecrementExpression)): statement = line; break;
                        default: why = "used in some other way than a plain write"; break;
                    }
                    if (why == null && statement.Parent is not (BlockSyntax or SwitchSectionSyntax)) why = "a write that is the whole body of an if or a loop";
                    if (why != null) { refused[symbol] = why; continue; }
                    if (!cuts.TryGetValue(symbol, out var list)) cuts[symbol] = list = new();
                    list.Add((rel, statement.SpanStart, statement.Span.End));
                }
            }

        var byFile = new Dictionary<string, List<Cut>>();
        int members = 0, statements = 0;
        foreach (var pair in symbols)
        {
            if (refused.TryGetValue(pair.Key, out string why)) { left.Add(pair.Value + ": " + why); continue; }
            members++;
            if (!cuts.TryGetValue(pair.Key, out var list)) continue;
            foreach (var (rel, start, end) in list.Distinct())
            {
                if (!byFile.TryGetValue(rel, out var edits)) byFile[rel] = edits = new List<Cut>();
                edits.Add(new Cut { Start = start, End = end, With = "", Tidy = true });
                statements++;
            }
        }
        var (written, _) = Edits.Commit(byFile, dry);
        File.WriteAllLines(Path.Combine(Project.Out, "writeonly-left.txt"), left.OrderBy(l => l));
        Console.WriteLine($"WRITEONLY {(dry ? "(dry) " : "")}members {members}, write statements removed {statements}, files {written}, left alone {left.Count} (Logs/CodeTools/writeonly-left.txt)");
    }
}
