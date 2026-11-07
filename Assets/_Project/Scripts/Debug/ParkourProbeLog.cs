using System.IO;
using UnityEngine;

// Temporary, 2026-11-07: why a vault carried by its clip's hands (WarpedTraversal) didn't start or stopped, and what was
// measured from each clip (ObstacleClipContacts), appended to Logs/body-probe.txt (no console). Goes with PlayerBody.Probe
// once the warped vault works.
internal static class ParkourProbeLog
{
    internal static void Line(string text)
    {
        try
        {
            string path = Path.Combine(Application.dataPath, "..", "Logs", "body-probe.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, $"[{System.DateTime.Now:HH:mm:ss.fff}] {text}\n");
        }
        catch (IOException) { }
    }
}
