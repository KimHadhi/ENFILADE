using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// Looks for mistakes a compiler lets through: copy-and-paste slips, a loop variable caught by a callback, whole-number
/// division where a fraction was meant, events never let go of, null tests Unity's destroyed objects slip past, and names
/// written in code (tables, columns, texts, layers, tags, shaders, Resources paths) that the project does not have.
/// Every finding is a place to read, not a verdict.
static class Audit
{
    static readonly ConcurrentBag<(string check, string where, string text)> Found = new();

    static void Note(string check, SyntaxNode at, string text) => Found.Add((check, Project.Where(at.GetLocation()), text));

    // ---- What the project has ----

    sealed class Table
    {
        public bool KeyLayout;
        public HashSet<string> Columns = new(StringComparer.OrdinalIgnoreCase), Rows = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>> Cells = new(StringComparer.OrdinalIgnoreCase);   // row -> fields it fills
        public Dictionary<string, string> First = new(StringComparer.OrdinalIgnoreCase);              // row -> its first filled cell (a text's words)
    }

    static readonly Dictionary<string, Table> Tables = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> Layers = new(), Tags = new() { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };
    static readonly HashSet<string> Shaders = new(StringComparer.Ordinal), ResourceFiles = new(StringComparer.OrdinalIgnoreCase), ResourceFolders = new(StringComparer.OrdinalIgnoreCase);

    static List<List<string>> Records(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { record.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                record.Add(cell.ToString()); cell.Clear();
                if (record.Count > 1 || record[0].Length > 0) records.Add(record);
                record = new List<string>();
            }
            else cell.Append(c);
        }
        record.Add(cell.ToString());
        if (record.Count > 1 || record[0].Length > 0) records.Add(record);
        return records;
    }

    static void LoadProjectData()
    {
        string tables = Path.Combine(Project.Root, "Assets", "_Project", "Data", "Resources", "Tables");
        foreach (string file in Directory.EnumerateFiles(tables, "*.csv", SearchOption.AllDirectories))
        {
            var records = Records(File.ReadAllText(file).TrimStart('﻿'));
            var table = new Table();
            Tables[Path.GetFileNameWithoutExtension(file)] = table;
            if (records.Count == 0) continue;
            var header = records[0].Select(h => h.Trim().TrimStart('#').Trim()).ToList();
            table.KeyLayout = header.Count > 1 && header[0].Equals("key", StringComparison.OrdinalIgnoreCase) && header[1].Equals("value", StringComparison.OrdinalIgnoreCase);
            string where = Project.Rel(file);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!table.KeyLayout)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string column in records[0].Skip(1).Select(h => h.Trim()).Where(h => h.Length > 0 && h[0] != '#'))
                    if (!names.Add(column)) Found.Add(("a table column written twice", where, column));
            }
            for (int r = 1; r < records.Count; r++)
            {
                string first = records[r][0].Trim();
                if (first.Length == 0 || first[0] == '#') continue;
                if (records[r].Skip(records[0].Count).Any(cell => cell.Trim().Length > 0))
                    Found.Add(("a table row with more cells than columns (a comma without quotes cuts the cell)", where, first));
                if (!seen.Add(first)) Found.Add(("a table row written twice", where, first));
                if (table.KeyLayout)
                {
                    if (records[r].Count < 2) continue;
                    int dot = first.IndexOf('.');
                    string row = dot < 0 ? "" : first.Substring(0, dot), field = dot < 0 ? first : first.Substring(dot + 1);
                    table.Rows.Add(row); table.Columns.Add(field);
                    if (!table.Cells.TryGetValue(row, out var fields)) table.Cells[row] = fields = new(StringComparer.OrdinalIgnoreCase);
                    fields.Add(field);
                    continue;
                }
                table.Rows.Add(first);
                if (!table.Cells.TryGetValue(first, out var filled)) table.Cells[first] = filled = new(StringComparer.OrdinalIgnoreCase);
                for (int c = 1; c < records[r].Count && c < records[0].Count; c++)
                {
                    string field = records[0][c].Trim();
                    if (field.Length == 0 || field[0] == '#' || records[r][c].Trim().Length == 0) continue;
                    filled.Add(field);
                    if (!table.First.ContainsKey(first)) table.First[first] = records[r][c];
                }
            }
            if (!table.KeyLayout)
                foreach (string column in records[0].Skip(1).Select(h => h.Trim()).Where(h => h.Length > 0 && h[0] != '#')) table.Columns.Add(column);
        }

        string manager = File.ReadAllText(Path.Combine(Project.Root, "ProjectSettings", "TagManager.asset"));
        string section = "";
        foreach (string raw in manager.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("  ") && !line.StartsWith("  -") && line.TrimEnd().EndsWith(":")) section = line.Trim().TrimEnd(':');
            else if (line.StartsWith("  - ") && section == "tags") Tags.Add(line.Substring(4).Trim());
            else if (line.StartsWith("  - ") && section == "layers" && line.Trim().Length > 2) Layers.Add(line.Substring(4).Trim());
        }

        var shaderName = new Regex(@"^\s*Shader\s+""([^""]+)""", RegexOptions.Multiline);
        foreach (string root in new[] { "Assets", "Packages", Path.Combine("Library", "PackageCache") })
        {
            string folder = Path.Combine(Project.Root, root);
            if (!Directory.Exists(folder)) continue;
            foreach (string file in Directory.EnumerateFiles(folder, "*.shader", SearchOption.AllDirectories))
            {
                try { foreach (Match match in shaderName.Matches(File.ReadAllText(file))) Shaders.Add(match.Groups[1].Value); }
                catch (IOException) { }
            }
            foreach (string file in Directory.EnumerateFiles(folder, "*.shadergraph", SearchOption.AllDirectories))
                Shaders.Add("Shader Graphs/" + Path.GetFileNameWithoutExtension(file));
        }

        foreach (string root in new[] { "Assets", "Packages", Path.Combine("Library", "PackageCache") })
        {
            string folder = Path.Combine(Project.Root, root);
            if (!Directory.Exists(folder)) continue;
            foreach (string drawer in Directory.EnumerateDirectories(folder, "Resources", SearchOption.AllDirectories))
                foreach (string file in Directory.EnumerateFiles(drawer, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string rel = Path.GetRelativePath(drawer, file).Replace('\\', '/');
                    string noExtension = rel.Contains('.') ? rel.Substring(0, rel.LastIndexOf('.')) : rel;
                    ResourceFiles.Add(noExtension);
                    for (string up = Path.GetDirectoryName(rel)?.Replace('\\', '/'); !string.IsNullOrEmpty(up); up = Path.GetDirectoryName(up)?.Replace('\\', '/')) ResourceFolders.Add(up);
                }
        }
    }

    // ---- Small helpers ----

    static readonly HashSet<string> Immediate = new()
    {
        "Where", "Select", "SelectMany", "Any", "All", "Find", "FindIndex", "FindAll", "FindLast", "FindLastIndex", "Count", "First", "FirstOrDefault",
        "Last", "LastOrDefault", "Single", "SingleOrDefault", "Exists", "Sum", "Max", "Min", "Average", "OrderBy", "OrderByDescending", "ThenBy",
        "ThenByDescending", "RemoveAll", "Contains", "TrueForAll", "Sort", "ForEach", "Aggregate", "ToDictionary", "GroupBy", "TakeWhile", "SkipWhile",
        "IndexOf", "ConvertAll", "Array",
    };

    static bool DerivesFrom(ITypeSymbol type, string name)
    {
        for (var at = type; at != null; at = at.BaseType)
            if (at.Name == name) return true;
        return false;
    }

    static bool IsIntegral(ITypeSymbol type) => type != null && type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16
        or SpecialType.System_Byte or SpecialType.System_UInt32 or SpecialType.System_UInt16 or SpecialType.System_SByte or SpecialType.System_UInt64;

    static bool IsFraction(ITypeSymbol type) => type != null && type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;

    static bool Pure(SyntaxNode node) => !node.DescendantNodesAndSelf().Any(n => n is InvocationExpressionSyntax or ObjectCreationExpressionSyntax
        || n is PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax && (n.IsKind(SyntaxKind.PreIncrementExpression) || n.IsKind(SyntaxKind.PreDecrementExpression))
        || n is IdentifierNameSyntax { Identifier.ValueText: "Random" or "value" or "insideUnitSphere" or "insideUnitCircle" or "onUnitSphere" });

    static string Text(SyntaxNode node)
    {
        string text = Regex.Replace(node.ToString(), @"\s+", " ");
        return text.Length > 110 ? text.Substring(0, 110) + "…" : text;
    }

    static string Constant(SemanticModel model, ExpressionSyntax expression)
        => expression != null && model.GetConstantValue(expression) is { HasValue: true, Value: string value } ? value : null;

    static IEnumerable<ExpressionSyntax> Flatten(ExpressionSyntax expression, SyntaxKind kind)
    {
        while (expression is ParenthesizedExpressionSyntax wrapped) expression = wrapped.Expression;
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(kind))
        {
            foreach (var part in Flatten(binary.Left, kind)) yield return part;
            foreach (var part in Flatten(binary.Right, kind)) yield return part;
        }
        else yield return expression;
    }

    static ExpressionSyntax NullTested(ExpressionSyntax test, SyntaxKind kind)
    {
        while (test is ParenthesizedExpressionSyntax wrapped) test = wrapped.Expression;
        if (test is not BinaryExpressionSyntax binary || !binary.IsKind(kind)) return null;
        if (binary.Right.IsKind(SyntaxKind.NullLiteralExpression)) return binary.Left;
        return binary.Left.IsKind(SyntaxKind.NullLiteralExpression) ? binary.Right : null;
    }

    static bool Dereferences(SyntaxNode where, ExpressionSyntax what) => where.DescendantNodesAndSelf().Any(n =>
        n is MemberAccessExpressionSyntax access && SyntaxFactory.AreEquivalent(access.Expression, what)
        || n is ElementAccessExpressionSyntax element && SyntaxFactory.AreEquivalent(element.Expression, what));

    // ---- The walk ----

    sealed class PerType
    {
        public HashSet<string> Tables = new(StringComparer.OrdinalIgnoreCase);
        public bool AnyTableUnknown, ReadsAllCells;
        public List<(string key, SyntaxNode at)> Keys = new();
        public List<(IEventSymbol what, ISymbol handler, bool add, bool lambda, bool fromInstance, SyntaxNode at)> Events = new();
    }

    static readonly ConcurrentDictionary<INamedTypeSymbol, PerType> Types = new(SymbolEqualityComparer.Default);
    static readonly ConcurrentDictionary<ISymbol, byte> AsDelegates = new(SymbolEqualityComparer.Default);
    static readonly ConcurrentBag<(IMethodSymbol method, IParameterSymbol parameter, SyntaxNode at)> Unused = new();

    static INamedTypeSymbol Outer(ISymbol symbol)
    {
        var type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
        while (type?.ContainingType != null) type = type.ContainingType;
        return type;
    }

    static readonly string[] Messages =
    {
        "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy", "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay",
        "OnCollisionEnter", "OnCollisionExit", "OnCollisionStay", "OnApplicationQuit", "OnApplicationFocus", "OnApplicationPause", "OnValidate", "OnAnimatorIK",
        "OnAnimatorMove", "OnGUI", "OnDrawGizmos", "OnDrawGizmosSelected", "OnBecameVisible", "OnBecameInvisible", "OnControllerColliderHit", "OnNetworkSpawn",
        "OnNetworkDespawn", "OnRenderObject", "OnPreRender", "OnPostRender", "OnParticleCollision", "OnTransformChildrenChanged", "OnTransformParentChanged", "Reset",
    };

    static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    static void Walk(SemanticModel model, SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            switch (node)
            {
                case ForStatementSyntax loop when loop.Declaration != null:
                {
                    var counters = loop.Declaration.Variables.Select(v => model.GetDeclaredSymbol(v)).Where(s => s != null).ToList();
                    foreach (var callback in loop.Statement.DescendantNodes().Where(n => n is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                    {
                        var caught = callback.DescendantNodes().OfType<IdentifierNameSyntax>()
                            .FirstOrDefault(name => counters.Any(c => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, c)));
                        if (caught == null) continue;
                        var call = callback.Ancestors().TakeWhile(a => a != loop).OfType<InvocationExpressionSyntax>().FirstOrDefault();
                        string taker = call?.Expression switch { MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText, IdentifierNameSyntax name => name.Identifier.ValueText, _ => "" };
                        if (Immediate.Contains(taker)) continue;
                        Note("loop variable caught by a callback", callback, $"'{caught.Identifier.ValueText}' in {(taker.Length > 0 ? taker + "(…)" : "a stored callback")}: {Text(callback)}");
                    }
                    if (loop.Condition is BinaryExpressionSyntax bound && bound.IsKind(SyntaxKind.LessThanOrEqualExpression)
                        && bound.Right is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" or "Count" or "childCount" })
                        Note("loop runs one past the end", bound, Text(bound));
                    break;
                }
                case BinaryExpressionSyntax binary:
                {
                    var kind = binary.Kind();
                    if (kind is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression or SyntaxKind.SubtractExpression or SyntaxKind.DivideExpression
                            or SyntaxKind.LessThanExpression or SyntaxKind.GreaterThanExpression or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanOrEqualExpression
                            or SyntaxKind.BitwiseAndExpression or SyntaxKind.BitwiseOrExpression or SyntaxKind.ExclusiveOrExpression or SyntaxKind.ModuloExpression
                        && SyntaxFactory.AreEquivalent(binary.Left, binary.Right) && Pure(binary))
                        Note("both sides the same", binary, Text(binary));
                    if (kind is SyntaxKind.LogicalOrExpression or SyntaxKind.LogicalAndExpression && !(binary.Parent is BinaryExpressionSyntax up && up.IsKind(kind)))
                    {
                        var parts = Flatten(binary, kind).ToList();
                        for (int i = 0; i < parts.Count; i++)
                            for (int j = i + 1; j < parts.Count; j++)
                                if (SyntaxFactory.AreEquivalent(parts[i], parts[j]) && Pure(parts[i])) Note("the same test twice in one condition", parts[j], Text(parts[j]) + "   in   " + Text(binary));
                        // "x == null && x.y" and "x != null || x.y": the second half runs exactly when x is null
                        for (int i = 0; i < parts.Count; i++)
                        {
                            var tested = NullTested(parts[i], kind == SyntaxKind.LogicalAndExpression ? SyntaxKind.EqualsExpression : SyntaxKind.NotEqualsExpression);
                            if (tested == null) continue;
                            for (int j = i + 1; j < parts.Count; j++)
                                if (Dereferences(parts[j], tested)) Note("used exactly when it is null", parts[j], Text(binary));
                        }
                    }
                    if (kind == SyntaxKind.DivideExpression)
                    {
                        var left = model.GetTypeInfo(binary.Left).Type;
                        var right = model.GetTypeInfo(binary.Right).Type;
                        if (IsIntegral(left) && IsIntegral(right))
                        {
                            SyntaxNode outer = binary;
                            while (outer.Parent is ParenthesizedExpressionSyntax) outer = outer.Parent;
                            bool toFraction = IsFraction(model.GetTypeInfo((ExpressionSyntax)outer).ConvertedType)
                                              || outer.Parent is CastExpressionSyntax cast && IsFraction(model.GetTypeInfo(cast.Type).Type);
                            var a = model.GetConstantValue(binary.Left);
                            var b = model.GetConstantValue(binary.Right);
                            bool exact = a.HasValue && b.HasValue && Convert.ToInt64(b.Value) != 0 && Convert.ToInt64(a.Value) % Convert.ToInt64(b.Value) == 0;
                            if (toFraction && !exact) Note("whole-number division where a fraction is wanted", binary, Text(outer.Parent ?? binary));
                        }
                    }
                    if (kind == SyntaxKind.CoalesceExpression) UnityNull(model, binary.Left, binary, "??");
                    break;
                }
                case IfStatementSyntax test:
                {
                    if (test.Else != null && SyntaxFactory.AreEquivalent(test.Statement, test.Else.Statement)) Note("both branches the same", test, Text(test.Condition));
                    if (test.Parent is not ElseClauseSyntax)
                    {
                        var conditions = new List<ExpressionSyntax>();
                        for (var at = test; at != null; at = at.Else?.Statement as IfStatementSyntax) conditions.Add(at.Condition);
                        for (int i = 0; i < conditions.Count; i++)
                            for (int j = i + 1; j < conditions.Count; j++)
                                if (SyntaxFactory.AreEquivalent(conditions[i], conditions[j]) && Pure(conditions[i])) Note("the same test again further down the else-if chain", conditions[j], Text(conditions[j]));
                    }
                    break;
                }
                case ConditionalExpressionSyntax choice:
                    if (SyntaxFactory.AreEquivalent(choice.WhenTrue, choice.WhenFalse)) Note("both branches the same", choice, Text(choice));
                    break;
                case ConditionalAccessExpressionSyntax access:
                    UnityNull(model, access.Expression, access, "?.");
                    break;
                case IsPatternExpressionSyntax pattern when pattern.Pattern is ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression }
                                                            || pattern.Pattern is UnaryPatternSyntax { Pattern: ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression } }:
                    UnityNull(model, pattern.Expression, pattern, "is null");
                    break;
                case AssignmentExpressionSyntax assignment:
                {
                    if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression)) UnityNull(model, assignment.Left, assignment, "??=");
                    if (assignment.Left is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "Time" }, Name.Identifier.ValueText: "timeScale" or "fixedDeltaTime" }
                        && !Project.Rel(tree.FilePath).EndsWith("/Core/Time/WorldTime.cs"))
                        Note("the world's speed written outside WorldTime", assignment, Text(assignment));
                    if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && SyntaxFactory.AreEquivalent(assignment.Left, assignment.Right)
                        && assignment.Parent is not InitializerExpressionSyntax)
                        Note("assigned to itself", assignment, Text(assignment));
                    if ((assignment.IsKind(SyntaxKind.AddAssignmentExpression) || assignment.IsKind(SyntaxKind.SubtractAssignmentExpression))
                        && model.GetSymbolInfo(assignment.Left).Symbol is IEventSymbol happening)
                    {
                        var holder = model.GetEnclosingSymbol(assignment.SpanStart);
                        var type = Outer(holder);
                        if (type != null)
                        {
                            bool lambda = assignment.Right is AnonymousFunctionExpressionSyntax;
                            var handler = lambda ? null : model.GetSymbolInfo(assignment.Right).Symbol ?? model.GetSymbolInfo(assignment.Right).CandidateSymbols.FirstOrDefault();
                            Types.GetOrAdd(type, _ => new PerType()).Events.Add((happening, handler, assignment.IsKind(SyntaxKind.AddAssignmentExpression), lambda,
                                holder is IMethodSymbol { IsStatic: false }, assignment));
                        }
                    }
                    // the x / y / z slip: two lines that differ on the left only by the letter, with the same right side
                    if (assignment.Parent is ExpressionStatementSyntax line && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                        && assignment.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "x" or "y" or "z" or "w" } leftNow
                        && line.Parent is BlockSyntax block)
                    {
                        int index = block.Statements.IndexOf(line);
                        if (index > 0 && block.Statements[index - 1] is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax before }
                            && before.IsKind(SyntaxKind.SimpleAssignmentExpression)
                            && before.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "x" or "y" or "z" or "w" } leftBefore
                            && SyntaxFactory.AreEquivalent(leftBefore.Expression, leftNow.Expression)
                            && leftBefore.Name.Identifier.ValueText != leftNow.Name.Identifier.ValueText
                            && SyntaxFactory.AreEquivalent(before.Right, assignment.Right)
                            && before.Right.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Any(m => m.Name.Identifier.ValueText is "x" or "y" or "z" or "w"))
                            Note("two components given the same value", assignment, Text(before) + "   then   " + Text(assignment));
                    }
                    break;
                }
                case ObjectCreationExpressionSyntax made when made.Type.ToString() is "Vector3" or "Vector2" or "Vector4" && made.ArgumentList is { Arguments.Count: >= 2 }:
                {
                    var given = made.ArgumentList.Arguments.Select(a => a.Expression).ToList();
                    for (int i = 0; i < given.Count; i++)
                        for (int j = i + 1; j < given.Count; j++)
                            if (given[i] is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "x" or "y" or "z" } one && SyntaxFactory.AreEquivalent(given[i], given[j])
                                && given.Any(g => g is MemberAccessExpressionSyntax other && other != one && SyntaxFactory.AreEquivalent(other.Expression, one.Expression)
                                                  && other.Name.Identifier.ValueText != one.Name.Identifier.ValueText))
                                Note("one component twice in a vector", made, Text(made));
                    break;
                }
                case InvocationExpressionSyntax call:
                    Call(model, call);
                    break;
                case MethodDeclarationSyntax method:
                    Method(model, method);
                    break;
                case IdentifierNameSyntax name when name.Parent is not InvocationExpressionSyntax
                                                    && !(name.Parent is MemberAccessExpressionSyntax owner && owner.Name == name && owner.Parent is InvocationExpressionSyntax):
                    if (model.GetSymbolInfo(name).Symbol is IMethodSymbol group) AsDelegates[group.OriginalDefinition] = 0;
                    break;
            }
        }
    }

    static void UnityNull(SemanticModel model, ExpressionSyntax tested, SyntaxNode at, string how)
    {
        if (Fixes.SaysOrNull(tested)) return;
        var type = model.GetTypeInfo(tested).Type;
        if (type == null || !Fixes.UnityObject(type)) return;
        bool inScene = DerivesFrom(type, "Component") || type.Name == "GameObject";
        Note(inScene ? "null test a destroyed object slips past (scene object)" : "null test a destroyed object slips past (asset)", at, how + "  " + type.Name + ":  " + Text(at));
    }

    static void Call(SemanticModel model, InvocationExpressionSyntax call)
    {
        if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol called) return;
        string owner = called.ContainingType?.Name ?? "", name = called.Name;
        var arguments = call.ArgumentList.Arguments;
        var original = (called.ReducedFrom ?? called).OriginalDefinition;

        if (owner == "Random" && name == "Range" && called.Parameters.Length == 2 && called.Parameters[0].Type.SpecialType == SpecialType.System_Int32
            && arguments[1].Expression is BinaryExpressionSyntax upper && upper.IsKind(SyntaxKind.SubtractExpression)
            && upper.Right is LiteralExpressionSyntax { Token.ValueText: "1" }
            && upper.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" or "Count" or "childCount" })
            Note("Random.Range never picks the last one", call, Text(call));

        if (owner is "Single" or "Double" or "Decimal" && name is "Parse" or "TryParse" && !called.Parameters.Any(p => p.Type.Name == "IFormatProvider"))
            Note("number read in the PC's own language", call, Text(call));
        if (owner == "Convert" && name is "ToSingle" or "ToDouble" && called.Parameters.Length == 1 && called.Parameters[0].Type.SpecialType == SpecialType.System_String)
            Note("number read in the PC's own language", call, Text(call));
        if (name == "ToString" && called.ReceiverType != null && IsFraction(called.ReceiverType) && !called.Parameters.Any(p => p.Type.Name == "IFormatProvider"))
        {
            var holder = call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            string around = (holder?.Identifier.ValueText ?? "") + " " + (Outer(model.GetEnclosingSymbol(call.SpanStart))?.Name ?? "");
            if (Regex.IsMatch(around, "Write|Save|Export|Csv|Json|Serial|Table|Sheet|Dump|Store|Persist|Encode", RegexOptions.IgnoreCase))
                Note("number written in the PC's own language", call, Text(call) + "   in " + around.Trim());
        }

        if (owner == "Mathf" && name == "Clamp" && arguments.Count == 3 && model.GetConstantValue(arguments[1].Expression) is { HasValue: true } low
            && model.GetConstantValue(arguments[2].Expression) is { HasValue: true } high && Convert.ToDouble(low.Value) > Convert.ToDouble(high.Value))
            Note("Clamp with its bounds the wrong way round", call, Text(call));

        if (owner == "LayerMask" && name is "NameToLayer" or "GetMask")
            foreach (var argument in arguments)
                if (Constant(model, argument.Expression) is string layer && !Layers.Contains(layer)) Note("no such layer", argument, layer);
        if (name is "CompareTag" or "FindWithTag" or "FindGameObjectsWithTag" or "FindGameObjectWithTag" && arguments.Count == 1
            && Constant(model, arguments[0].Expression) is string tag && !Tags.Contains(tag))
            Note("no such tag", call, tag);

        if (owner == "Resources" && name is "Load" or "LoadAsync" or "LoadAll" && arguments.Count >= 1 && Constant(model, arguments[0].Expression) is string path)
        {
            string clean = path.Trim('/');
            bool there = name == "LoadAll" ? clean.Length == 0 || ResourceFolders.Contains(clean) || ResourceFiles.Contains(clean) : ResourceFiles.Contains(clean);
            if (!there) Note("nothing in a Resources drawer by this name", call, path);
        }
        if (owner == "Shader" && name == "Find" && arguments.Count == 1 && Constant(model, arguments[0].Expression) is string shader && !Shaders.Contains(shader)
            && !Regex.IsMatch(shader, "^(Hidden/|Sprites/|UI/|Standard|Unlit/|Legacy Shaders/|Skybox/|Particles/|Mobile/|GUI/|TextMeshPro/)"))
            Note("no such shader", call, shader);

        var type = Outer(model.GetEnclosingSymbol(call.SpanStart));
        if (type == null) return;
        if (owner == "GameTables" && name is "TryGetRow" or "RowIds" or "Bind" or "BindNested" && arguments.Count >= 1)
        {
            var per = Types.GetOrAdd(type, _ => new PerType());
            string table = Constant(model, arguments[0].Expression);
            if (table == null) { per.AnyTableUnknown = true; return; }
            per.Tables.Add(table);
            if (!Tables.TryGetValue(table, out var known)) { Note("no such table", arguments[0], table); return; }
            if (name is "TryGetRow" or "Bind" or "BindNested" && arguments.Count >= 2 && Constant(model, arguments[1].Expression) is string row
                && row.Length > 0 && !known.Rows.Contains(row))
                Note("no such row in the table", arguments[1], table + " [" + row + "]");
            if (name is "Bind" or "BindNested" && arguments.Count >= 3 && Constant(model, arguments[1].Expression) is string bound && known.Cells.TryGetValue(bound, out var cells))
            {
                var targets = new List<ITypeSymbol>();
                foreach (var argument in arguments.Skip(name == "BindNested" ? 3 : 2))
                    if (model.GetTypeInfo(argument.Expression).Type is { } target) targets.Add(target);
                if (targets.Count > 0 && targets.All(t => t.TypeKind != TypeKind.Array && t.SpecialType != SpecialType.System_Object && t.Name != "Object"))
                    foreach (string cell in cells)
                        if (!targets.Any(t => Takes(t, cell))) Note("a table cell no field takes", call, $"{table} [{bound}] {cell}  (targets: {string.Join(", ", targets.Select(t => t.Name))})");
            }
            return;
        }
        if (original.ContainingType?.Name == "TableCells" && name is "Number" or "Text" or "Vector")
        {
            var keyArgument = called.ReducedFrom != null ? arguments.ElementAtOrDefault(0) : arguments.ElementAtOrDefault(1);
            var per = Types.GetOrAdd(type, _ => new PerType());
            if (keyArgument != null && Constant(model, keyArgument.Expression) is string key)
            {
                if (name == "Vector") { per.Keys.Add((key + "X", keyArgument)); per.Keys.Add((key + "Y", keyArgument)); per.Keys.Add((key + "Z", keyArgument)); }
                else per.Keys.Add((key, keyArgument));
            }
            return;
        }
        if (name is "TryGetValue" or "ContainsKey" && called.ContainingType is { } dictionary && dictionary.TypeArguments.Length == 2
            && dictionary.TypeArguments[0].SpecialType == SpecialType.System_String && dictionary.TypeArguments[1].SpecialType == SpecialType.System_String
            && arguments.Count >= 1 && Constant(model, arguments[0].Expression) is string cellKey
            && call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "row" or "cells" or "cell" or "line" or "data" } })
            Types.GetOrAdd(type, _ => new PerType()).Keys.Add((cellKey, arguments[0]));

        if (owner == "Texts" && name is "Get" or "Format" && arguments.Count >= 1 && Constant(model, arguments[0].Expression) is string id && Tables.TryGetValue("Texts", out var texts))
        {
            if (!texts.Rows.Contains(id)) { Note("no such text id", arguments[0], id); return; }
            if (name == "Format" && texts.First.TryGetValue(id, out string words) && !arguments.Skip(1).Any(a => model.GetTypeInfo(a.Expression).Type is IArrayTypeSymbol))
            {
                int needed = Regex.Matches(words, @"\{(\d+)[^}]*\}").Select(m => int.Parse(m.Groups[1].Value) + 1).DefaultIfEmpty(0).Max();
                int given = arguments.Count - 1;
                if (given < needed) Note("a text asks for more values than it is given", call, $"{id}: \"{words}\" wants {needed}, gets {given}");
                else if (given > needed) Note("a text is given values it never shows", call, $"{id}: \"{words}\" shows {needed}, gets {given}");
            }
        }
        if (owner == "String" && name == "Format" && arguments.Count >= 1 && Constant(model, arguments[0].Expression) is string format
            && !arguments.Skip(1).Any(a => model.GetTypeInfo(a.Expression).Type is IArrayTypeSymbol))
        {
            int needed = Regex.Matches(format, @"\{(\d+)[^}]*\}").Select(m => int.Parse(m.Groups[1].Value) + 1).DefaultIfEmpty(0).Max();
            if (arguments.Count - 1 < needed) Note("a text asks for more values than it is given", call, Text(call));
        }
    }

    /// Whether a table cell of this name would land on the type: a serialized field of it or its bases, else a public settable property (TableFieldWriter).
    static bool Takes(ITypeSymbol type, string cell)
    {
        for (var at = type; at != null; at = at.BaseType)
            foreach (var member in at.GetMembers())
            {
                if (!string.Equals(member.Name, cell, StringComparison.OrdinalIgnoreCase)) continue;
                if (member is IFieldSymbol field && !field.IsStatic && !field.IsConst
                    && (field.DeclaredAccessibility == Accessibility.Public || field.GetAttributes().Any(a => a.AttributeClass?.Name == "SerializeField"))) return true;
                if (member is IPropertySymbol property && property.DeclaredAccessibility == Accessibility.Public && property.SetMethod is { DeclaredAccessibility: Accessibility.Public }) return true;
            }
        return false;
    }

    static void Method(SemanticModel model, MethodDeclarationSyntax method)
    {
        if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol) return;
        var type = symbol.ContainingType;
        bool behaviour = DerivesFrom(type, "MonoBehaviour");
        string name = symbol.Name;
        if (behaviour && Array.IndexOf(Messages, name) < 0 && !symbol.IsOverride)
        {
            string near = Messages.FirstOrDefault(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase) || (name.Length > 5 && Distance(m, name) == 1 && name.StartsWith("On") == m.StartsWith("On")));
            if (near != null && symbol.Parameters.Length == 0 && symbol.DeclaredAccessibility == Accessibility.Private && !AsDelegates.ContainsKey(symbol.OriginalDefinition))
                Note("looks like a Unity message but is not spelt like one", method, $"{type.Name}.{name}  (meant {near}?)");
        }
        if (behaviour && Array.IndexOf(Messages, name) >= 0 && symbol.IsStatic) Note("a Unity message cannot be static", method, type.Name + "." + name);
        if (behaviour && name.StartsWith("OnTrigger") && symbol.Parameters.Length == 1 && symbol.Parameters[0].Type.Name is not ("Collider" or "Collider2D"))
            Note("a Unity message with the wrong parameter", method, type.Name + "." + name + "(" + symbol.Parameters[0].Type.Name + ")");
        if (behaviour && name.StartsWith("OnCollision") && symbol.Parameters.Length == 1 && symbol.Parameters[0].Type.Name is not ("Collision" or "Collision2D"))
            Note("a Unity message with the wrong parameter", method, type.Name + "." + name + "(" + symbol.Parameters[0].Type.Name + ")");
        if (DerivesFrom(type, "NetworkBehaviour") && name == "OnDestroy" && symbol.IsOverride
            && !method.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => i.Expression is MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax, Name.Identifier.ValueText: "OnDestroy" }))
            Note("NetworkBehaviour.OnDestroy overridden without calling the base", method, type.Name);

        // parameters nothing reads
        SyntaxNode body = (SyntaxNode)method.Body ?? method.ExpressionBody;
        if (body == null || symbol.MethodKind != MethodKind.Ordinary || symbol.IsOverride || symbol.IsVirtual || symbol.IsAbstract || symbol.IsExtern
            || symbol.GetAttributes().Length > 0 || !symbol.ExplicitInterfaceImplementations.IsEmpty || Array.IndexOf(Messages, name) >= 0
            || symbol.PartialDefinitionPart != null || symbol.IsPartialDefinition) return;
        if (type.AllInterfaces.SelectMany(i => i.GetMembers(name)).Any(m => SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(m), symbol))) return;
        var read = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
            if (model.GetSymbolInfo(identifier).Symbol is IParameterSymbol parameter) read.Add(parameter);
        foreach (var parameter in symbol.Parameters)
            if (!read.Contains(parameter) && parameter.RefKind != RefKind.Out && !parameter.Name.StartsWith("_") && !(symbol.IsExtensionMethod && parameter.Ordinal == 0 && false))
                Unused.Add((symbol, parameter, method));
    }

    public static void Run()
    {
        LoadProjectData();
        var build = Project.Load(false);
        foreach (var compilation in build.All)
            Parallel.ForEach(compilation.SyntaxTrees.Where(t => Project.IsProject(Project.Rel(t.FilePath))), tree => Walk(compilation.GetSemanticModel(tree), tree));

        foreach (var (method, parameter, at) in Unused)
            if (!AsDelegates.ContainsKey(method.OriginalDefinition))
                Note(method.DeclaredAccessibility == Accessibility.Private ? "a parameter nothing reads (private method)" : "a parameter nothing reads", at,
                    $"{method.ContainingType.Name}.{method.Name}({parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {parameter.Name})");

        foreach (var pair in Types)
        {
            var per = pair.Value;
            // column names read from a row that no table of this class has
            if (per.Tables.Count > 0 && !per.AnyTableUnknown && per.Tables.All(Tables.ContainsKey))
            {
                var columns = new HashSet<string>(per.Tables.SelectMany(t => Tables[t].Columns), StringComparer.OrdinalIgnoreCase);
                foreach (var (key, at) in per.Keys)
                    if (!columns.Contains(key)) Note("a column no table of this class has", at, $"\"{key}\"  ({string.Join(", ", per.Tables)})");
            }
            // events taken and never let go
            foreach (var taken in per.Events.Where(e => e.add))
            {
                bool own = SymbolEqualityComparer.Default.Equals(Outer(taken.what), pair.Key);
                if (own) continue;
                bool released = !taken.lambda && per.Events.Any(e => !e.add && SymbolEqualityComparer.Default.Equals(e.what, taken.what) && SymbolEqualityComparer.Default.Equals(e.handler, taken.handler));
                if (released) continue;
                string kind = taken.what.IsStatic ? (taken.fromInstance ? "a static event taken by an object and never let go" : "a static event taken and never let go (static code)")
                    : "another object's event taken and never let go";
                Note(kind, taken.at, Text(taken.at));
            }
        }

        // the same words under two names
        var groups = Rewrite.SameBodies(build);
        foreach (var group in groups.Where(g => g.Select(m => m.Name).Distinct().Count() > 1))
            Found.Add(("the same body under different names", Project.Where(group[0].Locations[0]), string.Join("  =  ", group.Select(m => m.ContainingType.Name + "." + m.Name))));

        var text = new StringBuilder();
        var summary = new StringBuilder();
        foreach (var check in Found.GroupBy(f => f.check).OrderBy(g => g.Key))
        {
            summary.AppendLine($"{check.Count(),5}  {check.Key}");
            text.AppendLine($"== {check.Key} ({check.Count()})");
            foreach (var finding in check.OrderBy(f => f.where, StringComparer.Ordinal)) text.AppendLine($"  {finding.where.Replace("Assets/_Project/", "")}: {finding.text}");
            text.AppendLine();
        }
        Directory.CreateDirectory(Project.Out);
        File.WriteAllText(Path.Combine(Project.Out, "audit.txt"), summary + "\n" + text);
        Console.Write(summary.ToString());
    }
}
