using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dev-text: file (log lines and console answers for whoever makes an avatar mod; nothing here is shown to players)

/// <summary>
/// Characters players bring themselves: any humanoid made in Unity (a VRChat avatar most of all), packed by the
/// exporter (ModAvatarExporter) into a mod's Avatars folder as a bundle and a file saying what it is (ModAvatarFile).
/// <code>
/// Mods/
///   MyAvatar/
///     Avatars/
///       myavatar.bundle          the prefab, its meshes and textures
///       myavatar.avatar.json     its materials, PhysBones and face, as its maker had them
///     Tables/…                   optional: rows of the game's tables for it, which win over what is worked out here
/// </code>
/// Each character in those files is offered beside the game's own (PlayerBodyProfile.Skins), under its id. Nothing is
/// loaded until one is asked for: then its bundle is opened, its materials are made as near to its maker's as the game
/// can draw (ModMaterials), and its PhysBones become rows of Cloth.csv the way the editor's import makes them for the
/// game's own characters (VrcSway, GameTables.Lay), so its hair, tail and skirt sway as tuned. What its avatar
/// descriptor says of the face (the blink and the mouth shapes) is laid under FaceKeys.csv as rows of its own; the
/// rest of the game already measures any humanoid as it comes (the head for first person, hands, feet, the fit of
/// clothes, its face's moods by the usual shape key names).
/// A PC that hasn't the mod shows the default character for a friend who wears it, as for any id it doesn't know.
/// </summary>
public static class ModAvatars
{
    /// <summary>One character a mod offers.</summary>
    public sealed class Character
    {
        public string Id, Name, Mod;
        internal string Path;
        internal ModAvatarFile File;
        internal ModAvatarFile.Character Data;
        internal GameObject Loaded;
        internal bool Tried;

        /// <summary>Its model, loaded from the mod's bundle the first time it is asked for; null when it can't be.</summary>
        public GameObject Model => ModelOf(this);
    }

    private const string SwayTable = "Cloth", FaceTable = "FaceKeys";
    // VRChat's visemes (ModAvatarFile.Face.visemes) that are the game's five mouth shapes: aa, ih, ou, E, oh.
    private static readonly (int viseme, string mood)[] Mouths = { (10, "talk.a"), (12, "talk.i"), (14, "talk.u"), (11, "talk.e"), (13, "talk.o") };

    private static List<Character> characters;
    private static readonly Dictionary<string, List<Character>> byMod = new Dictionary<string, List<Character>>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, AssetBundle> bundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> told = new HashSet<string>();

    /// <summary>Goes up whenever the characters are read again: whoever keeps a list made from them makes it again.</summary>
    public static int Generation { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        // The editor keeps bundles open from one play to the next, and a bundle can't be opened twice.
        foreach (var bundle in bundles.Values)
            if (bundle != null) bundle.Unload(true);
        bundles.Clear();
        told.Clear();
        Forget();
    }

    /// <summary>The mods' folders are read again (GameMods.Rescan): the characters are found afresh. Bundles already open stay open.</summary>
    internal static void Forget()
    {
        characters = null;
        byMod.Clear();
        Generation++;
    }

    /// <summary>Every character the loaded mods offer, in load order; a later mod's takes the place of an earlier one's of the same id.</summary>
    public static IReadOnlyList<Character> Characters => characters ??= Scan();

    private static List<Character> Scan()
    {
        var found = new List<Character>();
        byMod.Clear();
        foreach (var mod in GameMods.Loaded)
        {
            var own = new List<Character>();
            byMod[mod.Name] = own;
            foreach (string path in mod.Avatars)
            {
                string text = GameMods.Read(path);
                if (text == null) continue;
                ModAvatarFile file;
                try
                {
                    file = JsonUtility.FromJson<ModAvatarFile>(text);
                }
                catch (ArgumentException error)
                {
                    Tell($"{mod.Name}: {System.IO.Path.GetFileName(path)}을 읽지 못했습니다(JSON이 깨졌습니다: {error.Message}).");
                    continue;
                }
                if (file == null || file.characters == null) continue;
                if (file.version > ModAvatarFile.Latest)
                    Tell($"{mod.Name}: {System.IO.Path.GetFileName(path)}은 이 게임보다 새 내보내기 도구로 만든 파일입니다(v{file.version}). 읽을 수 있는 데까지 읽습니다.");
                foreach (var data in file.characters)
                {
                    if (data == null || string.IsNullOrEmpty(data.id) || string.IsNullOrEmpty(data.prefab)) continue;
                    if (!ValidId(data.id))
                    {
                        Tell($"{mod.Name}: 캐릭터 id '{data.id}'에는 영문 소문자·숫자·-·_만 쓸 수 있습니다. 건너뜁니다.");
                        continue;
                    }
                    var character = new Character
                    {
                        Id = data.id, Name = string.IsNullOrEmpty(data.name) ? data.id : data.name, Mod = mod.Name, Path = path, File = file, Data = data
                    };
                    found.RemoveAll(other => string.Equals(other.Id, data.id, StringComparison.Ordinal));
                    found.Add(character);
                    own.Add(character);
                }
            }
        }
        if (found.Count > 0)
        {
            var names = new List<string>();
            foreach (var character in found) names.Add(character.Id);
            Debug.Log("[Mods] 모드 캐릭터 " + found.Count + "명: " + string.Join(", ", names));
        }
        return found;
    }

    // An id travels in saved settings, in what other PCs are sent and in table rows, each with separators of its own.
    private static bool ValidId(string id)
    {
        foreach (char c in id)
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
        return true;
    }

    private static GameObject ModelOf(Character character)
    {
        if (character.Loaded != null || character.Tried) return character.Loaded;
        character.Tried = true;
        try
        {
            string folder = System.IO.Path.GetDirectoryName(character.Path) ?? "";
            var bundle = Open(System.IO.Path.Combine(folder, character.File.bundle ?? ""), character);
            if (bundle == null) return null;
            var prefab = bundle.LoadAsset<GameObject>(character.Data.prefab);
            if (prefab == null)
            {
                Tell($"{character.Mod}: 번들 {character.File.bundle}에 '{character.Data.prefab}'가 없습니다. 다시 내보내 주세요.");
                return null;
            }
            ModMaterials.Apply(prefab, character.File, bundle, character.Id);
            var animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                Tell($"{character.Mod}: '{character.Id}'에 휴머노이드 Avatar가 없습니다. 모델의 Rig를 Humanoid로 하고 다시 내보내야 움직입니다.");
            else LaySway(character, prefab, animator.avatar);
            character.Loaded = prefab;
        }
        catch (Exception error)
        {
            Tell($"{character.Mod}: '{character.Id}'를 불러오다 멈췄습니다({error.GetType().Name}: {error.Message}).");
        }
        return character.Loaded;
    }

    private static AssetBundle Open(string path, Character character)
    {
        string full = System.IO.Path.GetFullPath(path);
        if (bundles.TryGetValue(full, out var open) && open != null) return open;
        if (!File.Exists(full))
        {
            Tell($"{character.Mod}: 번들 파일이 없습니다: {full}");
            return null;
        }
        var bundle = AssetBundle.LoadFromFile(full);
        if (bundle == null)
        {
            Tell($"{character.Mod}: 번들 {System.IO.Path.GetFileName(full)}을 열지 못했습니다(파일이 깨졌거나, 같은 이름의 번들이 다른 모드에 또 있습니다).");
            return null;
        }
        bundles[full] = bundle;
        return bundle;
    }

    // ---- Sway: its PhysBones as rows of Cloth.csv ----

    private static void LaySway(Character character, GameObject prefab, Avatar avatar)
    {
        var bones = Bones(prefab, character.Data.sway);
        if (bones.Count == 0) return;
        // The chains may not start at a bone the body itself moves, and a collider counts as the body part it stands on.
        var humanoid = new HashSet<string>(StringComparer.Ordinal);
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var human in avatar.humanDescription.human)
        {
            humanoid.Add(human.boneName);
            if (VrcSway.BodyPart(human.humanName) is string part) parts[human.boneName] = part;
        }
        string PartOf(Transform bone)
        {
            for (var at = bone; at != null; at = at.parent)
                if (parts.TryGetValue(at.name, out var found)) return found;
            return null;
        }
        var rows = VrcSway.Rows(bones, prefab, character.Id, humanoid, PartOf);
        if (rows.Count > 0) GameTables.Lay(SwayTable, "모드 캐릭터 " + character.Id + "의 흔들림", Csv(rows));
    }

    /// <summary>
    /// Rows as a table's text, to lay over Cloth.csv (GameTables.Lay): the columns are whatever the rows fill, id first.
    /// Two rows that came out with one id (two roots of one name) get a number after it.
    /// </summary>
    private static string Csv(List<Dictionary<string, string>> all)
    {
        var columns = new List<string> { IdColumn };
        foreach (var row in all)
            foreach (string column in row.Keys)
                if (!columns.Contains(column)) columns.Add(column);
        var text = new StringBuilder(string.Join(",", columns)).Append('\n');
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in all)
        {
            string named = row.TryGetValue(IdColumn, out var given) ? given ?? "" : "", id = named;
            for (int n = 2; !ids.Add(id); n++) id = named + "-" + n;
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) text.Append(',');
                string cell = i == 0 ? id : row.TryGetValue(columns[i], out var value) ? value : "";
                text.Append(cell.IndexOfAny(Quoted) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell);
            }
            text.Append('\n');
        }
        return text.ToString();
    }

    private const string IdColumn = "id";
    private static readonly char[] Quoted = { ',', '"', '\n', '\r' };

    /// <summary>A file's PhysBones on a loaded model: each bone found by its path from the model's root.</summary>
    public static List<VrcPhysBone> Bones(GameObject root, ModAvatarFile.PhysBone[] sway)
    {
        var found = new List<VrcPhysBone>();
        if (root == null || sway == null) return found;
        foreach (var data in sway)
        {
            if (data == null) continue;
            var at = At(root.transform, data.root);
            if (at == null) continue;
            var bone = new VrcPhysBone
            {
                Root = at, Endpoint = data.endpoint, Integration = data.integration, LimitType = data.limitType, Pull = data.pull, Spring = data.spring,
                Stiffness = data.stiffness, Gravity = data.gravity, GravityFalloff = data.gravityFalloff, Immobile = data.immobile, Radius = data.radius,
                MaxAngleX = data.maxAngleX, MaxAngleZ = data.maxAngleZ, Collides = data.collides,
                PullCurve = data.pullCurve?.ToCurve(), SpringCurve = data.springCurve?.ToCurve(), StiffnessCurve = data.stiffnessCurve?.ToCurve(),
                GravityCurve = data.gravityCurve?.ToCurve(), ImmobileCurve = data.immobileCurve?.ToCurve(), RadiusCurve = data.radiusCurve?.ToCurve(),
                MaxAngleXCurve = data.maxAngleXCurve?.ToCurve(), MaxAngleZCurve = data.maxAngleZCurve?.ToCurve()
            };
            foreach (string path in data.ignore ?? Array.Empty<string>())
                if (At(root.transform, path) is Transform ignored) bone.Ignore.Add(ignored);
            foreach (string path in data.colliders ?? Array.Empty<string>())
                if (At(root.transform, path) is Transform collider) bone.Colliders.Add(collider);
            found.Add(bone);
        }
        return found;
    }

    private static Transform At(Transform root, string path) => string.IsNullOrEmpty(path) ? root : root.Find(path);

    // ---- Rows worked out from the files alone, laid under each mod's own tables ----

    /// <summary>
    /// The rows a mod's avatars give a table by what their files say, before the mod's own file of that table (which
    /// wins): for FaceKeys, each character's blink and mouth shapes as its avatar descriptor names them.
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, string>> TableTexts(GameMods.Mod mod, string table)
    {
        if (!string.Equals(table, FaceTable, StringComparison.OrdinalIgnoreCase)) yield break;
        _ = Characters;
        if (!byMod.TryGetValue(mod.Name, out var own) || own.Count == 0) yield break;
        var text = new StringBuilder();
        foreach (var character in own)
        {
            var face = character.Data.face;
            if (face == null) continue;
            Row(text, character.Id, "blink", face.blink);
            foreach (var (viseme, mood) in Mouths)
                Row(text, character.Id, mood, face.visemes != null && viseme < face.visemes.Length ? face.visemes[viseme] : "");
        }
        if (text.Length > 0) yield return new KeyValuePair<string, string>(mod.Name + "의 캐릭터", "id,skin,mood,keys\n" + text);
    }

    // One FaceKeys row: the key at full. A name the cell's own marks would cut ( = | / , " ) is left to the usual names.
    private static void Row(StringBuilder text, string skin, string mood, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(KeyMarks) >= 0) return;
        text.Append(skin).Append('.').Append(mood).Append(',').Append(skin).Append(',').Append(mood).Append(',').Append(key).Append("=100\n");
    }

    private static readonly char[] KeyMarks = { '=', '|', '/', ',', '"', '@', '\n', '\r' };

    /// <summary>The console's /mods: the characters the mods offer, each with whether it is loaded.</summary>
    internal static string Summary()
    {
        var all = Characters;
        if (all.Count == 0) return "";
        var text = new StringBuilder("모드 캐릭터 " + all.Count + "명:");
        foreach (var character in all)
            text.Append("\n· ").Append(character.Id).Append(" (").Append(character.Name).Append(", ").Append(character.Mod).Append(")")
                .Append(character.Loaded != null ? " — 불러옴" : character.Tried ? " — 불러오지 못함(콘솔 경고 참고)" : "");
        return text.ToString();
    }

    private static void Tell(string message)
    {
        if (told.Add(message)) Debug.LogWarning("[Mods] " + message);
    }
}
