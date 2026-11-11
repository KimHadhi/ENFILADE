using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Call of Duty (MW3)-style kill rewards. A "110 XP" number slams in on each kill over the events that earned it
/// (☠ 처치, 헤드샷, 원거리 사격, 관통 사살, 처형, 반격, 더블 킬 ...), hung off the top right of the gun like Dead Space's ammo count
/// (GunScoreAnchor) so the middle of the screen stays clear. 처형 and 반격 carry their own icon
/// (knife with a slash trail, return arrow) in the line colour. The number accumulates over a kill streak and the group fades after a quiet spell. Only kills score, so sustained fire stays quiet.
/// A rolling money total (Wallet: points are money in play, a number beside a banknote icon; no experience on screen)
/// and a kill feed sit at the top right. Created on first use during Play; GunFeel → Score.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.CombatFeedHud)]
public sealed class CombatFeedHUD : MonoBehaviour
{
    private const int MaxLines = 4, MaxKills = 5;
    private const string TableName = "KillFeed", TableRow = "Default";
    private const float KillLifetime = 5f, LineHeight = 25f, FadeSeconds = .3f, IconSize = 17f;
    private static readonly Color Soft = new Color(.86f, .88f, .92f), KillRed = new Color(1f, .2f, .14f), Number = new Color(1f, .96f, .86f);
    private static CombatFeedHUD instance;

    public static long TotalScore { get; private set; }
    /// <summary>A new run starts from nothing, whatever the last room earned.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetScore() => TotalScore = 0;

    private enum LineIcon { None, Skull, Headshot, Counter, Execution }
    private sealed class Line
    {
        public RectTransform root; public TextMeshProUGUI label; public RawImage icon; public Shadow iconShadow;
        public float age; public bool accent, tintIcon; public Color? tint;
    }

    private RectTransform streakRoot, linesRoot, totalRoot, feedRoot;
    private TextMeshProUGUI streakLabel, totalLabel;
    private RawImage totalIcon;
    private Image streakBracket;
    private CanvasGroup streakGroup;
    private readonly List<Line> lines = new List<Line>();
    private readonly Stack<Line> spareLines = new Stack<Line>();
    private readonly List<KillFeedRow> kills = new List<KillFeedRow>(), shown = new List<KillFeedRow>();
    private readonly Stack<KillFeedRow> spareKills = new Stack<KillFeedRow>();
    private readonly KillFeedLook look = new KillFeedLook();
    private int streakScore, multiKill;
    private bool fresh;
    private float streakIdle = 99f, popAge = 99f, totalPop, lastKillTime = -99f;
    // The money shown, rolling up to the balance (won; double, as a float can't hold big sums to the won).
    private double totalShown;

    public string StreakText => streakLabel != null ? streakLabel.text : string.Empty;

    private static CombatFeedHUD Get()
    {
        if (instance == null)
        {
            var host = new GameObject("Combat Feed", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = host.AddComponent<CombatFeedHUD>();
        }
        return instance;
    }

    /// <summary>distance: shooter to hit point, metres (0 when unknown); gun kills past Long Range Distance earn a bonus. feedRow is false when the host will send the row.
    /// pierced: the killing bullet went through or off something first (관통 사살). standout: the enemy was named or elite, so
    /// its row burns magenta (backlog 325). takedown: an execution of a zombie that never knew they were there (the
    /// stealth round's 암살, Stealth.csv Score.takedown on top of the execution's). bounty: points a Schießhund's own mark
    /// pays on top (현상금, ClassAbilities Schiesshund.bountyMoney / bountyElite), 0 for none.</summary>
    public static void Kill(string weapon, string target, bool headshot, bool execution, bool counter, bool bullet, float distance, bool feedRow,
        bool pierced = false, bool standout = false, bool takedown = false, int bounty = 0)
    {
        if (!Application.isPlaying) return;
        var settings = GunFeelSettings.Instance;
        var self = Get();
        float now = Time.unscaledTime;
        // The kill feed's window, so the popup's multi kill and the feed's numeral count the same kills.
        self.multiKill = now - self.lastKillTime <= StreakWindow ? self.multiKill + 1 : 1;
        self.lastKillTime = now;
        if (settings.scoreFeed)
        {
            // A new streak starts clean; within a streak the newest kill's events replace the list, the number keeps adding.
            self.fresh = self.streakIdle > settings.scoreStreakSeconds + FadeSeconds || self.streakScore == 0;
            if (self.fresh) self.streakScore = 0;
            self.ClearLines();
            int gained = settings.killScore;
            self.AddLine(Texts.Get("feed.score.kill"), LineIcon.Skull, false);
            if (headshot) { gained += settings.headshotBonus; self.AddLine(Texts.Get("feed.score.headshot"), LineIcon.Headshot, true); }
            if (bullet && distance >= settings.longRangeDistance) { gained += settings.longRangeBonus; self.AddLine(Texts.Get("feed.score.long_range"), LineIcon.None, false); }
            if (bullet && pierced) { gained += settings.piercedBonus; self.AddLine(Texts.Get("feed.score.pierced"), LineIcon.None, false); }
            if (execution) { gained += settings.executionBonus; self.AddLine(Texts.Get("feed.score.execution"), LineIcon.Execution, true, KillRed); }
            if (takedown) { gained += StealthScore.Takedown; self.AddLine(Texts.Get("feed.score.takedown"), LineIcon.None, true, KitStyle.Accent); }
            if (bounty > 0) { gained += bounty; self.AddLine(Texts.Get("feed.score.bounty"), LineIcon.None, true, PingMarks.BountyColor); }
            if (counter) { gained += settings.counterBonus; self.AddLine(Texts.Get("feed.score.counter"), LineIcon.Counter, true, CombatFeedbackSettings.InstanceOrDefaults.counterColor); }
            if (self.multiKill >= 2)
            {
                gained += settings.multiKillBonus * (self.multiKill - 1);
                self.AddLine(MultiKillName(self.multiKill), LineIcon.None, true);
            }
            // Heriot (사망세), a perk card: a share of this kill's money on top (PlayerPerks; this PC's own kills only).
            int tax = PlayerPerks.DeathTax(gained);
            if (tax > 0) { gained += tax; self.AddLine(Texts.Get("feed.score.heriot"), LineIcon.None, true, new Color(.25f, .85f, .38f)); }
            TotalScore += gained;
            self.streakScore += gained;
            self.streakIdle = 0f; self.popAge = 0f; self.totalPop = 1f;
            // Points are money in play (Wallet, stage 6A): the popup reads +110.
            self.streakLabel.text = "<size=62%><color=#" + ColorUtility.ToHtmlStringRGB(settings.scoreColor) + ">+</color></size>"
                + Wallet.FormatPoints(self.streakScore);
        }
        if (feedRow && settings.killFeed) self.AddKill(Credit(PlayerRoster.Local, weapon), target, headshot, true, StreakOf(PlayerRoster.Local), standout);
    }

    /// <summary>
    /// Points for something other than a kill (the stealth round: a stretch got through unseen), shown as a kill's are:
    /// its line, and the popup's number adding up with the streak's.
    /// </summary>
    public static void Award(string what, int points)
    {
        if (!Application.isPlaying || points <= 0) return;
        var settings = GunFeelSettings.Instance;
        var self = Get();
        TotalScore += points;
        if (!settings.scoreFeed) return;
        self.fresh = self.streakIdle > settings.scoreStreakSeconds + FadeSeconds || self.streakScore == 0;
        if (self.fresh) self.streakScore = 0;
        self.ClearLines();
        self.AddLine(what, LineIcon.None, true, KitStyle.Accent);
        self.streakScore += points;
        self.streakIdle = 0f; self.popAge = 0f; self.totalPop = 1f;
        self.streakLabel.text = "<size=62%><color=#" + ColorUtility.ToHtmlStringRGB(settings.scoreColor) + ">+</color></size>"
            + Wallet.FormatPoints(self.streakScore);
    }

    /// <summary>A kill in a room, as the host counted it, in the feed only: the score is the killer's own.</summary>
    public static void PlayerKill(PlayerMovement who, string weapon, string target, bool headshot, int streak, bool standout = false)
    {
        if (Application.isPlaying && GunFeelSettings.Instance.killFeed) Get().AddKill(Credit(who, weapon), target, headshot, who.IsLocal, streak, standout);
    }

    /// <summary>What just happened to a player, for the whole team to read.</summary>
    public static void Notice(PlayerMovement who, string what, bool grave)
    {
        if (!Application.isPlaying || !GunFeelSettings.Instance.killFeed) return;
        var settings = GunFeelSettings.Instance;
        Get().AddRow(NameOf(who), grave ? CombatUIAssets.Skull : null, Color.white, what, grave ? KillRed : settings.scoreColor, false, 0, grave);
    }

    private static int StreakOf(PlayerMovement who) => who != null && who.TryGetComponent(out PlayerTally tally) ? tally.Streak : 0;

    /// <summary>How long a kill keeps a streak alive: the score's double-kill window with some grace on top (s).</summary>
    public static float StreakWindow => GunFeelSettings.Instance.multiKillWindow + Get().look.streakGrace;

    /// <summary>The streak whose kill first shows the blue-eyed skull instead of a numeral.</summary>
    public static int SkullStreak => Get().look.mostNumeral + 1;

    // Alone there's only one name it could be.
    private static string Credit(PlayerMovement who, string weapon)
        => GameSession.IsOnline && who != null ? NameOf(who) + "  " + weapon : weapon;

    private static string NameOf(PlayerMovement who)
        => who.TryGetComponent(out PlayerTally tally) ? "<color=#" + ColorUtility.ToHtmlStringRGB(tally.Color) + ">" + tally.DisplayName + "</color>" : who.name;

    private static string MultiKillName(int count)
        => count == 2 ? Texts.Get("feed.multi.double") : count == 3 ? Texts.Get("feed.multi.triple") : count == 4 ? Texts.Get("feed.multi.quad")
            : Texts.Format("feed.multi.more", count);

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelUI.SetupHud(gameObject, 27);
        GameTables.BindNested(TableName, TableRow, this, look);
        // The total and the feed sit on the HUD's right plane with the ammo; the streak hangs off the top right of
        // whatever is in the player's hands (GunScoreAnchor).
        var side = HudPlane.Create("Right Plane", transform, HudPlane.Side.Right);
        var hold = FeelUI.Rect("Gun Hold", transform);
        gameObject.AddComponent<GunScoreAnchor>().Hold(hold);

        streakRoot = FeelUI.Rect("Streak", hold);
        streakGroup = streakRoot.gameObject.AddComponent<CanvasGroup>();
        streakGroup.interactable = streakGroup.blocksRaycasts = false;
        streakGroup.alpha = 0f;
        // A small panel growing up and right from the corner it hangs on: the events stacked from the bottom, the
        // number on top, a thin bracket down the left side.
        streakBracket = FeelUI.Rect("Bracket", streakRoot).gameObject.AddComponent<Image>();
        streakBracket.raycastTarget = false;
        streakBracket.rectTransform.pivot = Vector2.zero;
        streakBracket.rectTransform.anchoredPosition = new Vector2(-8f, -LineHeight * .5f);
        streakLabel = FeelUI.ScoreLabel("Streak Score", streakRoot, 30f, TextAlignmentOptions.BottomLeft);
        streakLabel.rectTransform.pivot = Vector2.zero;
        streakLabel.rectTransform.sizeDelta = new Vector2(220f, 40f);
        streakLabel.color = Number;
        linesRoot = FeelUI.Rect("Lines", streakRoot);

        totalRoot = FeelUI.Rect("Total", side);
        totalRoot.anchorMin = totalRoot.anchorMax = totalRoot.pivot = Vector2.one;
        // Both sit inside the right board (HudBoard), clear of its edges, where the HUD prototype put them.
        totalRoot.anchoredPosition = new Vector2(-70f, -58f);
        totalRoot.sizeDelta = new Vector2(320f, 40f);
        var caption = FeelUI.ScoreLabel("Caption", totalRoot, 13f, TextAlignmentOptions.Right);
        caption.rectTransform.anchoredPosition = new Vector2(0f, 13f);
        caption.rectTransform.sizeDelta = new Vector2(320f, 16f);
        caption.text = Texts.Get("feed.money"); caption.color = new Color(1f, 1f, 1f, .55f); caption.characterSpacing = 8f;
        totalLabel = FeelUI.ScoreLabel("Value", totalRoot, 28f, TextAlignmentOptions.Right);
        totalLabel.rectTransform.sizeDelta = new Vector2(320f, 34f);
        totalLabel.rectTransform.pivot = new Vector2(1f, .5f);
        totalLabel.rectTransform.anchoredPosition = new Vector2(160f, -8f);
        // The banknote stands just left of the number, wherever its width puts that (MoneyIcon, the user's pick).
        totalIcon = MoneyIcon.Make(totalRoot, 24f);
        totalRoot.gameObject.SetActive(false);

        feedRoot = FeelUI.Rect("Kill Feed", side);
        feedRoot.anchorMin = feedRoot.anchorMax = feedRoot.pivot = Vector2.one;
        feedRoot.anchoredPosition = new Vector2(-70f, -112f);
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        var settings = GunFeelSettings.Instance;
        streakIdle += dt; popAge += dt;

        // A new streak slams in from far larger; each later kill punches the number again. Fades upward when quiet.
        float fadeT = Mathf.Clamp01((streakIdle - settings.scoreStreakSeconds) / FadeSeconds);
        streakGroup.alpha = streakScore > 0 ? Mathf.Clamp01(popAge / .04f + (fresh ? 0f : 1f)) * (1f - fadeT) : 0f;
        streakRoot.anchoredPosition = new Vector2(0f, fadeT * 10f);
        float stack = lines.Count * LineHeight;
        streakLabel.rectTransform.anchoredPosition = new Vector2(0f, stack - LineHeight * .5f + 2f);
        streakBracket.rectTransform.sizeDelta = new Vector2(2f, stack + 40f);
        Color bracket = settings.scoreColor;
        bracket.a = .85f;
        streakBracket.color = bracket;
        float pop = 1f - Mathf.Pow(1f - Mathf.Clamp01(popAge / .13f), 3f);
        streakLabel.rectTransform.localScale = Vector3.one * Mathf.Lerp(fresh ? 1.9f : 1.35f, 1f, pop);
        streakLabel.color = Color.Lerp(Color.white, Number, Mathf.Clamp01(popAge / .1f));
        if (fadeT >= 1f && streakScore > 0) { streakScore = 0; ClearLines(); }

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            line.age += dt;
            // Events tick in one after another, each sliding from the left.
            float delayed = line.age - i * .05f;
            float enter = 1f - Mathf.Pow(1f - Mathf.Clamp01(delayed / .12f), 3f);
            line.root.anchoredPosition = new Vector2((1f - enter) * -14f, (lines.Count - 1 - i) * LineHeight);
            line.root.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, enter);
            Color color = Color.Lerp(Color.white, line.tint ?? (line.accent ? settings.scoreColor : Soft), Mathf.Clamp01(delayed / .12f));
            color.a = enter;
            line.label.color = color;
            Color iconColor = line.tintIcon && line.tint.HasValue ? line.tint.Value : Color.white;
            iconColor.a = enter;
            line.icon.color = iconColor;
        }

        // The money held (Wallet.Balance): rolls up as kills earn it, drops at once when it is dropped or spent.
        long balance = Wallet.Balance;
        if ((balance != 0 || TotalScore > 0) && !totalRoot.gameObject.activeSelf) totalRoot.gameObject.SetActive(true);
        totalShown = totalShown < balance
            ? System.Math.Min(balance, totalShown + System.Math.Max(80d * Wallet.WonPerPoint * dt, (balance - totalShown) * (1d - System.Math.Exp(-10d * dt))))
            : balance;
        totalPop = Mathf.MoveTowards(totalPop, 0f, dt * 3.5f);
        totalLabel.text = Wallet.Format((long)System.Math.Round(totalShown));
        totalLabel.rectTransform.localScale = Vector3.one * (1f + totalPop * totalPop * .25f);
        float numberWidth = totalLabel.GetPreferredValues(totalLabel.text).x * totalLabel.rectTransform.localScale.x;
        totalIcon.rectTransform.anchoredPosition = new Vector2(160f - numberWidth - 18f, -8f);
        totalLabel.color = Color.Lerp(Color.white, settings.scoreColor, totalPop);

        for (int i = kills.Count - 1; i >= 0; i--)
        {
            if (kills[i].Age + dt < KillLifetime) continue;
            kills[i].Hide(); spareKills.Push(kills[i]); kills.RemoveAt(i);
        }
        // Your own kills first, then everyone else's, each newest first (backlog 332): in a room your rows stay at the top
        // under a teammate's run of kills, and alone it is the plain newest-first list. Each row sits below the room the
        // ones above it take now, so the top row's bigger card never overlaps.
        shown.Clear();
        for (int i = kills.Count - 1; i >= 0; i--) if (kills[i].Mine) shown.Add(kills[i]);
        for (int i = kills.Count - 1; i >= 0; i--) if (!kills[i].Mine) shown.Add(kills[i]);
        float below = 0f;
        for (int rank = 0; rank < shown.Count; rank++)
        {
            shown[rank].Tick(dt, -below, KillLifetime, rank);
            below += shown[rank].Extent + 5f;
        }
    }

    private void ClearLines()
    {
        foreach (var line in lines) { line.root.gameObject.SetActive(false); spareLines.Push(line); }
        lines.Clear();
    }

    /// <summary>tint overrides the accent colour: counters match the counter damage popup, executions read red.</summary>
    private void AddLine(string text, LineIcon icon, bool accent, Color? tint = null)
    {
        if (lines.Count >= MaxLines) return;
        Line line;
        if (spareLines.Count > 0) line = spareLines.Pop();
        else
        {
            line = new Line { root = FeelUI.Rect("Line", linesRoot) };
            line.root.pivot = new Vector2(0f, .5f);
            line.icon = FeelUI.Rect("Icon", line.root).gameObject.AddComponent<RawImage>();
            line.icon.raycastTarget = false;
            line.icon.rectTransform.sizeDelta = new Vector2(IconSize, IconSize);
            // Tinted icons get a dark drop so the colour still reads over bright scenes (Linear washes thin colour out).
            line.iconShadow = line.icon.gameObject.AddComponent<Shadow>();
            line.iconShadow.effectColor = new Color(0f, 0f, 0f, .6f);
            line.iconShadow.effectDistance = new Vector2(1.2f, -1.2f);
            line.label = FeelUI.ScoreLabel("Label", line.root, 17f, TextAlignmentOptions.Left);
            line.label.rectTransform.pivot = new Vector2(0f, .5f);
            line.label.rectTransform.sizeDelta = new Vector2(240f, LineHeight);
        }
        bool hasIcon = icon != LineIcon.None;
        line.icon.gameObject.SetActive(hasIcon);
        var counterIcon = CombatFeedbackSettings.InstanceOrDefaults.counterIcon;
        if (hasIcon) line.icon.texture = icon == LineIcon.Skull ? CombatUIAssets.Skull
            : icon == LineIcon.Counter ? (counterIcon != null ? counterIcon : CombatUIAssets.CounterArrow)
            : icon == LineIcon.Execution ? CombatUIAssets.ExecutionKnife : CombatUIAssets.CriticalBurst;
        // Counter / execution icons take the line tint (same as their damage popup); skull and headshot stay white.
        line.tintIcon = icon == LineIcon.Counter || icon == LineIcon.Execution;
        line.iconShadow.enabled = line.tintIcon;
        line.icon.rectTransform.anchoredPosition = new Vector2(IconSize * .5f, 0f);
        // Labels align in one column whether or not the line has an icon.
        line.label.rectTransform.anchoredPosition = new Vector2(IconSize + 6f, 0f);
        line.label.text = text; line.accent = accent; line.tint = tint; line.age = 0f;
        line.root.gameObject.SetActive(true);
        lines.Add(line);
    }

    // A named or elite kill burns magenta round its row; a streak past the last numeral (its skull) burns blue (backlog 325).
    private void AddKill(string weapon, string target, bool headshot, bool mine, int streak, bool standout)
    {
        var settings = GunFeelSettings.Instance;
        var fire = standout ? FeedBorder.Standout : streak > look.mostNumeral ? FeedBorder.Skull : FeedBorder.None;
        AddRow(weapon, headshot ? CombatUIAssets.CriticalBurst : CombatUIAssets.Skull, headshot ? settings.scoreColor : Color.white,
            target, headshot ? settings.scoreColor : KillRed, mine, streak, headshot, fire);
    }

    private void AddRow(string left, Texture icon, Color iconColor, string right, Color accent, bool mine, int streak, bool emphatic,
        FeedBorder fire = FeedBorder.None)
    {
        if (kills.Count >= MaxKills)
        {
            kills[0].Hide(); spareKills.Push(kills[0]); kills.RemoveAt(0);
        }
        var row = spareKills.Count > 0 ? spareKills.Pop() : new KillFeedRow(feedRoot);
        row.Show(left, icon, iconColor, right, accent, mine, streak, look, emphatic, fire);
        kills.Add(row);
    }
}
