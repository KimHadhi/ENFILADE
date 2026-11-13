using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// A declared thing of the project that could be deleted: a type or one of its members.
sealed class Candidate
{
    public string Key, Kind, Name, Owner, File, OwnerKey;
    public int Line, Lines;
    public List<(string file, int start, int end)> Spans = new();   // declaration nodes (not yet widened to whole lines)
    public HashSet<string> Flags = new();
    public int Reads, Writes;
}

/// Mark and sweep over the whole project: what the game can reach from Unity's entry points, assets and other code stays;
/// the rest is dead. Both the editor and the player configuration are walked and a thing is dead only when dead in both.
static class Dead
{
    static readonly HashSet<string> UnityMessages = new(StringComparer.Ordinal)
    {
        "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy", "OnGUI", "OnValidate", "Reset",
        "OnApplicationQuit", "OnApplicationFocus", "OnApplicationPause", "OnBecameVisible", "OnBecameInvisible",
        "OnCollisionEnter", "OnCollisionExit", "OnCollisionStay", "OnCollisionEnter2D", "OnCollisionExit2D", "OnCollisionStay2D",
        "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay", "OnTriggerEnter2D", "OnTriggerExit2D", "OnTriggerStay2D",
        "OnControllerColliderHit", "OnJointBreak", "OnParticleCollision", "OnParticleTrigger", "OnParticleSystemStopped",
        "OnAnimatorIK", "OnAnimatorMove", "OnAudioFilterRead", "OnDrawGizmos", "OnDrawGizmosSelected",
        "OnMouseDown", "OnMouseDrag", "OnMouseEnter", "OnMouseExit", "OnMouseOver", "OnMouseUp", "OnMouseUpAsButton",
        "OnPreCull", "OnPreRender", "OnPostRender", "OnRenderImage", "OnRenderObject", "OnWillRenderObject",
        "OnTransformChildrenChanged", "OnTransformParentChanged", "OnRectTransformDimensionsChange", "OnCanvasGroupChanged",
        "OnCanvasHierarchyChanged", "OnDidApplyAnimationProperties", "OnBeforeTransformParentChanged",
        "OnInspectorGUI", "OnSceneGUI", "OnFocus", "OnLostFocus", "OnSelectionChange", "OnHierarchyChange", "OnProjectChange",
        "OnInspectorUpdate", "CreateGUI", "OnPreviewGUI", "ShowButton", "OnBecameVisible",
        "OnPreprocessAsset", "OnPreprocessTexture", "OnPostprocessTexture", "OnPreprocessModel", "OnPostprocessModel",
        "OnPreprocessAudio", "OnPostprocessAudio", "OnPostprocessAllAssets", "OnPreprocessAnimation", "OnPostprocessAnimation",
        "OnPostprocessGameObjectWithUserProperties", "OnAssignMaterialModel", "OnPostprocessMaterial", "OnPostprocessSprites",
        "OnPostprocessMeshHierarchy", "OnPostprocessPrefab", "OnPostprocessGameObjectWithAnimatedUserProperties",
        "OnPostprocessCubemap", "OnPreprocessMaterialDescription", "OnPreprocessCameraDescription", "OnPreprocessLightDescription",
        "OnWillSaveAssets", "OnWillCreateAsset", "OnWillDeleteAsset", "OnWillMoveAsset", "IsOpenForEdit",
        "OnServerInitialized", "OnConnectedToServer", "Main",
    };

    /// Methods the compiler calls by shape (foreach, await, using, deconstruction, collection initializers).
    static readonly HashSet<string> DuckNames = new(StringComparer.Ordinal)
    {
        "GetEnumerator", "MoveNext", "Dispose", "Add", "Deconstruct", "GetAwaiter", "GetResult", "OnCompleted", "GetPinnableReference",
    };

    /// Attributes that only describe; everything else on a member or type is taken as "something outside calls this".
    static readonly HashSet<string> PassiveAttributes = new(StringComparer.Ordinal)
    {
        "SerializeField", "Header", "Tooltip", "Range", "Min", "HideInInspector", "NonSerialized", "Space", "TextArea", "Multiline",
        "FormerlySerializedAs", "ColorUsage", "GradientUsage", "Delayed", "Obsolete", "MethodImpl", "Conditional", "ThreadStatic",
        "SerializeReference", "InspectorName", "NonReorderable", "Flags", "Serializable", "RequireComponent", "DisallowMultipleComponent",
        "DefaultExecutionOrder", "AddComponentMenu", "ExecuteAlways", "ExecuteInEditMode", "SelectionBase", "HelpURL", "StructLayout",
        "FieldOffset", "Rpc", "ServerRpc", "ClientRpc", "DebuggerDisplay", "DebuggerHidden", "DebuggerStepThrough", "CompilerGenerated",
        "IsReadOnly", "NativeDisableParallelForRestriction", "ReadOnly", "WriteOnly", "AttributeUsage", "GenerateSerializationForType",
        "GenerateSerializationForGenericParameter", "Icon", "CanEditMultipleObjects",
    };

    /// Calls that find a member by the name in a string.
    static readonly HashSet<string> ReflectiveCalls = new(StringComparer.Ordinal)
    {
        "Invoke", "InvokeRepeating", "CancelInvoke", "IsInvoking", "StartCoroutine", "StopCoroutine", "SendMessage", "SendMessageUpwards",
        "BroadcastMessage", "FindProperty", "FindPropertyRelative", "GetMethod", "GetField", "GetProperty", "GetMember", "GetEvent",
        "GetNestedType", "GetType", "AddComponent", "GetComponent", "CreateInstance", "GetMethods", "GetFields",
    };

    static readonly string[] DiscoveredBases =
    {
        "AssetPostprocessor", "AssetModificationProcessor", "ScriptedImporter", "VolumeComponent", "PropertyDrawer", "DecoratorDrawer",
        "IPreprocessBuildWithReport", "IPostprocessBuildWithReport", "IProcessSceneWithReport", "IPreprocessShaders",
        "IActiveBuildTargetChanged", "IPostBuildPlayerScriptDLLs", "IFilterBuildAssemblies", "IUnityLinkerProcessor",
        "ScriptableRendererFeature", "IPreprocessComputeShaders", "ScriptableWizard",
    };

    sealed class Graph
    {
        public Dictionary<string, Candidate> Candidates = new();
        public Dictionary<string, HashSet<string>> Edges = new();
        public HashSet<string> Roots = new();
        public HashSet<string> Literals = new(), ReflectNames = new();
        public HashSet<string> Alive = new();

        public void Edge(string from, string to)
        {
            if (to == null) return;
            if (from == null) { Roots.Add(to); return; }
            if (from == to) return;
            if (!Edges.TryGetValue(from, out var set)) Edges[from] = set = new HashSet<string>();
            set.Add(to);
        }
    }

    public static string Key(ISymbol symbol)
    {
        if (symbol == null) return null;
        symbol = symbol.OriginalDefinition;
        if (symbol is IMethodSymbol method)
        {
            if (method.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction) return null;
            if (method.ReducedFrom != null) symbol = method.ReducedFrom.OriginalDefinition;
            else if (method.AssociatedSymbol != null && method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
                         or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise)
                symbol = method.AssociatedSymbol.OriginalDefinition;
            if (symbol is IMethodSymbol partial && partial.PartialDefinitionPart != null) symbol = partial.PartialDefinitionPart;
        }
        if (symbol is IFieldSymbol field)
        {
            if (field.AssociatedSymbol != null) symbol = field.AssociatedSymbol.OriginalDefinition;
            else if (field.ContainingType?.TypeKind == TypeKind.Enum) symbol = field.ContainingType.OriginalDefinition;
        }
        if (symbol is not (INamedTypeSymbol or IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)) return null;
        if (symbol.ContainingType is { IsAnonymousType: true } || symbol is INamedTypeSymbol { IsAnonymousType: true }) return null;
        var declared = symbol.DeclaringSyntaxReferences;
        if (declared.IsDefaultOrEmpty) return null;
        SyntaxReference first = declared[0];
        for (int i = 1; i < declared.Length; i++)
        {
            int order = string.CompareOrdinal(declared[i].SyntaxTree.FilePath, first.SyntaxTree.FilePath);
            if (order < 0 || (order == 0 && declared[i].Span.Start < first.Span.Start)) first = declared[i];
        }
        string rel = Project.Rel(first.SyntaxTree.FilePath);
        return rel + "@" + first.Span.Start;
    }

    static bool IsProjectKey(string key) => Project.IsProject(key);

    static bool FromUnity(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b != null; b = b.BaseType)
        {
            if (b.DeclaringSyntaxReferences.Length > 0) continue;
            string space = b.ContainingNamespace?.ToDisplayString() ?? "";
            if (space.StartsWith("Unity") || space.StartsWith("TMPro")) return true;
        }
        return false;
    }

    static bool Discovered(INamedTypeSymbol type)
    {
        if (type.IsAbstract) return false;
        for (var b = type.BaseType; b != null; b = b.BaseType)
            if (Array.IndexOf(DiscoveredBases, b.Name) >= 0) return true;
        foreach (var i in type.AllInterfaces)
            if (Array.IndexOf(DiscoveredBases, i.Name) >= 0) return true;
        return false;
    }

    static bool HasActiveAttribute(ISymbol symbol, out string which)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            string name = attribute.AttributeClass?.Name ?? "";
            if (name.EndsWith("Attribute")) name = name.Substring(0, name.Length - 9);
            if (!PassiveAttributes.Contains(name)) { which = name; return true; }
        }
        which = null;
        return false;
    }

    static string Describe(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol t => t.TypeKind.ToString().ToLowerInvariant(),
        IMethodSymbol => "method",
        IPropertySymbol => "property",
        IFieldSymbol f => f.IsConst ? "const" : "field",
        IEventSymbol => "event",
        _ => "?"
    };

    static Candidate Declare(Graph graph, ISymbol symbol, string key, string ownerKey)
    {
        if (graph.Candidates.TryGetValue(key, out var existing)) return existing;
        var first = symbol.DeclaringSyntaxReferences[0];
        var candidate = new Candidate
        {
            Key = key, Kind = Describe(symbol), Name = symbol.Name, OwnerKey = ownerKey,
            Owner = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "",
            File = Project.Rel(first.SyntaxTree.FilePath),
            Line = first.SyntaxTree.GetLineSpan(first.Span).StartLinePosition.Line + 1,
        };
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            var span = node.Span;
            candidate.Spans.Add((Project.Rel(reference.SyntaxTree.FilePath), span.Start, span.End));
            var lines = reference.SyntaxTree.GetLineSpan(node is VariableDeclaratorSyntax ? node.Span : node.Span);
            candidate.Lines += lines.EndLinePosition.Line - lines.StartLinePosition.Line + 1;
        }
        graph.Candidates[key] = candidate;
        return candidate;
    }

    static void DeclareTypes(Graph graph, CSharpCompilation compilation, HashSet<string> attachedScripts)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(compilation.Assembly.GlobalNamespace);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is INamespaceSymbol space)
            {
                foreach (var member in space.GetMembers()) stack.Push(member);
                continue;
            }
            var type = (INamedTypeSymbol)current;
            foreach (var nested in type.GetTypeMembers()) stack.Push(nested);
            string typeKey = Key(type);
            if (typeKey == null) continue;
            bool project = IsProjectKey(typeKey);
            string ownerKey = type.ContainingType != null ? Key(type.ContainingType) : null;
            if (ownerKey != null) graph.Edge(typeKey, ownerKey);
            if (!project) { graph.Roots.Add(typeKey); continue; }

            var candidate = Declare(graph, type, typeKey, ownerKey);
            if (HasActiveAttribute(type, out string typeAttribute)) { graph.Roots.Add(typeKey); candidate.Flags.Add("attr:" + typeAttribute); }
            if (Discovered(type)) graph.Roots.Add(typeKey);
            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                string file = Project.Rel(reference.SyntaxTree.FilePath);
                if (attachedScripts.Contains(file) && Path.GetFileNameWithoutExtension(file) == type.Name && type.ContainingType == null)
                    graph.Roots.Add(typeKey);
            }
            if (type.TypeKind is TypeKind.Enum or TypeKind.Delegate) continue;

            bool unity = FromUnity(type);
            bool layout = type.GetAttributes().Any(a => a.AttributeClass?.Name == "StructLayoutAttribute");
            var implementing = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var contract in type.AllInterfaces)
                foreach (var member in contract.GetMembers())
                {
                    var found = type.FindImplementationForInterfaceMember(member);
                    if (found != null) implementing.Add(found.OriginalDefinition);
                }

            foreach (var member in type.GetMembers())
            {
                if (member.IsImplicitlyDeclared || member is INamedTypeSymbol) continue;
                if (member is IMethodSymbol { AssociatedSymbol: not null }) continue;
                string key = Key(member);
                if (key == null || key == typeKey) continue;
                var declared = Declare(graph, member, key, typeKey);
                graph.Edge(key, typeKey);
                bool keep = type.TypeKind == TypeKind.Interface || implementing.Contains(member.OriginalDefinition);
                switch (member)
                {
                    case IMethodSymbol method:
                        keep |= method.MethodKind != MethodKind.Ordinary || method.IsOverride || method.IsVirtual || method.IsAbstract || method.IsExtern
                                || method.IsPartialDefinition || method.PartialDefinitionPart != null
                                || (unity && UnityMessages.Contains(method.Name)) || DuckNames.Contains(method.Name);
                        break;
                    case IPropertySymbol property:
                        keep |= property.IsIndexer || property.IsOverride || property.IsVirtual || property.IsAbstract
                                || !property.ExplicitInterfaceImplementations.IsEmpty || property.Name is "Current" or "IsCompleted";
                        break;
                    case IEventSymbol happening:
                        keep |= happening.IsOverride || happening.IsVirtual || happening.IsAbstract || !happening.ExplicitInterfaceImplementations.IsEmpty;
                        break;
                    case IFieldSymbol field:
                        keep |= layout || field.IsFixedSizeBuffer;
                        if (type.TypeKind == TypeKind.Struct && !field.IsStatic && !field.IsConst) declared.Flags.Add("structField");
                        bool shown = field.DeclaredAccessibility == Accessibility.Public || field.GetAttributes().Any(a => a.AttributeClass?.Name == "SerializeField");
                        if (shown && !field.IsStatic && !field.IsConst && !field.IsReadOnly) declared.Flags.Add("serialized");
                        break;
                }
                if (keep) graph.Edge(typeKey, key);
                if (HasActiveAttribute(member, out string attribute)) { graph.Roots.Add(key); declared.Flags.Add("attr:" + attribute); }
            }
        }
    }

    sealed class TreeResult
    {
        public List<(string from, string to)> Edges = new();
        public List<(string key, bool write)> Uses = new();
        public HashSet<string> Literals = new(), Reflect = new();
    }

    static bool IsWrite(SyntaxNode name)
    {
        SyntaxNode e = name;
        while (true)
        {
            if (e.Parent is MemberAccessExpressionSyntax access && access.Name == e) { e = access; continue; }
            if (e.Parent is ParenthesizedExpressionSyntax) { e = e.Parent; continue; }
            if (e.Parent is MemberBindingExpressionSyntax) { e = e.Parent; continue; }
            break;
        }
        switch (e.Parent)
        {
            case AssignmentExpressionSyntax assignment when assignment.Left == e:
                return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || assignment.Parent is ExpressionStatementSyntax;
            case ArgumentSyntax argument when argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword):
                return true;
            case PostfixUnaryExpressionSyntax postfix when postfix.Parent is ExpressionStatementSyntax
                                                          && (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)):
                return true;
            case PrefixUnaryExpressionSyntax prefix when prefix.Parent is ExpressionStatementSyntax
                                                        && (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)):
                return true;
        }
        return false;
    }

    static TreeResult Walk(SemanticModel model, SyntaxTree tree)
    {
        var result = new TreeResult();
        bool project = Project.IsProject(Project.Rel(tree.FilePath));

        void Use(string[] from, ISymbol symbol, SyntaxNode node)
        {
            string to = Key(symbol);
            if (to == null) return;
            if (!project || from == null) result.Edges.Add((null, to));
            else foreach (string f in from) result.Edges.Add((f, to));
            if (symbol is IFieldSymbol or IPropertySymbol or IEventSymbol) result.Uses.Add((to, node is SimpleNameSyntax && IsWrite(node)));
        }

        void UseInfo(string[] from, SymbolInfo info, SyntaxNode node)
        {
            if (info.Symbol != null) Use(from, info.Symbol, node);
            else foreach (var candidate in info.CandidateSymbols) Use(from, candidate, node);
        }

        void Visit(SyntaxNode node, string[] from)
        {
            string[] here = from;
            switch (node)
            {
                case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax:
                {
                    string key = Key(model.GetDeclaredSymbol(node));
                    if (key != null) here = new[] { key };
                    break;
                }
                case BaseFieldDeclarationSyntax fields:
                {
                    var keys = fields.Declaration.Variables.Select(v => Key(model.GetDeclaredSymbol(v))).Where(k => k != null).ToArray();
                    if (keys.Length > 0) here = keys;
                    foreach (var child in fields.ChildNodes())
                    {
                        if (child is VariableDeclarationSyntax declaration)
                        {
                            Visit(declaration.Type, here);
                            foreach (var variable in declaration.Variables)
                            {
                                string key = Key(model.GetDeclaredSymbol(variable));
                                Visit(variable, key != null ? new[] { key } : here);
                            }
                        }
                        else Visit(child, here);
                    }
                    return;
                }
                case EnumMemberDeclarationSyntax:
                {
                    string key = Key(model.GetDeclaredSymbol(node));
                    if (key != null) here = new[] { key };
                    break;
                }
            }

            switch (node)
            {
                case SimpleNameSyntax:
                case BaseObjectCreationExpressionSyntax:
                case ConstructorInitializerSyntax:
                case ElementAccessExpressionSyntax:
                case AttributeSyntax:
                case BinaryExpressionSyntax:
                case PrefixUnaryExpressionSyntax:
                case PostfixUnaryExpressionSyntax:
                case CastExpressionSyntax:
                case AssignmentExpressionSyntax:
                    UseInfo(here, model.GetSymbolInfo(node), node);
                    break;
                case CommonForEachStatementSyntax each:
                {
                    var info = model.GetForEachStatementInfo(each);
                    Use(here, info.GetEnumeratorMethod, node); Use(here, info.MoveNextMethod, node); Use(here, info.CurrentProperty, node); Use(here, info.DisposeMethod, node);
                    break;
                }
                case AwaitExpressionSyntax awaited:
                {
                    var info = model.GetAwaitExpressionInfo(awaited);
                    Use(here, info.GetAwaiterMethod, node); Use(here, info.GetResultMethod, node); Use(here, info.IsCompletedProperty, node);
                    break;
                }
                case InitializerExpressionSyntax initializer when initializer.IsKind(SyntaxKind.CollectionInitializerExpression):
                    foreach (var element in initializer.Expressions) UseInfo(here, model.GetCollectionInitializerSymbolInfo(element), node);
                    break;
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                {
                    string value = literal.Token.ValueText;
                    if (value.Length is > 1 and < 80) result.Literals.Add(value);
                    break;
                }
                case InvocationExpressionSyntax invocation:
                {
                    string called = invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
                        SimpleNameSyntax simple => simple.Identifier.ValueText,
                        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
                        _ => null
                    };
                    if (called != null && ReflectiveCalls.Contains(called))
                        foreach (var argument in invocation.ArgumentList.Arguments)
                            if (argument.Expression is LiteralExpressionSyntax text && text.IsKind(SyntaxKind.StringLiteralExpression))
                                result.Reflect.Add(text.Token.ValueText);
                    break;
                }
            }

            // Implicit conversions written by nobody: a value handed over where another type is expected.
            if (node is ExpressionSyntax expression)
            {
                var conversion = model.GetConversion(expression);
                if (conversion.IsUserDefined && conversion.MethodSymbol != null) Use(here, conversion.MethodSymbol, node);
            }

            foreach (var child in node.ChildNodes()) Visit(child, here);
        }

        Visit(tree.GetRoot(), null);
        return result;
    }

    static Graph Analyse(Build build, HashSet<string> attachedScripts, HashSet<string> assetMethods)
    {
        var graph = new Graph();
        foreach (var compilation in build.All)
        {
            DeclareTypes(graph, compilation, attachedScripts);
            var results = new ConcurrentBag<TreeResult>();
            Parallel.ForEach(compilation.SyntaxTrees, tree => results.Add(Walk(compilation.GetSemanticModel(tree), tree)));
            foreach (var result in results)
            {
                foreach (var (from, to) in result.Edges) graph.Edge(from, to);
                foreach (var (key, write) in result.Uses)
                    if (graph.Candidates.TryGetValue(key, out var candidate)) { if (write) candidate.Writes++; else candidate.Reads++; }
                graph.Literals.UnionWith(result.Literals);
                graph.ReflectNames.UnionWith(result.Reflect);
            }
        }
        graph.ReflectNames.UnionWith(assetMethods);
        foreach (var candidate in graph.Candidates.Values)
        {
            if (candidate.Kind is "method" or "property" or "field" or "event" && graph.ReflectNames.Contains(candidate.Name))
            {
                graph.Roots.Add(candidate.Key);
                candidate.Flags.Add("reflect");
            }
            if (graph.Literals.Contains(candidate.Name)) candidate.Flags.Add("literal");
        }

        var stack = new Stack<string>(graph.Roots);
        foreach (string root in graph.Roots) graph.Alive.Add(root);
        while (stack.Count > 0)
        {
            string at = stack.Pop();
            if (!graph.Edges.TryGetValue(at, out var next)) continue;
            foreach (string to in next)
                if (graph.Alive.Add(to)) stack.Push(to);
        }
        return graph;
    }

    public static List<Candidate> Find(out Dictionary<string, Candidate> all)
    {
        var (attached, methods) = AssetScan.Run();

        var editor = Analyse(Project.Load(false), attached, methods);
        var player = Analyse(Project.Load(true), attached, methods);
        all = new Dictionary<string, Candidate>(editor.Candidates);
        foreach (var pair in player.Candidates)
        {
            if (all.TryGetValue(pair.Key, out var known)) { known.Reads += pair.Value.Reads; known.Writes += pair.Value.Writes; }
            else all[pair.Key] = pair.Value;
        }
        var alive = new HashSet<string>(editor.Alive);
        alive.UnionWith(player.Alive);
        var dead = all.Values.Where(c => !alive.Contains(c.Key)).ToList();
        // A member of a dead type goes with its type.
        var deadKeys = new HashSet<string>(dead.Select(d => d.Key));
        foreach (var candidate in dead)
            if (candidate.OwnerKey != null && deadKeys.Contains(candidate.OwnerKey)) candidate.Flags.Add("inDeadType");
        foreach (var candidate in all.Values)
            if (alive.Contains(candidate.Key) && candidate.Kind is "field" or "property" or "event" && candidate.Reads == 0 && candidate.Writes > 0) candidate.Flags.Add("writeOnly");
        return dead.OrderBy(d => d.File, StringComparer.Ordinal).ThenBy(d => d.Line).ToList();
    }

    public static void Report()
    {
        var dead = Find(out var all);
        var top = dead.Where(d => !d.Flags.Contains("inDeadType")).ToList();
        var text = new StringBuilder();
        text.AppendLine($"candidates {all.Count}, dead {dead.Count} (top level {top.Count}), lines {top.Sum(d => d.Lines)}");
        foreach (var group in top.GroupBy(d => d.Kind).OrderByDescending(g => g.Sum(d => d.Lines)))
            text.AppendLine($"  {group.Key,-10} {group.Count(),5}  lines {group.Sum(d => d.Lines)}");
        text.AppendLine();
        foreach (var file in top.GroupBy(d => d.File).OrderByDescending(g => g.Sum(d => d.Lines)))
        {
            text.AppendLine($"{file.Key}  ({file.Sum(d => d.Lines)} lines)");
            foreach (var d in file)
                text.AppendLine($"    {d.Line,5} {d.Kind,-9} {(d.Owner.Length > 0 ? d.Owner + "." : "")}{d.Name}  [{d.Lines}]  {string.Join(",", d.Flags.OrderBy(f => f))}");
        }
        var writeOnly = all.Values.Where(c => c.Flags.Contains("writeOnly")).OrderBy(c => c.File).ThenBy(c => c.Line).ToList();
        text.AppendLine();
        text.AppendLine($"WRITE-ONLY (assigned, never read): {writeOnly.Count}");
        foreach (var c in writeOnly)
            text.AppendLine($"    {c.File}:{c.Line} {c.Kind} {c.Owner}.{c.Name} writes={c.Writes} {string.Join(",", c.Flags.OrderBy(f => f))}");
        Directory.CreateDirectory(Project.Out);
        File.WriteAllText(Path.Combine(Project.Out, "dead.txt"), text.ToString());
        var json = dead.Select(d => new
        {
            key = d.Key, kind = d.Kind, name = d.Name, owner = d.Owner, file = d.File, line = d.Line, lines = d.Lines,
            flags = d.Flags.OrderBy(f => f).ToArray(), spans = d.Spans.Select(s => new { s.file, s.start, s.end }).ToArray()
        });
        File.WriteAllText(Path.Combine(Project.Out, "dead.json"), JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = false }));
        Console.WriteLine(text.ToString().Split('\n').Take(12).Aggregate((a, b) => a + "\n" + b));
    }
}
