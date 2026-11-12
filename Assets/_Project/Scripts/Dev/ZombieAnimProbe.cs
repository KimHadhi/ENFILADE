using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Records what every zombie body shows, so one play leaves the numbers behind (the user reported woken calm zombies
/// coming on in their pose, frozen, three times from 2026-11-12: "모션이 굳은 채로 온다", "부동 자세로 나한테 와", "좀비
/// 애니메이션이 여전히 안 나와"; nothing of it can be run here). In the editor and in development builds, from the first
/// scene on, it writes Logs/zombie-anim.txt (the previous play's is kept as zombie-anim.previous.txt):
/// - each body within Near of this PC's player every LineEvery seconds: its mood and pose, how much of its pose shows,
///   its getting up, its speed, the walk, idle and run weights, every top-level clip with its weight and name, the graph's
///   clock, the Animator's state and culling, whether a camera sees it, and how far its bones turned since the last look;
/// - at once, what changed: a mood, a pose taken or left, a getting up begun or done, a body dressed or gone, the graph
///   put back (HEAL, ZombieBody.GuardGraph);
/// - FROZEN: a body moving over the ground in plain view whose bones have not turned for a second (its pose stopped being
///   worked out); POSE+MOVE: its calm pose, or the first half of its getting up, showing for half a second while it moves.
/// /zanim shows the same over their heads (30 m), with a count at the top left; /poses turns it on. Nothing here changes a
/// zombie: it only reads (ZombieBody.Probe.cs).
/// </summary>
public sealed class ZombieAnimProbe : MonoBehaviour
{
    // How often the bodies are looked at (s), how often each near one gets a line anyway (s), how near is near (m), how
    // near a label shows (m), and how big the record may grow (bytes).
    private const float LookEvery = .25f, LineEvery = 2f, Near = 45f, ShowWithin = 30f;
    private const long MostBytes = 6_000_000;

    private static ZombieAnimProbe instance;
    private static bool show;

    /// <summary>The labels over the zombies' heads (/zanim; /poses turns them on).</summary>
    public static bool Show { get => show; set => show = value; }

    private static string Folder => Path.Combine(Application.dataPath, "..", "Logs");
    private static string FilePath => Path.Combine(Folder, "zombie-anim.txt");

    private readonly StringBuilder line = new StringBuilder(1024);
    private readonly Dictionary<ZombieBody, float> lineAt = new Dictionary<ZombieBody, float>();
    private readonly HashSet<ZombieBody> frozenSaid = new HashSet<ZombieBody>(), poseSaid = new HashSet<ZombieBody>();
    private readonly List<(ZombieBody body, string text)> labels = new List<(ZombieBody, string)>();
    private StreamWriter writer;
    private long written;
    private bool capped;
    private float lookLeft, flushLeft;
    private int frozenNow, poseNow, frozenAll, poseAll, healsSeen;
    private GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        show = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        if ((!Application.isEditor && !Debug.isDebugBuild) || instance != null) return;
        var host = new GameObject("Zombie Anim Probe");
        RuntimeSceneHierarchy.Keep(host, "Dev");
        instance = host.AddComponent<ZombieAnimProbe>();
    }

    private void Awake()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            // The last play's record stays beside it: playing again to look doesn't lose what the last play showed.
            if (File.Exists(FilePath)) File.Copy(FilePath, Path.Combine(Folder, "zombie-anim.previous.txt"), true);
            writer = new StreamWriter(FilePath, false, new UTF8Encoding(false));
            Write("Zombie animation probe, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ", game " + Application.version
                + ", scene " + SceneManager.GetActiveScene().name);
            Write("Lines: each body within " + Near + " m every " + LineEvery + " s, and every change at once. FROZEN = moving in view with"
                + " its bones still for 1 s. POSE+MOVE = its calm pose or the first half of its getting up showing for 0.5 s while"
                + " it moves. HEAL = its graph found stopped or off the game clock and put back.");
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[ZombieAnimProbe] No record: " + problem.Message);
            writer = null;
        }
        SceneManager.activeSceneChanged += OnScene;
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnScene;
        Close();
        if (instance == this) instance = null;
    }

    private void OnApplicationQuit() => Close();

    private void OnScene(Scene from, Scene to) => Write("--- scene " + to.name + " at " + Time.time.ToString("0.0") + " s");

    private void Update()
    {
        var events = ZombieBody.ProbeEvents;
        while (events.Count > 0)
        {
            string happened = events.Dequeue();
            if (happened.Contains(" HEAL ")) healsSeen++;
            Write(happened);
        }
        float dt = Time.unscaledDeltaTime;
        if ((flushLeft -= dt) <= 0f)
        {
            flushLeft = 1f;
            try { writer?.Flush(); } catch (Exception) { writer = null; }
        }
        if ((lookLeft -= dt) > 0f) return;
        lookLeft = LookEvery;
        Look();
    }

    private void Look()
    {
        var mine = PlayerRoster.Local;
        var view = Camera.main;
        Vector3 from = mine != null ? mine.transform.position : view != null ? view.transform.position : Vector3.zero;
        float now = Time.time;
        frozenNow = poseNow = 0;
        labels.Clear();
        foreach (var body in ZombieBody.Probed)
        {
            if (body == null) continue;
            body.ProbeLook(LookEvery, out bool frozen, out bool poseMoving);
            if (frozen) frozenNow++;
            if (poseMoving) poseNow++;
            string flag = null;
            if (frozen) { if (frozenSaid.Add(body)) { flag = "FROZEN"; frozenAll++; } }
            else frozenSaid.Remove(body);
            if (poseMoving) { if (poseSaid.Add(body)) { flag = flag == null ? "POSE+MOVE" : flag + " POSE+MOVE"; poseAll++; } }
            else poseSaid.Remove(body);
            float distance = Vector3.Distance(from, body.transform.position);
            bool due = distance <= Near && (!lineAt.TryGetValue(body, out float at) || now - at >= LineEvery);
            if (flag != null || due)
            {
                line.Clear();
                if (flag != null) line.Append("!! ").Append(flag).Append(' ');
                line.Append(now.ToString("0.00")).Append(' ');
                body.ProbeLine(line, from);
                Write(line.ToString());
                lineAt[body] = now;
            }
            if (show && distance <= ShowWithin) labels.Add((body, body.ProbeLabel(frozen, poseMoving)));
        }
        if (lineAt.Count > 256) Forget();
    }

    // Bodies gone are dropped from the tallies whole, as a destroyed key can't be looked up.
    private void Forget()
    {
        var keep = new Dictionary<ZombieBody, float>();
        foreach (var pair in lineAt) if (pair.Key != null) keep[pair.Key] = pair.Value;
        lineAt.Clear();
        foreach (var pair in keep) lineAt[pair.Key] = pair.Value;
        frozenSaid.RemoveWhere(body => body == null);
        poseSaid.RemoveWhere(body => body == null);
    }

    private void OnGUI()
    {
        if (!show) return;
        var view = Camera.main;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = false };
        string head = "zombie anim probe: " + ZombieBody.Probed.Count + " bodies | FROZEN now " + frozenNow + " (all " + frozenAll
            + ") | POSE+MOVE now " + poseNow + " (all " + poseAll + ") | heals " + healsSeen + " | Logs/zombie-anim.txt";
        var headSize = style.CalcSize(new GUIContent(head));
        GUI.Box(new Rect(8f, 8f, headSize.x + 8f, headSize.y + 4f), GUIContent.none);
        GUI.Label(new Rect(12f, 10f, headSize.x, headSize.y), head, style);
        if (view == null) return;
        foreach (var (body, text) in labels)
        {
            if (body == null) continue;
            Vector3 over = body.transform.position + Vector3.up * 2.2f;
            Vector3 screen = view.WorldToScreenPoint(over);
            if (screen.z <= 0f) continue;
            var size = style.CalcSize(new GUIContent(text));
            var rect = new Rect(screen.x - size.x * .5f, Screen.height - screen.y - size.y, size.x + 6f, size.y + 2f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 3f, rect.y + 1f, size.x, size.y), text, style);
        }
    }

    /// <summary>
    /// /zanim: the labels on or off ("on", "off", nothing to switch), or "mark words" to write a line into the record
    /// (say what you just saw: "mark 3번 앉은 채로 옴").
    /// </summary>
    public static string Command(string args)
    {
        string text = (args ?? "").Trim();
        if (instance == null) return "좀비 애니메이션 기록은 에디터(또는 개발 빌드)에서만 돕니다."; // dev-text
        if (text.StartsWith("mark", StringComparison.OrdinalIgnoreCase))
        {
            instance.Write("--- mark at " + Time.time.ToString("0.0") + " s: " + text.Substring(4).Trim());
            return "기록에 표시를 남겼습니다(Logs/zombie-anim.txt)."; // dev-text
        }
        show = text.Equals("on", StringComparison.OrdinalIgnoreCase) || (!text.Equals("off", StringComparison.OrdinalIgnoreCase) && !show);
        return show ? "좀비 머리 위에 애니메이션 상태를 띄웁니다(30m 안). 기록은 늘 Logs/zombie-anim.txt" // dev-text
            : "좀비 머리 위 표시를 껐습니다. 기록은 계속 Logs/zombie-anim.txt에 남습니다."; // dev-text
    }

    /// <summary>A line into the record from elsewhere (/poses says what it set up).</summary>
    public static void Mark(string what)
    {
        if (instance != null) instance.Write("--- " + what + " at " + Time.time.ToString("0.0") + " s");
    }

    private void Write(string text)
    {
        if (writer == null || capped) return;
        try
        {
            written += text.Length + 1;
            if (written > MostBytes)
            {
                capped = true;
                writer.WriteLine("--- the record stops here (" + MostBytes / 1000000 + " MB)");
                writer.Flush();
                return;
            }
            writer.WriteLine(text);
        }
        catch (Exception) { writer = null; }
    }

    private void Close()
    {
        if (writer == null) return;
        try
        {
            writer.WriteLine("--- play ended at " + Time.time.ToString("0.0") + " s: FROZEN " + frozenAll + ", POSE+MOVE " + poseAll
                + ", heals " + healsSeen);
            writer.Flush();
            writer.Dispose();
        }
        catch (Exception) { }
        writer = null;
    }
}
