#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

/// <summary>
/// Temporary, editor only. Writes two records to Logs/hitches.txt, without opening the Profiler: every frame of
/// HitchMilliseconds or more with what took its time, and, when Play stops, each movement state's frame times with the
/// markers that cost more in it than standing does. The second finds a steady drop (a mantle at half the frame rate) that
/// never makes one long frame. The next Play starts the file again, so a stopped run is also kept in Logs/Hitches.
/// Not Temp: Unity empties Temp when it closes, which lost every run recorded before 2026-11-06's restart.
/// Each report also says what happened in that frame (FrameTags): what code marked (a zombie made from its look) and
/// what this probe sees changing from outside (zombies coming and going, a brute spawning and arriving, corpses laid
/// and gone, ragdoll dolls made, the campaign's song and its peak, the first callout feed and alert band, hits, kills,
/// limbs off). Frames under the loading screen are marked [loading] and counted apart, so a load's own long frames no
/// longer use up the play's reports (the 22:13 run of 2026-11-12 ran out at 145 s, before its chapter's brute came).
/// Delete this file once the stalls are understood.
/// </summary>
public sealed class HitchProbe : MonoBehaviour
{
    // A mantle's stutter may stay under the 50 ms the first-shot stall was hunted with: 25 ms is six frames at 240 fps.
    private const float HitchMilliseconds = 25f;
    private const int MaxReports = 400, MaxLoadingReports = 200, TopMarkers = 26, SummaryMarkers = 12, LeastFrames = 20;
    // Reading every marker costs a fraction of a millisecond, so they are read on one frame in this many.
    private const int SampleEvery = 4;
    // Frames this soon after a state begins are counted apart, as the state's entry.
    private const float EntrySeconds = 0.3f;
    private const int NoPlayer = -1;
    private static readonly float[] ScanTimes = { 1f, 4f, 9f };
    private static string Folder => Path.Combine(Application.dataPath, "..", "Logs");
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
    private readonly List<(int frame, string what)> happened = new List<(int frame, string what)>();
    private readonly Watch watch = new Watch();
    private ProfilerRecorder allocated;
    private PlayerMovement.MovementState lastState;
    private float started, stateSince;
    private int nextScan, reports, loadingReports, frameKey = NoPlayer;
    private bool scanned, written, capNoted, wasLoading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, $"Play started {System.DateTime.Now:HH:mm:ss}\n");
        var host = new GameObject("Hitch Probe");
        RuntimeSceneHierarchy.Keep(host, "Dev");
        host.AddComponent<HitchProbe>();
    }

    private void Start()
    {
        started = Time.unscaledTime;
        allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        Scan();
        watch.Listen();
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
        // What changed since the last look, marked this frame: it was done in the frame being reported or this one.
        watch.Look();
        // The time and markers read now belong to the last frame, so they go to the state that frame ran in.
        int key = frameKey;
        frameKey = StateKey();
        bool loading = wasLoading || LoadingScreen.Visible;
        wasLoading = LoadingScreen.Visible;
        // The scan itself is slow, so the frame after it says nothing.
        if (scanned) scanned = false;
        else
        {
            if (!loading) Count(key, ms);
            if (ms >= HitchMilliseconds) Report(ms, age, key, loading);
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

    private void Report(float ms, float age, int key, bool loading)
    {
        if (loading ? loadingReports >= MaxLoadingReports : reports >= MaxReports)
        {
            if (!loading && !capNoted)
            {
                capNoted = true;
                File.AppendAllText(FilePath, $"\n(no more play reports after {MaxReports}, at {age:0.00} s)\n");
            }
            return;
        }
        if (loading) loadingReports++;
        else reports++;
        // What the player was doing, to tell a mantle's stall from a kill's, and what happened in the frame.
        var text = new StringBuilder($"\n--- {ms:0} ms at {age:0.00} s, frame {Time.frameCount}, {Describe(key)}{(loading ? " [loading]" : "")}, {recorders.Count} markers\n");
        string what = Happened();
        if (what.Length > 0) text.Append("  happened: ").Append(what).Append('\n');
        foreach (var marker in Enumerable.Range(0, recorders.Count).Select(i => (name: markerNames[i], ms: recorders[i].LastValue * 1e-6))
                     .Where(entry => entry.ms >= 1.0 && Sane(entry.ms, ms)).OrderByDescending(entry => entry.ms).Take(TopMarkers))
            text.Append($"{marker.ms,8:0.0}  {marker.name}\n");
        File.AppendAllText(FilePath, text.ToString());
    }

    // The marks of the reported frame, the one before it and this one, each told once with how many times it came.
    private string Happened()
    {
        happened.Clear();
        int now = Time.frameCount;
        FrameTags.Between(now - 2, now, happened);
        if (happened.Count == 0) return "";
        var order = new List<string>();
        var counts = new Dictionary<string, int>();
        foreach (var (_, what) in happened)
        {
            if (!counts.ContainsKey(what)) { counts[what] = 0; order.Add(what); }
            counts[what]++;
        }
        return string.Join(" · ", order.Select(what => counts[what] > 1 ? $"{what} ×{counts[what]}" : what));
    }

    private void OnApplicationQuit() => WriteSummary();

    private void WriteSummary()
    {
        if (written) return;
        written = true;
        var text = new StringBuilder($"\n=== States, Play stopped at {System.DateTime.Now:HH:mm:ss} after {Time.unscaledTime - started:0} s (frames under the loading screen left out) ===\n");
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
        watch.Stop();
        foreach (var recorder in recorders) recorder.Dispose();
        allocated.Dispose();
    }

    // ---- What happened, seen from outside ----

    // Reads the game's own registries each frame and marks what changed. Some are private: read by reflection, which
    // is fine for an editor-only probe and leaves those files as they are; a field renamed later reads as nothing.
    private sealed class Watch
    {
        private const BindingFlags Statics = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly FieldInfo LyingField = typeof(ZombieCorpse).GetField("lying", Statics);
        private static readonly FieldInfo MadeField = typeof(ZombieCorpse).GetField("made", Statics);
        private static readonly FieldInfo ActiveRunField = typeof(CampaignRun).GetField("active", Statics);
        private static readonly FieldInfo MusicField = typeof(CampaignRun).GetField("music", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo FeedField = typeof(CalloutFeedHUD).GetField("instance", Statics);
        private static readonly FieldInfo AlertField = typeof(AlertHUD).GetField("instance", Statics);

        private readonly HashSet<int> known = new HashSet<int>(), seenNow = new HashSet<int>();
        private readonly List<ZombieBody> brutes = new List<ZombieBody>();
        private readonly HashSet<ZombieBody> arrived = new HashSet<ZombieBody>();
        private int lying = -1, dolls = -1;
        private string song = "";
        private bool peak, feed, alert;

        public void Listen()
        {
            ZombieGore.Crippled += OnCrippled;
            GunKills.Counted += OnKill;
            SpecialKills.Scored += OnSpecial;
            CombatDamageEvents.Damaged += OnDamaged;
        }

        public void Stop()
        {
            ZombieGore.Crippled -= OnCrippled;
            GunKills.Counted -= OnKill;
            SpecialKills.Scored -= OnSpecial;
            CombatDamageEvents.Damaged -= OnDamaged;
        }

        private static void OnCrippled(EnemyHealth enemy, BodyPart part) => FrameTags.Mark("limb off");
        private static void OnKill(WeaponData weapon, int kills, bool rankUp) => FrameTags.Mark("kill");
        private static void OnSpecial(SpecialKills.Kind kind) => FrameTags.Mark("special kill " + kind);
        private static void OnDamaged(CombatDamageReport report) => FrameTags.Mark("hit");

        public void Look()
        {
            Zombies();
            Corpses();
            Song();
            Firsts();
        }

        // Zombies that came and went; a brute's spawn, and its arrival (roar, boom, alert, leg shout) once its body stands.
        private void Zombies()
        {
            seenNow.Clear();
            int came = 0;
            foreach (var enemy in EnemyHealth.Living)
            {
                if (enemy == null) continue;
                int id = enemy.GetInstanceID();
                seenNow.Add(id);
                if (!known.Add(id)) continue;
                came++;
                if (enemy.TryGetComponent(out ZombieType type) && type.Kind == ZombieKind.Brute && enemy.TryGetComponent(out ZombieBody body))
                {
                    brutes.Add(body);
                    FrameTags.Mark("brute spawned");
                }
            }
            int gone = known.RemoveWhere(id => !seenNow.Contains(id));
            if (came > 0) FrameTags.Mark($"zombies +{came} (now {seenNow.Count})");
            if (gone > 0) FrameTags.Mark($"zombies -{gone} (now {seenNow.Count})");
            for (int i = brutes.Count - 1; i >= 0; i--)
            {
                var body = brutes[i];
                if (body == null) { brutes.RemoveAt(i); continue; }
                if (body.Wears && arrived.Add(body)) FrameTags.Mark("brute arrives (roar, alert, leg shout)");
            }
            arrived.RemoveWhere(body => body == null);
        }

        private void Corpses()
        {
            if (LyingField?.GetValue(null) is IList bodies)
            {
                int count = bodies.Count;
                if (lying >= 0 && count != lying) FrameTags.Mark(count > lying ? $"corpse laid +{count - lying}" : $"corpse gone -{lying - count}");
                lying = count;
            }
            if (MadeField?.GetValue(null) is IDictionary made)
            {
                int count = 0;
                foreach (var list in made.Values)
                    if (list is IList dollsOfRig) count += dollsOfRig.Count;
                if (dolls >= 0 && count > dolls) FrameTags.Mark($"ragdoll doll made +{count - dolls}");
                dolls = count;
            }
        }

        private void Song()
        {
            var run = ActiveRunField?.GetValue(null);
            var music = run != null ? MusicField?.GetValue(run) as CampaignMusic : null;
            string playing = music != null ? music.Playing : "";
            bool past = music != null && music.PastPeak;
            if (playing != song && playing.Length > 0) FrameTags.Mark("song starts: " + playing);
            if (past && !peak) FrameTags.Mark("song peak");
            song = playing;
            peak = past;
        }

        // Things made on their first use: the callout feed (its face camera) and the alert band.
        private void Firsts()
        {
            bool feedNow = FeedField?.GetValue(null) is Object feedHud && feedHud != null;
            bool alertNow = AlertField?.GetValue(null) is Object alertHud && alertHud != null;
            if (feedNow && !feed) FrameTags.Mark("callout feed made (face camera)");
            if (alertNow && !alert) FrameTags.Mark("alert band made");
            feed = feedNow;
            alert = alertNow;
        }
    }
}
#endif
