using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// Every use of a named field or method reads as something else: the line "PathArm.upper=arm.Upper" turns upper (and
/// other.upper) into arm.Upper wherever it is used, in the type itself, in what inherits it and in anyone else who
/// reaches it. For moving a class's members into an object it holds: the uses are found by what they mean, so a local
/// or a parameter of the same name is left alone. The declarations stay for the hand that moves them, so run this
/// first, on code that still compiles, and "check" once the declarations have followed.
static class Retarget
{
    public static void Run(string specPath, bool dry)
    {
        var build = Project.Load(false);
        var wanted = new Dictionary<string, string>();   // a member's written name -> what its uses become
        var names = new HashSet<string>();
        foreach (string raw in File.ReadAllLines(specPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int equals = line.IndexOf('=');
            if (equals < 0) throw new Exception("no '=' in: " + line);
            string path = line.Substring(0, equals).Trim(), with = line.Substring(equals + 1).Trim();
            int dot = path.LastIndexOf('.');
            string typeName = path.Substring(0, dot).Replace('.', '+'), member = path.Substring(dot + 1);
            bool any = false;
            foreach (var compilation in build.All)
            {
                var type = compilation.Assembly.GetTypeByMetadataName(typeName);
                if (type == null) continue;
                foreach (var symbol in type.GetMembers(member))
                {
                    string id = symbol.GetDocumentationCommentId();
                    if (id == null) continue;
                    wanted[id] = with;
                    any = true;
                }
            }
            if (!any) throw new Exception("no member " + path);
            names.Add(member);
        }

        var byFile = new Dictionary<string, List<Cut>>();
        var perMember = new Dictionary<string, int>();
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
                    var symbol = model.GetSymbolInfo(name).Symbol;
                    symbol = symbol is IMethodSymbol method ? (method.ReducedFrom ?? method).OriginalDefinition : symbol?.OriginalDefinition;
                    string id = symbol?.GetDocumentationCommentId();
                    if (id == null || !wanted.TryGetValue(id, out string with)) continue;
                    if (!byFile.TryGetValue(rel, out var cuts)) byFile[rel] = cuts = new List<Cut>();
                    cuts.Add(new Cut { Start = name.SpanStart, End = name.Span.End, With = with, Tidy = false });
                    perMember[id] = perMember.TryGetValue(id, out int had) ? had + 1 : 1;
                }
            }
        foreach (var pair in wanted.OrderBy(p => p.Key))
            Console.WriteLine($"  {perMember.GetValueOrDefault(pair.Key),4}  {pair.Key.Substring(2)} -> {pair.Value}");
        var (written, _) = Edits.Commit(byFile, dry);
        Console.WriteLine($"RETARGET {(dry ? "(dry) " : "")}uses {perMember.Values.Sum()}, files {written}");
    }
}
