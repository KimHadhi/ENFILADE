#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

/// <summary>
/// Temporary, editor only. Writes two records to Temp/hitches.txt, without opening the Profiler: every frame of
/// HitchMilliseconds or more with what took its time, and, when Play stops, each movement state's frame times with the
/// markers that cost more in it than standing does. The second finds a steady drop (a mantle at half the frame rate) that
/// never makes one long frame. The next Play starts the file again, so a stopped run is also kept in Temp/Hitches.
/// Delete this file once the stalls are understood.
/// </summary>
public sealed class HitchProbe : MonoBehaviour
{
    // A mantle's stutter may stay under the 50 ms the first-shot stall was hunted with: 25 ms is six frames at 240 fps.
    private const float HitchMilliseconds = 25f;
    private const int MaxReports = 80, TopMarkers = 26, SummaryMarkers = 12, LeastFrames = 20;
    // Reading every marker costs a fraction of a millisecond, so they are read on one frame in this many.
    private const int SampleEvery = 4;
    // Frames this soon after a state begins are counted apart, as the state's entry.
    private const float EntrySeconds = 0.3f;
    private const int NoPlayer = -1;
    private static readonly float[] ScanTimes = { 1f, 4f, 9f };
    private static string Folder => Path.Combine(Application.dataPath, "..", "Temp");
    private static string FilePath => Path.Combine(Folder, "hitches.txt");

    private sealed class Bucket
    {
        public readonly List<float> Frames = new List<float>();
        // Per marker: the sum of its sane readings and how many there were.
        public double[] Markers = new double[0];
        public int[] Counts = new int[0];
        public int Sampled;
        public double Allocated;
    }

    // Some recorders hand back nonsense (negative, or seconds of a 30 ms frame, which wrecked the first summaries). A
    // marker can't take more of one frame than the frame itself, times the worker threads that may add to it.
    private static bool Sane(double ms, float frameMs) => ms >= 0 && ms <= Mathf.Max(100f, frameMs * 16f);

    private readonly Dictionary<string, int> markerIndex = new Dictionary<string, int>();
    private readonly List<string> markerNames = new List<string>();
    private readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
    private readonly Dictionary<int, Bucket> buckets = new Dictionary<int, Bucket>();
    private ProfilerRecorder allocated;
    private PlayerMovement.MovementState lastState;
    private float started, stateSince;
    private int nextScan, reports, frameKey = NoPlayer;
    private bool scanned, written;

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
        allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
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
            if (info.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || markerIndex.ContainsKey(info.Name)) continue;
            markerIndex[info.Name] = recorders.Count;
            markerNames.Add(info.Name);
            recorders.Add(ProfilerRecorder.StartNew(info.Category, info.Name));
        }
        scanned = true;
    }

    private void Update()
    {
        float ms = Time.unscaledDeltaTime * 1000f, age = Time.unscaledTime - started;
        // The time and markers read now belong to the last frame, so they go to the state that frame ran in.
        int key = frameKey;
        frameKey = StateKey();
        // The scan itself is slow, so the frame after it says nothing.
        if (scanned) scanned = false;
        else
        {
            Count(key, ms);
            if (ms >= HitchMilliseconds && reports < MaxReports) Report(ms, age, key);
        }
        if (nextScan < ScanTimes.Length && age >= ScanTimes[nextScan]) { nextScan++; Scan(); }
    }

    // The local player's movement state, twice over: frames in its first EntrySeconds are its entry.
    private int StateKey()
    {
        var player = PlayerRoster.Local;
        if (player == null || !player.IsInitialized) return NoPlayer;
        var state = player.CurrentState;
        if (state != lastState) { lastState = state; stateSince = Time.unscaledTime; }
        return (int)state * 2 + (Time.unscaledTime - stateSince < EntrySeconds ? 1 : 0);
    }

    private static string Describe(int key)
        => key == NoPlayer ? "-" : ((PlayerMovement.MovementState)(key / 2)) + (key % 2 == 1 ? " (entry)" : "");

    private void Count(int key, float ms)
    {
        if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new Bucket();
        bucket.Frames.Add(ms);
        if (Time.frameCount % SampleEvery != 0) return;
        if (bucket.Markers.Length < recorders.Count)
        {
            System.Array.Resize(ref bucket.Markers, recorders.Count);
            System.Array.Resize(ref bucket.Counts, recorders.Count);
        }
        for (int i = 0; i < recorders.Count; i++)
        {
            double value = recorders[i].LastValue * 1e-6;
            if (!Sane(value, ms)) continue;
            bucket.Markers[i] += value;
            bucket.Counts[i]++;
        }
        bucket.Allocated += allocated.Valid ? allocated.LastValue : 0;
        bucket.Sampled++;
    }

    private void Report(float ms, float age, int key)
    {
        reports++;
        // What the player was doing, to tell a mantle's stall from a kill's.
        var text = new StringBuilder($"\n--- {ms:0} ms at {age:0.00} s, frame {Time.frameCount}, {Describe(key)}, {recorders.Count} markers\n");
        foreach (var marker in Enumerable.Range(0, recorders.Count).Select(i => (name: markerNames[i], ms: recorders[i].LastValue * 1e-6))
                     .Where(entry => entry.ms >= 1.0 && Sane(entry.ms, ms)).OrderByDescending(entry => entry.ms).Take(TopMarkers))
            text.Append($"{marker.ms,8:0.0}  {marker.name}\n");
        File.AppendAllText(FilePath, text.ToString());
    }

    private void OnApplicationQuit() => WriteSummary();

    private void WriteSummary()
    {
        if (written) return;
        written = true;
        var text = new StringBuilder($"\n=== States, Play stopped at {System.DateTime.Now:HH:mm:ss} after {Time.unscaledTime - started:0} s ===\n");
        text.Append($"{"state",-28}{"frames",8}{"mean",8}{"p95",8}{"max",8}{"GC kB",8}\n");
        var shown = buckets.Where(pair => pair.Value.Frames.Count >= LeastFrames).OrderBy(pair => pair.Key).ToList();
        foreach (var (key, bucket) in shown.Select(pair => (pair.Key, pair.Value)))
        {
            var sorted = bucket.Frames.OrderBy(ms => ms).ToList();
            float p95 = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * .95f))];
            double kb = bucket.Sampled > 0 ? bucket.Allocated / bucket.Sampled / 1024.0 : 0;
            text.Append($"{Describe(key),-28}{sorted.Count,8}{sorted.Average(),8:0.0}{p95,8:0.0}{sorted[sorted.Count - 1],8:0.0}{kb,8:0.0}\n");
        }

        // Standing is the baseline: whatever a state spends beyond it is what the state itself costs.
        int baseKey = (int)PlayerMovement.MovementState.Standing * 2;
        buckets.TryGetValue(baseKey, out var baseline);
        foreach (var (key, bucket) in shown.Select(pair => (pair.Key, pair.Value)))
        {
            if (key == baseKey || bucket.Sampled == 0) continue;
            var rows = Enumerable.Range(0, bucket.Markers.Length)
                .Select(i => (name: markerNames[i], ms: Mean(bucket, i), more: Mean(bucket, i) - Mean(baseline, i)))
                .Where(row => row.more >= .05).OrderByDescending(row => row.more).Take(SummaryMarkers).ToList();
            if (rows.Count == 0) continue;
            text.Append($"\n--- {Describe(key)}: ms per frame, and more than Standing\n");
            foreach (var row in rows) text.Append($"{row.ms,8:0.00}{row.more,9:+0.00}  {row.name}\n");
        }
        File.AppendAllText(FilePath, text.ToString());
        string kept = Path.Combine(Folder, "Hitches");
        Directory.CreateDirectory(kept);
        File.Copy(FilePath, Path.Combine(kept, $"{System.DateTime.Now:yyyyMMdd-HHmmss}.txt"), true);
    }

    private static double Mean(Bucket bucket, int marker)
        => bucket == null || marker >= bucket.Counts.Length || bucket.Counts[marker] == 0 ? 0 : bucket.Markers[marker] / bucket.Counts[marker];

    private void OnDestroy()
    {
        WriteSummary();
        foreach (var recorder in recorders) recorder.Dispose();
        allocated.Dispose();
    }
}
#endif
