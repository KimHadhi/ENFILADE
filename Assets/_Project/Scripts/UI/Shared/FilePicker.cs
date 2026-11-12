using System;
using System.Runtime.InteropServices;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Asks the player to choose a file with the system's own open dialog (Windows' "열기" window in a build, Unity's in
/// the editor). The game waits while it is open. Other platforms have no dialog and get null.
/// </summary>
public static class FilePicker
{
    /// <summary>The chosen file's full path, or null if the player cancelled.</summary>
    /// <param name="kind">What the files are, shown in the dialog's type list ("이미지").</param>
    /// <param name="extensions">The extensions allowed, without dots ("png", "jpg").</param>
    public static string Open(string title, string kind, params string[] extensions)
    {
#if UNITY_EDITOR
        string chosen = EditorUtility.OpenFilePanelWithFilters(title, "", new[] { kind, string.Join(",", extensions) });
        return string.IsNullOrEmpty(chosen) ? null : chosen;
#elif UNITY_STANDALONE_WIN
        return OpenOnWindows(title, kind, extensions);
#else
        return null;
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    // OPENFILENAMEW from commdlg.h, field for field.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int structSize;
        public IntPtr owner;
        public IntPtr instance;
        public string filter;
        public string customFilter;
        public int maxCustomFilter;
        public int filterIndex;
        public IntPtr file;
        public int maxFile;
        public string fileTitle;
        public int maxFileTitle;
        public string initialDirectory;
        public string title;
        public int flags;
        public short fileOffset;
        public short fileExtension;
        public string defaultExtension;
        public IntPtr customData;
        public IntPtr hook;
        public string templateName;
        public IntPtr reservedPointer;
        public int reservedNumber;
        public int flagsEx;
    }

    private const int Explorer = 0x00080000, FileMustExist = 0x00001000, PathMustExist = 0x00000800, KeepFolder = 0x00000008;
    private const int Capacity = 1024;

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OpenFileName dialog);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    private static string OpenOnWindows(string title, string kind, string[] extensions)
    {
        string pattern = string.Join(";", Array.ConvertAll(extensions, extension => "*." + extension));
        IntPtr buffer = Marshal.AllocHGlobal(Capacity * 2);
        try
        {
            Marshal.WriteInt16(buffer, 0); // starts empty
            var dialog = new OpenFileName
            {
                structSize = Marshal.SizeOf<OpenFileName>(),
                owner = GetActiveWindow(),
                filter = kind + " (" + pattern + ")\0" + pattern + "\0\0",
                filterIndex = 1,
                file = buffer,
                maxFile = Capacity,
                title = title,
                flags = Explorer | FileMustExist | PathMustExist | KeepFolder,
            };
            return GetOpenFileNameW(ref dialog) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
#endif
}
