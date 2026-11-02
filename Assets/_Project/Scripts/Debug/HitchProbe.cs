#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

/// <summary>
/// Temporary, editor only: writes what took the time in any frame over HitchMilliseconds to Temp/hitches.txt, to find
/// the first-shot stall without opening the Profiler. Delete this file once the stall is understood.
/// </summary>
public sealed class HitchProbe : MonoBehaviour
{
    private const float HitchMilliseconds = 50f;
    private const int MaxReports = 40, TopMarkers = 26;
    private static readonly float[] ScanTimes = { 1f, 4f, 9f };
    private static string FilePath => Path.Combine(Application.dataPath, "..", "Temp", "hitches.txt");

    private readonly Dictionary<string, ProfilerRecorder> recorders = new Dictionary<string, ProfilerRecorder>();
    private float started;
    private int nextScan, reports;
    private bool scanned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        File.WriteAllText(FilePath, $"Play started {System.DateTime.Now:HH:mm:ss}\n");
        var host = new GameObject("Hitch Probe");
        DontDestroyOnLoad(host);
        host.AddComponent<HitchProbe>();
    }

    private void Start()
    {
        started = Time.unscaledTime;
        Scan();
    }

    // Script markers only exist once their code has run, so the list is taken again a few times.
    private void Scan()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        foreach (var handle in handles)
        {
            var info = ProfilerRecorderHandle.GetDescription(handle);
            if (info.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || recorders.ContainsKey(info.Name)) continue;
            recorders[info.Name] = ProfilerRecorder.StartNew(info.Category, info.Name);
        }
        scanned = true;
    }

    private void Update()
    {
        float ms = Time.unscaledDeltaTime * 1000f, age = Time.unscaledTime - started;
        // The scan itself is slow, so the frame after it says nothing.
        if (scanned) scanned = false;
        else if (ms >= HitchMilliseconds && reports < MaxReports) Report(ms, age);
        if (nextScan < ScanTimes.Length && age >= ScanTimes[nextScan]) { nextScan++; Scan(); }
    }

    private void Report(float ms, float age)
    {
        reports++;
        var text = new StringBuilder($"\n--- {ms:0} ms at {age:0.00} s, frame {Time.frameCount}, {recorders.Count} markers\n");
        foreach (var marker in recorders.Select(pair => (name: pair.Key, ms: pair.Value.LastValue * 1e-6))
                     .Where(entry => entry.ms >= 1.0).OrderByDescending(entry => entry.ms).Take(TopMarkers))
            text.Append($"{marker.ms,8:0.0}  {marker.name}\n");
        File.AppendAllText(FilePath, text.ToString());
    }

    private void OnDestroy()
    {
        foreach (var recorder in recorders.Values) recorder.Dispose();
    }
}
#endif
