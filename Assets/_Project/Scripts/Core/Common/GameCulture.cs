using System.Globalization;
using UnityEngine;

/// <summary>
/// The game writes and reads numbers one way on every PC: a point for the decimals, a comma between the thousands.
/// Left alone, .NET follows the PC's region (a German Windows writes 1.234,5), so every ToString, string.Format and
/// $"..." that did not name a culture would show, save or send a different text there: kills read "1.234" beside
/// money that reads "1,234", and a number written into a file would not read back. Set once as play starts, for
/// every thread, so the whole class of mistake is gone rather than each place remembering to guard itself.
/// Files and the network still name the invariant culture where they read and write (the code check asks for it).
/// </summary>
internal static class GameCulture
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Set()
    {
#if UNITY_EDITOR
        // The editor shares this thread: its own windows get the PC's culture back when Play ends.
        var before = CultureInfo.CurrentCulture;
        Application.quitting += () => CultureInfo.CurrentCulture = before;
#endif
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }
}
