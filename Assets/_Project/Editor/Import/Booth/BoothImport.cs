using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Makes the BOOTH avatars and outfits under Assets/ThirdParty/BOOTH (unpacked by Tools/Booth/booth_extract.py, or
/// imported as packages) into the game's characters and outfits, as BoothSources.csv lists them. The package's prefabs
/// stay as they came, VRChat's parts and all, so their PhysBones can always be read again; the game wears copies:
/// - reads each prefab's VRChat PhysBones and writes their sway into Cloth.csv (VrcSway), replacing the rows it wrote
///   before for that character or outfit (`vrc` true); rows without the mark are left alone, so hand tuning survives;
/// - saves a copy of each prefab in Art/Models/Booth without what only VRChat runs (scripts the game doesn't have:
///   avatar descriptor, PhysBones, Modular Avatar, VRChat constraints; the AFK animals; nested prefabs that didn't come
///   in; the avatar's animator controller), so the game makes them without a missing-script warning;
/// - registers each character's copy in the PlayerBody profile with its colour sets, and each outfit model's copy in the
///   OutfitLibrary, its colour sets read from the package's prefab of each colour (the materials that differ from the
///   base prefab's, renderer by renderer).
/// Runs once an import under the BOOTH folder (or of BoothSources.csv) is over, whenever the editor loads its scripts
/// and something listed isn't registered yet, and from Tools/Characters/BOOTH 다시 가져오기. Running again gives the
/// same result.
/// </summary>
internal sealed class BoothImport : AssetPostprocessor
{
    internal const string Root = "Assets/ThirdParty/BOOTH/";
    private const string CopiesFolder = "Assets/_Project/Art/Models/Booth";
    private const string SourcesPath = "Assets/_Project/Editor/Import/Booth/BoothSources.csv";
    private const string ProfilePath = "Assets/_Project/Resources/Characters/PlayerBody.asset";
    private const string ClothPath = "Assets/_Project/Resources/Tables/Cloth.csv";
    private static bool queued, running;
    // A table write that meets a lock: how many times it is tried and how long apart (s).
    private const int WriteTries = 6;
    private const double RetrySeconds = .5;

    // Colour words of BOOTH colour prefabs, for the colour cards.
    // White Board (Ene Collection, 2026-11-12) brought Basis, Burgundy, Gold, Lime and Sax (Japanese サックス, saxe blue).
    private static readonly (string English, string Korean)[] ColourWords =
    {
        ("Basic", "기본"), ("Basis", "기본"), ("Beige", "베이지"), ("Black", "검정"), ("White", "흰색"), ("Yellow", "노랑"),
        ("Brown", "갈색"), ("Gray", "회색"), ("Grey", "회색"), ("Navy", "남색"), ("Pink", "분홍"), ("Purple", "보라"),
        ("Blue", "파랑"), ("Sky", "하늘"), ("Red", "빨강"), ("Green", "초록"), ("Mint", "민트"), ("Orange", "주황"),
        ("Transparent", "비침"), ("Burgundy", "버건디"), ("Gold", "금색"), ("Lime", "라임"), ("Sax", "색스"),
    };

    private sealed class Source
    {
        public string Id, Kind, Skin, Path, Colours, Name;
    }

    // What the outfits need to know of a character: its bones, and which body part a bone is.
    private sealed class Character
    {
        public readonly HashSet<string> Humanoid = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Bones = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> parts = new Dictionary<string, string>(StringComparer.Ordinal);

        public Character(GameObject prefab)
        {
            var animator = prefab.GetComponentInChildren<Animator>(true);
            var avatar = animator != null ? animator.avatar : null;
            string hipsName = null;
            if (avatar != null && avatar.isHuman)
                foreach (var human in avatar.humanDescription.human)
                {
                    Humanoid.Add(human.boneName);
                    if (human.humanName == "Hips") hipsName = human.boneName;
                    string part = BodyPart(human.humanName);
                    if (part != null) parts[human.boneName] = part;
                }
            var hips = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == hipsName);
            foreach (var bone in (hips != null ? hips : prefab.transform).GetComponentsInChildren<Transform>(true)) Bones.Add(bone.name);
        }

        // The body part a collider stands on: its own bone, or the nearest humanoid bone above it (colliders often hang
        // from bones of their own, "Collider_UpperLeg.L").
        public string PartOf(Transform bone)
        {
            for (var at = bone; at != null; at = at.parent)
                if (parts.TryGetValue(at.name, out var part)) return part;
            return null;
        }

        // Cloth.csv's shapes: legs are the hips, legs and feet; head is the head, neck, chest and spine.
        private static string BodyPart(string human) => human switch
        {
            "Hips" or "LeftUpperLeg" or "RightUpperLeg" or "LeftLowerLeg" or "RightLowerLeg" or "LeftFoot" or "RightFoot"
                or "LeftToes" or "RightToes" => "legs",
            "Spine" or "Chest" or "UpperChest" or "Neck" or "Head" or "Jaw" => "head",
            _ => null,
        };
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (running || queued) return;
        if (!imported.Concat(moved).Any(path => path.StartsWith(Root, StringComparison.OrdinalIgnoreCase) || path == SourcesPath)) return;
        Queue();
    }

    // The files can come in while the project's scripts don't build (another change half done), and then this never
    // sees them arrive: the first time the editor loads it, it finishes whatever BoothSources.csv lists and isn't done.
    // On 2026-11-11 Mayo and Ichigo stayed off the character screen that way.
    [InitializeOnLoadMethod]
    private static void FinishPending()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !running && Pending()) Run();
        };
    }

    // After the import that brought the files is over: saving prefabs inside it would import them inside it.
    private static void Queue()
    {
        queued = true;
        EditorApplication.delayCall += () =>
        {
            queued = false;
            Run();
        };
    }

    // A character or outfit model whose files are here but which isn't registered with its copy yet.
    private static bool Pending()
    {
        if (!File.Exists(SourcesPath)) return false;
        var profile = AssetDatabase.LoadAssetAtPath<PlayerBodyProfile>(ProfilePath);
        var library = AssetDatabase.LoadAssetAtPath<OutfitLibrary>(OutfitLibraryBuilder.LibraryPath);
        foreach (var source in ReadSources())
        {
            if (!File.Exists(Root + source.Path)) continue;
            if (source.Kind == "character")
            {
                var skin = profile != null ? profile.Skins.FirstOrDefault(s => s != null && s.Id == source.Id) : null;
                if (skin == null || AssetDatabase.GetAssetPath(skin.Model) != CopyOf(source)) return true;
            }
            else if (source.Kind == "outfit")
            {
                var entry = library != null ? library.Find(source.Id, source.Skin) : null;
                if (entry == null || AssetDatabase.GetAssetPath(entry.Model) != CopyOf(source)) return true;
            }
        }
        return false;
    }

    // Where the game's copy of a source's prefab lives.
    private static string CopyOf(Source source) =>
        CopiesFolder + "/" + (source.Kind == "outfit" ? source.Id + "." + source.Skin : source.Id) + ".prefab";

    [MenuItem("Tools/Characters/BOOTH 다시 가져오기")]
    private static void Run()
    {
        if (running || !File.Exists(SourcesPath)) return;
        running = true;
        try
        {
            if (!AssetDatabase.IsValidFolder(CopiesFolder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(CopiesFolder).Replace('\\', '/'), Path.GetFileName(CopiesFolder));
            var sources = ReadSources();
            var profile = AssetDatabase.LoadAssetAtPath<PlayerBodyProfile>(ProfilePath);
            var library = OutfitLibraryBuilder.Library(true);
            // Each character's or outfit's sway rows, by Cloth.csv's skin column.
            var sway = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
            var characters = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources.Where(s => s.Kind == "character"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + source.Path);
                if (prefab == null)
                {
                    Debug.LogWarning($"[BOOTH] {source.Id}: {Root + source.Path}가 없습니다.");
                    continue;
                }
                var character = new Character(prefab);
                characters[source.Id] = character;
                Sway(sway, source.Id, Root + source.Path, prefab, character.Humanoid, character);
                var copy = Copy(Root + source.Path, CopyOf(source));
                if (profile != null && copy != null) profile.EditorPutSkin(source.Id, source.Name, copy, CharacterColours(source.Id, sources));
            }
            var entries = new List<OutfitLibrary.Entry>();
            foreach (var source in sources.Where(s => s.Kind == "outfit"))
            {
                string path = Root + source.Path;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || !characters.TryGetValue(source.Skin, out var character))
                {
                    Debug.LogWarning($"[BOOTH] {source.Id}: {path} 또는 캐릭터 '{source.Skin}'가 없습니다.");
                    continue;
                }
                // Per character: the outfit made for each has bones of its own.
                Sway(sway, source.Id + OutfitLook.Separator + source.Skin, path, prefab, character.Bones, character);
                var copy = Copy(path, CopyOf(source));
                if (copy == null) continue;
                var colourPaths = string.IsNullOrEmpty(source.Colours) || !AssetDatabase.IsValidFolder(Root + source.Colours)
                    ? new List<string>()
                    : Directory.GetFiles(Root + source.Colours, "*.prefab").Select(p => p.Replace('\\', '/'))
                        .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal).ToList();
                entries.Add(new OutfitLibrary.Entry(source.Id, source.Skin, copy, OutfitColours(prefab, colourPaths)));
            }
            library.EditorReplace(entry => entry.Model == null || IsOurs(AssetDatabase.GetAssetPath(entry.Model)), entries);
            // Saved before the table is written: a write that fails must not take the characters and outfits with it.
            if (profile != null) EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            ReplaceClothRows(sway);
            GameTables.Reload();
        }
        finally
        {
            running = false;
        }
    }

    private static bool IsOurs(string path) =>
        path.StartsWith(Root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(CopiesFolder + "/", StringComparison.OrdinalIgnoreCase);

    // The prefab's PhysBones as sway rows under this owner; nothing when it has none.
    private static void Sway(Dictionary<string, List<Dictionary<string, string>>> sway, string owner, string path, GameObject prefab,
        ISet<string> shared, Character character)
    {
        var read = VrcPrefab.Read(path);
        if (read.PhysBones.Count == 0) return;
        sway[owner] = VrcSway.Rows(read, prefab, owner, shared, character.PartOf);
    }

    private static List<Source> ReadSources()
    {
        var sources = new List<Source>();
        var records = CsvTable.ReadRecords(File.ReadAllText(SourcesPath));
        if (records.Count == 0) return sources;
        var header = records[0].Select(cell => cell.Trim().TrimStart('﻿')).ToList();
        string Cell(List<string> record, string column)
        {
            int at = header.IndexOf(column);
            return at >= 0 && at < record.Count ? record[at].Trim() : "";
        }
        foreach (var record in records.Skip(1))
        {
            string id = record.Count > 0 ? record[0].Trim() : "";
            if (id.Length == 0 || id[0] == '#') continue;
            string kind = Cell(record, "kind");
            // An outfit's row id is "model@skin": one model, made for each character it fits.
            int at = id.IndexOf('@');
            sources.Add(new Source
            {
                Id = kind == "outfit" && at > 0 ? id.Substring(0, at) : id,
                Kind = kind,
                Skin = kind == "outfit" && at > 0 ? id.Substring(at + 1) : Cell(record, "skin"),
                Path = Cell(record, "source"),
                Colours = Cell(record, "colours"),
                Name = Cell(record, "name"),
            });
        }
        return sources;
    }

    // Saves the game's copy of a prefab without what only VRChat runs, and returns it.
    private static GameObject Copy(string source, string target)
    {
        var root = PrefabUtility.LoadPrefabContents(source);
        try
        {
            foreach (var part in root.GetComponentsInChildren<Transform>(true))
            {
                if (part == null || part == root.transform) continue;
                var go = part.gameObject;
                // VRChat's AFK animals ("_AFK_Neko") are a VRChat gimmick; a nested prefab that didn't come in is a placeholder.
                if (!PrefabUtility.IsPrefabAssetMissing(go) && go.name.IndexOf("AFK", StringComparison.Ordinal) < 0) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go)) continue;
                Object.DestroyImmediate(go);
            }
            foreach (var part in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(part.gameObject);
            foreach (var animator in root.GetComponentsInChildren<Animator>(true)) animator.runtimeAnimatorController = null;
            return PrefabUtility.SaveAsPrefabAsset(root, target);
        }
        catch (Exception problem)
        {
            Debug.LogWarning($"[BOOTH] {source}의 사본을 {target}에 만들지 못했습니다: {problem.Message}");
            return null;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // The character as it comes first ("기본"), then each colour row: "from.mat>to.mat|..." under the BOOTH folder.
    private static SkinVariant[] CharacterColours(string skin, List<Source> sources)
    {
        var sets = new List<SkinVariant> { new SkinVariant("base", "기본", Array.Empty<SkinVariant.Swap>()) };
        foreach (var source in sources.Where(s => s.Kind == "colour" && string.Equals(s.Skin, skin, StringComparison.OrdinalIgnoreCase)))
        {
            var swaps = new List<SkinVariant.Swap>();
            foreach (string pair in source.Path.Split('|'))
            {
                var ends = pair.Split('>');
                if (ends.Length != 2) continue;
                var from = AssetDatabase.LoadAssetAtPath<Material>(Root + ends[0].Trim());
                var to = AssetDatabase.LoadAssetAtPath<Material>(Root + ends[1].Trim());
                if (from != null && to != null) swaps.Add(new SkinVariant.Swap { from = from, to = to });
                else Debug.LogWarning($"[BOOTH] {skin}#{source.Id}: '{pair}'의 재질을 찾지 못했습니다.");
            }
            if (swaps.Count > 0) sets.Add(new SkinVariant(source.Id, source.Name, swaps.ToArray()));
        }
        return sets.Count > 1 ? sets.ToArray() : Array.Empty<SkinVariant>();
    }

    // The base prefab's colour first, then every colour prefab whose materials differ from it, renderer by renderer
    // (the copy keeps the base prefab's renderers and materials, so the swaps fit it).
    private static SkinVariant[] OutfitColours(GameObject model, List<string> colourPaths)
    {
        if (model == null || colourPaths.Count == 0) return Array.Empty<SkinVariant>();
        string baseName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(model));
        var sets = new List<SkinVariant> { new SkinVariant("base", Korean(baseName), Array.Empty<SkinVariant.Swap>()) };
        var original = new Dictionary<string, Material[]>(StringComparer.Ordinal);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            original[AnimationUtility.CalculateTransformPath(renderer.transform, model.transform)] = renderer.sharedMaterials;
        foreach (string path in colourPaths)
        {
            var colour = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (colour == null) continue;
            var swaps = new List<SkinVariant.Swap>();
            foreach (var renderer in colour.GetComponentsInChildren<Renderer>(true))
            {
                if (!original.TryGetValue(AnimationUtility.CalculateTransformPath(renderer.transform, colour.transform), out var mine)) continue;
                var theirs = renderer.sharedMaterials;
                for (int i = 0; i < mine.Length && i < theirs.Length; i++)
                    if (mine[i] != null && theirs[i] != null && mine[i] != theirs[i] && swaps.All(s => s.from != mine[i]))
                        swaps.Add(new SkinVariant.Swap { from = mine[i], to = theirs[i] });
            }
            string name = Path.GetFileNameWithoutExtension(path);
            if (swaps.Count > 0) sets.Add(new SkinVariant(VrcSway.Slug(name), Korean(name), swaps.ToArray()));
        }
        return sets.Count > 1 ? sets.ToArray() : Array.Empty<SkinVariant>();
    }

    // "Black&White2" → "검정·흰색 2", "Pink 1_Transparent" → "분홍 1 비침", "_Basic" → "기본", "Black_and_Gold" → "검정·금색".
    private static string Korean(string name)
    {
        string text = name.Replace('&', '·').Replace('_', ' ').Trim();
        text = Regex.Replace(text, @"\s+and\s+", "·", RegexOptions.IgnoreCase);
        foreach (var (english, korean) in ColourWords)
            text = Regex.Replace(text, english, korean, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(\D)(\d)", "$1 $2");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    // Cloth.csv keeps its own column order. For each owner read now, the rows this wrote before (vrc) go and the new ones
    // come in at the end; every other row stays as it is.
    private static void ReplaceClothRows(Dictionary<string, List<Dictionary<string, string>>> sway)
    {
        if (sway.Count == 0 || !File.Exists(ClothPath)) return;
        string text = File.ReadAllText(ClothPath);
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var records = CsvTable.ReadRecords(text);
        if (records.Count == 0) return;
        var header = records[0].Select(cell => cell.Trim().TrimStart('﻿')).ToList();
        int skinColumn = header.IndexOf("skin"), vrcColumn = header.IndexOf("vrc");
        var lines = text.Split(new[] { newline }, StringSplitOptions.None).ToList();
        var kept = new StringBuilder();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Line by line, as the file has them (rows of this table never break across lines).
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (i == lines.Count - 1 && line.Length == 0) break;
            var cells = i == 0 ? null : CsvTable.ReadRecords(line).FirstOrDefault();
            bool ours = cells != null && skinColumn >= 0 && vrcColumn >= 0 && cells.Count > vrcColumn
                && sway.ContainsKey(cells[skinColumn].Trim()) && cells[vrcColumn].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            if (ours) continue;
            if (cells != null && cells.Count > 0) ids.Add(cells[0].Trim());
            kept.Append(line).Append(newline);
        }
        foreach (var rows in sway.Values)
            foreach (var row in rows)
            {
                string id = row["id"];
                for (int n = 2; !ids.Add(id); n++) id = row["id"] + "-" + n;
                row["id"] = id;
                kept.Append(string.Join(",", header.Select(column => Quote(row.TryGetValue(column, out var value) ? value : "")))).Append(newline);
            }
        string written = kept.ToString();
        if (written == text) return;
        WriteTable(ClothPath, written, WriteTries);
    }

    // Writes a table the editor may hold open. Just after an import it keeps cached handles on the files it read, and
    // Windows then refuses the truncating write (Win32 IO 1224, "user-mapped section", on Cloth.csv: the user's console,
    // 2026-11-12, a second run of the White Board import, which threw and stopped there). The editor lets go of its
    // handles first; a write that still meets a lock is tried again a moment later, a few times, instead of stopping the
    // import, and the tables are read again once it lands.
    private static void WriteTable(string path, string text, int tries)
    {
        AssetDatabase.ReleaseCachedFileHandles();
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }
        catch (IOException error)
        {
            if (tries <= 1)
            {
                Debug.LogError($"[BOOTH] {path}에 흔들림 줄을 쓰지 못했습니다({error.Message}). 그 파일을 연 다른 프로그램을 닫고 Tools → Characters → BOOTH 다시 가져오기를 눌러 주세요.");
                return;
            }
            double at = EditorApplication.timeSinceStartup + RetrySeconds;
            void Later()
            {
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= Later;
                WriteTable(path, text, tries - 1);
            }
            EditorApplication.update += Later;
            return;
        }
        AssetDatabase.ImportAsset(path);
        GameTables.Reload();
    }

    private static string Quote(string cell) =>
        cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
}
