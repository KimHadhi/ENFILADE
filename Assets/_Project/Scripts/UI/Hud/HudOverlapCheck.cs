using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
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
/// HUD setting it logs it once, with both pieces, the depth in px and where. Tools → HUD → 겹침 검사 (HudOverlapSweep)
/// sweeps the screen shapes, the HUD's angle, tilt and lean and the pieces' passing states (IHudPreview) and writes
/// Logs/HudOverlap/report.txt with a picture of every problem, for anyone who cannot run the game to read.
/// </summary>
[DisallowMultipleComponent]
public sealed class HudOverlapCheck : MonoBehaviour
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

    /// <summary>Tools → HUD → 겹침 검사 holds the watch while it sweeps, so it does not log what the sweep sets up.</summary>
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
        DontDestroyOnLoad(host);
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

    // ---- Measuring ----

    /// <summary>
    /// Measures the HUD as drawn this frame and fills <paramref name="findings"/> with every overlap between two pieces
    /// and every piece past the screen's edge, deepest first. Call after the frame is drawn (end of frame) for this
    /// frame's letters and meshes.
    /// </summary>
    public static void Measure(List<Finding> findings)
    {
        var s = Current;
        findings.Clear();
        Gather(s);
        float tolerance = Mathf.Max(0f, s.tolerance);
        for (int i = 0; i < measured.Count; i++)
        {
            var a = measured[i];
            Outside(a, s, findings);
            for (int j = i + 1; j < measured.Count; j++)
            {
                var b = measured[j];
                if (!a.Bounds.Overlaps(b.Bounds)) continue;
                float deepest = 0f;
                Vector2[] shapeA = null, shapeB = null;
                foreach (var one in a.Shapes)
                {
                    Rect boxA = Box(one);
                    foreach (var other in b.Shapes)
                    {
                        if (!boxA.Overlaps(Box(other))) continue;
                        float depth = Depth(one, other);
                        if (depth > deepest) { deepest = depth; shapeA = one; shapeB = other; }
                    }
                }
                if (deepest > tolerance)
                    findings.Add(new Finding { A = a.Name, B = b.Name, Depth = deepest, At = (Middle(shapeA) + Middle(shapeB)) * .5f, ShapeA = shapeA, ShapeB = shapeB, ScreenHeight = Screen.height });
            }
        }
        findings.Sort((x, y) => y.Depth.CompareTo(x.Depth));
    }

    /// <summary>The pieces measured last, by name, each with how many shapes it had: what the check sees as the HUD.</summary>
    public static string Pieces()
    {
        var text = new StringBuilder();
        foreach (var piece in measured) text.Append(piece.Name).Append(" (").Append(piece.Shapes.Count).Append(")\n");
        return text.ToString();
    }

    private static void Outside(Piece piece, Settings s, List<Finding> findings)
    {
        float edge = Mathf.Max(0f, s.edge), width = Screen.width, height = Screen.height;
        float worst = 0f;
        Vector2[] worstShape = null;
        foreach (var shape in piece.Shapes)
            foreach (var p in shape)
            {
                float past = Mathf.Max(Mathf.Max(-p.x, p.x - width), Mathf.Max(-p.y, p.y - height));
                if (past > worst) { worst = past; worstShape = shape; }
            }
        if (worst > edge) findings.Add(new Finding { A = piece.Name, Depth = worst, At = Middle(worstShape), ShapeA = worstShape, ScreenHeight = Screen.height });
    }

    private static void Gather(Settings s)
    {
        if (rules == null) ReadRules();
        foreach (var piece in pieces.Values) { piece.Shapes.Clear(); piece.Empty = true; }
        measured.Clear();
        kinds.Clear();
        canvases.Clear();
        canvases.AddRange(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        float screenArea = Mathf.Max(1f, (float)Screen.width * Screen.height);
        foreach (var canvas in canvases)
        {
            if (!canvas.isRootCanvas || !canvas.isActiveAndEnabled) continue;
            // A world canvas is part of the scene; a camera canvas without its camera is drawn as an overlay.
            if (canvas.renderMode == RenderMode.WorldSpace || Kind(canvas.transform) == SkipKind) continue;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            graphics.Clear();
            canvas.GetComponentsInChildren(false, graphics);
            foreach (var graphic in graphics)
            {
                if (graphic == null || !graphic.enabled || graphic.canvasRenderer.cull || graphic is TMP_SubMeshUI) continue;
                var root = PieceOf(graphic.transform, canvas.transform);
                if (root == null) continue;
                if (!pieces.TryGetValue(root, out var piece))
                    pieces[root] = piece = new Piece { Root = root, Name = PathOf(root) };
                MeasureGraphic(graphic, camera, piece, s, screenArea);
            }
        }
        // Pieces whose transform has gone are dropped; the rest are measured if anything of them is drawn.
        var dead = new List<Transform>();
        foreach (var pair in pieces)
        {
            if (pair.Key == null) { dead.Add(pair.Key); continue; }
            if (!pair.Value.Empty) measured.Add(pair.Value);
        }
        foreach (var key in dead) pieces.Remove(key);
    }

    private static void MeasureGraphic(Graphic graphic, Camera camera, Piece piece, Settings s, float screenArea)
    {
        float alpha = graphic.canvasRenderer.GetInheritedAlpha();
        if (alpha * graphic.color.a < s.faint) return;
        var rect = graphic.rectTransform;
        rect.GetWorldCorners(corners);
        var box = new Vector2[4];
        for (int i = 0; i < 4; i++) box[i] = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
        if (Mathf.Abs(SignedArea(box)) > s.backdrop * screenArea) return;
        Masks(rect, camera);
        if (graphic is TMP_Text text) Letters(text, camera, piece, s, alpha);
        else Triangles(graphic, camera, piece, s, alpha);
    }

    // Each visible letter's box; a letter's own alpha (rich text) counts.
    private static void Letters(TMP_Text text, Camera camera, Piece piece, Settings s, float alpha)
    {
        var info = text.textInfo;
        if (info == null || info.characterInfo == null) return;
        var t = text.rectTransform;
        int count = Mathf.Min(info.characterCount, info.characterInfo.Length);
        for (int i = 0; i < count; i++)
        {
            var c = info.characterInfo[i];
            if (!c.isVisible || alpha * (c.color.a / 255f) < s.faint) continue;
            var shape = new[]
            {
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(c.bottomLeft)),
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(c.topLeft)),
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(c.topRight)),
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(c.bottomRight)),
            };
            Keep(piece, shape);
        }
    }

    // The graphic's triangles as the canvas builds them; triangles too faint to see are left out.
    private static void Triangles(Graphic graphic, Camera camera, Piece piece, Settings s, float alpha)
    {
        if (populate == null)
            populate = typeof(Graphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null);
        if (populate == null) return;
        var rect = graphic.rectTransform.rect;
        if (rect.width < 0f || rect.height < 0f) return;
        helper ??= new VertexHelper();
        helper.Clear();
        populateArgs[0] = helper;
        try
        {
            populate.Invoke(graphic, populateArgs);
            modifiers.Clear();
            graphic.GetComponents(typeof(IMeshModifier), modifiers);
            foreach (var modifier in modifiers)
                if (!(modifier is Behaviour behaviour) || behaviour.isActiveAndEnabled) ((IMeshModifier)modifier).ModifyMesh(helper);
        }
        catch (System.Exception)
        {
            // A graphic that cannot build its mesh outside a canvas rebuild is measured by its box.
            helper.Clear();
            Keep(piece, Box(graphic.rectTransform, camera));
            return;
        }
        stream.Clear();
        helper.GetUIVertexStream(stream);
        var t = graphic.rectTransform;
        for (int i = 0; i + 2 < stream.Count; i += 3)
        {
            float shown = Mathf.Max(stream[i].color.a, Mathf.Max(stream[i + 1].color.a, stream[i + 2].color.a)) / 255f;
            if (shown * alpha < s.faint) continue;
            var shape = new[]
            {
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(stream[i].position)),
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(stream[i + 1].position)),
                RectTransformUtility.WorldToScreenPoint(camera, t.TransformPoint(stream[i + 2].position)),
            };
            if (Mathf.Abs(SignedArea(shape)) < .25f) continue;
            Keep(piece, shape);
        }
    }

    private static Vector2[] Box(RectTransform rect, Camera camera)
    {
        rect.GetWorldCorners(corners);
        var box = new Vector2[4];
        for (int i = 0; i < 4; i++) box[i] = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
        return box;
    }

    // The RectMask2D and Mask boxes above a graphic, up to its canvas, on the screen.
    private static void Masks(RectTransform from, Camera camera)
    {
        masks.Clear();
        for (Transform t = from; t != null; t = t.parent)
        {
            if (t.TryGetComponent(out RectMask2D rectMask) && rectMask.isActiveAndEnabled) masks.Add(Box(rectMask.rectTransform, camera));
            else if (t.TryGetComponent(out Mask mask) && mask.isActiveAndEnabled) masks.Add(Box(mask.rectTransform, camera));
            if (t.TryGetComponent(out Canvas canvas) && canvas.isRootCanvas) break;
        }
    }

    // A shape cut by the masks found for its graphic, kept if anything of it is left.
    private static void Keep(Piece piece, Vector2[] shape)
    {
        foreach (var mask in masks)
        {
            shape = Clip(shape, mask);
            if (shape == null) return;
        }
        piece.Add(shape);
    }

    // ---- Pieces ----

    // The piece a graphic belongs to, or null when HudOverlap.csv leaves it out: the nearest piece row above it, else the
    // child of the nearest group above it (its canvas is a group unless listed).
    private static Transform PieceOf(Transform from, Transform canvasRoot)
    {
        Transform found = null, child = null;
        for (Transform t = from; t != null; t = t.parent)
        {
            string kind = Kind(t);
            if (kind == SkipKind) return null;
            if (kind == null && t == canvasRoot) kind = GroupKind;
            if (found == null)
            {
                if (kind == PieceKind) found = t;
                else if (kind == GroupKind) found = child != null ? child : t;
            }
            if (t == canvasRoot) break;
            child = t;
        }
        return found;
    }

    private static string Kind(Transform t)
    {
        if (kinds.TryGetValue(t, out string known)) return known;
        string kind = null;
        foreach (var rule in rules)
        {
            if (!Matches(rule, t)) continue;
            kind = rule.Kind;
            break;
        }
        kinds[t] = kind;
        return kind;
    }

    private static bool Matches(Rule rule, Transform t)
    {
        for (int i = 0; i < rule.Path.Length; i++, t = t.parent)
        {
            if (t == null) return false;
            string name = Plain(t.name), wanted = rule.Path[i];
            bool same = i == 0 && rule.Prefix ? name.StartsWith(wanted, System.StringComparison.OrdinalIgnoreCase)
                : string.Equals(name, wanted, System.StringComparison.OrdinalIgnoreCase);
            if (!same) return false;
        }
        return true;
    }

    private static string Plain(string name) => name.EndsWith("(Clone)") ? name.Substring(0, name.Length - 7).TrimEnd() : name;

    private static void ReadRules()
    {
        rules = new List<Rule>();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !row.TryGetValue("kind", out string kind)) continue;
            var path = id.Split('/');
            System.Array.Reverse(path);
            for (int i = 0; i < path.Length; i++) path[i] = path[i].Trim();
            bool prefix = path[0].EndsWith("*");
            if (prefix) path[0] = path[0].Substring(0, path[0].Length - 1);
            rules.Add(new Rule { Path = path, Prefix = prefix, Kind = kind.Trim().ToLowerInvariant() });
        }
        // The longest paths first, so "Alerts/Mark" wins over a plain "Mark"; exact names before prefixes.
        rules.Sort((a, b) => a.Path.Length != b.Path.Length ? b.Path.Length.CompareTo(a.Path.Length) : a.Prefix.CompareTo(b.Prefix));
    }

    // A piece's name as the log writes it: its path from its canvas, without groups' "(Clone)".
    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent)
        {
            parts.Add(Plain(t.name));
            if (t.TryGetComponent(out Canvas canvas) && canvas.isRootCanvas) break;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    // ---- Shapes ----

    private static Rect Box(Vector2[] shape)
    {
        float xMin = float.PositiveInfinity, yMin = float.PositiveInfinity, xMax = float.NegativeInfinity, yMax = float.NegativeInfinity;
        foreach (var p in shape)
        {
            if (p.x < xMin) xMin = p.x;
            if (p.y < yMin) yMin = p.y;
            if (p.x > xMax) xMax = p.x;
            if (p.y > yMax) yMax = p.y;
        }
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static Vector2 Middle(Vector2[] shape)
    {
        if (shape == null || shape.Length == 0) return Vector2.zero;
        Vector2 sum = Vector2.zero;
        foreach (var p in shape) sum += p;
        return sum / shape.Length;
    }

    private static float SignedArea(Vector2[] shape)
    {
        float area = 0f;
        for (int i = 0; i < shape.Length; i++)
        {
            Vector2 a = shape[i], b = shape[(i + 1) % shape.Length];
            area += a.x * b.y - b.x * a.y;
        }
        return area * .5f;
    }

    // How far two convex shapes would have to move apart to clear each other (px); 0 when they do not overlap.
    private static float Depth(Vector2[] a, Vector2[] b)
    {
        float best = float.PositiveInfinity;
        if (!Separates(a, a, b, ref best) || !Separates(b, a, b, ref best)) return 0f;
        return float.IsInfinity(best) ? 0f : best;
    }

    // Tries the edge normals of one shape as separating axes: false when one of them separates the shapes; best is the
    // smallest overlap along any axis tried.
    private static bool Separates(Vector2[] edges, Vector2[] a, Vector2[] b, ref float best)
    {
        for (int i = 0; i < edges.Length; i++)
        {
            Vector2 along = edges[(i + 1) % edges.Length] - edges[i];
            var normal = new Vector2(-along.y, along.x);
            float length = normal.magnitude;
            if (length < 1e-5f) continue;
            normal /= length;
            Project(a, normal, out float aMin, out float aMax);
            Project(b, normal, out float bMin, out float bMax);
            float overlap = Mathf.Min(aMax - bMin, bMax - aMin);
            if (overlap <= 0f) return false;
            if (overlap < best) best = overlap;
        }
        return true;
    }

    private static void Project(Vector2[] shape, Vector2 axis, out float min, out float max)
    {
        min = float.PositiveInfinity;
        max = float.NegativeInfinity;
        foreach (var p in shape)
        {
            float d = Vector2.Dot(p, axis);
            if (d < min) min = d;
            if (d > max) max = d;
        }
    }

    // Sutherland-Hodgman: what of a convex shape lies inside a convex clip shape (either winding); null when nothing.
    private static Vector2[] Clip(Vector2[] subject, Vector2[] clip)
    {
        float winding = Mathf.Sign(SignedArea(clip));
        if (winding == 0f) return null;
        var output = new List<Vector2>(subject);
        for (int e = 0; e < clip.Length && output.Count > 0; e++)
        {
            Vector2 a = clip[e], b = clip[(e + 1) % clip.Length];
            var input = output;
            output = new List<Vector2>(input.Count + 2);
            for (int i = 0; i < input.Count; i++)
            {
                Vector2 p = input[i], q = input[(i + 1) % input.Count];
                bool pIn = Inside(a, b, p, winding), qIn = Inside(a, b, q, winding);
                if (pIn)
                {
                    output.Add(p);
                    if (!qIn) output.Add(Crossing(a, b, p, q));
                }
                else if (qIn) output.Add(Crossing(a, b, p, q));
            }
        }
        return output.Count >= 3 ? output.ToArray() : null;
    }

    private static bool Inside(Vector2 a, Vector2 b, Vector2 p, float winding)
        => winding * ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) >= 0f;

    private static Vector2 Crossing(Vector2 a, Vector2 b, Vector2 p, Vector2 q)
    {
        Vector2 r = q - p, s = b - a;
        float across = r.x * s.y - r.y * s.x;
        if (Mathf.Abs(across) < 1e-6f) return p;
        float t = ((a.x - p.x) * s.y - (a.y - p.y) * s.x) / across;
        return p + r * t;
    }
}
