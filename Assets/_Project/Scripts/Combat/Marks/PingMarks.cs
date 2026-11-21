using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pinged enemies (PlayerPing, stage 3 of the classes; the user's Back 4 Blood ping): F on an enemy marks it for the
/// whole team for a few seconds, on every PC (the pinger's at once, the others as PlayerNetworkCombat tells them): its
/// outline in the ping's colour all round its body, seen or not, a flat breathing fill over it where something stands
/// in front of it or smoke lies on the way (EnemyOutlines: smoke counts as a wall there since 2026-11-18, SmokeSight), and a
/// diamond over its head with how far it is. A plain ping is its pinger's own colour (PlayerTally.Color, the one the board and the chat show them in),
/// so the mark says who called it; until 2026-11-18 every ping was the HUD's pink whoever made it (the user: "콜아웃
/// 색상이 Tab 현황판의 팀원 색상(파랑, 주황 등)과 연동되지 않고 분홍색으로 고정되는 문제"). A Schießhund's is its teal
/// bounty mark, the class's own colour whoever plays it, which its class hunts (ClassKit) and lasts longer. Each player has one of each at a time: a new one moves it. A mark
/// ends with its time or its enemy's life. Made on first need and lives with the scene. Numbers: Tables/ClassAbilities,
/// Ping.* and Schiesshund.markSeconds. Any foe takes a mark (ICombatFoe): a zombie, or a target on the hub's range, whose
/// mark ends as it tips over.
/// </summary>
[DisallowMultipleComponent]
public sealed class PingMarks : MonoBehaviour
{
    // How solid the fill over its hidden parts is at the top of its breath, how far it dims between (a share of that), and
    // how many breaths a second: a smooth one, never a blink (the user's rule of 2026-11-14; until 2026-11-21 it was a
    // saw at two a second that jumped back to full).
    private const float FillAlpha = .62f, FillDim = .55f, Breaths = 1f;
    // The diamond's size, how far over the head it stands (m), how much of the screen's edge it keeps off (px at 1080p),
    // and how long a new mark takes to land (s).
    private const float DiamondSize = 22f, OverHead = .45f, EdgeMargin = 40f, LandSeconds = .18f;

    /// <summary>A Schießhund's bounty mark: teal (the user's).</summary>
    public static readonly Color BountyColor = new Color(.12f, .92f, .82f, 1f);

    private sealed class Mark
    {
        // A zombie or a range target, and the same as the foe it is.
        public Component Enemy;
        public ICombatFoe Foe;
        public PlayerMovement By;
        public bool Bounty;
        public float Until, Since;
        // Its colour as last drawn: marks are different colours at once now.
        public Color Colour;
        public RectTransform Marker;
        public DiamondGraphic Diamond;
        public TextMeshProUGUI Distance;
    }

    private static PingMarks instance;
    private readonly List<Mark> marks = new List<Mark>();
    private RectTransform canvas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>
    /// Marks this foe (a zombie, or a range target in the hub) as pinged by <paramref name="by"/>: a bounty if they play
    /// a Schießhund (PlayerPing decides on their own PC and says so to the others). Their earlier mark of the same kind
    /// moves here.
    /// </summary>
    public static void Ping(Component enemy, PlayerMovement by, bool bounty)
    {
        if (!Application.isPlaying || !CombatFoe.IsAlive(enemy)) return;
        if (instance == null)
        {
            instance = new GameObject("Ping Marks").AddComponent<PingMarks>();
            RuntimeSceneHierarchy.Group(instance.gameObject, RuntimeSceneHierarchy.Systems, "Combat");
        }
        instance.Add(enemy, (ICombatFoe)enemy, by, bounty);
    }

    /// <summary>Whether this enemy carries <paramref name="hunter"/>'s bounty mark now (a Schießhund's hunt: ClassKit, AttackTrace).</summary>
    public static bool IsBountyOf(Component enemy, PlayerMovement hunter)
    {
        if (instance == null || enemy == null || hunter == null) return false;
        foreach (var mark in instance.marks)
            if (mark.Bounty && mark.By == hunter && mark.Enemy != null && mark.Enemy.gameObject == enemy.gameObject && Time.time < mark.Until)
                return true;
        return false;
    }

    /// <summary>
    /// Whether anyone's mark (a ping or a bounty) is on this foe now: its outline is the mark's, so a Schießhund's bounty
    /// dye (DyeMarks), which outlines through the same channel, leaves it alone.
    /// </summary>
    public static bool IsMarked(Component enemy)
    {
        if (instance == null || enemy == null) return false;
        foreach (var mark in instance.marks)
            if (mark.Enemy != null && mark.Enemy.gameObject == enemy.gameObject && Time.time < mark.Until) return true;
        return false;
    }

    /// <summary>The foe carrying <paramref name="hunter"/>'s bounty mark now, or null (the HUD's).</summary>
    public static Component BountyOf(PlayerMovement hunter)
    {
        if (instance == null || hunter == null) return null;
        foreach (var mark in instance.marks)
            if (mark.Bounty && mark.By == hunter && mark.Enemy != null && mark.Foe.IsAlive && Time.time < mark.Until) return mark.Enemy;
        return null;
    }

    private void Awake()
    {
        var host = new GameObject("Ping markers", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        host.transform.SetParent(transform, false);
        var overlay = host.GetComponent<Canvas>();
        overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        overlay.sortingOrder = 72;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        canvas = (RectTransform)host.transform;
    }

    private void OnDestroy()
    {
        foreach (var mark in marks) Clear(mark);
        marks.Clear();
        if (instance == this) instance = null;
    }

    private void Add(Component enemy, ICombatFoe foe, PlayerMovement by, bool bounty)
    {
        // Their earlier mark of this kind goes; so does anyone's mark already on this enemy, which this one takes over.
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var old = marks[i];
            if ((old.By == by && old.Bounty == bounty) || old.Enemy == enemy) { Clear(old); marks.RemoveAt(i); }
        }
        float seconds = bounty ? PlayerClass.Ability("Schiesshund", "markSeconds", 15f) : PlayerClass.Ability("Ping", "seconds", 8f);
        var mark = new Mark
        {
            Enemy = enemy, Foe = foe, By = by, Bounty = bounty, Until = Time.time + Mathf.Max(.5f, seconds), Since = Time.unscaledTime
        };
        mark.Colour = ColourOf(mark);
        EnemyOutlines.Set(enemy, OutlineSource.Ping, LookOf(mark.Colour, 1f));
        mark.Marker = KitStyle.TopLeft(bounty ? "Bounty mark" : "Ping mark", canvas, Vector2.zero, new Vector2(140f, 60f));
        mark.Marker.anchorMin = mark.Marker.anchorMax = Vector2.zero;
        mark.Marker.pivot = new Vector2(.5f, 0f);
        mark.Diamond = HudShapes.Make<DiamondGraphic>("Diamond", mark.Marker, mark.Colour);
        var diamond = mark.Diamond.rectTransform;
        diamond.anchorMin = diamond.anchorMax = new Vector2(.5f, 0f);
        diamond.sizeDelta = Vector2.one * DiamondSize;
        diamond.anchoredPosition = new Vector2(0f, DiamondSize * .5f);
        mark.Distance = KitStyle.Label(KitStyle.TopLeft("Distance", mark.Marker, Vector2.zero, new Vector2(140f, 22f)), "", 14f,
            TextAlignmentOptions.Center, KitStyle.Ice);
        var label = mark.Distance.rectTransform.parent as RectTransform;
        label.anchorMin = label.anchorMax = new Vector2(.5f, 0f);
        label.pivot = new Vector2(.5f, 0f);
        label.anchoredPosition = new Vector2(0f, DiamondSize + 4f);
        marks.Add(mark);
    }

    private void Update()
    {
        float breath = .5f + .5f * Mathf.Cos(Time.unscaledTime * Breaths * 2f * Mathf.PI);
        var view = Camera.main;
        var local = PlayerRoster.Local;
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var mark = marks[i];
            if (mark.Enemy == null || !mark.Foe.IsAlive || Time.time >= mark.Until) { Clear(mark); marks.RemoveAt(i); continue; }
            // Its pinger's colour as it stands now: they may choose another while the mark is up.
            Color colour = ColourOf(mark);
            if (colour != mark.Colour)
            {
                mark.Colour = colour;
                if (mark.Diamond != null) mark.Diamond.color = colour;
            }
            EnemyOutlines.Set(mark.Enemy, OutlineSource.Ping, LookOf(colour, breath));
            Place(mark, view, local);
        }
    }

    // A bounty is the Schießhund's teal; a plain ping is its pinger's own colour, the HUD's pink once they have left.
    private static Color ColourOf(Mark mark)
        => mark.Bounty ? BountyColor : mark.By != null && mark.By.TryGetComponent(out PlayerTally tally) ? tally.Color : KitStyle.Accent;

    /// <summary>
    /// A mark's look on its body (EnemyOutlines; the editor's OutlineBench draws the same): the band in its colour seen or
    /// hidden, and over the hidden parts a fill that breathes (breath 1 at the top, 0 at the bottom).
    /// </summary>
    public static OutlineLook LookOf(Color colour, float breath)
    {
        var band = colour;
        band.a = 1f;
        var fill = colour;
        fill.a = FillAlpha * Mathf.Lerp(FillDim, 1f, Mathf.Clamp01(breath));
        return new OutlineLook(band, band, fill);
    }

    // The diamond over its head, landing from large as the mark is made, kept on screen at its edge; how far off it is.
    private void Place(Mark mark, Camera view, PlayerMovement local)
    {
        if (mark.Marker == null) return;
        // Over its head: a zombie's capsule's top, a range target's head part (CombatFoe.Top).
        Vector3 head = CombatFoe.Top(mark.Enemy) + Vector3.up * OverHead;
        Vector3 screen = view != null ? view.WorldToScreenPoint(head) : Vector3.zero;
        bool shown = view != null && screen.z > 0f;
        mark.Marker.gameObject.SetActive(shown);
        if (!shown) return;
        float scale = canvas.rect.width / Mathf.Max(1f, Screen.width);
        Vector2 at = new Vector2(screen.x * scale, screen.y * scale);
        at.x = Mathf.Clamp(at.x, EdgeMargin, canvas.rect.width - EdgeMargin);
        at.y = Mathf.Clamp(at.y, EdgeMargin, canvas.rect.height - EdgeMargin - 40f);
        mark.Marker.anchoredPosition = at;
        float landed = Mathf.Clamp01((Time.unscaledTime - mark.Since) / LandSeconds);
        mark.Marker.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, KitReveal.EaseOut(landed));
        // A bounty's diamond turns slowly, the hound's eye on it.
        mark.Diamond.rectTransform.localRotation = mark.Bounty ? Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 3f) * 12f) : Quaternion.identity;
        float metres = local != null ? Vector3.Distance(local.transform.position, mark.Enemy.transform.position) : 0f;
        string text = Mathf.RoundToInt(metres) + "m";
        if (mark.Distance.text != text) mark.Distance.text = text;
    }

    private static void Clear(Mark mark)
    {
        if (mark.Enemy != null)
        {
            EnemyOutlines.Clear(mark.Enemy, OutlineSource.Ping);
            // A bounty dye's outline is the dye's to say again: DyeMarks says it every frame the dye lasts and no mark is on
            // the foe, so one that ran out under this mark would otherwise stay (as it was when both shared one outline).
            EnemyOutlines.Clear(mark.Enemy, OutlineSource.Dye);
        }
        if (mark.Marker != null) Destroy(mark.Marker.gameObject);
    }
}
