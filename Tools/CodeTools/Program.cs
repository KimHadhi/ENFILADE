using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

static class Program
{
    static int Errors(CSharpCompilation compilation, string label, int show)
    {
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Console.WriteLine($"[{label}] {compilation.AssemblyName}: {compilation.SyntaxTrees.Count()} files, errors {errors.Count}");
        foreach (var d in errors.Take(show))
            Console.WriteLine("  " + (d.Location.IsInSource ? Project.Where(d.Location) : "") + ": " + d.Id + ": " + d.GetMessage());
        return errors.Count;
    }

    static void ErrorSummary()
    {
        var build = Project.Load(false);
        var all = build.All.SelectMany(c => c.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (var group in all.GroupBy(d => d.Id + " " + (d.Location.IsInSource ? Project.Rel(d.Location.SourceTree.FilePath) : "")).OrderByDescending(g => g.Count()))
            Console.WriteLine($"{group.Count(),4}  {group.Key}  {group.First().GetMessage()}");
    }

    static int Check(bool player, int show)
    {
        var build = Project.Load(false);
        int errors = Errors(build.Runtime, "editor", show) + Errors(build.Editor, "editor", show);
        if (player) errors += Errors(Project.Load(true).Runtime, "player", show);
        Console.WriteLine("CHECK " + (errors == 0 ? "OK" : "FAILED: " + errors));
        return errors == 0 ? 0 : 1;
    }

    static void Warnings()
    {
        var build = Project.Load(false);
        var all = build.All.SelectMany(c => c.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Warning && d.Location.IsInSource
                                                                         && Project.IsProject(Project.Rel(d.Location.SourceTree.FilePath))).ToList();
        var text = new StringBuilder();
        foreach (var group in all.GroupBy(d => d.Id).OrderByDescending(g => g.Count()))
        {
            text.AppendLine($"{group.Key}  {group.Count()}  {group.First().Descriptor.Title}");
            foreach (var d in group.OrderBy(d => Project.Where(d.Location))) text.AppendLine("    " + Project.Where(d.Location) + ": " + d.GetMessage());
        }
        File.WriteAllText(Path.Combine(Project.Out, "warnings.txt"), text.ToString());
        foreach (var group in all.GroupBy(d => d.Id).OrderByDescending(g => g.Count())) Console.WriteLine($"{group.Key}  {group.Count()}  {group.First().Descriptor.Title}");
    }

    static HashSet<string> Set(string[] args, string name, string fallback = "")
    {
        int at = Array.IndexOf(args, name);
        string value = at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
        if (value.StartsWith("@")) return new HashSet<string>(File.ReadAllLines(value.Substring(1)).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")));
        return new HashSet<string>(value.Split(',', StringSplitOptions.RemoveEmptyEntries));
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Directory.CreateDirectory(Project.Out);
        var watch = Stopwatch.StartNew();
        int code = 0;
        switch (args.Length > 0 ? args[0] : "check")
        {
            case "check": code = Check(args.Contains("--player"), 60); break;
            case "warn": Warnings(); break;
            case "errors": ErrorSummary(); break;
            case "remove": Rewrite.Remove(args[1].Split(','), args.Contains("--dry")); break;
            case "dead": Dead.Report(); break;
            case "kill":
                Kill.Run(Set(args, "--kinds", "method,property,field,const,event,class,struct,enum,interface,delegate"), Set(args, "--skip"),
                    Set(args, "--except"), Set(args, "--only"), args.Contains("--dry"));
                break;
            case "dupes": Dupes.Report(); break;
            case "split":
            {
                string Opt(string name) { int at = Array.IndexOf(args, name); return at >= 0 ? args[at + 1] : null; }
                Split.Run(args[1], Opt("--parts"), Opt("--names"), int.Parse(Opt("--least") ?? "30"), args.Contains("--dry"), Opt("--titles"));
                break;
            }
            case "lift":
            {
                string Arg(string name) { int at = Array.IndexOf(args, name); return at >= 0 ? args[at + 1] : null; }
                Lift.Run(Arg("--base"), Arg("--file"), Arg("--types").Split(','), Arg("--primary"), Arg("--summary") ?? "", args.Contains("--dry"));
                break;
            }
            case "forwarders": Rewrite.Forwarders(args.Contains("--dry")); break;
            case "dedupe":
            {
                int at = Array.IndexOf(args, "--map");
                int least = Array.IndexOf(args, "--least");
                int homes = Array.IndexOf(args, "--homes");
                Rewrite.Dedupe(at >= 0 ? args[at + 1] : null, args.Contains("--dry"), least >= 0 ? int.Parse(args[least + 1]) : 60, homes >= 0 ? args[homes + 1] : null);
                break;
            }
            case "stats": Stats.Report(); break;
            case "clones": Clones.Report(); break;
            case "outline": Outline.Print(args[1]); break;
            case "usings": Usings.Run(args.Contains("--dry")); break;
            default: Console.WriteLine("unknown command"); code = 2; break;
        }
        Console.WriteLine($"({watch.Elapsed.TotalSeconds:0.0} s)");
        return code;
    }
}
