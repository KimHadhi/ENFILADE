using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// One way to read what a person wrote in a table.
/// "numbers": every float.TryParse / int.TryParse written out with the invariant culture calls the game's own reader
/// (TableCells.TryNumber / TryWhole), so a number is read the same way everywhere.
/// "cells": the lines that fetch a row, take a cell and read it, each written out by hand in two dozen files, become
/// the one call that says it: GameTables.Number(table, row, field, fallback) or row.Number(field, fallback).
static class Numbers
{
    public static void Run(bool dry)
    {
        var build = Project.Load(false);
        var byFile = new Dictionary<string, List<Cut>>();
        int count = 0;
        foreach (var compilation in build.All)
            foreach (var tree in compilation.SyntaxTrees)
            {
                string rel = Project.Rel(tree.FilePath);
                if (!Project.IsProject(rel) || rel.Contains("/Core/Tables/")) continue;
                var model = compilation.GetSemanticModel(tree);
                foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol called || called.Name != "TryParse" || called.Parameters.Length != 4) continue;
                    var kind = called.ContainingType?.SpecialType;
                    if (kind != SpecialType.System_Single && kind != SpecialType.System_Int32) continue;
                    var arguments = call.ArgumentList.Arguments;
                    string style = kind == SpecialType.System_Single ? "NumberStyles.Float" : "NumberStyles.Integer";
                    if (!arguments[1].ToString().EndsWith(style) || !arguments[2].ToString().EndsWith("CultureInfo.InvariantCulture")) continue;
                    if (called.Parameters[0].Type.SpecialType != SpecialType.System_String) continue;
                    string reader = kind == SpecialType.System_Single ? "TableCells.TryNumber(" : "TableCells.TryWhole(";
                    Add(byFile, rel, call.SpanStart, call.Span.End, reader + arguments[0] + ", " + arguments[3] + ")");
                    count++;
                }
            }
        var (written, _) = Edits.Commit(byFile, dry);
        Console.WriteLine($"NUMBERS {(dry ? "(dry) " : "")}calls {count}, files {written}");
    }

    static void Add(Dictionary<string, List<Cut>> byFile, string rel, int start, int end, string with)
    {
        if (!byFile.TryGetValue(rel, out var cuts)) byFile[rel] = cuts = new List<Cut>();
        cuts.Add(new Cut { Start = start, End = end, With = with, Tidy = false });
    }

    static bool Same(ISymbol a, ISymbol b) => a != null && SymbolEqualityComparer.Default.Equals(a, b);

    static void Flatten(ExpressionSyntax condition, List<ExpressionSyntax> terms)
    {
        while (condition is ParenthesizedExpressionSyntax inner) condition = inner.Expression;
        if (condition is BinaryExpressionSyntax both && both.IsKind(SyntaxKind.LogicalAndExpression))
        {
            Flatten(both.Left, terms);
            Flatten(both.Right, terms);
        }
        else terms.Add(condition);
    }

    /// A fallback that costs nothing and changes nothing when it is worked out every time, not only when the cell is missing.
    static bool Plain(ExpressionSyntax e) => e switch
    {
        LiteralExpressionSyntax => true,
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax access => Plain(access.Expression),
        PrefixUnaryExpressionSyntax unary => Plain(unary.Operand),
        ParenthesizedExpressionSyntax inner => Plain(inner.Expression),
        BinaryExpressionSyntax both => Plain(both.Left) && Plain(both.Right),
        CastExpressionSyntax cast => Plain(cast.Expression),
        _ => false,
    };

    sealed class Found
    {
        public string Kind;                 // Number, Whole or Flag
        public ISymbol Value;               // the out variable the reader fills
        public List<ISymbol> Declared = new();
        public Func<string, string> Call;   // fallback text → the one call
    }

    /// "[GameTables.TryGetRow(T, R, out var row) &&] [row != null &&] row.TryGetValue(K, out string text)
    ///  [&& text is filled] && TableCells.TryNumber(text[.Trim()], out float value)", and nothing else.
    static Found Match(SemanticModel model, Compilation compilation, ITypeSymbol rowType, ExpressionSyntax condition)
    {
        var terms = new List<ExpressionSyntax>();
        Flatten(condition, terms);
        if (terms.Count < 2) return null;
        if (terms[^1] is not InvocationExpressionSyntax read || model.GetSymbolInfo(read).Symbol is not IMethodSymbol reader) return null;
        if (reader.ContainingType?.Name != "TableCells" || read.ArgumentList.Arguments.Count != 2) return null;
        string kind = reader.Name switch { "TryNumber" => "Number", "TryWhole" => "Whole", "TryFlag" => "Flag", _ => null };
        if (kind == null) return null;
        if (read.ArgumentList.Arguments[1].Expression is not DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax valueName }) return null;
        var value = model.GetDeclaredSymbol(valueName);
        var textExpression = read.ArgumentList.Arguments[0].Expression;
        if (textExpression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Trim" } trimmed } trim && trim.ArgumentList.Arguments.Count == 0)
            textExpression = trimmed.Expression;
        if (textExpression is not IdentifierNameSyntax textName || model.GetSymbolInfo(textName).Symbol is not ILocalSymbol text) return null;

        int at = terms.Count - 2;
        while (at >= 0 && GuardOf(model, terms[at], text)) at--;
        if (at < 0) return null;
        if (terms[at] is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "TryGetValue" } getter } get) return null;
        if (get.ArgumentList.Arguments.Count != 2) return null;
        if (get.ArgumentList.Arguments[1].Expression is not DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax textDeclared }
            || !Same(model.GetDeclaredSymbol(textDeclared), text)) return null;
        var row = getter.Expression;
        var rowIs = model.GetTypeInfo(row).Type;
        if (rowIs == null || !compilation.ClassifyConversion(rowIs, rowType).IsImplicit) return null;
        string key = get.ArgumentList.Arguments[0].Expression.ToString();
        var rowSymbol = model.GetSymbolInfo(row).Symbol;
        at--;
        while (at >= 0 && terms[at] is BinaryExpressionSyntax notNull && notNull.IsKind(SyntaxKind.NotEqualsExpression)
               && notNull.Right.IsKind(SyntaxKind.NullLiteralExpression) && Same(model.GetSymbolInfo(notNull.Left).Symbol, rowSymbol)) at--;

        var found = new Found { Kind = kind, Value = value };
        found.Declared.Add(value);
        found.Declared.Add(text);
        if (at < 0)
        {
            if (row is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)) return null;
            found.Call = fallback => row + "." + kind + "(" + key + ", " + fallback + ")";
            return found;
        }
        if (at != 0 || terms[0] is not InvocationExpressionSyntax fetch || model.GetSymbolInfo(fetch).Symbol is not IMethodSymbol fetcher) return null;
        if (fetcher.Name != "TryGetRow" || fetcher.ContainingType?.Name != "GameTables" || fetch.ArgumentList.Arguments.Count != 3) return null;
        if (fetch.ArgumentList.Arguments[2].Expression is not DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax rowDeclared }
            || !Same(model.GetDeclaredSymbol(rowDeclared), rowSymbol)) return null;
        found.Declared.Add(rowSymbol);
        string table = fetch.ArgumentList.Arguments[0].Expression.ToString(), id = fetch.ArgumentList.Arguments[1].Expression.ToString();
        found.Call = fallback => "GameTables." + kind + "(" + table + ", " + id + ", " + key + ", " + fallback + ")";
        return found;
    }

    /// "text != null", "!string.IsNullOrEmpty(text)", "!string.IsNullOrWhiteSpace(text)", "text.Length > 0": the reader says no to all of these by itself.
    static bool GuardOf(SemanticModel model, ExpressionSyntax term, ISymbol text)
    {
        while (term is ParenthesizedExpressionSyntax inner) term = inner.Expression;
        if (term is BinaryExpressionSyntax both)
        {
            if (both.IsKind(SyntaxKind.NotEqualsExpression) && both.Right.IsKind(SyntaxKind.NullLiteralExpression)) return Same(model.GetSymbolInfo(both.Left).Symbol, text);
            if (both.IsKind(SyntaxKind.GreaterThanExpression) && both.Right.ToString() == "0" && both.Left is MemberAccessExpressionSyntax { Name.Identifier.Text: "Length" } length)
                return Same(model.GetSymbolInfo(length.Expression).Symbol, text);
            return false;
        }
        if (term is PrefixUnaryExpressionSyntax not && not.IsKind(SyntaxKind.LogicalNotExpression) && not.Operand is InvocationExpressionSyntax test
            && test.Expression.ToString() is "string.IsNullOrEmpty" or "string.IsNullOrWhiteSpace" or "String.IsNullOrEmpty" or "String.IsNullOrWhiteSpace"
            && test.ArgumentList.Arguments.Count == 1)
            return Same(model.GetSymbolInfo(test.ArgumentList.Arguments[0].Expression).Symbol, text);
        return false;
    }

    /// The names the condition declared are used nowhere but inside it (and in what is replaced with it).
    static bool OnlyInside(SemanticModel model, SyntaxNode replaced, Found found)
    {
        SyntaxNode scope = replaced.FirstAncestorOrSelf<SyntaxNode>(n => n is MemberDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax) ?? replaced.SyntaxTree.GetRoot();
        foreach (var name in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
            if (!replaced.Span.Contains(name.Span) && found.Declared.Any(d => Same(model.GetSymbolInfo(name).Symbol, d))) return false;
        return true;
    }

    public static void Cells(bool dry)
    {
        var build = Project.Load(false);
        var byFile = new Dictionary<string, List<Cut>>();
        int picks = 0, sets = 0;
        foreach (var compilation in build.All)
        {
            var text = compilation.GetSpecialType(SpecialType.System_String);
            var rowType = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2").Construct(text, text);
            foreach (var tree in compilation.SyntaxTrees)
            {
                string rel = Project.Rel(tree.FilePath);
                if (!Project.IsProject(rel) || rel.Contains("/Core/Tables/")) continue;
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                // GameTables.TryGetRow(T, R, out var row) ? row.Number(K, F) : F
                foreach (var pick in root.DescendantNodes().OfType<ConditionalExpressionSyntax>())
                {
                    ExpressionSyntax condition = pick.Condition;
                    while (condition is ParenthesizedExpressionSyntax inner) condition = inner.Expression;
                    if (condition is not InvocationExpressionSyntax fetch || model.GetSymbolInfo(fetch).Symbol is not IMethodSymbol { Name: "TryGetRow", ContainingType.Name: "GameTables" }) continue;
                    if (fetch.ArgumentList.Arguments.Count != 3 || fetch.ArgumentList.Arguments[2].Expression is not DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax rowName }) continue;
                    var row = model.GetDeclaredSymbol(rowName);
                    if (pick.WhenTrue is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } reading } read) continue;
                    if (!Same(model.GetSymbolInfo(receiver).Symbol, row) || model.GetSymbolInfo(read).Symbol is not IMethodSymbol { ContainingType.Name: "TableCells" } reader) continue;
                    if (reader.Name is not ("Number" or "Whole" or "Flag" or "Text")) continue;
                    var given = read.ArgumentList.Arguments;
                    string fallback = pick.WhenFalse.ToString();
                    if (given.Count == 2 ? given[1].ToString() != fallback : !(given.Count == 1 && reader.Name == "Text" && fallback == "\"\"")) continue;
                    if (!Plain(pick.WhenFalse)) continue;
                    var found = new Found { Kind = reader.Name };
                    found.Declared.Add(row);
                    if (!OnlyInside(model, pick, found)) continue;
                    SyntaxNode whole = pick.Parent is ParenthesizedExpressionSyntax wrap ? wrap : pick;
                    Add(byFile, rel, whole.SpanStart, whole.Span.End, "GameTables." + reader.Name + "(" + fetch.ArgumentList.Arguments[0].Expression + ", " + fetch.ArgumentList.Arguments[1].Expression
                        + ", " + given[0] + (given.Count == 2 ? ", " + fallback : "") + ")");
                    picks++;
                }
                // cond ? value : fallback
                foreach (var pick in root.DescendantNodes().OfType<ConditionalExpressionSyntax>())
                {
                    var found = Match(model, compilation, rowType, pick.Condition);
                    if (found == null || pick.WhenTrue is not IdentifierNameSyntax taken || !Same(model.GetSymbolInfo(taken).Symbol, found.Value)) continue;
                    if (!Plain(pick.WhenFalse) || !OnlyInside(model, pick, found)) continue;
                    SyntaxNode whole = pick.Parent is ParenthesizedExpressionSyntax wrap ? wrap : pick;
                    Add(byFile, rel, whole.SpanStart, whole.Span.End, found.Call(pick.WhenFalse.ToString()));
                    picks++;
                }
                // if (cond) target = value;
                foreach (var test in root.DescendantNodes().OfType<IfStatementSyntax>())
                {
                    if (test.Else != null) continue;
                    var body = test.Statement is BlockSyntax block && block.Statements.Count == 1 ? block.Statements[0] : test.Statement;
                    if (body is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax set } || !set.IsKind(SyntaxKind.SimpleAssignmentExpression)) continue;
                    var found = Match(model, compilation, rowType, test.Condition);
                    if (found == null || set.Right is not IdentifierNameSyntax taken || !Same(model.GetSymbolInfo(taken).Symbol, found.Value)) continue;
                    if (model.GetSymbolInfo(set.Left).Symbol is not (ILocalSymbol or IFieldSymbol or IParameterSymbol) || !Plain(set.Left)) continue;
                    if (!OnlyInside(model, test, found)) continue;
                    Add(byFile, rel, test.SpanStart, test.Span.End, set.Left + " = " + found.Call(set.Left.ToString()) + ";");
                    sets++;
                }
            }
        }
        var (written, _) = Edits.Commit(byFile, dry);
        Console.WriteLine($"CELLS {(dry ? "(dry) " : "")}picks {picks}, sets {sets}, files {written}");
    }
}
