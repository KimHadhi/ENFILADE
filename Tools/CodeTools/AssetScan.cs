using System.Collections.Concurrent;
using System.Text.RegularExpressions;

/// What the project's assets say about code, which the compiler cannot see: the scripts attached to prefabs, scenes
/// and assets (by GUID), and the methods they call by name (UnityEvent listeners, animation events).
static class AssetScan
{
    static readonly Regex Guid = new(@"guid:\s*([0-9a-f]{32})", RegexOptions.Compiled);
    static readonly Regex Method = new(@"(?:m_MethodName|functionName):\s*([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    static readonly HashSet<string> Yaml = new(StringComparer.OrdinalIgnoreCase)
    {
        ".unity", ".prefab", ".asset", ".controller", ".playable", ".overridecontroller", ".mat", ".anim", ".preset", ".spriteatlas",
        ".mixer", ".vfx", ".shadergraph", ".inputactions", ".signal",
    };

    public static (HashSet<string> scripts, HashSet<string> methods) Run()
    {
        var scriptOf = new Dictionary<string, string>();
        foreach (string meta in Directory.EnumerateFiles(Path.Combine(Project.Root, "Assets", "_Project"), "*.cs.meta", SearchOption.AllDirectories))
        {
            var match = Guid.Match(File.ReadAllText(meta));
            if (match.Success) scriptOf[match.Groups[1].Value] = Project.Rel(meta.Substring(0, meta.Length - 5));
        }
        var scripts = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var methods = new ConcurrentDictionary<string, byte>();
        var files = new[] { "Assets", "ProjectSettings", "Packages" }.Select(f => Path.Combine(Project.Root, f)).Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories))
            .Where(f => Yaml.Contains(Path.GetExtension(f)) || (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".cs.meta", StringComparison.OrdinalIgnoreCase)));
        Parallel.ForEach(files, file =>
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            foreach (Match match in Method.Matches(text)) methods[match.Groups[1].Value] = 0;
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return;
            foreach (Match match in Guid.Matches(text))
                if (scriptOf.TryGetValue(match.Groups[1].Value, out string script)) scripts[script] = 0;
        });
        return (new HashSet<string>(scripts.Keys, StringComparer.OrdinalIgnoreCase), new HashSet<string>(methods.Keys));
    }
}
