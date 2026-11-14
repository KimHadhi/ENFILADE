using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lets the Adaptive Probe Volume bake write its cell files. The rebuild of every map on 2026-11-14 at 23:00 could not
/// write the cell data of chapters 1-4, seven times ("System.IO.IOException: Win32 IO returned 1224. Path: ...
/// Ch1_Rooftops Baking Set-Default.CellData.bytes", in AdaptiveProbeVolumes.WriteBakingCells and WriteDilatedCells).
/// The baking set had already saved the new cells' layout, so the maps kept old cell data under a new layout while the
/// build said the probes were baked.
/// 1224 is ERROR_USER_MAPPED_FILE: something has the file mapped into memory, and a mapped file can't be cut short, which
/// the bake's FileMode.Create does. It was measured that night, and the holder is not anything outside the editor:
/// - Not git: a status of the whole tree takes 0.36 s, so it holds one cell file for a few ms.
/// - Not the antivirus: a cell-sized file was written and at once written over 120 times on F:, and none failed.
/// - Nothing holds the files between bakes: the Restart Manager and the editor's own memory map both showed nothing.
/// The editor keeps handles open to files it has read, to reuse them, and those block writes to the same files. Its
/// own documentation says so for AssetDatabase.ReleaseCachedFileHandles, which "will release file handles internally
/// cached by Unity ... avoiding potential file sharing IO errors". The bake reads the cell files itself: the dilation
/// reads them all back between its two writes, and the import after the first write reads them too.
/// So right before each write, Unity calls AssetDatabase.MakeEditable on the files, which calls this processor's
/// MakeEditable. For the cell files it lets the probe volume's own reads finish closing, releases the cached handles,
/// then asks Windows who still has each file open.
/// Anyone left is named in the console and in the build's window (Notes), so a failed write says why.
/// </summary>
internal sealed class ProbeCellWriteGuard : AssetModificationProcessor
{
    /// <summary>What the last bakes found still holding a cell file, for the build's window (GreyboxBuilder clears and
    /// reads it around its bake). Empty when every file was free.</summary>
    internal static readonly List<string> Notes = new List<string>();

    // Unity calls this with the files it is about to write (AssetDatabase.MakeEditable); true lets it go on.
    private static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths)
    {
        try
        {
            if (paths == null || !Array.Exists(paths, IsCellFile)) return true;
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
            foreach (string path in paths)
            {
                if (!IsCellFile(path)) continue;
                string who = Holders(Path.GetFullPath(path));
                if (who.Length == 0) continue;
                string note = Path.GetFileName(path) + ": " + who;
                Notes.Add(note);
                Debug.LogWarning("[ProbeCellWriteGuard] " + note + " still has the file open as the probe bake writes it; the write may fail.");
            }
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

    // Who has the file open, as Explorer's "file in use" finds it (the Windows Restart Manager): "Unity (this editor)"
    // or a process's name and id, comma separated; "" when nobody or off Windows.
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

    private const int MoreData = 234;

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
