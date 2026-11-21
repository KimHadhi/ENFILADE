#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The editor's bench for what characters wear, without playing: every character is stood as its file has it and dressed
/// by the very rule the game dresses a body by (OutfitDresser), and what comes of it is measured and written down, so a
/// piece that is wrong on one character is a line in a report before anyone plays it (the user, 2026-11-18, heading the
/// whole list: "문제를 근본적으로 생각하고 뿌리를 고치며").
/// What it checks:
/// - the worn-parts rule itself (WornParts), on parts made up for it: everything a look may not keep goes, in one pass,
///   and nothing it keeps is touched. A few milliseconds, so it runs by itself after every script reload
///   (Logs/WardrobeBench/rule.txt) and warns in the Console when it is broken;
/// - every character's and every outfit's file: which of them carry parts a worn thing may not keep (the welding mask
///   came with a prop pack's solid BoxCollider), and
/// - the gate: each character dressed in each outfit model it can wear, then searched for anything solid (a collider,
///   a body, a joint, a controller) left under it. None may be: nothing the game dresses is part of the world's physics
///   until the game itself makes it so;
/// - what a look says of where a piece is worn (WearPosition, ItemAdjust), as words: a nudge reads back as it was
///   written, a look saved with an item that has since become another item's position wears that item there with its
///   nudge, a preset keeps all of it, and every position an item names is a row. Milliseconds too, so it runs with the
///   rule after every script reload (WardrobeBench.Positions.cs);
/// - every way of wearing every hung piece on every character: dressed, then measured from the eyes in eye spacings
///   (where the piece's own spot went, what it follows, whether a pair mirrors, whether it covers what it should), and
///   photographed from the front, the side and the back;
/// - the heels every character stands in, as it comes, barefoot and in each pair of shoes it can wear, as the feet will
///   sound them (DrawnHeels; WardrobeBench.Heels.cs, Logs/WardrobeBench/heels.txt).
/// Those that load every model run when asked: ENFILADE → 캐릭터 → 옷장 검사대, or a tool outside Unity making the file
/// Temp/enfilade-wardrobebench.request, whose lines say what to stand (all optional):
///   skins=mayo,ichigo        characters by id (all)
///   models=welding-mask      outfit models by id (all)
///   check=parts,positions    which of parts, positions and heels (all three)
///   pictures=no              the positions' numbers only (pictures are taken unless told not to)
///   width=256                one picture's width and height in pixels
/// Logs/WardrobeBench/parts.txt and positions.txt have the answers; each character's pictures are one sheet,
/// positions/&lt;character&gt;.png, a row to an item's way of wearing and a column to a view. It lives beside OutfitDresser
/// because it dresses with the game's own classes.
/// </summary>
[InitializeOnLoad]
internal static partial class WardrobeBench
{
    private const string Request = "Temp/enfilade-wardrobebench.request", Folder = "Logs/WardrobeBench",
        RulePath = Folder + "/rule.txt", PartsPath = Folder + "/parts.txt";

    private static double next;
    private static bool ruleRan;

    static WardrobeBench() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!ruleRan)
        {
            ruleRan = true;
            RunRule();
        }
        if (!File.Exists(Request)) return;
        Ask ask;
        try
        {
            ask = Ask.Read(File.ReadAllLines(Request));
            File.Delete(Request);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return;
        }
        Run(ask);
    }

    [MenuItem("ENFILADE/캐릭터/옷장 검사대 (모든 캐릭터, 모든 옷)")]
    private static void RunAll() => Run(new Ask());

    // ---- What was asked ----

    private sealed class Ask
    {
        public HashSet<string> Skins, Models;
        public bool Parts = true, Positions = true, Heels = true, Pictures = true;
        public int Width = 256;

        public static Ask Read(string[] lines)
        {
            var ask = new Ask();
            foreach (string raw in lines)
            {
                int cut = raw.IndexOf('=');
                if (cut <= 0) continue;
                string key = raw.Substring(0, cut).Trim().ToLowerInvariant();
                var values = raw.Substring(cut + 1).Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (key)
                {
                    case "skins":
                        ask.Skins = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
                        break;
                    case "models":
                        ask.Models = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
                        break;
                    case "check":
                        var which = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
                        ask.Parts = which.Contains("parts");
                        ask.Positions = which.Contains("positions");
                        ask.Heels = which.Contains("heels");
                        break;
                    case "pictures":
                        ask.Pictures = values.Length == 0 || values[0].ToLowerInvariant() != "no";
                        break;
                    case "width":
                        if (values.Length > 0 && int.TryParse(values[0], out int width)) ask.Width = Mathf.Clamp(width, 96, 1024);
                        break;
                }
            }
            return ask;
        }
    }

    // What a check found: a line per thing looked at, the failures counted.
    private sealed class Report
    {
        public readonly StringBuilder Text = new StringBuilder();
        public int Failed, Looked;

        public void Line(string line) => Text.AppendLine(line);

        public void Expect(bool holds, string what, string numbers)
        {
            Looked++;
            if (holds) return;
            Failed++;
            Text.AppendLine("  FAIL " + what + ": " + numbers);
        }
    }

    private static void Write(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("WardrobeBench: " + path + " could not be written: " + error.Message);
        }
    }

    // ---- The rule, on parts made up for it ----

    private static void RunRule()
    {
        var report = new Report();
        try
        {
            report.Line("== the worn-parts rule (WornParts), on parts made up for it");
            Rule(report);
            report.Line("");
            report.Line("== what a look says of where a piece is worn (WearPosition, ItemAdjust, OutfitLook.Settled)");
            LookRules(report);
        }
        catch (Exception problem)
        {
            report.Failed++;
            report.Line("  FAIL the check itself stopped: " + problem);
        }
        Write(RulePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} the wardrobe's rules that need no model, {report.Looked} things checked: "
            + (report.Failed == 0 ? "ALL KEPT" : report.Failed + " BROKEN") + "\n\n" + report.Text);
        if (report.Failed > 0) Debug.LogWarning($"WardrobeBench: {report.Failed} of the wardrobe's rules are broken, see {RulePath}.");
    }

    // A made-up model carrying one of everything, each part with what leans on it and put on in the order that is worst
    // for taking it off. After the rule: nothing foreign is left, nothing was refused on the way, and what a look keeps
    // is all still there.
    private static void Rule(Report report)
    {
        var host = new GameObject("Worn parts rule") { hideFlags = HideFlags.HideAndDontSave };
        // Switched off first, so nothing made here lives for a moment (no camera draws, no listener hears).
        host.SetActive(false);
        var refused = new List<string>();
        void Heard(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) refused.Add(message);
        }
        try
        {
            GameObject Under(string name)
            {
                var made = new GameObject(name);
                made.transform.SetParent(host.transform, false);
                return made;
            }
            // What a look keeps.
            host.AddComponent<Animator>();
            host.AddComponent<LODGroup>();
            var plain = Under("Plain mesh");
            plain.AddComponent<MeshFilter>();
            plain.AddComponent<MeshRenderer>();
            var skin = Under("Skinned mesh");
            skin.AddComponent<SkinnedMeshRenderer>();
            Under("Held").AddComponent<UnityEngine.Animations.ParentConstraint>();
            int kept = host.GetComponentsInChildren<Component>(true).Length, objects = host.GetComponentsInChildren<Transform>(true).Length;
            // What it may not: the world's physics …
            var solid = Under("Solid");
            solid.AddComponent<Rigidbody>();
            solid.AddComponent<BoxCollider>();
            solid.AddComponent<CharacterJoint>();
            solid.AddComponent<ConstantForce>();
            Under("Ball").AddComponent<SphereCollider>();
            Under("Rod").AddComponent<CapsuleCollider>();
            Under("Shell").AddComponent<MeshCollider>();
            Under("Walker").AddComponent<CharacterController>();
            // … and whatever else acts on the world by itself.
            var sound = Under("Sound");
            sound.AddComponent<AudioSource>();
            sound.AddComponent<AudioLowPassFilter>();
            sound.AddComponent<AudioReverbFilter>();
            var eye = Under("Eye");
            eye.AddComponent<Camera>();
            eye.AddComponent<FlareLayer>();
            eye.AddComponent<AudioListener>();
            Under("Lamp").AddComponent<Light>();
            Under("Sparks").AddComponent<ParticleSystem>();
            Under("Tail").AddComponent<TrailRenderer>();
            // A script, the game's own: a file may not bring one either (the game puts its own on after dressing).
            Under("Script").AddComponent<ItemAdjust>();
            // The objects made for them stay, each with its Transform.
            objects = host.GetComponentsInChildren<Transform>(true).Length - objects;

            var foreign = WornParts.Foreign(host);
            int solidCount = 0;
            foreach (var part in foreign) if (WornParts.Solid(part)) solidCount++;
            report.Expect(solidCount == 7, "the made-up model's solid parts are told from the rest", $"7 put on (a body, a joint, four colliders, a controller), {solidCount} told as solid");
            report.Expect(foreign.Count > 0 && !WornParts.Solid(host.GetComponent<Animator>()), "a kept part is not solid", "the Animator read as solid");

            Application.logMessageReceived += Heard;
            string went;
            try
            {
                went = WornParts.Strip(host);
            }
            finally
            {
                Application.logMessageReceived -= Heard;
            }
            var left = WornParts.Foreign(host);
            report.Line("  took off: " + went);
            report.Expect(left.Count == 0, "everything a look may not keep goes", left.Count + " left: " + WornParts.Describe(left));
            report.Expect(refused.Count == 0, "nothing is refused on the way (each part goes after what leans on it)",
                refused.Count + " refused: " + string.Join(" | ", refused));
            int now = host.GetComponentsInChildren<Component>(true).Length;
            report.Expect(now == kept + objects, "what a look keeps is not touched", $"{kept} kept parts before the foreign ones went on, {now - objects} after they came off");
            report.Expect(WornParts.Strip(host).Length == 0, "a clean model is left alone", "a second pass took something off");
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    // ---- Every file, and the gate ----

    private static void Run(Ask ask)
    {
        var profile = PlayerBodyProfile.Load();
        if (profile == null)
        {
            Debug.LogWarning("WardrobeBench: the body profile (Resources/Characters/PlayerBody) is missing; nothing to stand.");
            return;
        }
        if (ask.Parts) RunParts(ask, profile);
        if (ask.Positions) RunPositions(ask, profile);
        if (ask.Heels) RunHeels(ask, profile);
    }

    private static void RunParts(Ask ask, PlayerBodyProfile profile)
    {
        var report = new Report();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int files = 0, dressed = 0;
        try
        {
            Rule(report);
            report.Line("");
            report.Line("== files: what each model's file carries that a worn thing may not keep (dressing takes it off; 'SOLID' marks the world's physics)");
            var seen = new HashSet<GameObject>();
            int done = 0;
            foreach (var skin in profile.Skins)
            {
                done++;
                if (skin == null || (ask.Skins != null && !ask.Skins.Contains(skin.Id))) continue;
                EditorUtility.DisplayProgressBar("ENFILADE: 옷장 검사대", skin.Id, done / (float)Mathf.Max(1, profile.Skins.Count));
                var model = profile.ModelOf(skin);
                if (model == null) continue;
                if (seen.Add(model)) { FileLine(report, "character " + skin.Id, model); files++; }
                foreach (var (id, entry) in ModelsFor(skin.Id, ask))
                    if (entry.Model != null && seen.Add(entry.Model)) { FileLine(report, "outfit " + id + (entry.ForAnyone ? " (anyone)" : " (" + skin.Id + ")"), entry.Model); files++; }
            }
            report.Line("");
            report.Line("== the gate: each character dressed in each outfit model, then searched for anything solid under it (none may be left)");
            done = 0;
            foreach (var skin in profile.Skins)
            {
                done++;
                if (skin == null || (ask.Skins != null && !ask.Skins.Contains(skin.Id))) continue;
                var model = profile.ModelOf(skin);
                if (model == null) continue;
                EditorUtility.DisplayProgressBar("ENFILADE: 옷장 검사대", skin.Id + " 입히는 중", done / (float)Mathf.Max(1, profile.Skins.Count));
                dressed += Gate(report, skin, model, null, new OutfitLook());
                foreach (var (id, _) in ModelsFor(skin.Id, ask))
                    dressed += Gate(report, skin, model, id, LookOf(id, skin.Id));
            }
        }
        catch (Exception problem)
        {
            report.Failed++;
            report.Line("  FAIL the bench stopped: " + problem);
            Debug.LogException(problem);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        Write(PartsPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} wardrobe bench, worn parts: {files} files read, {dressed} dressings, {report.Looked} things checked: "
            + (report.Failed == 0 ? "ALL KEPT" : report.Failed + " BROKEN") + $"  ({watch.Elapsed.TotalSeconds:0.0} s)\n"
            + "The rule (WornParts): a character's model and what it wears keep their bones, the Animator, meshes and renderers, a LOD group and\n"
            + "Unity's constraints; everything else a file brings is taken off as it is dressed.\n\n== the rule, on parts made up for it\n" + report.Text);
        if (report.Failed > 0) Debug.LogWarning($"WardrobeBench: {report.Failed} checks broken, see {PartsPath}.");
        else Debug.Log($"WardrobeBench: {files} files, {dressed} dressings, all kept; {PartsPath}.");
    }

    // Every outfit model this character can wear, by its id, in the tables' order: the game's own and the mods'.
    private static List<(string Id, OutfitLibrary.Entry Entry)> ModelsFor(string skinId, Ask ask)
    {
        var models = new List<(string, OutfitLibrary.Entry)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in OutfitCatalog.Slots)
            foreach (var item in OutfitCatalog.ItemsFor(skinId, slot.id))
            {
                if (!seen.Add(item.ModelId) || (ask.Models != null && !ask.Models.Contains(item.ModelId))) continue;
                var entry = OutfitCatalog.Model(item.id, skinId);
                if (entry != null) models.Add((item.ModelId, entry));
            }
        return models;
    }

    // A look wearing the model's pieces: one in each slot that has one, the first the table lists.
    private static OutfitLook LookOf(string modelId, string skinId)
    {
        var look = new OutfitLook();
        foreach (var slot in OutfitCatalog.Slots)
            foreach (var item in OutfitCatalog.ItemsFor(skinId, slot.id))
            {
                if (!string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase)) continue;
                look.Set(slot.id, item.id);
                break;
            }
        return look;
    }

    private static void FileLine(Report report, string what, GameObject model)
    {
        var foreign = WornParts.Foreign(model);
        var solid = foreign.FindAll(WornParts.Solid);
        string path = AssetDatabase.GetAssetPath(model);
        report.Line($"{(solid.Count > 0 ? "SOLID" : foreign.Count > 0 ? "other" : "clean")} {what}: "
            + (foreign.Count > 0 ? WornParts.Describe(foreign) : "nothing but what a look keeps")
            + (string.IsNullOrEmpty(path) ? "  (a mod's bundle)" : "  " + path));
    }

    // One character dressed in one model's pieces (or in nothing), as the game dresses it; then nothing solid may be
    // under it. Returns 1 when it was dressed.
    private static int Gate(Report report, PlayerBodyProfile.Skin skin, GameObject model, string modelId, OutfitLook look)
    {
        string what = skin.Id + (modelId != null ? " in " + modelId : " as it comes");
        var instance = Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var dressing = OutfitDresser.Dress(instance, model, skin, look);
            var solid = new List<Component>();
            foreach (var part in instance.GetComponentsInChildren<Component>(true))
                if (part != null && WornParts.Solid(part)) solid.Add(part);
            report.Expect(solid.Count == 0, what, solid.Count + " solid parts left under the dressed model: " + WornParts.Describe(solid));
            if (modelId != null)
            {
                int wanted = 0, on = 0;
                foreach (string slot in look.Slots)
                {
                    wanted++;
                    if (look.TryGet(slot, out var pick) && dressing.Pieces.ContainsKey(pick.Item)) on++;
                }
                report.Expect(on > 0, what, $"none of its {wanted} pieces went on (see the Console's [Outfits] lines)");
            }
            return 1;
        }
        catch (Exception problem)
        {
            report.Expect(false, what, "dressing stopped: " + problem.Message);
            return 0;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
#endif
