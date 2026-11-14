using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.Profiling;
using UnityEditorInternal;
#endif

/// <summary>
/// /perf: what each script and each part of the engine costs a frame, read from Unity's profiler while it is on, so the
/// next cuts start from numbers (the user, 2026-11-14 past 23 h in chapter 1: "최적화가 뒤지게 안 돼서 기본적으로 렉이
/// 엄청 걸리고"). HitchProbe's markers can't name a script, and that night's record could not say which of the game's
/// scripts made chapter 1's frames 16 ms; the profiler can, as every MonoBehaviour's Update, LateUpdate, FixedUpdate and
/// coroutine is a sample of its own there. While /perf is on, the editor's profiler records Play (not the editor), and
/// one frame in ReadEvery is read whole, besides every frame of LongFrameMs or more. Per scene it adds up each marker's
/// time with what it calls (total) and without (self); /perf again, or Play stopping, writes Logs/perf.txt: the frame
/// times, the main thread split into the game and the editor around it, the scripts by their time a frame, the markers by
/// their own time, and the longest frames with what took them. Editor only. The profiler's own cost is in the numbers
/// (a few percent with deep profiling off); compare runs made the same way.
/// </summary>
public sealed class PerfProbe : MonoBehaviour
{
    /// <summary>/perf: on, or off with the record written.</summary>
    public static string Command(string args)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return "Play 중에만 잽니다."; // dev-text
        if (instance != null) return instance.Finish();
        var host = new GameObject("Perf Probe");
        RuntimeSceneHierarchy.Keep(host, "Dev");
        instance = host.AddComponent<PerfProbe>();
        return "프레임 비용 측정을 켰습니다(에디터 프로파일러). 평소처럼 1~2분 하고 /perf 를 다시 치면 Logs/perf.txt 에 스크립트별·엔진 부분별 비용이 남습니다. Play를 멈춰도 남습니다."; // dev-text
#else
        return "프레임 비용 측정(/perf)은 에디터에서만 됩니다."; // dev-text
#endif
    }

#if UNITY_EDITOR
    // One frame in this many is read whole: reading one costs about a millisecond for every ten thousand samples.
    private const int ReadEvery = 3;
    // Frames this long or longer are read whole too, and the longest of them kept with what took them.
    private const float LongFrameMs = 50f;
    private const int KeptLongFrames = 12, LongFrameRows = 10, ScriptsShown = 45, MarkersShown = 35, LeastFrames = 30;
    // A frame with more samples than this (deep profiling switched on in the Profiler window) is not read: it would stall.
    private const int MostSamples = 400000;
    private const string GameLoop = "PlayerLoop", EditorLoop = "EditorLoop", EditorRepaint = "GUIView.RepaintAll.PlayerLoopController",
        GameCameras = "RenderPlayModeViewCameras", Self = "PerfProbe.";
    private static string Folder => Path.Combine(Application.dataPath, "..", "Logs");
    private static string FilePath => Path.Combine(Folder, "perf.txt");

    private static PerfProbe instance;

    private sealed class Stat
    {
        public double Total, Self;
        public int Calls;
    }

    private sealed class LongFrame
    {
        public float Ms, At;
        public string Rows;
    }

    // One scene's numbers while measuring.
    private sealed class Section
    {
        public string Scene, From, To;
        public float Since, Seconds;
        public readonly List<float> Frames = new List<float>();
        public int Read;
        public double Main, Loop, Editor, Repaint, Cameras;
        public readonly Dictionary<int, Stat> Markers = new Dictionary<int, Stat>();
        public readonly List<LongFrame> Long = new List<LongFrame>();
    }

    private readonly List<Section> done = new List<Section>();
    private readonly Dictionary<int, string> names = new Dictionary<int, string>();
    private readonly Dictionary<int, Stat> frameStats = new Dictionary<int, Stat>();
    private int[] stackMarker = new int[64], stackLeft = new int[64];
    private float[] stackTime = new float[64], stackChildren = new float[64];
    private Section section;
    private int nextFrame = -1, skippedFrames;
    private bool wasEnabled, wasEditor, deep, finished;
    private string began;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void Awake()
    {
        wasEnabled = ProfilerDriver.enabled;
        wasEditor = ProfilerDriver.profileEditor;
        deep = ProfilerDriver.deepProfiling;
        // Play Mode is what is measured, as the Profiler window's target would be; the editor's own work still shows as
        // one EditorLoop sample beside the player loop.
        ProfilerDriver.profileEditor = false;
        ProfilerDriver.enabled = true;
        nextFrame = ProfilerDriver.lastFrameIndex + 1;
        began = System.DateTime.Now.ToString("HH:mm:ss");
        Open(SceneManager.GetActiveScene().name);
        SceneManager.activeSceneChanged += OnSceneChanged;
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        Finish();
        if (instance == this) instance = null;
    }

    private void OnApplicationQuit() => Finish();

    private void OnSceneChanged(Scene from, Scene to)
    {
        Close();
        Open(to.name);
    }

    private void Open(string scene)
    {
        section = new Section { Scene = string.IsNullOrEmpty(scene) ? "?" : scene, From = System.DateTime.Now.ToString("HH:mm:ss"), Since = Time.unscaledTime };
    }

    private void Close()
    {
        if (section == null) return;
        section.To = System.DateTime.Now.ToString("HH:mm:ss");
        section.Seconds = Time.unscaledTime - section.Since;
        if (section.Frames.Count >= LeastFrames && section.Read > 0) done.Add(section);
        section = null;
    }

    private void Update()
    {
        if (finished) return;
        // Frames under the loading screen are a load's, not the scene's play.
        if (LoadingScreen.Visible || section == null) { nextFrame = Mathf.Max(nextFrame, ProfilerDriver.lastFrameIndex + 1); return; }
        section.Frames.Add(Time.unscaledDeltaTime * 1000f);
        int last = ProfilerDriver.lastFrameIndex;
        if (last < 0) return;
        int first = ProfilerDriver.firstFrameIndex;
        if (nextFrame < first)
        {
            skippedFrames += first - nextFrame;
            nextFrame = first;
        }
        for (int frame = nextFrame; frame <= last; frame++)
        {
            using var view = ProfilerDriver.GetRawFrameDataView(frame, 0);
            if (view == null || !view.valid) continue;
            bool longOne = view.frameTimeMs >= LongFrameMs;
            if (frame % ReadEvery != 0 && !longOne) continue;
            if (view.sampleCount > MostSamples) { skippedFrames++; continue; }
            Read(view, longOne);
        }
        nextFrame = last + 1;
    }

    // One frame of the main thread, whole: each sample adds its time to its marker's total (once, when the same marker is
    // not already above it) and its time without its children to the marker's self. Samples come depth first; a sample's
    // children follow it.
    private void Read(RawFrameDataView view, bool longOne)
    {
        int count = view.sampleCount;
        if (count <= 0) return;
        int need = Mathf.Max(view.maxDepth, 1) + 2;
        if (stackMarker.Length < need)
        {
            System.Array.Resize(ref stackMarker, need);
            System.Array.Resize(ref stackLeft, need);
            System.Array.Resize(ref stackTime, need);
            System.Array.Resize(ref stackChildren, need);
        }
        int loop = view.GetMarkerId(GameLoop), editor = view.GetMarkerId(EditorLoop), repaint = view.GetMarkerId(EditorRepaint), cameras = view.GetMarkerId(GameCameras);
        double loopMs = 0, editorMs = 0, repaintMs = 0, camerasMs = 0;
        if (longOne) frameStats.Clear();
        int depth = 0;
        for (int i = 0; i < count; i++)
        {
            int marker = view.GetSampleMarkerId(i);
            float time = view.GetSampleTimeMs(i);
            int children = view.GetSampleChildrenCount(i);
            if (depth > 0)
            {
                stackChildren[depth - 1] += time;
                stackLeft[depth - 1]--;
            }
            if (!section.Markers.TryGetValue(marker, out var stat))
            {
                section.Markers[marker] = stat = new Stat();
                // Named while its frame is at hand: a marker is named once, the first time any scene meets it.
                if (!names.ContainsKey(marker)) names[marker] = view.GetMarkerName(marker);
            }
            bool inside = false;
            for (int d = 0; d < depth; d++)
                if (stackMarker[d] == marker) { inside = true; break; }
            if (!inside)
            {
                stat.Total += time;
                if (longOne) Of(frameStats, marker).Total += time;
                if (marker == loop) loopMs += time;
                else if (marker == editor) editorMs += time;
                else if (marker == repaint) repaintMs += time;
                else if (marker == cameras) camerasMs += time;
            }
            stat.Calls++;
            if (depth >= stackMarker.Length) break;
            stackMarker[depth] = marker;
            stackTime[depth] = time;
            stackChildren[depth] = 0f;
            stackLeft[depth] = children;
            depth++;
            while (depth > 0 && stackLeft[depth - 1] <= 0)
            {
                depth--;
                float self = Mathf.Max(0f, stackTime[depth] - stackChildren[depth]);
                Of(section.Markers, stackMarker[depth]).Self += self;
                if (longOne) Of(frameStats, stackMarker[depth]).Self += self;
            }
        }
        section.Read++;
        section.Main += view.frameTimeMs;
        section.Loop += loopMs;
        section.Editor += editorMs;
        section.Repaint += repaintMs;
        section.Cameras += camerasMs;
        if (longOne) KeepLong(view.frameTimeMs);
    }

    private static Stat Of(Dictionary<int, Stat> stats, int marker)
    {
        if (!stats.TryGetValue(marker, out var stat)) stats[marker] = stat = new Stat();
        return stat;
    }

    private string NameOf(int marker) => names.TryGetValue(marker, out string name) && !string.IsNullOrEmpty(name) ? name : "#" + marker;

    // A long frame kept with the samples that took it: the largest own times, and the scripts with what they called.
    private void KeepLong(float ms)
    {
        var list = section.Long;
        if (list.Count >= KeptLongFrames && list.Min(frame => frame.Ms) >= ms) return;
        var rows = new StringBuilder();
        rows.Append("    own time: ").Append(string.Join(" · ", frameStats.OrderByDescending(pair => pair.Value.Self).Take(LongFrameRows)
            .Select(pair => $"{NameOf(pair.Key)} {pair.Value.Self:0.0}"))).Append('\n');
        var scripts = frameStats.Where(pair => IsScript(NameOf(pair.Key))).OrderByDescending(pair => pair.Value.Total).Take(6).ToList();
        if (scripts.Count > 0)
            rows.Append("    scripts: ").Append(string.Join(" · ", scripts.Select(pair => $"{NameOf(pair.Key)} {pair.Value.Total:0.0}"))).Append('\n');
        list.Add(new LongFrame { Ms = ms, At = Time.unscaledTime - section.Since, Rows = rows.ToString() });
        if (list.Count > KeptLongFrames) list.Remove(list.OrderBy(frame => frame.Ms).First());
    }

    // A script's own sample: "Type.Method() [Invoke]", a coroutine's step, an invoked callback.
    private static bool IsScript(string name) => name.Contains("()") && !name.StartsWith(Self);

    private string Finish()
    {
        if (finished) return "이미 껐습니다: Logs/perf.txt"; // dev-text
        finished = true;
        Close();
        ProfilerDriver.enabled = wasEnabled;
        ProfilerDriver.profileEditor = wasEditor;
        int read = done.Sum(part => part.Read);
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(FilePath)) File.Copy(FilePath, Path.Combine(Folder, "perf.previous.txt"), true);
            File.WriteAllText(FilePath, Write());
        }
        catch (System.Exception problem)
        {
            Debug.LogWarning("[PerfProbe] Logs/perf.txt could not be written: " + problem.Message);
        }
        if (instance == this) instance = null;
        Destroy(gameObject);
        return $"프레임 비용 측정을 껐습니다: Logs/perf.txt (장면 {done.Count}개, 통째로 읽은 프레임 {read}개)."; // dev-text
    }

    private string Write()
    {
        var text = new StringBuilder();
        text.Append($"Frame cost (/perf, PerfProbe), {began} to {System.DateTime.Now:HH:mm:ss}, game {Application.version}, profiler on Play Mode"
            + $" (deep profiling {(deep ? "ON: every C# call is timed, numbers far above normal" : "off")}); one frame in {ReadEvery} read whole,"
            + $" and every frame of {LongFrameMs:0} ms or more; frames under the loading screen left out");
        if (skippedFrames > 0) text.Append($"; {skippedFrames} frames passed unread (profiler buffer)");
        text.Append("\nms a frame below = summed over the frames read whole, divided by them. 'with calls' = the time of everything the script called from there.\n");
        foreach (var part in done) WriteSection(text, part);
        if (done.Count == 0) text.Append("\nNothing measured: no scene was played long enough out of the loading screen.\n");
        return text.ToString();
    }

    private void WriteSection(StringBuilder text, Section part)
    {
        var sorted = part.Frames.OrderBy(ms => ms).ToList();
        float At(float share) => sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * share))];
        float Share(float least) => 100f * sorted.Count(ms => ms >= least) / sorted.Count;
        double n = part.Read;
        double main = part.Main / n, loop = part.Loop / n, editorLoop = part.Editor / n, repaint = part.Repaint / n, cameras = part.Cameras / n;
        double windows = System.Math.Max(0, repaint - cameras), outside = System.Math.Max(0, main - loop), game = System.Math.Max(0, main - outside - windows);
        text.Append($"\n=== {part.Scene}, {part.From} to {part.To} ({part.Seconds:0} s, {sorted.Count} frames, {part.Read} read whole)\n");
        text.Append($"frame ms: mean {sorted.Average():0.0}  p50 {At(.5f):0.0}  p95 {At(.95f):0.0}  p99 {At(.99f):0.0}  max {sorted[sorted.Count - 1]:0}"
            + $"  | 25 ms or more {Share(25f):0.0}%  50 ms or more {Share(50f):0.00}%\n");
        text.Append($"main thread {main:0.0} ms a frame = the game {game:0.0} + the editor {outside + windows:0.0}"
            + $" (outside the player loop {outside:0.0}, of it EditorLoop {editorLoop:0.0}; the editor windows' repaint inside the loop {windows:0.0});"
            + $" the game's cameras draw {cameras:0.0} of the game's (in a build they draw in PostLateUpdate.FinishFrameRendering)\n");

        var rows = part.Markers.Select(pair => (name: NameOf(pair.Key), stat: pair.Value)).ToList();
        text.Append("\nscripts, ms a frame with calls (own ms, calls a frame):\n");
        foreach (var row in rows.Where(row => IsScript(row.name)).OrderByDescending(row => row.stat.Total).Take(ScriptsShown))
            text.Append($"{row.stat.Total / n,8:0.000}  ({row.stat.Self / n:0.000}, {row.stat.Calls / n:0.#})  {row.name}\n");
        text.Append("\nmarkers by own time, ms a frame (with calls, calls a frame):\n");
        foreach (var row in rows.OrderByDescending(row => row.stat.Self).Take(MarkersShown))
            text.Append($"{row.stat.Self / n,8:0.000}  ({row.stat.Total / n:0.000}, {row.stat.Calls / n:0.#})  {row.name}\n");
        var perf = rows.Where(row => row.name.StartsWith(Self)).Sum(row => row.stat.Total) / n;
        if (perf > 0) text.Append($"(this probe's own reading: {perf:0.000} ms a frame, left out of the scripts above)\n");
        if (part.Long.Count > 0)
        {
            text.Append("\nlongest frames:\n");
            foreach (var frame in part.Long.OrderByDescending(frame => frame.Ms))
                text.Append($"  {frame.Ms:0} ms at {frame.At:0.0} s into the scene\n").Append(frame.Rows);
        }
    }
#endif
}
