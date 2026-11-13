using System.Collections.Concurrent;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

/// One assembly's compile inputs as Unity last wrote them (Library/Bee/.../*.rsp), with the source list brought up to date.
sealed class Unit
{
    public string Name;
    public List<string> Defines = new(), Refs = new(), Sources = new(), NoWarn = new();
    public LanguageVersion Lang = LanguageVersion.CSharp9;
    public bool Unsafe;
}

/// One configuration held in memory: the runtime assembly and, in the editor configuration, the editor assembly.
sealed class Build
{
    public string Label;
    public CSharpCompilation Runtime, Editor;
    public IEnumerable<CSharpCompilation> All => Editor != null ? new[] { Runtime, Editor } : new[] { Runtime };
}

static class Project
{
    /// The Unity project: the first folder above this tool that has Assets and ProjectSettings in it.
    public static readonly string Root = FindRoot();
    /// Where Unity last wrote what it hands the compiler (defines, references, sources): the newest *.dag folder.
    static readonly string Dag = FindDag();
    /// Reports go to Logs/CodeTools (git ignores Logs), or where ENF_OUT says.
    public static readonly string Out = Environment.GetEnvironmentVariable("ENF_OUT") ?? Path.Combine(Root, "Logs", "CodeTools");

    static string FindRoot()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent)
            if (Directory.Exists(Path.Combine(at.FullName, "Assets")) && Directory.Exists(Path.Combine(at.FullName, "ProjectSettings"))) return at.FullName;
        throw new Exception("No Unity project above " + AppContext.BaseDirectory);
    }

    static string FindDag()
    {
        string artifacts = Path.Combine(Root, "Library", "Bee", "artifacts");
        var found = Directory.Exists(artifacts)
            ? Directory.GetDirectories(artifacts, "*.dag").Where(d => File.Exists(Path.Combine(d, "Assembly-CSharp.rsp")))
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "Assembly-CSharp.rsp"))).FirstOrDefault()
            : null;
        return found ?? throw new Exception("Unity has not compiled this project yet (no Library/Bee/artifacts/*.dag/Assembly-CSharp.rsp): open it in Unity once.");
    }
    /// Comments are plain text to the tools; the unused-using check needs them read (the compiler only reports those then).
    public static DocumentationMode Documentation = DocumentationMode.None;
    static readonly ConcurrentDictionary<string, MetadataReference> References = new(StringComparer.OrdinalIgnoreCase);

    public static string Rel(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');
    public static string Full(string rel) => Path.Combine(Root, rel.Replace('/', '\\'));
    public static bool IsProject(string rel) => rel != null && rel.StartsWith("Assets/_Project/", StringComparison.OrdinalIgnoreCase);
    public static bool InEditorFolder(string rel) => ("/" + rel).Contains("/Editor/", StringComparison.OrdinalIgnoreCase);

    static Unit Read(string name)
    {
        var unit = new Unit { Name = name };
        foreach (string raw in File.ReadAllLines(Path.Combine(Dag, name + ".rsp")))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("-define:")) unit.Defines.AddRange(line.Substring(8).Split(';', StringSplitOptions.RemoveEmptyEntries));
            else if (line.StartsWith("-r:")) unit.Refs.Add(line.Substring(3).Trim('"'));
            else if (line.StartsWith("-langversion:"))
            {
                if (LanguageVersionFacts.TryParse(line.Substring(13), out var version)) unit.Lang = version;
            }
            else if (line is "-unsafe" or "/unsafe" or "-unsafe+" or "/unsafe+") unit.Unsafe = true;
            else if (line.StartsWith("/nowarn:") || line.StartsWith("-nowarn:"))
                foreach (string id in line.Substring(8).Split(',', ';')) unit.NoWarn.Add("CS" + id.Trim().PadLeft(4, '0'));
            else if (line.StartsWith("\"") && line.EndsWith(".cs\"")) unit.Sources.Add(line.Trim('"').Replace('\\', '/'));
        }
        return unit;
    }

    /// Files Unity has not listed yet are added, files it lists that are gone are dropped.
    static void Sync(Unit unit, bool editor)
    {
        unit.Sources = unit.Sources.Where(s => File.Exists(Full(s))).ToList();
        var have = new HashSet<string>(unit.Sources, StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Root, "Assets", "_Project"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Rel(file);
            if (InEditorFolder(rel) == editor && have.Add(rel)) unit.Sources.Add(rel);
        }
        unit.Sources.Sort(StringComparer.OrdinalIgnoreCase);
    }

    static CSharpCompilation Create(Unit unit, IEnumerable<string> defines, Func<string, bool> keepRef, IEnumerable<MetadataReference> extra)
    {
        var parse = new CSharpParseOptions(unit.Lang, Documentation, SourceCodeKind.Regular, defines);
        var trees = unit.Sources.AsParallel().AsOrdered().Select(rel =>
        {
            string path = Full(rel);
            using var stream = File.OpenRead(path);
            return CSharpSyntaxTree.ParseText(SourceText.From(stream, Encoding.UTF8), parse, path);
        }).ToList();
        var refs = unit.Refs.Where(keepRef).Select(r => Path.IsPathRooted(r) ? r : Path.Combine(Root, r)).Where(File.Exists)
            .Select(r => References.GetOrAdd(r, p => MetadataReference.CreateFromFile(p))).Concat(extra);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: unit.Unsafe, concurrentBuild: true,
                optimizationLevel: OptimizationLevel.Release)
            .WithSpecificDiagnosticOptions(unit.NoWarn.Distinct().ToDictionary(w => w, _ => ReportDiagnostic.Suppress));
        return CSharpCompilation.Create(unit.Name, trees, refs, options);
    }

    static List<string> PlayerDefines(Unit runtime) => runtime.Defines.Where(d => !d.StartsWith("UNITY_EDITOR") && d != "UNITY_INCLUDE_TESTS")
        .Append("UNITY_STANDALONE").Append("UNITY_STANDALONE_WIN").Distinct().ToList();

    static CSharpParseOptions editorParse, playerParse;

    /// The parse options of one configuration, for tools that parse a file on their own.
    public static CSharpParseOptions Parse(bool player)
    {
        if (editorParse == null)
        {
            var runtime = Read("Assembly-CSharp");
            editorParse = new CSharpParseOptions(runtime.Lang, DocumentationMode.None, SourceCodeKind.Regular, runtime.Defines);
            playerParse = new CSharpParseOptions(runtime.Lang, DocumentationMode.None, SourceCodeKind.Regular, PlayerDefines(runtime));
        }
        return player ? playerParse : editorParse;
    }

    public static Build Load(bool player)
    {
        var runtime = Read("Assembly-CSharp");
        Sync(runtime, false);
        if (player)
        {
            // A player build has no editor: UNITY_EDITOR is gone and so are the editor's assemblies.
            var defines = PlayerDefines(runtime);
            return new Build
            {
                Label = "player",
                Runtime = Create(runtime, defines, r => !Path.GetFileName(r).Contains("Editor", StringComparison.OrdinalIgnoreCase), Array.Empty<MetadataReference>())
            };
        }
        var editor = Read("Assembly-CSharp-Editor");
        Sync(editor, true);
        var first = Create(runtime, runtime.Defines, _ => true, Array.Empty<MetadataReference>());
        var second = Create(editor, editor.Defines, r => !r.Replace('\\', '/').EndsWith("/Assembly-CSharp.ref.dll", StringComparison.OrdinalIgnoreCase),
            new[] { first.ToMetadataReference() });
        return new Build { Label = "editor", Runtime = first, Editor = second };
    }

    /// Reads a source file the way it is on disk, remembering whether it starts with a byte order mark.
    public static string ReadText(string rel, out bool bom)
    {
        byte[] bytes = File.ReadAllBytes(Full(rel));
        bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
    }

    public static void WriteText(string rel, string text, bool bom)
    {
        byte[] body = new UTF8Encoding(false).GetBytes(text);
        byte[] bytes = bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
        for (int attempt = 0; ; attempt++)
        {
            try { File.WriteAllBytes(Full(rel), bytes); return; }
            catch (IOException) when (attempt < 20) { Thread.Sleep(150); }
            catch (UnauthorizedAccessException) when (attempt < 20) { Thread.Sleep(150); }
        }
    }

    public static void WriteNew(string rel, string text, bool bom)
    {
        byte[] body = new UTF8Encoding(false).GetBytes(text);
        File.WriteAllBytes(Full(rel), bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body);
    }

    public static string Where(Location location)
    {
        var span = location.GetLineSpan();
        return Rel(span.Path) + "(" + (span.StartLinePosition.Line + 1) + "," + (span.StartLinePosition.Character + 1) + ")";
    }
}
