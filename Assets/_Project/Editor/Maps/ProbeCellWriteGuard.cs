using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lets the Adaptive Probe Volume bake write its cell files. The rebuild of every map on 2026-11-14 at 23:00 could not
/// write the cell data of chapters 1-4, seven times ("System.IO.IOException: Win32 IO returned 1224. Path: ...
/// Ch1_Rooftops Baking Set-Default.CellData.bytes", in AdaptiveProbeVolumes.WriteBakingCells and WriteDilatedCells).
/// The baking set had already saved the new cells' layout, so the maps kept old cell data under a new layout while the
/// build said the probes were baked.
/// 1224 is ERROR_USER_MAPPED_FILE: something has the file mapped into memory, and a mapped file can't be cut short,
/// which the bake's FileMode.Create does. It was measured that night, and the holder is not anything outside the editor:
/// - Not git: a status of the whole tree takes 0.36 s, so it holds one cell file for a few ms.
/// - Not the antivirus: a cell-sized file was written and at once written over 120 times on F:, and none failed.
/// - Nothing holds the files between bakes: the Restart Manager and the editor's own memory map both showed nothing.
/// 0.9.90 released the editor's cached file handles right before each write (AssetDatabase.ReleaseCachedFileHandles,
/// which "will release file handles internally cached by Unity ... avoiding potential file sharing IO errors"). The
/// rebuild of 2026-11-15 00:50 still failed the second write, the one after the dilation, in chapters 1, 3 and 4. The
/// dilation reads the files back just before it, and Windows showed no process with the file open, so whatever holds it
/// has it mapped without an open handle.
/// A mapped file can't be cut short, but it can be renamed. So right before each write, Unity calls
/// AssetDatabase.MakeEditable on the files, which calls this processor's MakeEditable. For each cell file it:
/// 1. Lets the probe volume's own reads finish closing and releases the cached handles.
/// 2. Tries what the write will do: grows the file by a byte and cuts it back.
/// 3. If the file can't be cut, moves it aside into the project's Temp folder. That is a rename on the same disk, out of
///    the asset database, and Unity empties the folder when it closes. The bake then writes a fresh file at the same path.
/// The .meta never moves, so the file keeps its GUID: the file is away only until the bake writes it again in the same
/// call, before any refresh could see it missing. Whether this editor had the file mapped and what was done go to
/// Logs/probe-cell-writes.txt. A file that could be neither cut nor moved is named in the console and in the build's
/// window (Notes), with whoever Windows says has it open.
/// </summary>
internal sealed class ProbeCellWriteGuard : AssetModificationProcessor
{
    /// <summary>What the last bakes could not free for their writes, for the build's window (GreyboxBuilder clears and
    /// reads it around its bake). Empty when every file could be written.</summary>
    internal static readonly List<string> Notes = new List<string>();

    // Where a cell file that can't be cut short goes: the project's Temp (the same disk as Assets, so a rename, and
    // outside the asset database, so no .meta), and the record of every check.
    private const string AsideFolder = "Temp/ProbeCellsAside", LogPath = "Logs/probe-cell-writes.txt";

    // Unity calls this with the files it is about to write (AssetDatabase.MakeEditable); true lets it go on.
    private static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths)
    {
        try
        {
            if (paths == null || !Array.Exists(paths, IsCellFile)) return true;
            ClearAside();
            // The probe volume's own reads (AsyncReadManager) close without being waited for: let them run, and close any
            // the read manager keeps cached, before the asset database's cached handles go.
            Unity.Jobs.JobHandle.ScheduleBatchedJobs();
            foreach (string path in paths)
                if (IsCellFile(path))
                {
                    Unity.IO.LowLevel.Unsafe.AsyncReadManager.CloseCachedFileAsync(path).Complete();
                    Unity.IO.LowLevel.Unsafe.AsyncReadManager.CloseCachedFileAsync(Path.GetFullPath(path)).Complete();
                }
            AssetDatabase.ReleaseCachedFileHandles();
            var mapped = MappedHere();
            var record = new StringBuilder();
            foreach (string path in paths)
            {
                if (!IsCellFile(path)) continue;
                string full = Path.GetFullPath(path), name = Path.GetFileName(path);
                if (!File.Exists(full) || new FileInfo(full).Length == 0) continue;
                bool mine = IsMapped(mapped, full);
                record.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(' ').Append(name)
                    .Append(mine ? ": mapped by this editor" : ": not mapped by this editor");
                if (CanCut(full))
                {
                    record.Append(", free\n");
                    continue;
                }
                string who = Holders(full);
                record.Append(", can't be cut short").Append(who.Length > 0 ? " (open in " + who + ")" : "");
                if (MoveAside(full, out string aside))
                {
                    record.Append(", moved aside to ").Append(aside).Append('\n');
                    continue;
                }
                record.Append(", could not be moved\n");
                string note = name + ": " + (who.Length > 0 ? who : mine ? "Unity (this editor, mapped)" : "nobody Windows can name");
                Notes.Add(note);
                Debug.LogWarning("[ProbeCellWriteGuard] " + note + " holds the file and it could not be moved aside; the probe bake's write will fail.");
            }
            Record(record.ToString());
        }
        catch (Exception error)
        {
            Debug.LogWarning("[ProbeCellWriteGuard] could not check the probe cell files: " + error.Message);
        }
        return true;
    }

    // The probe volume's cell files (<baking set>.CellSharedData.bytes, <baking set>-<scenario>.CellData.bytes and the like).
    private static bool IsCellFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase)) return false;
        string name = Path.GetFileName(path);
        return name.IndexOf(" Baking Set", StringComparison.Ordinal) >= 0 && name.IndexOf(".Cell", StringComparison.Ordinal) >= 0;
    }

    // Whether the file can be cut short, as the write's FileMode.Create does: grown by a byte and cut back. A mapped file
    // can grow but not be cut (1224); one left a byte longer is moved aside, and the bake replaces it whole anyway.
    private static bool CanCut(string full)
    {
        try
        {
            using (var file = new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                long length = file.Length;
                file.SetLength(length + 1);
                file.SetLength(length);
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // The file into the aside folder under a name of its own; false when Windows won't rename it either.
    private static bool MoveAside(string full, out string aside)
    {
        aside = Path.GetFullPath(Path.Combine(AsideFolder, Path.GetFileNameWithoutExtension(full) + "-" + DateTime.Now.Ticks + ".old"));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(aside));
            File.Move(full, aside);
            return true;
        }
        catch (Exception) { return false; }
    }

    // The files moved aside before, where nothing holds them any more.
    private static void ClearAside()
    {
        if (!Directory.Exists(AsideFolder)) return;
        foreach (string old in Directory.GetFiles(AsideFolder))
            try { File.Delete(old); }
            catch (Exception) { }
    }

    private static void Record(string lines)
    {
        if (lines.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath, lines);
        }
        catch (Exception) { }
    }

    // Who has the file open, as Explorer's "file in use" finds it (the Windows Restart Manager): "Unity (this editor)"
    // or a process's name and id, comma separated; "" when nobody or off Windows. A file mapped with no handle open is
    // not found here (see MappedHere).
    private static string Holders(string fullPath)
    {
        if (Application.platform != RuntimePlatform.WindowsEditor) return "";
        if (RmStartSession(out uint session, 0, Guid.NewGuid().ToString()) != 0) return "";
        try
        {
            if (RmRegisterResources(session, 1, new[] { fullPath }, 0, IntPtr.Zero, 0, null) != 0) return "";
            uint needed, count = 0, reasons = 0;
            int result = RmGetList(session, out needed, ref count, null, ref reasons);
            if (result != MoreData || needed == 0) return "";
            var found = new ProcessInfo[needed];
            count = needed;
            if (RmGetList(session, out needed, ref count, found, ref reasons) != 0) return "";
            int self = System.Diagnostics.Process.GetCurrentProcess().Id;
            var names = new List<string>();
            for (int i = 0; i < count; i++)
            {
                int id = found[i].Process.ProcessId;
                names.Add(id == self ? "Unity (this editor)" : found[i].AppName + " (" + id + ")");
            }
            return string.Join(", ", names);
        }
        finally
        {
            RmEndSession(session);
        }
    }

    // The files this editor has mapped into its memory, as Windows names them (\Device\HarddiskVolumeN\...).
    private static List<string> MappedHere()
    {
        var names = new List<string>();
        if (Application.platform != RuntimePlatform.WindowsEditor) return names;
        IntPtr self = GetCurrentProcess();
        var name = new StringBuilder(1024);
        long address = 0;
        int size = Marshal.SizeOf(typeof(MemoryInfo));
        while (VirtualQuery(new IntPtr(address), out MemoryInfo info, new IntPtr(size)) != IntPtr.Zero)
        {
            if (info.Type == MemMapped && info.State == MemCommit)
            {
                name.Length = 0;
                if (GetMappedFileNameW(self, info.BaseAddress, name, (uint)name.Capacity) > 0) names.Add(name.ToString());
            }
            long next = info.BaseAddress.ToInt64() + info.RegionSize.ToInt64();
            if (next <= address) break;
            address = next;
        }
        return names;
    }

    // Whether a mapped name is this file (the device names the volume, the rest is the path after the drive).
    private static bool IsMapped(List<string> mapped, string full)
    {
        string tail = full.Length > 2 && full[1] == ':' ? full.Substring(2) : full;
        foreach (string each in mapped)
            if (each.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private const int MoreData = 234;
    private const uint MemMapped = 0x40000, MemCommit = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State, Protect, Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess
    {
        public int ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME StartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceName;
        public int ApplicationType;
        public uint AppStatus;
        public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr VirtualQuery(IntPtr address, out MemoryInfo info, IntPtr length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetMappedFileNameW(IntPtr process, IntPtr address, StringBuilder name, uint size);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, int flags, string key);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint fileCount, string[] files, uint appCount, IntPtr apps,
        uint serviceCount, string[] services);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[] found, ref uint reasons);
}
