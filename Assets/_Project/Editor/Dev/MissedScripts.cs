using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>
/// Gets a script of ours back into the compile when the open editor has it on disk but leaves it out of every
/// assembly. On 2026-11-14 one file (Player/Face/CharacterFace.Room.cs, written 12 s before a script reload while
/// sessions were writing scripts) stayed out of Assembly-CSharp for four hours. Every compile failed with CS0103 on
/// the members that file holds, though the code was whole. Changing the file did not bring it back; the same code
/// under a new name and GUID (CharacterFace.BlinkRoom.cs) went in at once.
///
/// So after each compile and each script reload this lists our scripts that no assembly has. For each one it takes
/// these steps in turn: import it again (what right-click → Reimport does), reload the scripts, then ask in the
/// console for an editor restart. Each step is taken once per script per editor run (SessionState keeps the count
/// across reloads), so it never loops. It looks only at a script whose .meta is there and that nobody has written for
/// a while, so it never races a session mid-write for a GUID.
/// </summary>
[InitializeOnLoad]
internal static class MissedScripts
{
    private const string Root = "Assets/_Project", Steps = "ENFILADE.MissedScripts";
    // Seconds a script and its .meta must have been left alone before a missing script counts as missed, not as still
    // arriving.
    private const double Settled = 30;
    private const int Import = 0, Reload = 1, Restart = 2, Done = 3;
    private static double checkAt = -1;

    static MissedScripts()
    {
        CompilationPipeline.compilationFinished += _ => Soon(1);
        EditorApplication.update += Tick;
        Soon(2);
    }

    private static void Soon(double seconds)
    {
        double at = EditorApplication.timeSinceStartup + seconds;
        if (checkAt < 0 || at < checkAt) checkAt = at;
    }

    private static void Tick()
    {
        if (checkAt < 0 || EditorApplication.timeSinceStartup < checkAt) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        checkAt = -1;
        try
        {
            Check();
        }
        catch (Exception error)
        {
            Debug.LogWarning($"MissedScripts: 컴파일 목록을 확인하지 못했습니다({error.GetType().Name}: {error.Message}).");
        }
    }

    private static void Check()
    {
        var compiled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in CompilationPipeline.GetAssemblies())
        {
            assemblies.Add(assembly.name);
            foreach (string file in assembly.sourceFiles) compiled.Add(file.Replace('\\', '/'));
        }
        // Unity has not made its assemblies yet (a fresh Library): nothing can be told from the list.
        if (compiled.Count == 0) return;

        var steps = Load();
        // A script back in the compile starts over, should it ever go missing again.
        foreach (string path in new List<string>(steps.Keys))
            if (compiled.Contains(path) || !File.Exists(path)) steps.Remove(path);

        var import = new List<string>();
        var reload = new List<string>();
        var restart = new List<string>();
        bool arriving = false;
        DateTime now = DateTime.Now;
        foreach (string found in Directory.EnumerateFiles(Root, "*.cs", SearchOption.AllDirectories))
        {
            string path = found.Replace('\\', '/');
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || compiled.Contains(path) || Ignored(path) || !Compiles(path, assemblies)) continue;
            string meta = path + ".meta";
            if (!File.Exists(meta)) continue;
            if ((now - Latest(path, meta)).TotalSeconds < Settled)
            {
                arriving = true;
                continue;
            }
            steps.TryGetValue(path, out int step);
            if (step == Import) import.Add(path);
            else if (step == Reload) reload.Add(path);
            else if (step == Restart) restart.Add(path);
        }
        // A script still being written is looked at again once it has settled.
        if (arriving) Soon(Settled);

        foreach (string path in restart) steps[path] = Done;
        if (restart.Count > 0)
            Debug.LogError($"편집기가 이 스크립트 {restart.Count}개를 다시 가져오고 다시 읽어도 컴파일하지 않습니다. 코드 문제가 아니니 편집기를 껐다 켜 주세요:\n" + string.Join("\n", restart));

        if (import.Count > 0)
        {
            foreach (string path in import) steps[path] = Reload;
            Save(steps);
            Debug.LogWarning($"편집기가 디스크에 있는 스크립트 {import.Count}개를 컴파일에서 빠뜨려 다시 가져옵니다(코드 문제가 아니라 편집기가 파일을 놓친 것이고, 곧 다시 컴파일됩니다):\n" + string.Join("\n", import));
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in import) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            // An import of an unchanged script may start no compile, so the next step does not wait for one.
            Soon(5);
            return;
        }
        if (reload.Count > 0)
        {
            foreach (string path in reload) steps[path] = Restart;
            Save(steps);
            Debug.LogWarning($"다시 가져와도 편집기가 이 스크립트 {reload.Count}개를 컴파일에서 빼서, 스크립트를 다시 읽습니다:\n" + string.Join("\n", reload));
            EditorUtility.RequestScriptReload();
            return;
        }
        Save(steps);
    }

    // Each script's next step, kept across script reloads for this editor run: "path=step" lines.
    private static Dictionary<string, int> Load()
    {
        var steps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in SessionState.GetString(Steps, "").Split('\n'))
        {
            int cut = line.LastIndexOf('=');
            if (cut > 0 && int.TryParse(line.Substring(cut + 1), out int step)) steps[line.Substring(0, cut)] = step;
        }
        return steps;
    }

    private static void Save(Dictionary<string, int> steps)
    {
        var lines = new List<string>();
        foreach (var pair in steps) lines.Add(pair.Key + "=" + pair.Value);
        SessionState.SetString(Steps, string.Join("\n", lines));
    }

    // Files and folders the editor never imports: names starting with a dot or ending with a tilde.
    private static bool Ignored(string path)
    {
        foreach (string part in path.Split('/'))
            if (part.StartsWith(".", StringComparison.Ordinal) || part.EndsWith("~", StringComparison.Ordinal)) return true;
        return false;
    }

    // A script under an assembly definition that this editor does not build (its platforms or define constraints say
    // no) is left out on purpose.
    private static bool Compiles(string path, HashSet<string> assemblies)
    {
        if (string.IsNullOrEmpty(CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath(path))) return true;
        string name = CompilationPipeline.GetAssemblyNameFromScriptPath(path);
        if (string.IsNullOrEmpty(name)) return false;
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
        return assemblies.Contains(name);
    }

    private static DateTime Latest(string path, string meta)
    {
        DateTime a = File.GetLastWriteTime(path), b = File.GetLastWriteTime(meta);
        return a > b ? a : b;
    }
}
