using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The standing check that no two HUD pieces overlap and none runs off the screen. It came out of three in one day
/// (2026-11-12): the stance figure stood over the shield numbers, an assault's time row lay over the 목표·점수판 hint
/// ("아니 TAB이랑 목표 점수판 저거랑 겹치는데?"), and a 4:3 screen cut the left HUD in half. Each piece was placed by
/// its own code with no idea of the others, so the check measures what is really drawn, in screen pixels, through the
/// camera each canvas is drawn by: the HUD's planes and their angle, leans, perspective and every piece's own tilt are all
/// in it. It takes every visible graphic's triangles as the canvas builds them (Graphic.OnPopulateMesh and its mesh
/// effects) and every visible letter's box, cut by the RectMask2D and Mask they sit in.
/// Graphics are gathered into pieces by HudOverlap.csv: the children of a group are pieces of their own (a canvas is a
/// group unless listed otherwise; the panels and planes are listed), a piece row makes something inside another piece a
/// piece of its own, and a skip row leaves something out with all it holds (world marks, the crosshair, screen effects,
/// menus). Anything covering more than Overlap.backdrop of the screen is a backdrop, not a piece. Two pieces overlap when
/// any of their shapes do by more than Overlap.tolerance px (the distance one would have to move to clear the other).
/// In the editor and development builds it watches in Play (Hud.csv Overlap.watch): every Overlap.interval seconds while
/// no menu is open it looks, and the first time an overlap or a run-off lasts Overlap.persist looks on a screen size and
/// HUD setting it logs it once, with both pieces, the depth in px and where. ENFILADE → HUD → 겹침 검사 (HudOverlapSweep)
/// sweeps the screen shapes, the HUD's angle, tilt and lean and the pieces' passing states (IHudPreview) and writes
/// Logs/HudOverlap/report.txt with a picture of every problem, for anyone who cannot run the game to read.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class HudOverlapCheck : MonoBehaviour
{
    public const string TableName = "HudOverlap";
    private const string GroupKind = "group", PieceKind = "piece", SkipKind = "skip";

    [System.Serializable]
    public sealed class Settings
    {
        [Tooltip("Whether the check watches in Play (editor and development builds only).")]
        public bool watch = true;
        [Tooltip("Seconds between two looks while it watches.")]
        public float interval = .25f;
        [Tooltip("Looks in a row an overlap must last before it is logged, so a slide or a flash passing by is not.")]
        public int persist = 2;
        [Tooltip("How deep two pieces must overlap to count (px on the screen).")]
        public float tolerance = 1f;
        [Tooltip("How far a piece may run past the screen's edge before it counts (px).")]
        public float edge = 1f;
        [Tooltip("Graphics fainter than this (alpha, with their CanvasGroups) are not counted as drawn.")]
        public float faint = .08f;
        [Tooltip("A graphic covering more than this share of the screen is a backdrop or a screen effect, not a piece.")]
        public float backdrop = .35f;
        [Tooltip("While watching, outline what it finds on the screen for a few seconds (1/0). The sweep always does for its pictures.")]
        public bool draw;
        [Tooltip("The view's roll at a full Q/E lean (°); the sweep holds the HUD at this times Hud.csv Lean.share each way.")]
        public float lean = 18f;
    }

    /// <summary>One overlap (two pieces) or run-off (B is null): how deep (px), and where on the screen.</summary>
    public struct Finding
    {
        public string A, B;
        public float Depth;
        public Vector2 At;
        public Vector2[] ShapeA, ShapeB;
        // The screen's height when it was measured, for reading the place from the top.
        public int ScreenHeight;
        public bool OffScreen => B == null;
        public string Key => OffScreen ? A : string.CompareOrdinal(A, B) < 0 ? A + " | " + B : B + " | " + A;

        public override string ToString()
        {
            // Screen y counts from the bottom; people read from the top.
            string at = $"왼쪽에서 {At.x:0}, 위에서 {(ScreenHeight > 0 ? ScreenHeight : Screen.height) - At.y:0}";
            return OffScreen ? $"{A}: 화면 밖으로 {Depth:0.#} px ({at})" : $"{A} ↔ {B}: {Depth:0.#} px 겹침 ({at})";
        }
    }

    private sealed class Piece
    {
        public Transform Root;
        public string Name;
        public readonly List<Vector2[]> Shapes = new List<Vector2[]>();
        public Rect Bounds;
        public bool Empty = true;

        public void Add(Vector2[] shape)
        {
            Shapes.Add(shape);
            Rect box = Box(shape);
            Bounds = Empty ? box : Rect.MinMaxRect(Mathf.Min(Bounds.xMin, box.xMin), Mathf.Min(Bounds.yMin, box.yMin),
                Mathf.Max(Bounds.xMax, box.xMax), Mathf.Max(Bounds.yMax, box.yMax));
            Empty = false;
        }
    }

    // One HudOverlap.csv row: its name and the names of the parents above it, nearest first (a row "Alerts/Mark" is a Mark
    // right under Alerts), whether its last name is a prefix (ends in *), and its kind.
    private sealed class Rule
    {
        public string[] Path;
        public bool Prefix;
        public string Kind;
    }

    private static HudOverlapCheck instance;
    private static readonly Settings settings = new Settings();
    private static List<Rule> rules;
    private static readonly List<Canvas> canvases = new List<Canvas>();
    private static readonly List<Graphic> graphics = new List<Graphic>();
    private static readonly Dictionary<Transform, Piece> pieces = new Dictionary<Transform, Piece>();
    private static readonly Dictionary<Transform, string> kinds = new Dictionary<Transform, string>();
    private static readonly List<Piece> measured = new List<Piece>();
    private static readonly List<Vector2[]> masks = new List<Vector2[]>();
    private static readonly List<UIVertex> stream = new List<UIVertex>();
    private static readonly List<Component> modifiers = new List<Component>();
    private static readonly Vector3[] corners = new Vector3[4];
    private static readonly object[] populateArgs = new object[1];
    private static VertexHelper helper;
    private static MethodInfo populate;

    // While watching: how many looks in a row each finding has lasted, and what was already logged on which screen.
    private readonly Dictionary<string, int> lasting = new Dictionary<string, int>();
    private readonly HashSet<string> logged = new HashSet<string>();
    private readonly List<Finding> found = new List<Finding>();
    private float nextLook;

    /// <summary>ENFILADE → HUD → 겹침 검사 holds the watch while it sweeps, so it does not log what the sweep sets up.</summary>
    public static bool Sweeping { get; set; }

    /// <summary>The check's numbers (Hud.csv Overlap), bound when the check's host is first made. Play only.</summary>
    public static Settings Current
    {
        get
        {
            Host();
            return settings;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        rules = null;
        Sweeping = false;
        HudCamera.LeanHeld = float.NaN;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartWatching() => Host();
#endif

    private static HudOverlapCheck Host()
    {
        if (instance != null) return instance;
        var host = new GameObject("HUD Overlap Check");
        RuntimeSceneHierarchy.Keep(host, "Dev");
        instance = host.AddComponent<HudOverlapCheck>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
        GameTables.BindNested(HudCamera.TableName, "Overlap", this, settings);
        GameTables.Reloaded += ForgetRules;
    }

    private void OnDestroy()
    {
        GameTables.Reloaded -= ForgetRules;
        if (instance == this) instance = null;
    }

    private static void ForgetRules() => rules = null;

    private void OnEnable() => StartCoroutine(Watch());

    private IEnumerator Watch()
    {
        var end = new WaitForEndOfFrame();
        while (true)
        {
            // After the frame is drawn, so every text has its letters and every graphic its mesh for this frame.
            yield return end;
            if (!settings.watch || Sweeping || Time.unscaledTime < nextLook) continue;
            nextLook = Time.unscaledTime + Mathf.Max(.05f, settings.interval);
            // A menu, the scoreboard or a pause screen covers the HUD on purpose.
            if (Cursor.lockState != CursorLockMode.Locked || ScoreboardHUD.Openness > .01f) { lasting.Clear(); continue; }
            Measure(found);
            Note(found);
        }
    }

    // Logs each finding once per screen and setting, when it has lasted long enough.
    private void Note(List<Finding> findings)
    {
        string situation = Situation();
        var now = new HashSet<string>();
        var drawnOverlaps = new List<Vector2[]>();
        var drawnOutside = new List<Vector2[]>();
        foreach (var finding in findings)
        {
            string key = finding.Key;
            now.Add(key);
            lasting.TryGetValue(key, out int looks);
            lasting[key] = ++looks;
            if (looks < Mathf.Max(1, settings.persist)) continue;
            if (finding.OffScreen) drawnOutside.Add(finding.ShapeA);
            else { drawnOverlaps.Add(finding.ShapeA); drawnOverlaps.Add(finding.ShapeB); }
            if (logged.Add(situation + " | " + key))
                Debug.LogWarning($"[HUD 겹침] {situation}: {finding}");
        }
        var gone = new List<string>();
        foreach (var key in lasting.Keys) if (!now.Contains(key)) gone.Add(key);
        foreach (var key in gone) lasting.Remove(key);
        if (settings.draw && (drawnOverlaps.Count > 0 || drawnOutside.Count > 0)) HudOverlapMarks.Show(drawnOverlaps, drawnOutside, 3f);
    }

    /// <summary>The screen and HUD settings a finding was seen at, as the log and the report write them.</summary>
    public static string Situation()
    {
        var s = GameSettings.Current;
        float lean = HudCamera.LeanRoll;
        return $"{Screen.width}×{Screen.height} ({Aspect(Screen.width, Screen.height)}) · 원근 {s.hudAngle:0.#}° · 기울기 {s.hudTilt:0.#}"
            + (Mathf.Abs(lean) > .05f ? $" · 기울이기 {lean:+0.#;-0.#}°" : "");
    }

    /// <summary>A screen's shape as people say it (16:9, 21:9), or the ratio itself.</summary>
    public static string Aspect(int width, int height)
    {
        if (height <= 0) return "?";
        float ratio = (float)width / height;
        if (Mathf.Abs(ratio - 16f / 9f) < .02f) return "16:9";
        if (Mathf.Abs(ratio - 16f / 10f) < .02f) return "16:10";
        if (Mathf.Abs(ratio - 4f / 3f) < .02f) return "4:3";
        if (Mathf.Abs(ratio - 5f / 4f) < .02f) return "5:4";
        if (Mathf.Abs(ratio - 21f / 9f) < .04f || Mathf.Abs(ratio - 2560f / 1080f) < .04f) return "21:9";
        if (Mathf.Abs(ratio - 32f / 9f) < .04f) return "32:9";
        return ratio.ToString("0.00") + ":1";
    }
}
