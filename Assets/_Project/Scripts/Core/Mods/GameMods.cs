using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dev-text: file (console answers and log lines for whoever makes a mod; the words players read are in Texts.csv)

/// <summary>
/// Mods: folders of plain files a player drops beside the game, read when the game starts. No Unity, no rebuild.
/// <code>
/// Mods/
///   MyMod/                 one folder is one mod; folders load in name order, so a later name wins
///     mod.txt              optional: a line about the mod, shown by the console's /mods
///     Tables/Weapons.csv   a table of the game's, by file name (sub-folders are free), laid over the game's own
///                          (CsvTable.Overlay): a row whose id the game has changes that row's filled cells,
///                          a new id adds a row
///   _Off/                  a folder starting with _ or . is not loaded
/// </code>
/// The folder stands next to the game's executable; in the editor, at the project's root (<see cref="Root"/>).
/// Every number and every word of the game is a table (GameTables, Texts), so this reaches balance, the shop, zombies,
/// waves, classes, the HUD's layout and colours, and translations. The console's /mods lists what loaded, /mods reload
/// reads the folders again, and /mods export writes the game's own tables out to copy from.
/// Everyone in a room should run the same mods: the host decides most numbers, but each PC draws from its own tables.
/// </summary>
public static class GameMods
{
    public const string FolderName = "Mods", TablesFolder = "Tables", NoteFile = "mod.txt";
    /// <summary>Where /mods export writes the game's own tables. Starts with _, so it is never loaded as a mod.</summary>
    public const string ExportFolder = "_게임 원본";

    /// <summary>One loaded mod: its folder's name, where it is, its note and its tables' files by table name.</summary>
    public sealed class Mod
    {
        public string Name, Folder, Note = "";
        public readonly Dictionary<string, string> Tables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static List<Mod> mods;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => mods = null;

    /// <summary>The mods folder: beside the executable in a build, at the project's root in the editor.</summary>
    public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", FolderName));

    /// <summary>The mods in load order. Read from the disk the first time it is asked and after <see cref="Rescan"/>.</summary>
    public static IReadOnlyList<Mod> Loaded => mods ??= Scan();

    /// <summary>Forget what was read, so the next ask reads the folders again (GameTables.Reload does this).</summary>
    public static void Rescan() => mods = null;

    /// <summary>Each loaded mod's text of this table (the mod's name, the text), in load order.</summary>
    public static IEnumerable<KeyValuePair<string, string>> TableTexts(string table)
    {
        if (string.IsNullOrEmpty(table)) yield break;
        foreach (var mod in Loaded)
        {
            if (!mod.Tables.TryGetValue(table, out string path)) continue;
            string text = Read(path);
            if (text != null) yield return new KeyValuePair<string, string>(mod.Name, text);
        }
    }

    /// <summary>The console's /mods: what is loaded; "reload" reads the folders again; "export" writes the game's own tables out.</summary>
    public static string Command(string args)
    {
        string word = (args ?? "").Trim().ToLowerInvariant();
        if (word == "reload")
        {
            GameTables.Reload();
            return "모드 폴더와 표를 다시 읽었습니다.\n" + Summary();
        }
        if (word == "export") return Export();
        return Summary() + "\n/mods reload — 다시 읽기 · /mods export — 게임의 원래 표를 " + FolderName + "/" + ExportFolder + " 폴더에 꺼내기";
    }

    private static string Summary()
    {
        var loaded = Loaded;
        if (loaded.Count == 0) return "불러온 모드가 없습니다. 모드 폴더: " + Root;
        var text = new StringBuilder("불러온 모드 " + loaded.Count + "개 (" + Root + ")");
        foreach (var mod in loaded)
        {
            text.Append("\n· ").Append(mod.Name).Append(": 표 ").Append(mod.Tables.Count).Append("개");
            if (mod.Note.Length > 0) text.Append(" — ").Append(mod.Note);
        }
        return text.ToString();
    }

    // The game's own tables as files (UTF-8 with a byte order mark, so Excel reads the Korean), for a mod to copy from.
    private static string Export()
    {
        string folder = Path.Combine(Root, ExportFolder, TablesFolder);
        int written = 0;
        try
        {
            Directory.CreateDirectory(folder);
            foreach (string table in new List<string>(GameTables.SourceNames))
            {
                string text = GameTables.SourceText(table);
                if (text == null) continue;
                if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
                File.WriteAllText(Path.Combine(folder, table + ".csv"), text, new UTF8Encoding(true));
                written++;
            }
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return "표를 꺼내지 못했습니다: " + error.Message;
        }
        return "게임의 원래 표 " + written + "개를 " + folder + " 에 꺼냈습니다. 고칠 표만 내 모드 폴더의 " + TablesFolder + " 안에 복사해서 고치세요.";
    }

    private static List<Mod> Scan()
    {
        var found = new List<Mod>();
        string root = Root;
        string[] folders;
        try
        {
            if (!Directory.Exists(root)) return found;
            folders = Directory.GetDirectories(root);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("[Mods] " + root + " 폴더를 읽지 못했습니다: " + error.Message);
            return found;
        }
        Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            string name = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(name) || name[0] == '_' || name[0] == '.') continue;
            var mod = new Mod { Name = name, Folder = folder };
            string note = Read(Path.Combine(folder, NoteFile), quiet: true);
            if (note != null) mod.Note = FirstLine(note);
            try
            {
                string tables = Path.Combine(folder, TablesFolder);
                if (Directory.Exists(tables))
                {
                    // In name order, so "the first one found" is the same file on every PC (the disk's own order is not).
                    string[] files = Directory.GetFiles(tables, "*.csv", SearchOption.AllDirectories);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string file in files)
                    {
                        string table = Path.GetFileNameWithoutExtension(file);
                        if (mod.Tables.ContainsKey(table))
                            Debug.LogWarning("[Mods] " + name + ": " + table + ".csv가 두 곳에 있습니다. 먼저 찾은 것을 씁니다.");
                        else mod.Tables[table] = file;
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Mods] " + name + "의 표를 읽지 못했습니다: " + error.Message);
            }
            found.Add(mod);
        }
        if (found.Count > 0)
        {
            var names = new List<string>();
            foreach (var mod in found) names.Add(mod.Name);
            Debug.Log("[Mods] 모드 " + found.Count + "개를 불러옵니다: " + string.Join(", ", names));
        }
        return found;
    }

    // A file's text, or null when it isn't there or can't be read. The file is opened so that a program that has it
    // open for writing (a text editor, a spreadsheet) does not shut the game out.
    private static string Read(string path, bool quiet = false)
    {
        try
        {
            if (!File.Exists(path)) return null;
            byte[] bytes;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[file.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int got = file.Read(bytes, read, bytes.Length - read);
                    if (got <= 0) break;
                    read += got;
                }
                if (read < bytes.Length) Array.Resize(ref bytes, read);
            }
            return Decode(bytes, path, quiet);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            if (!quiet) Debug.LogWarning("[Mods] " + path + " 을 읽지 못했습니다(다른 프로그램이 열어 두었나요?): " + error.Message);
            return null;
        }
    }

    // Bytes as text. The game's files are UTF-8; a spreadsheet's plain "CSV" on a Korean PC is CP949 instead, which
    // read as UTF-8 turns every Korean letter into a box. Such a file is read in the PC's own code page where that
    // is there, and either way the maker is told how to save it.
    private static string Decode(byte[] bytes, string path, bool quiet)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return (bytes[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes, 2, bytes.Length - 2);
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException) { }
        string text = null;
        try
        {
            text = Encoding.GetEncoding(949).GetString(bytes);
        }
        catch (Exception error) when (error is ArgumentException || error is NotSupportedException) { }
        if (!quiet)
            Debug.LogWarning("[Mods] " + path + " 은 UTF-8이 아닙니다. " + (text != null ? "한국어 Windows 글자(CP949)로 읽었습니다. " : "한글이 깨져 보일 수 있습니다. ")
                + "엑셀에서는 'CSV UTF-8(쉼표로 분리)'로 저장해 주세요.");
        return text ?? Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    private static string FirstLine(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim().TrimStart((char)0xFEFF);
            if (trimmed.Length > 0) return trimmed.Length > 80 ? trimmed.Substring(0, 80) : trimmed;
        }
        return "";
    }
}
