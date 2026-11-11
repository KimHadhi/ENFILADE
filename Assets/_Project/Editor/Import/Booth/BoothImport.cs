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
/// imported as packages) into the game's characters and outfits, as BoothSources.csv lists them:
/// - reads each prefab's VRChat PhysBones before anything else and adds their sway to Cloth.csv (VrcSway), keeping rows
///   already there, so hand tuning survives;
/// - takes VRChat's parts off the prefabs (scripts the game doesn't have: avatar descriptor, PhysBones, Modular Avatar,
///   VRChat constraints), the AFK effects and nested prefabs that didn't come in, and the avatar's animator controller,
///   so the game makes them without a missing-script warning each time;
/// - registers each character in the PlayerBody profile with its colour sets, and each outfit model in the
///   OutfitLibrary, its colour sets read from the package's prefab of each colour (the materials that differ from the
///   base prefab's, renderer by renderer).
/// Runs once an import under the BOOTH folder (or of BoothSources.csv) is over, and from Tools/Characters/BOOTH 다시
/// 가져오기. Running again changes nothing that is already done.
/// </summary>
internal sealed class BoothImport : AssetPostprocessor
{
    internal const string Root = "Assets/ThirdParty/BOOTH/";
    private const string SourcesPath = "Assets/_Project/Editor/Import/Booth/BoothSources.csv";
    private const string ProfilePath = "Assets/_Project/Resources/Characters/PlayerBody.asset";
    private const string ClothPath = "Assets/_Project/Resources/Tables/Cloth.csv";
    private static bool queued, running;

    // Colour words of BOOTH colour prefabs, for the colour cards.
    private static readonly (string English, string Korean)[] ColourWords =
    {
        ("Basic", "기본"), ("Beige", "베이지"), ("Black", "검정"), ("White", "흰색"), ("Yellow", "노랑"), ("Brown", "갈색"),
        ("Gray", "회색"), ("Grey", "회색"), ("Navy", "남색"), ("Pink", "분홍"), ("Purple", "보라"), ("Blue", "파랑"),
        ("Sky", "하늘"), ("Red", "빨강"), ("Green", "초록"), ("Mint", "민트"), ("Orange", "주황"), ("Transparent", "비침"),
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

        public string PartOf(string bone) => parts.TryGetValue(bone, out var part) ? part : null;

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
        // After this import is over: saving prefabs inside it would import them inside it.
        queued = true;
        EditorApplication.delayCall += () =>
        {
            queued = false;
            Run();
        };
    }

    [MenuItem("Tools/Characters/BOOTH 다시 가져오기")]
    private static void Run()
    {
        if (running || !File.Exists(SourcesPath)) return;
        running = true;
        try
        {
            var sources = ReadSources();
            var profile = AssetDatabase.LoadAssetAtPath<PlayerBodyProfile>(ProfilePath);
            var library = OutfitLibraryBuilder.Library(true);
            var cloth = new List<Dictionary<string, string>>();
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
                cloth.AddRange(VrcSway.Rows(VrcPrefab.Read(Root + source.Path), prefab, source.Id, character.Humanoid, character.PartOf));
                Clean(Root + source.Path);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + source.Path);
                if (profile != null) profile.EditorPutSkin(source.Id, source.Name, prefab, CharacterColours(source.Id, sources));
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
                cloth.AddRange(VrcSway.Rows(VrcPrefab.Read(path), prefab, source.Id, character.Bones, character.PartOf));
                var colourPaths = string.IsNullOrEmpty(source.Colours) || !AssetDatabase.IsValidFolder(Root + source.Colours)
                    ? new List<string>()
                    : Directory.GetFiles(Root + source.Colours, "*.prefab").Select(p => p.Replace('\\', '/'))
                        .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal).ToList();
                Clean(path);
                foreach (string colour in colourPaths) Clean(colour);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                entries.Add(new OutfitLibrary.Entry(source.Id, source.Skin, prefab, OutfitColours(prefab, colourPaths)));
            }
            library.EditorReplace(entry => entry.Model != null
                && AssetDatabase.GetAssetPath(entry.Model).StartsWith(Root, StringComparison.OrdinalIgnoreCase), entries);
            AddClothRows(cloth);
            if (profile != null) EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            OutfitCatalogReload();
        }
        finally
        {
            running = false;
        }
    }

    private static List<Source> ReadSources()
    {
        var sources = new List<Source>();
        var records = CsvTable.ReadRecords(File.ReadAllText(SourcesPath));
        if (records.Count == 0) return sources;
        var header = records[0].Select(cell => cell.Trim()).ToList();
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

    // Takes off what only VRChat runs, so the game makes the prefab without warnings. Changes nothing the second time.
    private static void Clean(string path)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        bool changed = false;
        try
        {
            foreach (var part in root.GetComponentsInChildren<Transform>(true))
            {
                if (part == null || part == root.transform) continue;
                var go = part.gameObject;
                bool missing = PrefabUtility.IsPrefabAssetMissing(go);
                // VRChat's AFK effects ("_AFK_Neko") are a VRChat gimmick; a nested prefab that didn't come in is a placeholder.
                if (!missing && go.name.IndexOf("AFK", StringComparison.Ordinal) < 0) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go)) continue;
                Object.DestroyImmediate(go);
                changed = true;
            }
            foreach (var part in root.GetComponentsInChildren<Transform>(true))
                changed |= GameObjectUtility.RemoveMonoBehavioursWithMissingScript(part.gameObject) > 0;
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController == null) continue;
                animator.runtimeAnimatorController = null;
                changed = true;
            }
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
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

    // The base prefab's colour first, then every colour prefab whose materials differ from it.
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

    // "Black&White2" → "검정·흰색 2", "Pink 1_Transparent" → "분홍 1 비침", "_Basic" → "기본".
    private static string Korean(string name)
    {
        string text = name.Replace('&', '·').Replace('_', ' ').Trim();
        foreach (var (english, korean) in ColourWords)
            text = Regex.Replace(text, english, korean, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(\D)(\d)", "$1 $2");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    // New rows go at the end of Cloth.csv in its own column order; a row whose id is there already stays as it is.
    private static void AddClothRows(List<Dictionary<string, string>> rows)
    {
        if (rows.Count == 0 || !File.Exists(ClothPath)) return;
        string text = File.ReadAllText(ClothPath);
        var records = CsvTable.ReadRecords(text);
        if (records.Count == 0) return;
        var header = records[0].Select(cell => cell.Trim().TrimStart('﻿')).ToList();
        var ids = new HashSet<string>(records.Skip(1).Where(r => r.Count > 0).Select(r => r[0].Trim()), StringComparer.OrdinalIgnoreCase);
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var added = new StringBuilder();
        var taken = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (ids.Contains(row["id"])) continue;
            string id = row["id"];
            for (int n = 2; !taken.Add(id); n++) id = row["id"] + "-" + n;
            row["id"] = id;
            added.Append(string.Join(",", header.Select(column => Quote(row.TryGetValue(column, out var value) ? value : "")))).Append(newline);
        }
        if (added.Length == 0) return;
        if (!text.EndsWith("\n")) text += newline;
        File.WriteAllText(ClothPath, text + added, new UTF8Encoding(true));
        AssetDatabase.ImportAsset(ClothPath);
    }

    private static string Quote(string cell) =>
        cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;

    // The tables the game reads changed on disk while the editor runs.
    private static void OutfitCatalogReload() => GameTables.Reload();
}
