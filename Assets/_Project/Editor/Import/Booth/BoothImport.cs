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
///   base prefab's, renderer by renderer), or, where a colours folder holds no prefabs, from each material in it (a
///   hair's colour pack: the one material the model is drawn with, swapped). A row "model@*" is an accessory sold for
///   any avatar: registered once, for anyone, it goes on every character whose spot for its slot is measured
///   (OutfitSpots.csv);
/// - puts the underwear that came with a character or an outfit into the underwear slots, where no row lists it yet
///   (BoothUnderwear), then every other piece of an outfit no row names yet into the slot it is for, one item each
///   (BoothPieces): a shirt that came with stockings and necklaces arrives as a shirt, stockings and necklaces.
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
    // The version (SwayWritten) this PC's Cloth.csv rows were last written with (FinishPending). A new name in 0.8.135: the
    // old key was noted even when the table write failed (Win32 IO 1224, 2026-11-12), so it could vouch for rows that
    // never reached the file (that day the Ene earrings and the Sera Yura skirt ring); every PC runs the import once more.
    private const string SwayVersionKey = "ENFILADE.BoothImport.SwayWritten";
    // Which rows this import gives an outfit, besides how VrcSway writes each (VrcSway.Version): raised when that changes,
    // so the rows already written are written again on the next script load (FinishPending), as a VrcSway change does.
    // 1: an outfit's bone that only shares a name with the character's sways on the outfit's own row (2026-11-12).
    private const int RowsVersion = 1;
    private static int SwayWritten => VrcSway.Version * 100 + RowsVersion;
    private static bool queued, running;

    // Colour words of BOOTH colour prefabs, for the colour cards: patterns, tried in this order on the name (_ as spaces).
    // White Board (Ene Collection, 2026-11-12) brought Basis, Burgundy, Gold, Lime and Sax (Japanese サックス, saxe blue).
    // Sera Yura (LookVook, 2026-11-12) names every colour after the outfit (Sera_Yura_Black&Aqua, the base _Sera_Yura),
    // which says nothing on a colour card, so the name goes first; its two-word colours (Sky Blue, Deep Blue) come before
    // their single words, and Deep / Pastel read as 진 / 연 (Deep Gray 진회색, Pastel Pink 연분홍).
    // MARIYURI's hair colour packs (ボサボサロングヘア, 2026-11-13) bring Kipfel, a light warm grey-brown (texture average
    // RGB 188 171 149, what Korean salons sell as 밀크티), and the Plus pack's dye styles by folder: Gradation, Half (a
    // split dye) and InOut (an inner colour).
    private static readonly (string English, string Korean)[] ColourWords =
    {
        ("Kipfel", "밀크티"), ("Gradation", "그라데이션"), ("InOut", "이너컬러"), ("^Half$", "반반"),
        ("^Sera Yura$", "기본"), ("^Sera Yura ", ""), ("Sky Blue", "하늘색"), ("Teal Blue", "청록"), ("Warm Pink", "웜핑크"),
        ("Deep Blue", "진청"), ("Deep Black", "칠흑"),
        ("Basic", "기본"), ("Basis", "기본"), ("Beige", "베이지"), ("Black", "검정"), ("White", "흰색"), ("Yellow", "노랑"),
        ("Brown", "갈색"), ("Gray", "회색"), ("Grey", "회색"), ("Navy", "남색"), ("Pink", "분홍"), ("Purple", "보라"),
        ("Blue", "파랑"), ("Sky", "하늘"), ("Red", "빨강"), ("Green", "초록"), ("Mint", "민트"), ("Orange", "주황"),
        ("Transparent", "비침"), ("Burgundy", "버건디"), ("Gold", "금색"), ("Lime", "라임"), ("Sax", "색스"),
        ("Deep ", "진"), ("Pastel ", "연"), ("3Line", "3줄"), ("Check", "체크"), ("Cow", "젖소"), ("Hologram", "홀로그램"),
        ("Latex", "라텍스"), ("Mono", "모노"), ("Water", "물결"), ("Silver", "은색"), ("Aqua", "아쿠아"), ("Cherry", "체리"),
        ("Grass", "풀색"), ("Wine", "와인"), ("Ivory", "아이보리"), ("Khaki", "카키"), ("Ash", "잿빛"), ("Lavender", "라벤더"),
        ("Coral", "코랄"),
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
        // Its bones as an outfit's file is joined to them when worn (OutfitDresser.Merge reads them the same way).
        private readonly OutfitSkeleton skeleton;

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
            var all = prefab.GetComponentsInChildren<Transform>(true);
            var hips = all.FirstOrDefault(t => t.name == hipsName);
            foreach (var bone in (hips != null ? hips : prefab.transform).GetComponentsInChildren<Transform>(true)) Bones.Add(bone.name);
            var drawn = new HashSet<Transform>(prefab.GetComponentsInChildren<Renderer>(true).Select(renderer => renderer.transform));
            var humanoid = new HashSet<Transform>(all.Where(bone => Humanoid.Contains(bone.name)));
            skeleton = new OutfitSkeleton(hips != null ? hips : prefab.transform, prefab.transform, drawn, humanoid);
        }

        /// <summary>
        /// The names of an outfit's bones that only share a name with one of the character's, as dressing sorts them
        /// (OutfitSkeleton): not a humanoid bone, standing elsewhere, and not hanging from her bone's parent as hers does.
        /// Worn, such a bone stays the outfit's own and is renamed after the model, so its chain sways on the outfit's own
        /// rows, under that name.
        /// </summary>
        public HashSet<string> Namesakes(GameObject outfit)
        {
            var namesakes = new List<Transform>();
            skeleton.Sort(outfit.transform, new Dictionary<Transform, Transform>(), namesakes);
            return new HashSet<string>(namesakes.Select(bone => bone.name), StringComparer.Ordinal);
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
    // On 2026-11-11 Mayo and Ichigo stayed off the character screen that way. It also runs once when VrcSway writes its
    // rows differently than it did for this table (VrcSway.Version), or this import gives outfits other rows
    // (RowsVersion), so a fix to the sway reaches the rows already written without anyone running the import by hand.
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

    // A character or outfit model whose files are here but which isn't registered with its copy yet, or sway rows
    // written by an older VrcSway.
    private static bool Pending()
    {
        if (!File.Exists(SourcesPath)) return false;
        var profile = AssetDatabase.LoadAssetAtPath<PlayerBodyProfile>(ProfilePath);
        var library = AssetDatabase.LoadAssetAtPath<OutfitLibrary>(OutfitLibraryBuilder.LibraryPath);
        bool present = false;
        foreach (var source in ReadSources())
        {
            if (!File.Exists(Root + source.Path)) continue;
            present = true;
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
        // Not while the rows wait for Cloth.csv (TableFile): they are made already and land by themselves.
        return present && EditorPrefs.GetInt(SwayVersionKey, 0) != SwayWritten && !TableFile.Waiting(ClothPath);
    }

    // Where the game's copy of a source's prefab lives. A model made for anyone is "<model>.any" ('*' is no file name).
    private static string CopyOf(Source source) =>
        CopiesFolder + "/" + (source.Kind == "outfit" ? source.Id + "." + (ForAnyone(source) ? "any" : source.Skin) : source.Id) + ".prefab";

    // An outfit row "model@*": an accessory sold for any avatar (the Ene earrings), not fitted to one character's bones.
    private static bool ForAnyone(Source source) => source.Kind == "outfit" && source.Skin == OutfitLibrary.AnySkin;

    // The bones such a model shares with a character: none.
    private static readonly HashSet<string> NoBones = new HashSet<string>(StringComparer.Ordinal);

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
            // The prefabs read, for the underwear in them (BoothUnderwear).
            var bodies = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            var bodyUnderwear = new List<BoothUnderwear.Source>();
            var outfitUnderwear = new List<BoothUnderwear.Source>();
            // Every outfit model read, for its pieces (BoothPieces); an accessory for anyone has no character.
            var outfitPieces = new List<BoothUnderwear.Source>();
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
                bodies[source.Id] = prefab;
                bodyUnderwear.Add(new BoothUnderwear.Source(source.Id, prefab, prefab, source.Id));
                Sway(sway, source.Id, Root + source.Path, prefab, character.Humanoid, character.PartOf);
                var copy = Copy(Root + source.Path, CopyOf(source));
                if (profile != null && copy != null) profile.EditorPutSkin(source.Id, source.Name, copy, CharacterColours(source.Id, sources));
            }
            var entries = new List<OutfitLibrary.Entry>();
            foreach (var source in sources.Where(s => s.Kind == "outfit"))
            {
                string path = Root + source.Path;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool anyone = ForAnyone(source);
                Character character = null;
                if (prefab == null || (!anyone && !characters.TryGetValue(source.Skin, out character)))
                {
                    Debug.LogWarning($"[BOOTH] {source.Id}: {path} 또는 캐릭터 '{source.Skin}'가 없습니다.");
                    continue;
                }
                // Per character: the outfit made for each has bones of its own. A model made for anyone shares none with
                // any character, so its rows are the model's own (skin = the model id) and swing on whoever wears it.
                if (anyone) Sway(sway, source.Id, path, prefab, NoBones, _ => null);
                else
                {
                    // The bones it shares with the character follow hers, which sway already. One that only shares a
                    // name stands elsewhere and is the outfit's own (Character.Namesakes): Chocolate rice's mirrored side
                    // locks hang from "R_Side" or "L_Side", the names of Mayo's other locks, and every bone of theirs
                    // carried one of her names, so they got no rows and hung still (2026-11-12). Such a bone sways on the
                    // outfit's row, written under the name the game gives it once worn ("R_Side [mayo-side-hair-1]").
                    var namesakes = character.Namesakes(prefab);
                    var shared = new HashSet<string>(character.Bones, StringComparer.Ordinal);
                    shared.ExceptWith(namesakes);
                    Sway(sway, source.Id + OutfitLook.Separator + source.Skin, path, prefab, shared, character.PartOf);
                    if (namesakes.Count > 0 && sway.TryGetValue(source.Id + OutfitLook.Separator + source.Skin, out var rows))
                        foreach (var row in rows)
                            row["roots"] = string.Join("|", row["roots"].Split('|')
                                .Select(name => namesakes.Contains(name) ? OutfitSkeleton.Mark(name, source.Id) : name));
                }
                var copy = Copy(path, CopyOf(source));
                if (copy == null) continue;
                // Underwear an outfit brings goes in the underwear slots; an accessory for anyone brings none.
                if (!anyone) outfitUnderwear.Add(new BoothUnderwear.Source(source.Id, prefab, bodies[source.Skin], source.Skin));
                outfitPieces.Add(anyone ? new BoothUnderwear.Source(source.Id, prefab, null) : new BoothUnderwear.Source(source.Id, prefab, bodies[source.Skin], source.Skin));
                entries.Add(new OutfitLibrary.Entry(source.Id, source.Skin, copy, OutfitColours(prefab, ColourFiles(source, path))));
            }
            library.EditorReplace(entry => entry.Model == null || IsOurs(AssetDatabase.GetAssetPath(entry.Model)), entries);
            // Saved before the table is written: a write that fails must not take the characters and outfits with it.
            if (profile != null) EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            // The sway version is noted only once the rows are in the file. While they wait for it, it stays unnoted:
            // the next script load after they land runs this again and finds them there (Pending), and if the editor
            // quit before they ever landed, that run writes them instead of their being lost.
            if (ReplaceClothRows(sway)) EditorPrefs.SetInt(SwayVersionKey, SwayWritten);
            else EditorPrefs.DeleteKey(SwayVersionKey);
            // Underwear no row lists yet goes in the underwear slots (CharacterParts.csv, Outfits.csv), then each other
            // new piece of an outfit in its own slot (Outfits.csv, its name in Texts.csv, OutfitFits.csv). A write that
            // waits for its file lands by itself; running the import again finds anything still missing.
            BoothUnderwear.Write(bodyUnderwear, outfitUnderwear);
            BoothPieces.Write(outfitPieces);
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
        ISet<string> shared, Func<Transform, string> partOf)
    {
        var read = VrcPrefab.Read(path);
        if (read.PhysBones.Count == 0) return;
        sway[owner] = VrcSway.Rows(read, prefab, owner, shared, partOf);
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

    // A colour file of an outfit row: a prefab or a material, and the dye style it is filed under ("" in the folder itself).
    private readonly struct ColourFile
    {
        public readonly string Path, Style;

        public ColourFile(string path, string style)
        {
            Path = path;
            Style = style;
        }

        public bool IsMaterial => Path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase);
    }

    // The colour files under an outfit row's colours cell, folder by folder (|). A folder of colour prefabs gives its
    // prefabs (Ene Collection, LookVook: each a whole outfit in one colour). A folder with none gives its materials, its
    // own and then each sub folder's: MARIYURI sells a hair's colours as loose materials, one per colour, in packs bought
    // apart from the hair (ボサボサロングヘア, 2026-11-13: 13 plain colours, and Gradation, Half and InOut folders of
    // two-colour dyes), and the default colour of each avatar's hair in that avatar's package (Ichigo's Pink 3), so its
    // row names two folders.
    private static List<ColourFile> ColourFiles(Source source, string basePrefab)
    {
        var files = new List<ColourFile>();
        foreach (string cell in (source.Colours ?? "").Split('|'))
        {
            string folder = Root + cell.Trim().Trim('/');
            if (cell.Trim().Length == 0) continue;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"[BOOTH] {source.Id}@{source.Skin}: 색 폴더 {folder}가 없습니다.");
                continue;
            }
            var prefabs = Directory.GetFiles(folder, "*.prefab").Select(p => p.Replace('\\', '/'))
                .Where(p => !string.Equals(p, basePrefab, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal).ToList();
            if (prefabs.Count > 0)
            {
                files.AddRange(prefabs.Select(p => new ColourFile(p, "")));
                continue;
            }
            foreach (string material in Directory.GetFiles(folder, "*.mat", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/'))
                         .OrderBy(p => p.Count(c => c == '/')).ThenBy(p => p, StringComparer.Ordinal))
            {
                string parent = Path.GetDirectoryName(material).Replace('\\', '/');
                files.Add(new ColourFile(material, string.Equals(parent, folder, StringComparison.OrdinalIgnoreCase) ? "" : Path.GetFileName(parent)));
            }
        }
        return files;
    }

    // The base prefab's colour first, then every colour prefab whose materials differ from it, renderer by renderer
    // (the copy keeps the base prefab's renderers and materials, so the swaps fit it), and every loose colour material
    // in place of the one material the model is drawn with.
    private static SkinVariant[] OutfitColours(GameObject model, List<ColourFile> colourFiles)
    {
        if (model == null || colourFiles.Count == 0) return Array.Empty<SkinVariant>();
        var original = new Dictionary<string, Material[]>(StringComparer.Ordinal);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            original[AnimationUtility.CalculateTransformPath(renderer.transform, model.transform)] = renderer.sharedMaterials;
        // A loose material is a colour of a model drawn with one material, a hair: it takes that material's place. On a
        // model of several (a hair with a ribbon of its own) nothing tells which of them it is a colour of.
        var drawn = original.Values.SelectMany(materials => materials).Where(material => material != null).Distinct().ToList();
        var only = drawn.Count == 1 ? drawn[0] : null;
        bool loose = colourFiles.Any(file => file.IsMaterial);
        if (loose && only == null)
            Debug.LogWarning($"[BOOTH] {model.name}: 재질이 {drawn.Count}개인 모델이라 낱개 재질 색 세트를 만들지 않았습니다(재질 하나로 그린 머리카락 같은 모델만 됩니다).");
        // The base colour is named after what it is: the hair's own material ("Beige 2" → 베이지 2), else the base prefab.
        string baseName = loose && only != null ? only.name : Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(model));
        var sets = new List<SkinVariant> { new SkinVariant("base", MaterialColourName(baseName, "", loose), Array.Empty<SkinVariant.Swap>()) };
        var ids = new HashSet<string>(StringComparer.Ordinal) { "base" };
        foreach (var file in colourFiles)
        {
            string name = Path.GetFileNameWithoutExtension(file.Path);
            if (file.IsMaterial)
            {
                var colour = AssetDatabase.LoadAssetAtPath<Material>(file.Path);
                // The model's own material is the base set already (Mayo's hair comes in the pack's Beige 2).
                if (colour == null || only == null || colour == only) continue;
                string stem = VrcSway.Slug(file.Style.Length > 0 ? file.Style + " " + name : name), id = stem;
                for (int n = 2; !ids.Add(id); n++) id = stem + "-" + n;
                sets.Add(new SkinVariant(id, MaterialColourName(name, file.Style, true), new[] { new SkinVariant.Swap { from = only, to = colour } }));
                continue;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(file.Path);
            if (prefab == null) continue;
            var swaps = new List<SkinVariant.Swap>();
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!original.TryGetValue(AnimationUtility.CalculateTransformPath(renderer.transform, prefab.transform), out var mine)) continue;
                var theirs = renderer.sharedMaterials;
                for (int i = 0; i < mine.Length && i < theirs.Length; i++)
                    if (mine[i] != null && theirs[i] != null && mine[i] != theirs[i] && swaps.All(s => s.from != mine[i]))
                        swaps.Add(new SkinVariant.Swap { from = mine[i], to = theirs[i] });
            }
            if (swaps.Count == 0) continue;
            ids.Add(VrcSway.Slug(name));
            sets.Add(new SkinVariant(VrcSway.Slug(name), Korean(name), swaps.ToArray()));
        }
        return sets.Count > 1 ? sets.ToArray() : Array.Empty<SkinVariant>();
    }

    // A loose material's colour name: its colour words, the words of a two-colour dye joined ("Pink Blue" → 분홍·파랑,
    // "Beige Pink 1" → 베이지·분홍 1), then its dye style (분홍·파랑 그라데이션). Prefab colours keep Korean's spacing.
    private static string MaterialColourName(string name, string style, bool loose)
    {
        string text = Korean(name);
        if (!loose) return text;
        var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var joined = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            if (i > 0) joined.Append(!words[i].Any(char.IsDigit) && !words[i - 1].Any(char.IsDigit) ? "·" : " ");
            joined.Append(words[i]);
        }
        return style.Length > 0 ? joined + " " + Korean(style) : joined.ToString();
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
    // come in at the end; every other row stays as it is. True once the file holds them; false while they wait for the
    // file (TableFile) or when it couldn't even be read.
    private static bool ReplaceClothRows(Dictionary<string, List<Dictionary<string, string>>> sway)
    {
        if (sway.Count == 0 || !File.Exists(ClothPath)) return true;
        // Built on what TableFile will put there: a write still waiting, else the file without padding.
        string text;
        try
        {
            text = TableFile.Read(ClothPath);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning($"[BOOTH] {ClothPath}를 읽지 못해 흔들림 줄을 쓰지 않았습니다({error.Message}). 그 파일을 연 프로그램을 닫으면 편집기가 스크립트를 다시 불러올 때 다시 씁니다(Tools → Characters → BOOTH 다시 가져오기로 바로 해도 됩니다).");
            return false;
        }
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var records = CsvTable.ReadRecords(text);
        if (records.Count == 0) return true;
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
        if (written == text) return !TableFile.Waiting(ClothPath);
        // Through TableFile, which writes over the file in place: Windows refused File.WriteAllText's truncating write
        // while some program had Cloth.csv mapped (Win32 IO 1224, the user's console 2026-11-12, twice, even after
        // 0.8.114's handle release and six retries). A write that still meets a lock waits and lands by itself.
        return TableFile.Write(ClothPath, written, "흔들림 줄", "Tools → Characters → BOOTH 다시 가져오기를 눌러 주세요.");
    }

    private static string Quote(string cell) =>
        cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
}
