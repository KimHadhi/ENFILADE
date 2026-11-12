using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → Models → 처형 클립 가져오기 메시지: the paired executions' clips' import messages (what each clip's Animation tab
/// shows under Import Messages: rig and animation errors and warnings, retargeting warnings) written to the Console and so
/// to Editor.log, all the clips at once, so a warning's cause can be read without opening each clip (the manager,
/// 2026-11-13: the player clips warned "has animation import warnings" and Editor.log never said what of).
/// </summary>
internal static class ExecutionClipMessages
{
    private const string Folder = "Assets/_Project/Art/Animations/Executions";
    // The importer's own fields behind Import Messages; its .meta keeps them as the same names without m_, first letter
    // small, which is what is read when this Unity names them otherwise.
    private static readonly string[] Fields =
    {
        "m_RigImportErrors", "m_RigImportWarnings", "m_AnimationImportErrors", "m_AnimationImportWarnings",
        "m_AnimationRetargetingWarnings",
    };

    [MenuItem("Tools/Models/처형 클립 가져오기 메시지")]
    private static void Log()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            Debug.Log($"[처형 클립] {Folder} 폴더가 없습니다.");
            return;
        }
        var report = new StringBuilder();
        int clips = 0, warned = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter model))
                continue;
            clips++;
            string messages = Messages(model, path);
            report.Append(Path.GetFileName(path));
            if (messages.Length == 0)
            {
                report.Append(": 메시지 없음\n");
                continue;
            }
            warned++;
            report.Append(":\n").Append(messages);
        }
        string text = $"[처형 클립] 가져오기 메시지: 클립 {clips}개 중 {warned}개에 메시지가 있음\n{report}";
        if (warned > 0)
            Debug.LogWarning(text);
        else
            Debug.Log(text);
    }

    private static string Messages(ModelImporter model, string path)
    {
        var found = new StringBuilder();
        var serialized = new SerializedObject(model);
        bool named = false;
        foreach (string field in Fields)
        {
            var property = serialized.FindProperty(field);
            if (property == null || property.propertyType != SerializedPropertyType.String)
                continue;
            named = true;
            Add(found, field.Substring(2), property.stringValue);
        }
        if (!named)
            FromMeta(found, path);
        return found.ToString();
    }

    // The same messages as the clip's .meta keeps them (YAML: a key, then its quoted text over the lines below it).
    private static void FromMeta(StringBuilder found, string path)
    {
        string meta = path + ".meta";
        if (!File.Exists(meta))
            return;
        string[] lines = File.ReadAllLines(meta);
        foreach (string field in Fields)
        {
            string key = char.ToLowerInvariant(field[2]) + field.Substring(3) + ":";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (!line.StartsWith(key))
                    continue;
                int indent = lines[i].Length - line.Length;
                var value = new StringBuilder(line.Substring(key.Length).Trim());
                for (int j = i + 1; j < lines.Length; j++)
                {
                    string next = lines[j];
                    if (next.Length - next.TrimStart().Length <= indent)
                        break;
                    value.Append(' ').Append(next.Trim());
                }
                Add(found, field.Substring(2), value.ToString().Trim('"').Replace("\\n", "\n").Replace("\\t", "  "));
                break;
            }
        }
    }

    private static void Add(StringBuilder found, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        found.Append("  [").Append(name).Append("] ").Append(value.Trim().Replace("\n", "\n    ")).Append('\n');
    }
}
