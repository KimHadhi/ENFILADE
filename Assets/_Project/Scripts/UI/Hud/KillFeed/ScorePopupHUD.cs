using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// The score popup, Black Ops 6's as the Garry's Mod addon "Black Ops VI Hitmarker" draws it in the user's video, picked on
/// 2026-11-22: "우리도 점수 팝업으로. 점수 체계도 저 애드온대로 … 점수 팝업은 언어 상관 없이 영문 통일".
/// <para>Right of and above the screen's middle stands a yellow total over rows of "+20 Elimination". Every kill gives one
/// row named by how it happened (Explosive Kill, Burn Kill, Headshot, Brutal Kill, else Elimination, the addon's order);
/// a kill made in the air adds Jumpshot; the third, fourth and every later kill of a chain (kills less than the chain's
/// seconds apart) add Slaughter, Warrior and God Slayer. Points for other things (a parry, a special kill, a tower floor)
/// add their own row at a share of their points. A row whose name is still showing takes the new points into itself
/// ("x2") and pops again. New rows come in on top, as in the video's build of the addon (its current build appends them
/// below and never merges). The total shows once two rows stand together, counts up toward the sum and fades when the
/// last row has gone.</para>
/// <para>Each row: its "+N" starts big and shrinks to size in .2 s, its name swings in from the left, overshoots and
/// settles (x = slide·sin θ, θ to ¾π), it fades in over .5 s, holds 3 s and fades over .33 s; rows from the third down
/// are dimmer. Places and sizes were measured off the video at 1080p (total's right edge x1122, baseline y449; rows' "+N"
/// right edge x1120, first baseline y476, rows 30.7 apart), timings read off the addon's code.</para>
/// <para>These points are the player's score and their experience (class ranks: Wallet.Experience), kept apart from
/// money, which kills pay as they always did (CombatFeedHUD.TotalScore → Wallet). Values and look: ScorePopup.csv; words:
/// Texts.csv score.line.* and the feed's own rows, always in English (Texts.InEnglish); font roles Score and ScoreLine
/// (Hitmarker Text, the addon video's face).</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class ScorePopupHUD : MonoBehaviour
{
    public const string TableName = "ScorePopup", ValuesRow = "Score", LookRow = "Look";

    /// <summary>Where and how the popup draws (ScorePopup.csv Look.*): 1920x1080 canvas units from the screen's middle, y up;
    /// rates per second.</summary>
    [System.Serializable]
    public sealed class Look
    {
        public float totalX = 162f, totalY = 91f, totalSize = 22f, totalPop = 24f;
        public float rowX = 160f, rowY = 64f, rowStep = 30.72f, plusSize = 14.5f, plusPop = 17f, nameSize = 14f;
        public float nameX = 115.2f, nameSlide = 76.8f, nameTurn = .75f, nameTurnRate = 10f;
        public Color totalColour = new Color(1f, .914f, .333f), lineColour = Color.white;
        public float rowSeconds = 3f, rowIn = 2f, rowOut = 3f, popOut = 5f, totalIn = 10f, totalOut = 5f, totalPopOut = 10f;
        public float count = 10f, slide = 10f, dim = .392f, outline = .2f, outlineAlpha = .55f, descent = .15f;
    }

    private sealed class Row
    {
        public RectTransform root; public TextMeshProUGUI plus, name;
        public string words; public int points, count; public float life, alpha, turn, pop, y;
    }

    /// <summary>Score this run: the player's experience, apart from money.</summary>
    public static long TotalScore { get; private set; }
    // The kills of the chain now (the addon's kill_count) and when the last one came.
    private static int chainKills;
    private static float lastKill = -99f;
    private static ScorePopupHUD instance;

    private readonly Look look = new Look();
    private readonly List<Row> rows = new List<Row>();
    private readonly Stack<Row> spare = new Stack<Row>();
    private TextMeshProUGUI total;
    private float totalAlpha, totalShown, totalPop = 1f;
    private long sum;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetScore()
    {
        TotalScore = 0;
        chainKills = 0;
        lastKill = -99f;
    }

    /// <summary>A score value (ScorePopup.csv Score.&lt;name&gt;).</summary>
    public static float Value(string name, float fallback) => GameTables.Number(TableName, ValuesRow, name, fallback);

    /// <summary>
    /// This PC's player's kill. Its row is the first that fits: an explosion's, a fire's, a headshot's, a blow's or blade's
    /// (an execution, a counter, a takedown, a thrown knife), else a plain Elimination; then Jumpshot when the player was
    /// in the air, then the chain's.
    /// </summary>
    public static void Kill(KillMarks marks, bool headshot, bool execution, bool counter, bool takedown)
    {
        if (!Application.isPlaying) return;
        const KillMarks Brutal = KillMarks.Melee | KillMarks.Blade | KillMarks.Shove | KillMarks.Thrown | KillMarks.Execution
            | KillMarks.Counter | KillMarks.Takedown;
        string kind = (marks & KillMarks.Explosion) != 0 ? "explosive" : (marks & KillMarks.Fire) != 0 ? "burn"
            : headshot ? "headshot" : execution || counter || takedown || (marks & Brutal) != 0 ? "brutal" : "elimination";
        Add("score.line." + kind, Mathf.RoundToInt(Value(kind, 20f)));
        var me = PlayerRoster.Local;
        if (me != null && !me.IsGrounded) Add("score.line.jumpshot", Mathf.RoundToInt(Value("jumpshot", 5f)));
        float now = Time.unscaledTime;
        // The kill streak's own window (CombatFeedHUD.StreakWindow), as the streak badge, the kill chain and the multi kill
        // count: one clock for every "kills in a row" (the user, 2026-11-22: "연속 처치 시간이랑 동기화 시켜야지").
        chainKills = now - lastKill <= CombatFeedHUD.StreakWindow ? chainKills + 1 : 1;
        lastKill = now;
        if (chainKills == 3) Add("score.line.slaughter", Mathf.RoundToInt(Value("slaughter", 5f)));
        else if (chainKills == 4) Add("score.line.warrior", Mathf.RoundToInt(Value("warrior", 5f)));
        else if (chainKills >= 5) Add("score.line.god_slayer", Mathf.RoundToInt(Value("godSlayer", 10f)));
    }

    /// <summary>Points for something else (a parry, a special kill, a floor): a row of its own words in English, worth the
    /// points times Score.perPoint (the share that keeps it worth to a kill what it was when money and score were one).</summary>
    public static void Points(string textId, int points, params object[] values)
    {
        if (!Application.isPlaying || points <= 0) return;
        Add(textId, Mathf.RoundToInt(points * Value("perPoint", .2f)), values);
    }

    private static void Add(string textId, int points, params object[] values)
    {
        if (points <= 0) return;
        TotalScore += points;
        if (!GunFeelSettings.Instance.scoreFeed) return;
        Get().AddRow(Texts.InEnglish(textId, values), points);
    }

    private static ScorePopupHUD Get()
    {
        if (instance == null)
        {
            var host = new GameObject("Score Popup", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            instance = host.AddComponent<ScorePopupHUD>();
        }
        return instance;
    }

    /// <summary>Makes the popup and a few rows ahead (behind the loading screen), so the first kill makes nothing.</summary>
    public static void Prewarm()
    {
        if (!Application.isPlaying) return;
        var self = Get();
        for (int i = self.rows.Count + self.spare.Count; i < 6; i++)
        {
            var row = self.MakeRow();
            row.root.gameObject.SetActive(false);
            self.spare.Push(row);
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        // Over the HUD, as the addon draws in DrawOverlay.
        FeelUI.SetupOverlay(gameObject, 27);
        GameTables.BindNested(TableName, LookRow, this, look);
        total = Label("Total", transform, FontRoles.Score, TextAlignmentOptions.BottomRight);
        total.rectTransform.pivot = new Vector2(1f, 0f);
        total.alpha = 0f;
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private TextMeshProUGUI Label(string name, Transform parent, string role, TextAlignmentOptions alignment)
    {
        var text = FeelUI.Label(name, parent, look.nameSize, alignment);
        FontRoles.Apply(text, role);
        var outlined = CombatUIAssets.Outlined(text.font, new Color(0f, 0f, 0f, look.outlineAlpha), look.outline);
        if (outlined != null) text.fontSharedMaterial = outlined;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(400f, 80f);
        return text;
    }

    private Row MakeRow()
    {
        var root = FeelUI.Rect("Score Row", transform);
        var row = new Row
        {
            root = root,
            plus = Label("Points", root, FontRoles.Score, TextAlignmentOptions.BottomRight),
            name = Label("Name", root, FontRoles.ScoreLine, TextAlignmentOptions.BottomLeft),
        };
        row.plus.rectTransform.pivot = new Vector2(1f, 0f);
        row.name.rectTransform.pivot = new Vector2(0f, 0f);
        return row;
    }

    private void AddRow(string words, int points)
    {
        float now = Time.unscaledTime;
        foreach (var row in rows)
            if (row.words == words && now <= row.life)
            {
                row.points += points; row.count++; row.life = now + look.rowSeconds; row.pop = 1f;
                sum += points;
                Paint(row);
                return;
            }
        var fresh = spare.Count > 0 ? spare.Pop() : MakeRow();
        fresh.root.gameObject.SetActive(true);
        fresh.words = words; fresh.points = points; fresh.count = 1; fresh.life = now + look.rowSeconds;
        fresh.alpha = 0f; fresh.turn = 0f; fresh.pop = 1f; fresh.y = 0f;
        rows.Insert(0, fresh);
        sum += points;
        Paint(fresh);
    }

    private static void Paint(Row row)
    {
        row.plus.text = "+" + row.points.ToString(CultureInfo.InvariantCulture);
        row.name.text = row.count > 1 ? row.words + " x" + row.count.ToString(CultureInfo.InvariantCulture) : row.words;
    }

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
        var l = look;
        // The total: shown once two rows stand together, kept while any is up, gone with the last.
        bool show = (totalAlpha < 1f && rows.Count > 1) || (totalAlpha >= 1f && rows.Count > 0);
        totalAlpha = Mathf.MoveTowards(totalAlpha, show ? 1f : 0f, dt * (show ? l.totalIn : l.totalOut));
        if (rows.Count == 0) sum = 0;
        if (totalAlpha <= 0f) { totalShown = sum; totalPop = 1f; }
        if (show) totalPop = Mathf.MoveTowards(totalPop, 0f, dt * l.totalPopOut);
        totalShown = Mathf.Lerp(totalShown, sum, Mathf.Clamp01(dt * l.count));
        total.alpha = totalAlpha;
        total.color = KitStyle.With(l.totalColour, totalAlpha);
        total.text = Mathf.RoundToInt(totalShown).ToString(CultureInfo.InvariantCulture);
        total.fontSize = l.totalSize + l.totalPop * totalPop;
        total.rectTransform.anchoredPosition = new Vector2(l.totalX, l.totalY - l.descent * total.fontSize);

        for (int i = rows.Count - 1; i >= 0; i--)
        {
            var row = rows[i];
            row.y = Mathf.Lerp(row.y, i, Mathf.Clamp01(dt * l.slide));
            bool alive = now <= row.life;
            row.alpha = Mathf.MoveTowards(row.alpha, alive ? 1f : 0f, dt * (alive ? l.rowIn : l.rowOut));
            if (alive) row.turn = Mathf.MoveTowards(row.turn, l.nameTurn * Mathf.PI, dt * l.nameTurnRate);
            row.pop = Mathf.MoveTowards(row.pop, 0f, dt * l.popOut);
            if (!alive && row.alpha <= 0f)
            {
                row.root.gameObject.SetActive(false);
                spare.Push(row);
                rows.RemoveAt(i);
                continue;
            }
            float baseline = l.rowY - row.y * l.rowStep;
            float dim = Mathf.Max(0f, i - 1) * l.dim;
            float alpha = Mathf.Clamp01(row.alpha - dim);
            row.plus.fontSize = l.plusSize + l.plusPop * row.pop;
            row.plus.rectTransform.anchoredPosition = new Vector2(l.rowX, baseline - l.descent * row.plus.fontSize);
            row.plus.color = KitStyle.With(l.lineColour, alpha);
            row.name.fontSize = l.nameSize;
            row.name.rectTransform.anchoredPosition = new Vector2(l.nameX + l.nameSlide * Mathf.Sin(row.turn), baseline - l.descent * l.nameSize);
            row.name.color = KitStyle.With(l.lineColour, alpha);
        }
    }
}
