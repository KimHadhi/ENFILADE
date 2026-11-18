using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The record of what happened to this PC's own player's rounds other than by a shot: Logs/gun-ammo.txt, in the editor
/// and in development builds (the last play's is kept as gun-ammo.previous.txt). One line for each thing that moved
/// rounds, with that gun's magazine and reserve before and after it, so one play leaves what the ammunition did as
/// numbers: nothing of it can be run by whoever changed the rules, and "주웠더니 탄약이 초기화됐다" can't be told apart
/// from a description alone (the user's reports of 2026-11-18). Shots are not lines: the rounds fired between two lines
/// of one gun are the difference between the first's "after" and the second's "before".
/// A gun's line reads: time, gun, magazine/size +reserve before -> after, what did it.
/// It only writes: nothing here changes a gun, and nothing depends on it being on.
/// </summary>
public static class GunAmmoLog
{
    // The record stops growing here (bytes): a long session's first hour says what there is to say.
    private const long MostBytes = 2_000_000;

    private static bool started, capped;
    private static long written;
    private static string path;
    private static readonly StringBuilder line = new StringBuilder(160);

    /// <summary>Lines are kept: a play in the editor or in a development build.</summary>
    public static bool On => Application.isPlaying && (Application.isEditor || Debug.isDebugBuild);

    // A new play starts a new record (Enter Play Mode without a domain reload keeps statics).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        started = capped = false;
        written = 0;
        SceneManager.activeSceneChanged -= OnScene;
    }

    /// <summary>One line of something that happened, at the game's time now.</summary>
    public static void Note(string what)
    {
        if (!On || capped || string.IsNullOrEmpty(what)) return;
        if (!started && !Start()) return;
        Write("[" + Time.time.ToString("0.00").PadLeft(8) + "] " + what);
    }

    /// <summary>
    /// One of this PC's own player's guns had its rounds moved (WeaponRuntime.Told): the gun as it was before, as it is
    /// now, and what did it.
    /// </summary>
    internal static void Gun(WeaponRuntime gun, string what, int ammoBefore, int reserveBefore)
    {
        if (!On || capped || gun == null) return;
        line.Clear();
        int size = gun.Data.MagazineSize;
        line.Append(gun.Data.name.PadRight(15)).Append(' ')
            .Append(ammoBefore).Append('/').Append(size).Append(" +").Append(reserveBefore)
            .Append(" -> ").Append(gun.Ammo).Append('/').Append(size).Append(" +").Append(gun.ReserveAmmo)
            .Append("  ").Append(what);
        Note(line.ToString());
    }

    private static bool Start()
    {
        started = true;
        try
        {
            string folder = Path.Combine(Application.dataPath, "..", "Logs");
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, "gun-ammo.txt");
            // The last play's record stays beside it: playing again to look doesn't lose what the last play showed.
            if (File.Exists(path)) File.Copy(path, Path.Combine(folder, "gun-ammo.previous.txt"), true);
            File.WriteAllText(path, "Gun ammunition record, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ", game " + Application.version
                + ", scene " + SceneManager.GetActiveScene().name + Environment.NewLine
                + "Lines: [game time s] gun  magazine/size +reserve before -> after  what did it. This PC's own player only; shots are "
                + "not lines (they are the difference between one line's after and the next one's before)." + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[GunAmmoLog] No record: " + problem.Message);
            capped = true;
            return false;
        }
        SceneManager.activeSceneChanged += OnScene;
        return true;
    }

    private static void OnScene(Scene from, Scene to) => Note("--- scene " + to.name);

    private static void Write(string text)
    {
        try
        {
            File.AppendAllText(path, text + Environment.NewLine);
            written += text.Length + 2;
            if (written < MostBytes) return;
            capped = true;
            File.AppendAllText(path, "(the record stops here: " + MostBytes + " bytes)" + Environment.NewLine);
        }
        catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException)
        {
            // A line lost to a file someone has open is not worth a warning a frame.
        }
    }
}
