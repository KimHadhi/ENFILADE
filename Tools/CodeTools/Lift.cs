using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// Members that several classes carry word for word move up into one base class they then share.
/// A member goes up only when every class has it the same down to what each name means, and everything of the class it
/// touches goes up with it. Static state that changes stays (one base would make the classes share it).
static class Lift
{
    static readonly HashSet<string> UnityMessages = new()
    {
        "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy", "OnGUI", "OnValidate", "Reset",
        "OnAnimatorIK", "OnAnimatorMove", "OnNetworkSpawn", "OnNetworkDespawn", "OnApplicationQuit",
    };

    sealed class Member
    {
        public string Key;                    // name, with parameter types for a method
        public ISymbol Symbol;
        public SyntaxNode Node;               // the declaration (a variable declarator for a field)
        public string Shape;
        public HashSet<string> Own = new();   // own members it names
    }

    static string KeyOf(ISymbol symbol) => symbol is IMethodSymbol m
        ? m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.RefKind + " " + p.Type.ToDisplayString())) + ")"
        : symbol.Name;

    static string ShapeOf(SemanticModel model, SyntaxNode node, INamedTypeSymbol owner, ISymbol self, HashSet<string> own)
    {
        var names = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var text = new StringBuilder();
        // what is compared: for a field its whole declaration without the other variables, for the rest the declaration
        IEnumerable<SyntaxToken> tokens;
        if (node is VariableDeclaratorSyntax variable && variable.Parent?.Parent is BaseFieldDeclarationSyntax field)
            tokens = field.Modifiers.Concat(field.Declaration.Type.DescendantTokens()).Concat(variable.DescendantTokens());
        else tokens = node.DescendantTokens();
        foreach (var token in tokens)
        {
            if (token.IsKind(SyntaxKind.PrivateKeyword) || token.IsKind(SyntaxKind.ProtectedKeyword) || token.IsKind(SyntaxKind.InternalKeyword) || token.IsKind(SyntaxKind.PublicKeyword))
            {
                if (token.Parent == node || token.Parent is BaseFieldDeclarationSyntax) { text.Append(token.IsKind(SyntaxKind.PublicKeyword) || token.IsKind(SyntaxKind.InternalKeyword) ? token.Text + " " : ""); continue; }
            }
            if (!token.IsKind(SyntaxKind.IdentifierToken)) { text.Append(token.Text).Append(' '); continue; }
            ISymbol meant = token.Parent is SimpleNameSyntax name ? model.GetSymbolInfo(name).Symbol ?? model.GetSymbolInfo(name).CandidateSymbols.FirstOrDefault()
                : model.GetDeclaredSymbol(token.Parent);
            if (meant is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol or ILabelSymbol || meant is IMethodSymbol { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction })
            {
                if (!names.TryGetValue(meant, out string alias)) names[meant] = alias = "$" + names.Count;
                text.Append(alias).Append(' ');
                continue;
            }
            if (meant == null) { text.Append('?').Append(token.Text).Append(' '); continue; }
            var definition = meant.OriginalDefinition;
            if (definition is IMethodSymbol reduced && reduced.ReducedFrom != null) definition = reduced.ReducedFrom.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(definition, owner)) { text.Append("@type "); own.Add("@type"); continue; }
            if (SymbolEqualityComparer.Default.Equals(definition.ContainingType, owner) && definition is not INamedTypeSymbol)
            {
                string key = KeyOf(definition);
                if (!SymbolEqualityComparer.Default.Equals(definition, self)) own.Add(key);
                text.Append('@').Append(key).Append(' ');
                continue;
            }
            if (definition is INamedTypeSymbol nested && SymbolEqualityComparer.Default.Equals(nested.ContainingType, owner)) { text.Append("@nested:").Append(nested.Name).Append(' '); own.Add("@nested:" + nested.Name); continue; }
            if (definition is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum }) { text.Append(definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(' '); continue; }
            text.Append(Dead.Key(definition) ?? definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(' ');
        }
        return text.ToString();
    }

    static Dictionary<string, Member> MembersOf(CSharpCompilation compilation, INamedTypeSymbol type)
    {
        var members = new Dictionary<string, Member>();
        foreach (var symbol in type.GetMembers())
        {
            if (symbol.IsImplicitlyDeclared || symbol is INamedTypeSymbol) continue;
            if (symbol is IMethodSymbol method && (method.MethodKind != MethodKind.Ordinary || UnityMessages.Contains(method.Name) || method.IsOverride || method.IsVirtual || method.IsAbstract)) continue;
            if (symbol is IPropertySymbol or IEventSymbol) { }
            if (symbol.DeclaringSyntaxReferences.Length != 1 || symbol.GetAttributes().Length > 0 && symbol is not IFieldSymbol) continue;
            if (symbol is IFieldSymbol field)
            {
                // Static state that changes would become one for all the classes.
                if (field.IsStatic && !field.IsConst && !(field.IsReadOnly && (field.Type is IArrayTypeSymbol || field.Type.IsValueType || field.Type.SpecialType == SpecialType.System_String))) continue;
                if (field.GetAttributes().Length > 0) continue;   // serialized or shown fields keep their place
            }
            var node = symbol.DeclaringSyntaxReferences[0].GetSyntax();
            var member = new Member { Key = KeyOf(symbol), Symbol = symbol, Node = node };
            member.Shape = ShapeOf(compilation.GetSemanticModel(node.SyntaxTree), node, type, symbol, member.Own);
            members[member.Key] = member;
        }
        return members;
    }

    static string Lifted(SyntaxNode node, string text)
    {
        // one member's source with its comment block, private made protected
        var (start, end) = Edits.WholeLines(node);
        string body = text.Substring(start, end - start);
        var modifiers = node switch { BaseMethodDeclarationSyntax m => m.Modifiers, BasePropertyDeclarationSyntax p => p.Modifiers, BaseFieldDeclarationSyntax f => f.Modifiers, _ => default };
        var access = modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.PublicKeyword));
        if (access == default)
        {
            int at = node.SpanStart - start;
            return body.Substring(0, at) + "protected " + body.Substring(at);
        }
        if (!access.IsKind(SyntaxKind.PrivateKeyword)) return body;
        int from = access.SpanStart - start;
        return body.Substring(0, from) + "protected" + body.Substring(from + access.Span.Length);
    }

    public static void Run(string baseName, string file, string[] typeNames, string primaryName, string summary, bool dry)
    {
        var build = Project.Load(false);
        var types = new List<(INamedTypeSymbol type, CSharpCompilation compilation)>();
        foreach (string name in typeNames)
        {
            var found = build.All.Select(c => (type: c.Assembly.GetTypeByMetadataName(name), c)).FirstOrDefault(x => x.type != null);
            if (found.type == null) throw new Exception("no type " + name);
            types.Add(found);
        }
        var parent = types[0].type.BaseType;
        if (types.Any(t => !SymbolEqualityComparer.Default.Equals(t.type.BaseType, parent))) throw new Exception("the classes do not share a base");
        var all = types.Select(t => MembersOf(t.compilation, t.type)).ToList();
        int primary = Math.Max(0, Array.IndexOf(typeNames, primaryName));

        var lifted = new HashSet<string>(all[0].Keys);
        foreach (var members in all) lifted.IntersectWith(members.Keys);
        lifted.RemoveWhere(key => all.Any(members => members[key].Shape != all[0][key].Shape));
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string key in lifted.ToList())
                if (all[primary][key].Own.Any(own => !lifted.Contains(own)))
                {
                    lifted.Remove(key);
                    changed = true;
                }
        }
        if (lifted.Count == 0) { Console.WriteLine("LIFT nothing in common"); return; }

        // the base class, in the order of the primary class
        var primaryTree = types[primary].type.DeclaringSyntaxReferences[0].SyntaxTree;
        string primaryText = primaryTree.GetText().ToString();
        string newline = primaryText.Contains("\r\n") ? "\r\n" : "\n";
        var root = (CompilationUnitSyntax)primaryTree.GetRoot();
        var text = new StringBuilder();
        foreach (var use in root.Usings) text.Append(use.ToString()).Append(newline);
        text.Append(newline).Append("/// <summary>").Append(newline);
        foreach (string line in summary.Split('|')) text.Append("/// ").Append(line.Trim()).Append(newline);
        text.Append("/// </summary>").Append(newline);
        text.Append("public abstract class ").Append(baseName).Append(" : ").Append(parent.Name).Append(newline).Append('{').Append(newline);
        var ordered = lifted.Select(key => all[primary][key]).OrderBy(m => m.Node.SpanStart).ToList();
        var doneFields = new HashSet<SyntaxNode>();
        int lines = 0;
        bool firstMember = true;
        foreach (var member in ordered)
        {
            // a blank line before anything that brings a comment or a body, so the base reads like written code
            SyntaxNode shown = member.Node is VariableDeclaratorSyntax d && d.Parent?.Parent is BaseFieldDeclarationSyntax owner ? owner : member.Node;
            bool again = shown is BaseFieldDeclarationSyntax seen && doneFields.Contains(seen);
            if (!firstMember && !again && (shown is not BaseFieldDeclarationSyntax || shown.GetLeadingTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)))) text.Append(newline);
            firstMember = false;
            if (member.Node is VariableDeclaratorSyntax variable && variable.Parent?.Parent is BaseFieldDeclarationSyntax field)
            {
                if (!doneFields.Add(field)) continue;
                var mine = field.Declaration.Variables.Where(v => lifted.Contains(v.Identifier.ValueText)).ToList();
                if (mine.Count == field.Declaration.Variables.Count) { text.Append(Lifted(field, primaryText)); continue; }
                var (start, _) = Edits.WholeLines(field);
                string lead = primaryText.Substring(start, field.SpanStart - start);
                string modifiers = string.Join(" ", field.Modifiers.Select(m => m.IsKind(SyntaxKind.PrivateKeyword) ? "protected" : m.Text));
                if (!field.Modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.PublicKeyword)))
                    modifiers = ("protected " + modifiers).Trim();
                text.Append(lead).Append(modifiers).Append(' ').Append(field.Declaration.Type.ToString()).Append(' ')
                    .Append(string.Join(", ", mine.Select(v => v.ToString()))).Append(';').Append(newline);
                continue;
            }
            text.Append(Lifted(member.Node, primaryText));
        }
        text.Append('}').Append(newline);

        // the classes: the lifted members go, the base changes
        var byFile = new Dictionary<string, List<Cut>>();
        for (int i = 0; i < types.Count; i++)
        {
            var groups = new Dictionary<BaseFieldDeclarationSyntax, List<int>>();
            foreach (string key in lifted)
            {
                var node = all[i][key].Node;
                string rel = Project.Rel(node.SyntaxTree.FilePath);
                if (!byFile.TryGetValue(rel, out var cuts)) byFile[rel] = cuts = new List<Cut>();
                if (node is VariableDeclaratorSyntax variable && variable.Parent?.Parent is BaseFieldDeclarationSyntax field)
                {
                    if (!groups.TryGetValue(field, out var list)) groups[field] = list = new List<int>();
                    list.Add(field.Declaration.Variables.IndexOf(variable));
                    continue;
                }
                var (start, end) = Edits.WholeLines(node);
                cuts.Add(new Cut { Start = start, End = end });
                var span = node.SyntaxTree.GetLineSpan(node.Span);
                lines += span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
            }
            foreach (var pair in groups)
            {
                var field = pair.Key;
                var cuts = byFile[Project.Rel(field.SyntaxTree.FilePath)];
                var variables = field.Declaration.Variables;
                if (pair.Value.Count == variables.Count)
                {
                    var (start, end) = Edits.WholeLines(field);
                    cuts.Add(new Cut { Start = start, End = end });
                    lines++;
                    continue;
                }
                var kept = Enumerable.Range(0, variables.Count).Where(k => !pair.Value.Contains(k)).Select(k => variables[k].ToString());
                cuts.Add(new Cut { Start = variables[0].SpanStart, End = variables[variables.Count - 1].Span.End, With = string.Join(", ", kept), Tidy = false });
            }
            foreach (var reference in types[i].type.DeclaringSyntaxReferences)
            {
                var declaration = (TypeDeclarationSyntax)reference.GetSyntax();
                var first = declaration.BaseList?.Types.FirstOrDefault();
                if (first == null) continue;
                string rel = Project.Rel(declaration.SyntaxTree.FilePath);
                if (!byFile.TryGetValue(rel, out var cuts)) byFile[rel] = cuts = new List<Cut>();
                if (first.Type.ToString() == parent.Name) cuts.Add(new Cut { Start = first.Type.SpanStart, End = first.Type.Span.End, With = baseName, Tidy = false });
            }
        }

        Console.WriteLine($"LIFT {(dry ? "(dry) " : "")}{baseName}: {lifted.Count} members out of {types.Count} classes, about {lines} lines leave them");
        foreach (var member in ordered) Console.WriteLine("    " + member.Key);
        var missing = all[primary].Keys.Where(k => !lifted.Contains(k) && all.All(m => m.ContainsKey(k))).OrderBy(k => k).ToList();
        Console.WriteLine("  in every class but not alike (or tied to what stays): " + string.Join(", ", missing));
        if (dry) return;
        string full = Project.Full(file);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full + ".meta", "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        File.WriteAllText(full, text.ToString(), new UTF8Encoding(false));
        Edits.Commit(byFile, false);
    }
}
