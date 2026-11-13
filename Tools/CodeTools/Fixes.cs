using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// Mechanical repairs of what the audit finds.
static class Fixes
{
    static void Add(Dictionary<string, List<Cut>> byFile, SyntaxTree tree, int start, int end, string with)
    {
        string rel = Project.Rel(tree.FilePath);
        if (!byFile.TryGetValue(rel, out var cuts)) byFile[rel] = cuts = new List<Cut>();
        cuts.Add(new Cut { Start = start, End = end, With = with, Tidy = false });
    }

    public static bool UnityObject(ITypeSymbol type)
    {
        for (var at = type; at != null; at = at.BaseType)
            if (at.Name == "Object" && at.ContainingNamespace?.ToDisplayString() == "UnityEngine") return true;
        return false;
    }

    public static bool SaysOrNull(ExpressionSyntax tested) => tested is InvocationExpressionSyntax
    {
        Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "OrNull" } or MemberBindingExpressionSyntax { Name.Identifier.ValueText: "OrNull" }
    };

    static bool Primary(ExpressionSyntax e) => e is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
        or ElementAccessExpressionSyntax or ThisExpressionSyntax or ParenthesizedExpressionSyntax or MemberBindingExpressionSyntax or ElementBindingExpressionSyntax
        or BaseExpressionSyntax or ObjectCreationExpressionSyntax;

    /// ?. , ?? and ??= on a Unity object take a destroyed one for a live one: the tested side gets .OrNull() (UnityNull).
    public static void OrNull(bool dry)
    {
        var build = Project.Load(false);
        var byFile = new Dictionary<string, List<Cut>>();
        int count = 0;
        foreach (var compilation in build.All)
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!Project.IsProject(Project.Rel(tree.FilePath)) || tree.FilePath.EndsWith("UnityNull.cs")) continue;
                var model = compilation.GetSemanticModel(tree);
                void Guard(ExpressionSyntax tested)
                {
                    if (SaysOrNull(tested) || !UnityObject(model.GetTypeInfo(tested).Type)) return;
                    if (Primary(tested)) Add(byFile, tree, tested.Span.End, tested.Span.End, ".OrNull()");
                    else
                    {
                        Add(byFile, tree, tested.SpanStart, tested.SpanStart, "(");
                        Add(byFile, tree, tested.Span.End, tested.Span.End, ").OrNull()");
                    }
                    count++;
                }
                foreach (var node in tree.GetRoot().DescendantNodes())
                    switch (node)
                    {
                        case ConditionalAccessExpressionSyntax access:
                            Guard(access.Expression);
                            break;
                        case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression):
                            Guard(binary.Left);
                            break;
                        case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                            if (!UnityObject(model.GetTypeInfo(assignment.Left).Type)) break;
                            string left = assignment.Left.ToString(), right = assignment.Right.ToString();
                            if (assignment.Parent is ExpressionStatementSyntax statement)
                                Add(byFile, tree, statement.SpanStart, statement.Span.End, "if (" + left + " == null) " + left + " = " + right + ";");
                            else Add(byFile, tree, assignment.SpanStart, assignment.Span.End, left + " = " + left + ".OrNull() ?? " + right);
                            count++;
                            break;
                    }
            }
        var (written, _) = Edits.Commit(byFile, dry);
        Console.WriteLine($"ORNULL {(dry ? "(dry) " : "")}places {count}, files {written}");
    }

    /// Takes a parameter nothing reads out of a method and its argument out of every call ("Type.Method/3:name").
    public static void DropParameters(string[] specs, bool dry)
    {
        var build = Project.Load(false);
        var byFile = new Dictionary<string, List<Cut>>();
        foreach (string spec in specs)
        {
            int colon = spec.LastIndexOf(':');
            var method = Rewrite.Named(build, spec.Substring(0, colon));
            var parameter = method.Parameters.First(p => p.Name == spec.Substring(colon + 1));
            void CutFromList<T>(SeparatedSyntaxList<T> list, int index, SyntaxTree tree) where T : SyntaxNode
            {
                if (index < 0 || index >= list.Count) return;
                if (list.Count == 1) Add(byFile, tree, list[0].SpanStart, list[0].Span.End, "");
                else if (index == 0) Add(byFile, tree, list[0].SpanStart, list[1].SpanStart, "");
                else Add(byFile, tree, list.GetSeparator(index - 1).SpanStart, list[index].Span.End, "");
            }
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var declaration = (MethodDeclarationSyntax)reference.GetSyntax();
                CutFromList(declaration.ParameterList.Parameters, parameter.Ordinal, declaration.SyntaxTree);
            }
            int calls = 0;
            foreach (var compilation in build.All)
                foreach (var tree in compilation.SyntaxTrees)
                {
                    if (!Project.IsProject(Project.Rel(tree.FilePath))) continue;
                    var model = compilation.GetSemanticModel(tree);
                    foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol called) continue;
                        // By its written name: the editor's assembly sees the game's method as another symbol object, and
                        // comparing the objects left its calls with one argument too many (0.9.4, CsvTable.Parse).
                        if ((called.ReducedFrom ?? called).OriginalDefinition.GetDocumentationCommentId() != method.OriginalDefinition.GetDocumentationCommentId()) continue;
                        var arguments = call.ArgumentList.Arguments;
                        int index = -1;
                        for (int i = 0; i < arguments.Count; i++)
                        {
                            bool named = arguments[i].NameColon != null;
                            if (named ? arguments[i].NameColon.Name.Identifier.ValueText == parameter.Name
                                : i == parameter.Ordinal - (called.ReducedFrom != null ? 1 : 0) && arguments.Take(i).All(a => a.NameColon == null)) index = i;
                        }
                        if (index < 0) continue;   // left to its default
                        if (arguments[index].Expression.DescendantNodesAndSelf().Any(n => n is InvocationExpressionSyntax or AssignmentExpressionSyntax))
                            Console.WriteLine("  !! the argument does something, look: " + Project.Where(call.GetLocation()) + "  " + arguments[index]);
                        CutFromList(arguments, index, tree);
                        calls++;
                    }
                }
            Console.WriteLine($"  {spec}: {calls} calls");
        }
        var (written, _) = Edits.Commit(byFile, dry);
        Console.WriteLine($"DROP {(dry ? "(dry) " : "")}{specs.Length} parameters, files {written}");
    }
}
