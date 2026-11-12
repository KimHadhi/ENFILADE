using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Writes a balance table (Resources/Tables/*.csv) from an editor tool while the editor runs: BoothImport's sway rows in
/// Cloth.csv and the Grip Tuner's GunGrips.csv. Build the new text on <see cref="Read"/> and hand it to
/// <see cref="Write"/>. What lands is what File.WriteAllText wrote before (the text, UTF-8 with BOM); only how it gets
/// into the file changed.
/// </summary>
/// <remarks>
/// Why: the user's console, 2026-11-12, twice, after 0.8.114 had already released Unity's cached handles and tried six
/// times over three seconds: "Win32 IO returned 1224" on Cloth.csv. That is ERROR_USER_MAPPED_FILE: some process had a
/// view of the file mapped into memory (which one couldn't be told; Unity, virus scanners, git and search tools all map
/// files they read), and Windows refuses to shrink a mapped file. File.WriteAllText opens with CREATE_ALWAYS, which first
/// cuts the file to nothing, so every write was refused. Writing over the bytes and growing the file both work while it
/// is mapped, so the file is overwritten where it is; a shorter text is padded to the old length with blank lines, which
/// every reader skips (CsvTable.ReadRecords makes no record of them, Read drops them), and they are cut off when Windows
/// allows it. A write that still can't get at the file (a program opened it without sharing writes, as Excel does) waits
/// and is tried again by itself, and is reported once, only when it has kept failing for a minute.
/// </remarks>
internal static class TableFile
{
    // Tried again after half a second, then twice as long each time up to half a minute, and whenever the editor gets
    // the focus back (the user has likely just closed the program that held the file).
    private const double FirstDelay = .5, LongestDelay = 30;
    // A write still failing after this long (s) is reported, once.
    private const double ReportAfter = 60;
    // Waiting writes outlive script reloads (SessionState lasts as long as the editor runs).
    private const string SavedKey = "ENFILADE.TableFile.Waiting";
    private const int MoreData = 234; // ERROR_MORE_DATA

    [Serializable]
    private sealed class Job
    {
        public string path, text, what, redo, problem;
        // The file as it was when the write began waiting (size and last write), empty when that couldn't be read.
        public string stamp;
        public double since, next, delay;
        public bool reported;
    }

    [Serializable]
    private sealed class Saved
    {
        public List<Job> jobs = new List<Job>();
    }

    private static readonly Dictionary<string, Job> waiting = new Dictionary<string, Job>(StringComparer.OrdinalIgnoreCase);
    private static bool hooked;

    private static double Now => EditorApplication.timeSinceStartup;

    /// <summary>
    /// The table's text for a change to build on: what a waiting write will put there, else the file without the blank
    /// lines a write that couldn't shrink it left at its end; "" when there is no file. Throws IOException only when a
    /// program keeps even readers out.
    /// </summary>
    internal static string Read(string path)
    {
        path = Normal(path);
        if (waiting.TryGetValue(path, out var job))
        {
            if (!Changed(job)) return job.text;
            Drop(job);
        }
        if (!File.Exists(path)) return "";
        // Sharing everything: a program that has the file open for writing (and shares it) must not stop the read, as
        // File.ReadAllText's read-only sharing would.
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(file, Encoding.UTF8, true))
            return WithoutFiller(reader.ReadToEnd());
    }

    /// <summary>Whether a write to the table waits for the file.</summary>
    internal static bool Waiting(string path) => waiting.ContainsKey(Normal(path));

    /// <summary>
    /// Writes <paramref name="text"/>, the whole table built on <see cref="Read"/>, to the table at
    /// <paramref name="path"/> (an asset path), then imports it and reloads GameTables. True when it is on disk now;
    /// false when the file is held and the write waits: it lands by itself as soon as it can (Read gives this text
    /// meanwhile), or is given up with a warning if someone else writes the file first.
    /// </summary>
    /// <param name="what">What the text brings, for the messages ("흔들림 줄").</param>
    /// <param name="redo">The sentence telling the user how to do it again if the write is given up.</param>
    internal static bool Write(string path, string text, string what, string redo)
    {
        path = Normal(path);
        if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1); // the BOM comes from the encoding
        string problem = TryWrite(path, text);
        if (problem == null)
        {
            // A newer text landed over one that waited (it was built on that one through Read).
            if (waiting.Remove(path)) Save();
            Landed(path);
            return true;
        }
        if (!waiting.TryGetValue(path, out var job))
            waiting[path] = job = new Job { path = path, stamp = Stamp(path), since = Now, delay = FirstDelay };
        // Each text is the whole table, so a newer one replaces the one waiting; the wait keeps its start and its stamp.
        job.text = text;
        job.what = what;
        job.redo = redo;
        job.problem = problem;
        job.next = Now + job.delay;
        Save();
        Hook();
        return false;
    }

    // Puts the text in the file without ever making the file shorter first. Null when it is there, else why not.
    private static string TryWrite(string path, string text)
    {
        // Unity keeps handles on files it read lately; they go first (0.8.114; cheap, and still worth it).
        AssetDatabase.ReleaseCachedFileHandles();
        var encoding = new UTF8Encoding(true);
        byte[] preamble = encoding.GetPreamble(), body = encoding.GetBytes(text);
        int length = preamble.Length + body.Length;
        try
        {
            // OpenOrCreate never truncates (CREATE_ALWAYS is what was refused), and sharing everything lets the write
            // in beside any reader or writer that shares in turn.
            using (var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1))
            {
                // A shorter text goes in with blank lines up to the old length, so the file is a whole table at every
                // step, and in one write (bigger than the stream's buffer, so one WriteFile), so a refused write leaves
                // the old table whole.
                var bytes = new byte[Math.Max(length, (int)file.Length)];
                Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
                Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
                Fill(bytes, length, text.Contains("\r\n"));
                file.Write(bytes, 0, bytes.Length);
                // Then the blank lines are cut off. Windows refuses that while the file is mapped: they stay until a
                // later write can cut them.
                if (bytes.Length > length)
                {
                    try
                    {
                        file.SetLength(length);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            return null;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return error.Message;
        }
    }

    // Blank lines from 'from' to the end, in the text's own line ending as far as the room allows.
    private static void Fill(byte[] bytes, int from, bool crlf)
    {
        for (int i = from; i < bytes.Length; i++)
            bytes[i] = crlf && (i - from) % 2 == 0 && i + 1 < bytes.Length ? (byte)'\r' : (byte)'\n';
    }

    // The text without the blank lines Fill left after its last line; that line keeps its own line break. So a padded
    // file is read as the table it holds and a tool's next write doesn't carry the padding along.
    private static string WithoutFiller(string text)
    {
        int end = text.Length;
        while (end > 0 && (text[end - 1] == '\n' || text[end - 1] == '\r')) end--;
        if (end == text.Length) return text;
        return text.Substring(0, end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1));
    }

    private static void Landed(string path)
    {
        AssetDatabase.ImportAsset(path);
        GameTables.Reload();
    }

    private static void Retry(Job job)
    {
        if (Changed(job))
        {
            Drop(job);
            return;
        }
        string problem = TryWrite(job.path, job.text);
        if (problem == null)
        {
            waiting.Remove(job.path);
            Save();
            Landed(job.path);
            // Quiet unless the wait was reported: then the user learns it is over.
            if (job.reported) Debug.Log($"[Tables] {Path.GetFileName(job.path)}: 기다리던 {job.what} 쓰기를 마쳤습니다.");
            return;
        }
        job.problem = problem;
        job.delay = Math.Min(job.delay * 2, LongestDelay);
        job.next = Now + job.delay;
        if (!job.reported && Now - job.since >= ReportAfter)
        {
            job.reported = true;
            Debug.LogWarning(Report(job));
        }
        Save();
    }

    // Someone else wrote the table while this waited: the waiting text was built on the old one and would undo theirs.
    private static void Drop(Job job)
    {
        waiting.Remove(job.path);
        Save();
        Debug.LogWarning($"[Tables] {Path.GetFileName(job.path)}: 기다리는 동안 다른 곳에서 파일이 바뀌어서, 그 내용을 덮지 않으려고 {job.what} 쓰기를 그만뒀습니다. {job.redo}");
    }

    private static bool Changed(Job job)
    {
        string now = Stamp(job.path);
        return !string.IsNullOrEmpty(job.stamp) && now != null && now != job.stamp;
    }

    // The file's size and time of last write ("-" when there is no file), null when they can't be read.
    private static string Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length + "@" + info.LastWriteTimeUtc.Ticks : "-";
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return null;
        }
    }

    // What the user can do: name the program holding the file when Windows can tell, else the likely fixes.
    private static string Report(Job job)
    {
        var others = Holders(job.path, out bool self);
        string help;
        if (others != null && others.Count > 0)
            help = $"파일을 잡고 있는 프로그램: {string.Join(", ", others)}. 그 프로그램에서 파일을 닫으면 저절로 써집니다.";
        else if (self)
            help = $"이 유니티 편집기가 파일을 잡고 있습니다. 유니티를 다시 켠 뒤 {job.redo}";
        else if (ReadOnly(job.path))
            help = "파일이 읽기 전용입니다. 탐색기에서 파일 속성의 '읽기 전용'을 끄면 저절로 써집니다.";
        else
            help = "표를 열어 둔 프로그램(엑셀·메모장 등)이 있으면 닫아 주세요. 닫으면 저절로 써집니다.";
        return $"[Tables] {Path.GetFileName(job.path)}: {job.what} 쓰기가 1분째 막혀 있습니다. {help} " +
               $"유니티로 돌아올 때와 30초마다 다시 해 봅니다. (오류: {job.problem})";
    }

    private static bool ReadOnly(string path)
    {
        try
        {
            return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The programs that have the file open, other than this editor (self tells whether it is one), from Windows' Restart
    // Manager: what Explorer asks when a file is in use, a few milliseconds. Null when it can't tell. A process that only
    // maps the file without keeping it open isn't listed.
    private static List<string> Holders(string path, out bool self)
    {
        self = false;
        if (Application.platform != RuntimePlatform.WindowsEditor) return null;
        try
        {
            if (RmStartSession(out uint session, 0, new StringBuilder(64)) != 0) return null;
            try
            {
                if (RmRegisterResources(session, 1, new[] { Path.GetFullPath(path) }, 0, null, 0, null) != 0) return null;
                uint count = 0, reasons = 0;
                if (RmGetList(session, out uint needed, ref count, null, ref reasons) != MoreData || needed == 0) return null;
                var found = new ProcessInfo[needed];
                count = needed;
                if (RmGetList(session, out needed, ref count, found, ref reasons) != 0) return null;
                int me;
                using (var current = Process.GetCurrentProcess()) me = current.Id;
                var names = new List<string>();
                foreach (var info in found.Take((int)count))
                {
                    if (info.Owner.Id == me) self = true;
                    else names.Add(Name(info));
                }
                return names.Distinct().ToList();
            }
            finally
            {
                RmEndSession(session);
            }
        }
        catch (Exception)
        {
            // No Restart Manager, or an answer this doesn't read: the report says what to do instead of who.
            return null;
        }
    }

    // "Microsoft Excel(EXCEL.exe)": the name Windows gives the program, with its file when the name doesn't say it.
    private static string Name(ProcessInfo info)
    {
        string exe = null;
        try
        {
            using (var process = Process.GetProcessById(info.Owner.Id)) exe = process.ProcessName;
        }
        catch (Exception)
        {
            // Gone already, or not ours to look at: the Restart Manager's name will do.
        }
        string app = (info.AppName ?? "").Trim();
        if (app.Length == 0) return exe != null ? exe + ".exe" : "프로세스 " + info.Owner.Id;
        return exe == null || app.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0 ? app : $"{app}({exe}.exe)";
    }

    private static void Hook()
    {
        if (hooked) return;
        hooked = true;
        EditorApplication.update += Tick;
        EditorApplication.focusChanged += Focused;
        EditorApplication.wantsToQuit += MayQuit;
    }

    private static void Unhook()
    {
        if (!hooked) return;
        hooked = false;
        EditorApplication.update -= Tick;
        EditorApplication.focusChanged -= Focused;
        EditorApplication.wantsToQuit -= MayQuit;
    }

    private static void Tick()
    {
        double now = Now;
        foreach (var job in waiting.Values.Where(j => now >= j.next).ToList()) Retry(job);
        if (waiting.Count == 0) Unhook();
    }

    private static void Focused(bool focused)
    {
        if (!focused) return;
        foreach (var job in waiting.Values.ToList()) Retry(job);
        if (waiting.Count == 0) Unhook();
    }

    // The editor is about to quit: one more try, then a question before a write is left undone.
    private static bool MayQuit()
    {
        foreach (var job in waiting.Values.ToList()) Retry(job);
        if (waiting.Count == 0)
        {
            Unhook();
            return true;
        }
        if (Application.isBatchMode) return true;
        string list = string.Join("\n", waiting.Values.Select(j => $"· {Path.GetFileName(j.path)}: {j.what}. {j.redo}"));
        return EditorUtility.DisplayDialog("표를 아직 다 쓰지 못했습니다",
            "다른 프로그램이 파일을 잡고 있어서 아래 내용이 아직 표에 쓰이길 기다립니다. 지금 끄면 쓰이지 않습니다.\n\n" + list,
            "그래도 끄기", "돌아가기");
    }

    private static void Save()
    {
        if (waiting.Count == 0) SessionState.EraseString(SavedKey);
        else SessionState.SetString(SavedKey, JsonUtility.ToJson(new Saved { jobs = waiting.Values.ToList() }));
    }

    // After a script reload: the writes that were waiting carry on.
    [InitializeOnLoadMethod]
    private static void Restore()
    {
        string saved = SessionState.GetString(SavedKey, "");
        if (saved.Length == 0) return;
        try
        {
            foreach (var job in JsonUtility.FromJson<Saved>(saved)?.jobs ?? new List<Job>())
                if (!string.IsNullOrEmpty(job.path) && job.text != null) waiting[job.path] = job;
        }
        catch (ArgumentException)
        {
            SessionState.EraseString(SavedKey);
        }
        if (waiting.Count > 0) Hook();
    }

    private static string Normal(string path) => path.Replace('\\', '/');

#pragma warning disable 0169, 0649 // filled in by the Restart Manager, not by code
    // RM_UNIQUE_PROCESS: the start time is a FILETIME, two DWORDs (a long would move it to an 8-byte boundary).
    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess
    {
        public int Id;
        public uint StartLow, StartHigh;
    }

    // RM_PROCESS_INFO: the names are WCHAR arrays of CCH_RM_MAX_APP_NAME + 1 and CCH_RM_MAX_SVC_NAME + 1.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        public UniqueProcess Owner;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceName;
        public int AppType;
        public uint AppStatus;
        public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
#pragma warning restore 0169, 0649

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, int flags, StringBuilder key);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint fileCount, string[] files, uint processCount,
        UniqueProcess[] processes, uint serviceCount, string[] services);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[] found, ref uint reasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);
}
