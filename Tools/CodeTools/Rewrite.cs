using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// Rewrites that keep the program the same: callers of a method that only hands its parameters on call the real one,
/// and copies of one static helper all call the one that is kept.
static class Rewrite
{
    static readonly SymbolDisplayFormat Nested = new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    static string TypeText(INamedTypeSymbol type) => type.ToDisplayString(Nested);

    static bool Simple(ExpressionSyntax e) => e is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
        or ElementAccessExpressionSyntax or ThisExpressionSyntax or ParenthesizedExpressionSyntax;

    static void Add(Dictionary<string, List<Cut>> byFile, SyntaxTree tree, int start, int end, string with)
    {
        string rel = Project.Rel(tree.FilePath);
        if (!byFile.TryGetValue(rel, out var cuts)) byFile[rel] = cuts = new List<Cut>();
        cuts.Add(new Cut { Start = start, End = end, With = with, Tidy = false });
    }

    /// The method a static one-liner hands all its parameters to, in order; null when it does anything else.
    static IMethodSymbol ForwardTarget(SemanticModel model, MethodDeclarationSyntax method, IMethodSymbol symbol)
    {
        if (!symbol.IsStatic || symbol.IsGenericMethod || symbol.MethodKind != MethodKind.Ordinary || symbol.GetAttributes().Length > 0) return null;
        ExpressionSyntax body = method.ExpressionBody?.Expression;
        if (body == null && method.Body?.Statements.Count == 1)
            body = method.Body.Statements[0] switch { ReturnStatementSyntax r => r.Expression, ExpressionStatementSyntax e => e.Expression, _ => null };
        if (body is not InvocationExpressionSyntax call) return null;
        if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol called) return null;
        var target = (called.ReducedFrom ?? called).OriginalDefinition;
        if (!target.IsStatic || target.IsGenericMethod || SymbolEqualityComparer.Default.Equals(target.ContainingType, symbol.ContainingType)) return null;
        if (target.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)) return null;
        if (target.DeclaringSyntaxReferences.Length == 0) return null;   // only onto the project's own helpers
        var given = new List<ExpressionSyntax>();
        if (called.ReducedFrom != null && call.Expression is MemberAccessExpressionSyntax access) given.Add(access.Expression);
        foreach (var argument in call.ArgumentList.Arguments)
        {
            if (argument.NameColon != null || !argument.RefKindKeyword.IsKind(SyntaxKind.None)) return null;
            given.Add(argument.Expression);
        }
        if (given.Count != symbol.Parameters.Length || target.Parameters.Length != given.Count) return null;
        for (int i = 0; i < given.Count; i++)
        {
            if (given[i] is not IdentifierNameSyntax name) return null;
            if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, symbol.Parameters[i])) return null;
            if (symbol.Parameters[i].RefKind != RefKind.None || target.Parameters[i].RefKind != RefKind.None) return null;
            if (!SymbolEqualityComparer.Default.Equals(symbol.Parameters[i].Type, target.Parameters[i].Type)) return null;
        }
        return SymbolEqualityComparer.Default.Equals(symbol.ReturnType, target.ReturnType) ? target : null;
    }

    public static void Forwarders(bool dry)
    {
        int total = 0;
        for (int round = 0; round < 8; round++)
        {
            var build = Project.Load(false);
            var forwarders = new Dictionary<ISymbol, IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var compilation in build.All)
                foreach (var tree in compilation.SyntaxTrees)
                {
                    if (!Project.IsProject(Project.Rel(tree.FilePath))) continue;
                    var model = compilation.GetSemanticModel(tree);
                    foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                    {
                        if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol) continue;
                        var target = ForwardTarget(model, method, symbol);
                        if (target != null) forwarders[symbol.OriginalDefinition] = target;
                    }
                }
            var byFile = new Dictionary<string, List<Cut>>();
            int count = 0;
            foreach (var compilation in build.All)
                foreach (var tree in compilation.SyntaxTrees)
                {
                    if (!Project.IsProject(Project.Rel(tree.FilePath))) continue;
                    var model = compilation.GetSemanticModel(tree);
                    IMethodSymbol Forwarded(InvocationExpressionSyntax invocation)
                    {
                        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called) return null;
                        return forwarders.TryGetValue((called.ReducedFrom ?? called).OriginalDefinition, out var target) ? target : null;
                    }
                    foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var target = Forwarded(invocation);
                        if (target == null) continue;
                        if (invocation.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(inner => Forwarded(inner) != null)) continue;   // inner ones first
                        var arguments = invocation.ArgumentList.Arguments;
                        bool plain = arguments.All(a => a.NameColon == null && a.RefKindKeyword.IsKind(SyntaxKind.None));
                        string text = target.IsExtensionMethod && plain && arguments.Count >= 1 && Simple(arguments[0].Expression)
                            ? arguments[0].Expression + "." + target.Name + "(" + string.Join(", ", arguments.Skip(1).Select(a => a.ToString())) + ")"
                            : TypeText(target.ContainingType) + "." + target.Name + invocation.ArgumentList;
                        Add(byFile, tree, invocation.SpanStart, invocation.Span.End, text);
                        count++;
                    }
                }
            if (count == 0) break;
            total += count;
            var (written, _) = Edits.Commit(byFile, dry);
            Console.WriteLine($"  round {round + 1}: forwarders {forwarders.Count}, calls rewritten {count} in {written} files");
            if (dry) break;
        }
        Console.WriteLine($"FORWARDERS {(dry ? "(dry) " : "")}calls rewritten {total}");
    }

    /// What a method is, written so that two copies read the same: locals and parameters numbered, every other name by
    /// the thing it means (so two bodies that say "Rect" but mean different Rects differ), a call to itself as $self.
    static string Signature(SemanticModel model, MethodDeclarationSyntax method, IMethodSymbol symbol)
    {
        SyntaxNode body = (SyntaxNode)method.Body ?? method.ExpressionBody;
        if (body == null) return null;
        var names = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var text = new StringBuilder();
        text.Append(symbol.IsStatic ? "static " : "instance ");
        foreach (var parameter in symbol.Parameters) text.Append(parameter.RefKind).Append(' ').Append(parameter.Type.ToDisplayString()).Append(parameter.IsParams ? " params" : "").Append(',');
        text.Append("=>").Append(symbol.ReturnType.ToDisplayString()).Append('|');
        foreach (var token in body.DescendantTokens())
        {
            if (!token.IsKind(SyntaxKind.IdentifierToken)) { text.Append(token.Text).Append(' '); continue; }
            ISymbol meant = token.Parent is SimpleNameSyntax name ? model.GetSymbolInfo(name).Symbol : model.GetDeclaredSymbol(token.Parent);
            if (meant == null && token.Parent is SimpleNameSyntax unresolved)
            {
                var candidates = model.GetSymbolInfo(unresolved).CandidateSymbols;
                if (candidates.Length > 0) meant = candidates[0];
            }
            if (meant is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol or ILabelSymbol || meant is IMethodSymbol { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction })
            {
                if (!names.TryGetValue(meant, out string alias)) names[meant] = alias = "$" + names.Count;
                text.Append(alias).Append(' ');
                continue;
            }
            if (meant == null) { text.Append('?').Append(token.Text).Append(' '); continue; }
            var definition = meant.OriginalDefinition;
            if (definition is IMethodSymbol m && m.ReducedFrom != null) definition = m.ReducedFrom.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(definition, symbol.OriginalDefinition)) { text.Append("$self "); continue; }
            // An enum's members share their type's key (Dead.Key); here each one is itself.
            if (definition is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum }) { text.Append(definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(' '); continue; }
            text.Append(Dead.Key(definition) ?? definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(' ');
        }
        return text.ToString();
    }

    public static IMethodSymbol Named(Build build, string spec)
    {
        // "Type.Method/3" or "Outer.Inner.Method/2"
        int slash = spec.LastIndexOf('/');
        int arity = int.Parse(spec.Substring(slash + 1));
        string path = spec.Substring(0, slash);
        int dot = path.LastIndexOf('.');
        string typeName = path.Substring(0, dot).Replace('.', '+'), methodName = path.Substring(dot + 1);
        foreach (var compilation in build.All)
        {
            var type = compilation.Assembly.GetTypeByMetadataName(typeName);
            if (type == null) continue;
            var found = type.GetMembers(methodName).OfType<IMethodSymbol>().Where(m => m.Parameters.Length == arity).ToList();
            if (found.Count == 1) return found[0];
            if (found.Count > 1) throw new Exception("more than one " + spec);
        }
        throw new Exception("no method " + spec);
    }

    /// Static methods grouped by what they do (Signature): each group of two or more is one body written more than once.
    public static List<List<IMethodSymbol>> SameBodies(Build build, int least = 40)
    {
        var groups = new Dictionary<string, List<IMethodSymbol>>();
        foreach (var compilation in build.All)
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!Project.IsProject(Project.Rel(tree.FilePath))) continue;
                var model = compilation.GetSemanticModel(tree);
                foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol || symbol.MethodKind != MethodKind.Ordinary || symbol.IsGenericMethod) continue;
                    string signature = Signature(model, method, symbol);
                    if (signature == null || signature.Length < least) continue;
                    if (!groups.TryGetValue(signature, out var list)) groups[signature] = list = new();
                    list.Add(symbol);
                }
            }
        return groups.Values.Where(g => g.Count > 1).ToList();
    }

    /// Takes the named methods ("Type.Method/2") out of their files, with their comments.
    public static void Remove(string[] specs, bool dry)
    {
        var build = Project.Load(false);
        var byFile = new Dictionary<string, List<Cut>>();
        foreach (string spec in specs)
        {
            var method = Named(build, spec);
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var node = reference.GetSyntax();
                var (start, end) = Edits.WholeLines(node);
                string rel = Project.Rel(node.SyntaxTree.FilePath);
                if (!byFile.TryGetValue(rel, out var cuts)) byFile[rel] = cuts = new List<Cut>();
                cuts.Add(new Cut { Start = start, End = end });
            }
        }
        var (written, deleted) = Edits.Commit(byFile, dry);
        Console.WriteLine($"REMOVE {(dry ? "(dry) " : "")}{specs.Length} methods, files {written}, deleted {deleted}");
    }

    public static void Dedupe(string mapPath, bool dry, int least, string homesPath = null)
    {
        var build = Project.Load(false);
        // Methods named as homes outright: every same-named copy goes to them, whatever is open.
        var homes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (homesPath != null)
            foreach (string raw in File.ReadAllLines(homesPath))
                if (raw.Trim().Length > 0 && !raw.TrimStart().StartsWith("#")) homes.Add(Named(build, raw.Trim()));
        var pairs = new List<(IMethodSymbol from, IMethodSymbol to)>();
        var report = new StringBuilder();

        // 1. copies that are the same down to what every name means: the one copy that is not private is the home
        var groups = new Dictionary<string, List<IMethodSymbol>>();
        foreach (var compilation in build.All)
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!Project.IsProject(Project.Rel(tree.FilePath))) continue;
                var model = compilation.GetSemanticModel(tree);
                foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol || !symbol.IsStatic || symbol.MethodKind != MethodKind.Ordinary) continue;
                    if (symbol.GetAttributes().Length > 0 || symbol.IsGenericMethod) continue;
                    string signature = Signature(model, method, symbol);
                    if (signature == null || signature.Length < least) continue;
                    if (!groups.TryGetValue(signature, out var list)) groups[signature] = list = new();
                    list.Add(symbol);
                }
            }
        foreach (var group in groups.Values.Where(g => g.Count > 1))
        {
            var open = group.Where(m => homes.Contains(m)).ToList();
            if (open.Count == 0) open = group.Where(m => m.DeclaredAccessibility != Accessibility.Private).ToList();
            string names = string.Join(", ", group.Select(m => TypeText(m.ContainingType) + "." + m.Name));
            if (open.Count != 1) { report.AppendLine($"  no single home ({open.Count} open): {names}"); continue; }
            foreach (var copy in group)
            {
                if (SymbolEqualityComparer.Default.Equals(copy, open[0])) continue;
                // The same words under another name mean something else; and the game cannot call into the editor.
                if (copy.Name != open[0].Name) { report.AppendLine($"  other name, left: {TypeText(copy.ContainingType)}.{copy.Name} = {TypeText(open[0].ContainingType)}.{open[0].Name}"); continue; }
                if (open[0].ContainingAssembly.Name.EndsWith("Editor") && !copy.ContainingAssembly.Name.EndsWith("Editor")) { report.AppendLine($"  home is in the editor, left: {TypeText(copy.ContainingType)}.{copy.Name}"); continue; }
                pairs.Add((copy, open[0]));
            }
        }
        // 2. copies named outright (read and found to do the same)
        if (mapPath != null)
            foreach (string raw in File.ReadAllLines(mapPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var sides = line.Split("->");
                pairs.Add((Named(build, sides[0].Trim()), Named(build, sides[1].Trim())));
            }

        var byFile = new Dictionary<string, List<Cut>>();
        var widen = new Dictionary<IMethodSymbol, Accessibility>(SymbolEqualityComparer.Default);
        var target = new Dictionary<ISymbol, IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var (from, to) in pairs) target[from.OriginalDefinition] = to;
        int rewritten = 0;
        foreach (var compilation in build.All)
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!Project.IsProject(Project.Rel(tree.FilePath))) continue;
                var model = compilation.GetSemanticModel(tree);
                foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    var info = model.GetSymbolInfo(name);
                    var meant = (info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null)) as IMethodSymbol;
                    if (meant == null) continue;
                    var definition = (meant.ReducedFrom ?? meant).OriginalDefinition;
                    if (!target.TryGetValue(definition, out var to)) continue;
                    // inside a copy that goes away anyway: leave it
                    var holder = name.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                    if (holder != null && model.GetDeclaredSymbol(holder) is IMethodSymbol inside && target.ContainsKey(inside.OriginalDefinition)) continue;
                    SyntaxNode whole = name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;
                    var here = model.GetEnclosingSymbol(name.SpanStart)?.ContainingType;
                    bool sameType = false;
                    for (var t = here; t != null; t = t.ContainingType) sameType |= SymbolEqualityComparer.Default.Equals(t, to.ContainingType);
                    Add(byFile, tree, whole.SpanStart, whole.Span.End, (sameType ? "" : TypeText(to.ContainingType) + ".") + to.Name);
                    rewritten++;
                    bool otherAssembly = !SymbolEqualityComparer.Default.Equals(compilation.Assembly, to.ContainingAssembly)
                                         && compilation.Assembly.Name != to.ContainingAssembly.Name;
                    var need = otherAssembly ? Accessibility.Public : sameType ? Accessibility.Private : Accessibility.Internal;
                    if (!widen.TryGetValue(to, out var had) || need > had) widen[to] = need;
                }
            }
        foreach (var pair in widen)
        {
            var to = pair.Key;
            if (pair.Value <= to.DeclaredAccessibility && !(pair.Value == Accessibility.Internal && to.DeclaredAccessibility == Accessibility.Protected)) continue;
            if (pair.Value == Accessibility.Private) continue;
            string word = pair.Value == Accessibility.Public ? "public" : "internal";
            var declaration = (MethodDeclarationSyntax)to.DeclaringSyntaxReferences[0].GetSyntax();
            var modifier = declaration.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword));
            if (modifier != default) Add(byFile, declaration.SyntaxTree, modifier.SpanStart, modifier.Span.End, word);
            else
            {
                int at = declaration.Modifiers.Count > 0 ? declaration.Modifiers[0].SpanStart : declaration.ReturnType.SpanStart;
                Add(byFile, declaration.SyntaxTree, at, at, word + " ");
            }
            report.AppendLine($"  {word}: {TypeText(to.ContainingType)}.{to.Name}");
            if (pair.Value == Accessibility.Public && to.ContainingType.DeclaredAccessibility != Accessibility.Public)
                report.AppendLine($"  !! {TypeText(to.ContainingType)} must be public for the editor assembly to call {to.Name}");
        }
        foreach (var (from, to) in pairs)
            report.AppendLine($"  {TypeText(from.ContainingType)}.{from.Name} -> {TypeText(to.ContainingType)}.{to.Name}");
        var (written, _) = Edits.Commit(byFile, dry);
        Console.Write(report.ToString());
        Console.WriteLine($"DEDUPE {(dry ? "(dry) " : "")}copies {pairs.Count}, references rewritten {rewritten}, files {written}");
    }
}
