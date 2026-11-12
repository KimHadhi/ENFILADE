#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

/// <summary>
/// ENFILADE → 글꼴: the font setup's check and upkeep (MaintenanceGuide, 글꼴).
/// 글꼴 검사 reads every table, every prefab and scene under Assets/_Project, the scripts and the font assets, and lists
/// what would draw a box or warn (a character no face of the chain has), what bypasses the roles (a text with no
/// TextRole, a script with a font field of its own), what drifted (a TextRole text in another face, TMP's default font
/// not the Body role's), and what would churn git or run out of room (glyphs saved in a font file, multi-atlas off).
/// 역할 맞추기 marks a prefab text that has no TextRole with the role its face and material already are (when one role
/// fits), gives every TextRole text in the prefabs and the open scenes its role's face and material, and makes TMP
/// Settings' default font the Body role's face: press it after changing a face or a preset in Fonts.csv, or after
/// adding texts to a prefab. Nothing here runs by itself; the check only reads.
/// </summary>
public static class FontTools
{
    private const string ProjectRoot = "Assets/_Project";
    private const string TablesFolder = TablePaths.Folder;
    private const string ScriptsFolder = "Assets/_Project/Scripts";
    private const string RoleScriptsFolder = "Assets/_Project/Scripts/UI/Fonts";

    [MenuItem("ENFILADE/글꼴/글꼴 검사")]
    public static void Check() => FontCheckWindow.Open(BuildReport());

    [MenuItem("ENFILADE/글꼴/역할 맞추기")]
    public static void SyncRoles()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[Fonts] Play를 멈춘 뒤 역할 맞추기를 누르세요.");
            return;
        }
        GameTables.Reload();
        int changed = 0, files = 0;
        var body = FontRoles.Font(FontRoles.Body);
        if (body != null && TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != body)
        {
            TMP_Settings.defaultFontAsset = body;
            EditorUtility.SetDirty(TMP_Settings.instance);
            AssetDatabase.SaveAssetIfDirty(TMP_Settings.instance);
            changed++;
        }
        string ugui = ScriptGuid(typeof(TextMeshProUGUI)), world = ScriptGuid(typeof(TextMeshPro));
        foreach (string path in Files("*.prefab"))
        {
            string file = File.ReadAllText(path);
            if ((ugui == null || !file.Contains(ugui)) && (world == null || !file.Contains(world))) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int before = changed;
                // A text with no mark takes the role its face and material already are, if exactly one fits.
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.GetComponent<TextRole>() != null) continue;
                    string guess = GuessRole(text, path);
                    if (guess == null) continue;
                    text.gameObject.AddComponent<TextRole>().role = guess;
                    changed++;
                }
                foreach (var role in root.GetComponentsInChildren<TextRole>(true))
                    if (ApplyRole(role, false)) changed++;
                if (changed > before) { PrefabUtility.SaveAsPrefabAsset(root, path); files++; }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (var role in Object.FindObjectsByType<TextRole>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (ApplyRole(role, true)) { changed++; EditorSceneManager.MarkSceneDirty(role.gameObject.scene); }
        Debug.Log($"[Fonts] 역할 맞추기: {changed}곳을 바꿨습니다(프리팹 {files}개). 열린 씬에서 바뀐 것은 씬을 저장해야 남습니다.");
    }

    // The role a text already draws as: same face, and the role's preset or the face's own material. Two roles on one
    // face (HudText and Chat) part by Fonts.csv's prefabs hint; with no hint to tell them apart there is no guess.
    private static string GuessRole(TMP_Text text, string path)
    {
        var fits = FontRoles.Roles.Where(role => FontRoles.Font(role) == text.font && FontRoles.MaterialOf(role) == text.fontSharedMaterial).ToList();
        if (fits.Count <= 1) return fits.FirstOrDefault();
        var hinted = fits.Where(role => !string.IsNullOrEmpty(FontRoles.PrefabHint(role)) && path.Contains(FontRoles.PrefabHint(role))).ToList();
        if (hinted.Count == 1) return hinted[0];
        var plain = fits.Where(role => string.IsNullOrEmpty(FontRoles.PrefabHint(role))).ToList();
        return plain.Count == 1 ? plain[0] : plain.Contains(FontRoles.Body) ? FontRoles.Body : null;
    }

    // The role's face and material onto its text. Returns whether anything changed.
    private static bool ApplyRole(TextRole role, bool undo)
    {
        var text = role != null ? role.GetComponent<TMP_Text>() : null;
        if (text == null || string.IsNullOrEmpty(role.role)) return false;
        var font = FontRoles.Font(role.role);
        var material = FontRoles.MaterialOf(role.role);
        if (font == null) return false;
        if (text.font == font && (material == null || text.fontSharedMaterial == material)) return false;
        if (undo) Undo.RecordObject(text, "역할 맞추기");
        text.font = font;
        if (material != null) text.fontSharedMaterial = material;
        EditorUtility.SetDirty(text);
        return true;
    }

    // ---- Report -------------------------------------------------------------------------------------------------

    private sealed class Report
    {
        public readonly List<string> Problems = new List<string>(), Notes = new List<string>();
        public readonly StringBuilder Body = new StringBuilder();
        public void Problem(string line) => Problems.Add(line);
        public void Note(string line) => Notes.Add(line);
        public void Section(string title) { Flush(); Body.AppendLine().AppendLine("■ " + title); }
        public void Flush()
        {
            foreach (string line in Problems) Body.AppendLine("  [문제] " + line);
            foreach (string line in Notes) Body.AppendLine("  [참고] " + line);
            problemCount += Problems.Count; Problems.Clear(); Notes.Clear();
        }
        public int problemCount;
    }

    /// <summary>The whole check as text (Korean, for the user).</summary>
    internal static string BuildReport()
    {
        GameTables.Reload();
        var report = new Report();
        var faces = FontFaces.Instance;
        var texts = ScanTexts();

        report.Section("역할 (Data/Resources/Tables/Core/Fonts.csv)");
        var roleFaces = new Dictionary<string, TMP_FontAsset>();
        var roles = FontRoles.Roles.ToList();
        if (faces == null) report.Problem($"Resources/{FontFaces.ResourcePath}.asset이 없습니다.");
        if (roles.Count == 0) report.Problem("Fonts.csv에 역할이 하나도 없습니다.");
        foreach (string role in roles)
        {
            string key = FontRoles.FaceKey(role), preset = FontRoles.PresetKey(role);
            var font = faces != null ? faces.Font(key) : null;
            if (font == null) { report.Problem($"{role}: face '{key}'가 FontFaces에 없습니다."); continue; }
            roleFaces[role] = font;
            string line = $"{role} = {key} ({font.name})";
            if (!string.IsNullOrEmpty(preset))
            {
                var material = faces.PresetMaterial(preset);
                if (material == null) report.Problem($"{role}: 재질 프리셋 '{preset}'이 FontFaces에 없습니다.");
                else if (material.GetTexture(ShaderUtilities.ID_MainTex) != font.atlasTexture)
                    report.Problem($"{role}: 프리셋 '{preset}'({material.name})이 {font.name}의 아틀라스를 쓰지 않습니다.");
                line += $" + {preset}";
            }
            report.Note(line + $", 미리 그리기 {FontRoles.WarmSet(role)}");
        }

        report.Section("TMP 설정 (ThirdParty/TextMeshPro/Resources/TMP Settings.asset)");
        var bodyFace = roleFaces.TryGetValue(FontRoles.Body, out var b) ? b : null;
        var defaultFont = TMP_Settings.defaultFontAsset;
        if (bodyFace != null && defaultFont != bodyFace)
            report.Problem($"기본 글꼴이 {Name(defaultFont)}인데 Body 역할은 {bodyFace.name}입니다. ENFILADE → 글꼴 → 역할 맞추기를 누르세요.");
        var fallbacks = TMP_Settings.fallbackFontAssets ?? new List<TMP_FontAsset>();
        if (!fallbacks.Any(f => f != null && f.fontWeightTable != null && f.fontWeightTable.Count(p => p.regularTypeface != null) >= 9))
            report.Problem("대체 글꼴에 굵기 표 글꼴(PRETENDARD Weights SDF)이 없습니다. 굵은 글자가 넘칠 때 '…'를 못 찾고 경고합니다.");
        report.Note("기본 글꼴: " + Name(defaultFont));
        report.Note("대체 글꼴: " + string.Join(" → ", fallbacks.Select(Name)) + " → (게임 중) 운영체제 글꼴");

        report.Section("글꼴 파일");
        var chain = Chain(roleFaces.Values, defaultFont, fallbacks);
        var used = new HashSet<TMP_FontAsset>(chain);
        foreach (var font in roleFaces.Values.Distinct())
        {
            if (font.atlasPopulationMode == AtlasPopulationMode.Dynamic && !font.isMultiAtlasTexturesEnabled)
                report.Problem($"{font.name}: 멀티 아틀라스가 꺼져 있어 아틀라스가 차면 글자가 대체 글꼴로 빠집니다.");
            if (font.fontWeightTable != null && font.fontWeightTable.Any(p => p.regularTypeface != null || p.italicTypeface != null))
                report.Problem($"{font.name}: 굵기 표가 채워져 있어 굵은 글씨가 합성 굵기 대신 표의 글꼴로 그려집니다.");
        }
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null || font.atlasPopulationMode != AtlasPopulationMode.Dynamic) continue;
            var clear = new SerializedObject(font).FindProperty("m_ClearDynamicDataOnBuild");
            if (clear != null && !clear.boolValue) report.Problem($"{path}: Clear Dynamic Data On Build가 꺼져 있습니다.");
            if (File.Exists(path) && !File.ReadAllText(path).Contains("m_GlyphTable: []"))
                report.Problem($"{path}: 파일에 그린 글리프가 저장돼 있어 git에 바뀐 것으로 보입니다. ENFILADE → 글꼴 → 동적 글꼴 비우고 저장.");
        }

        report.Section("글자 (모든 표와 프리팹·씬의 글자)");
        var characters = TableCharacters();
        foreach (var text in texts) foreach (char c in text.Text) if (c >= ' ' && !char.IsSurrogate(c) && !characters.ContainsKey(c)) characters[c] = text.Where;
        var wanted = characters.Keys.Select(c => (uint)c).ToList();
        var coverage = chain.ToDictionary(font => font, font => Covered(font, wanted));
        int missing = 0;
        foreach (var pair in characters.OrderBy(p => p.Key))
        {
            uint unicode = pair.Key;
            if (coverage.Values.Any(set => set.Contains(unicode))) continue;
            missing++;
            report.Problem($"{Show(unicode)} 어느 게임 글꼴에도 없습니다. 네모(□)와 경고가 납니다. 처음 나온 곳: {pair.Value}");
        }
        report.Note($"글자 {characters.Count}종을 확인했습니다. 빠진 글자 {missing}종.");
        foreach (var role in roles)
        {
            if (!roleFaces.TryGetValue(role, out var font) || FontRoles.WarmSet(role) != "texts") continue;
            var lacks = characters.Keys.Where(c => !coverage[font].Contains(c)).ToList();
            if (lacks.Count > 0)
                report.Note($"{role}({font.name})에 없는 표 글자 {lacks.Count}종은 다음 대체 글꼴로 그려집니다: {string.Join(" ", lacks.Take(12).Select(Show))}");
        }

        report.Section("글자마다 역할 (프리팹·씬의 TextMeshPro)");
        int tagged = 0;
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text.Role)) { report.Problem($"역할 없음: {text.Where} ({Name(text.Font)})"); continue; }
            tagged++;
            if (!roleFaces.TryGetValue(text.Role, out var face)) { report.Problem($"없는 역할 '{text.Role}': {text.Where}"); continue; }
            var material = FontRoles.MaterialOf(text.Role);
            if (text.Font != face || (material != null && text.Material != null && text.Material != material))
                report.Problem($"역할과 다름: {text.Where}는 {text.Role}인데 {Name(text.Font)} / {Name(text.Material)}. ENFILADE → 글꼴 → 역할 맞추기.");
        }
        report.Note($"TextMeshPro 글자 {texts.Count}개 중 {tagged}개에 역할이 있습니다. 코드로 만드는 글자는 FontRoles로 역할을 고릅니다.");

        report.Section("스크립트가 직접 고른 글꼴");
        foreach (string line in FontFields()) report.Problem(line);

        report.Section("쓰지 않는 글꼴 (빌드에 들어가지 않음)");
        var referenced = new HashSet<TMP_FontAsset>(used);
        foreach (var text in texts) if (text.Font != null) referenced.Add(text.Font);
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null && !referenced.Contains(font)) report.Note(path);
        }

        report.Section("이 PC의 운영체제 대체 글꼴 (Fonts.csv Fallback, 게임 중에 붙음)");
        var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
        foreach (string field in new[] { "families", "emoji" })
        {
            string list = GameTables.TryGetRow(FontRoles.TableName, "Fallback", out var row) && row.TryGetValue(field, out var value) ? value : "";
            foreach (string family in list.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0))
                report.Note($"{family}: {(installed.Contains(family) ? "있음" : "없음 (건너뜀)")}");
        }
        report.Flush();

        var head = new StringBuilder();
        head.AppendLine(report.problemCount == 0 ? "글꼴 검사: 문제 없음" : $"글꼴 검사: 문제 {report.problemCount}건");
        head.AppendLine($"({DateTime.Now:yyyy-MM-dd HH:mm}, 역할 {roles.Count}개, 글꼴 사슬 {chain.Count}개)");
        string result = head.ToString() + report.Body;
        Debug.Log("[Fonts] " + head.ToString().Trim().Replace("\n", " "));
        return result;
    }

    // Every font a label can end up in: the role faces, TMP's default and fallbacks, their weight tables and fallbacks.
    private static List<TMP_FontAsset> Chain(IEnumerable<TMP_FontAsset> roleFaces, TMP_FontAsset defaultFont, List<TMP_FontAsset> fallbacks)
    {
        var chain = new List<TMP_FontAsset>();
        var queue = new Queue<TMP_FontAsset>(roleFaces.Concat(new[] { defaultFont }).Concat(fallbacks));
        while (queue.Count > 0)
        {
            var font = queue.Dequeue();
            if (font == null || chain.Contains(font)) continue;
            chain.Add(font);
            if (font.fallbackFontAssetTable != null) foreach (var next in font.fallbackFontAssetTable) queue.Enqueue(next);
            if (font.fontWeightTable != null)
                foreach (var pair in font.fontWeightTable) { queue.Enqueue(pair.regularTypeface); queue.Enqueue(pair.italicTypeface); }
        }
        return chain;
    }

    // Which of these characters a font can draw: its source font file's map, or a static asset's own table.
    private static HashSet<uint> Covered(TMP_FontAsset font, List<uint> characters)
    {
        var covered = new HashSet<uint>();
        if (font == null) return covered;
        if (font.atlasPopulationMode == AtlasPopulationMode.Static)
        {
            var table = font.characterLookupTable;
            if (table != null) foreach (uint c in characters) if (table.ContainsKey(c)) covered.Add(c);
            return covered;
        }
        var source = font.sourceFontFile;
        if (source == null || FontEngine.LoadFontFace(source) != FontEngineError.Success) return covered;
        // GetGlyphIndex is internal to TextCore (TMP reaches it as a friend assembly); this is the public way in. A
        // character the file lacks has no glyph, or the .notdef glyph at index 0.
        foreach (uint c in characters)
            if (FontEngine.TryGetGlyphWithUnicodeValue(c, GlyphLoadFlags.LOAD_NO_BITMAP, out Glyph glyph) && glyph != null && glyph.index != 0)
                covered.Add(c);
        return covered;
    }

    // Every character of every table's cells, with where it first appears.
    private static Dictionary<uint, string> TableCharacters()
    {
        var found = new Dictionary<uint, string>();
        foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { TablesFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null || !path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
            var table = CsvTable.Parse(asset.text, asset.name);
            foreach (string id in table.RowIds)
            {
                if (!table.TryGetRow(id, out var row)) continue;
                foreach (var cell in row)
                    foreach (char c in cell.Value)
                        if (c >= ' ' && !char.IsSurrogate(c) && !found.ContainsKey(c)) found[c] = $"{asset.name}.csv {id}.{cell.Key}";
            }
        }
        return found;
    }

    private sealed class FoundText
    {
        public string Where, Text, Role;
        public TMP_FontAsset Font;
        public Material Material;
    }

    // TextMeshPro components of every prefab and scene under Assets/_Project, read from the files (no scene opens).
    private static List<FoundText> ScanTexts()
    {
        var found = new List<FoundText>();
        string ugui = ScriptGuid(typeof(TextMeshProUGUI)), world = ScriptGuid(typeof(TextMeshPro)), roleGuid = ScriptGuid(typeof(TextRole));
        if (ugui == null) return found;
        var reference = new Regex(@"\{fileID: (-?\d+), guid: ([0-9a-f]{32})");
        foreach (string path in Files("*.prefab").Concat(Files("*.unity")))
        {
            string file = File.ReadAllText(path);
            if (!file.Contains(ugui) && (world == null || !file.Contains(world))) continue;
            var names = new Dictionary<string, string>();
            var roles = new Dictionary<string, string>();
            var blocks = new List<string>();
            foreach (string doc in file.Split(new[] { "\n--- !u!" }, StringSplitOptions.None))
            {
                if (doc.StartsWith("1 &"))
                {
                    var name = Regex.Match(doc, @"\n  m_Name: (.*)");
                    names[doc.Substring(3, doc.IndexOf('\n') - 3).Trim()] = name.Success ? name.Groups[1].Value.Trim() : "?";
                    continue;
                }
                if (!doc.StartsWith("114 &")) continue;
                var owner = Regex.Match(doc, @"\n  m_GameObject: \{fileID: (-?\d+)\}");
                if (!owner.Success) continue;
                if (roleGuid != null && doc.Contains(roleGuid))
                {
                    var role = Regex.Match(doc, @"\n  role: (.*)");
                    roles[owner.Groups[1].Value] = role.Success ? role.Groups[1].Value.Trim() : "";
                }
                else if (doc.Contains(ugui) || (world != null && doc.Contains(world))) blocks.Add(doc);
            }
            foreach (string doc in blocks)
            {
                string owner = Regex.Match(doc, @"\n  m_GameObject: \{fileID: (-?\d+)\}").Groups[1].Value;
                var font = Regex.Match(doc, @"\n  m_fontAsset: \{fileID: -?\d+, guid: ([0-9a-f]{32})");
                var material = reference.Match(Regex.Match(doc, @"\n  m_sharedMaterial: (.*)").Groups[1].Value);
                var text = Regex.Match(doc, @"\n  m_text: (.*)");
                found.Add(new FoundText
                {
                    Where = $"{path} / {(names.TryGetValue(owner, out var n) ? n : owner)}",
                    Text = text.Success ? Unquote(text.Groups[1].Value) : "",
                    Role = roles.TryGetValue(owner, out var r) ? r : null,
                    Font = font.Success ? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(font.Groups[1].Value)) : null,
                    Material = material.Success ? LoadMaterial(material.Groups[2].Value, long.Parse(material.Groups[1].Value)) : null,
                });
            }
        }
        return found;
    }

    private static Material LoadMaterial(string guid, long localId)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
            if (asset is Material material && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out _, out long id) && id == localId)
                return material;
        return null;
    }

    // Serialized font fields in the game's scripts: a font picked by a file, not a role.
    private static IEnumerable<string> FontFields()
    {
        foreach (string path in Directory.EnumerateFiles(ScriptsFolder, "*.cs", SearchOption.AllDirectories))
        {
            string normal = path.Replace('\\', '/');
            if (normal.StartsWith(RoleScriptsFolder)) continue;
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.Contains("TMP_FontAsset ") || !line.EndsWith(";") || line.Contains("=>") || line.Contains("(") || line.StartsWith("//")) continue;
                bool serialized = line.Contains("[SerializeField") || (line.StartsWith("public ") && !line.Contains(" static ") && !line.Contains(" const "));
                if (serialized) yield return $"{normal}:{i + 1} {line} (FontRoles의 역할로 바꾸세요)";
            }
        }
    }

    private static IEnumerable<string> Files(string pattern)
        => Directory.EnumerateFiles(ProjectRoot, pattern, SearchOption.AllDirectories).Select(p => p.Replace('\\', '/'));

    private static string ScriptGuid(Type type)
    {
        foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
            if (script != null && script.GetClass() == type)
                return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
        return null;
    }

    private static string Unquote(string raw)
    {
        raw = raw.Trim();
        if (raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"') return raw;
        raw = raw.Substring(1, raw.Length - 2);
        return Regex.Replace(raw, @"\\u([0-9A-Fa-f]{4})|\\n|\\""|\\\\", m =>
            m.Value == "\\n" ? "\n" : m.Value == "\\\"" ? "\"" : m.Value == "\\\\" ? "\\" : ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
    }

    private static string Show(uint c) => $"'{(char)c}'(U+{c:X4})";

    private static string Name(Object asset) => asset != null ? asset.name : "없음";
}
#endif
