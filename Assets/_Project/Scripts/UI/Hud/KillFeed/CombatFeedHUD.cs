using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Kill rewards and the kill feed. A kill pays its points (money in play: Wallet turns them into money, a number beside a
/// banknote; the rolling total sits top right) and shows in the score popup (ScorePopupHUD: Black Ops 6's yellow total
/// and "+20 Elimination" rows, the user's pick of 2026-11-22, whose score is the player's experience, apart from money).
/// The old popup hung off the gun (GunScoreAnchor) with Korean lines and the money number; it went with that pick.
/// The kill feed sits at the top right. Created on first use during Play; GunFeel → Score.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.CombatFeedHud)]
public sealed class CombatFeedHUD : MonoBehaviour
{
    private const int MaxKills = 5;
    private const string TableName = "KillFeed", TableRow = "Default";
    private const float KillLifetime = 5f;
    private static readonly Color KillRed = new Color(1f, .2f, .14f);
    private static CombatFeedHUD instance;

    public static long TotalScore { get; private set; }
    /// <summary>A new run starts from nothing, whatever the last room earned: money's points and the popup's score.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetScore()
    {
        TotalScore = 0;
        ScorePopupHUD.ResetScore();
    }

    private RectTransform totalRoot, feedRoot;
    private TextMeshProUGUI totalLabel;
    private RawImage totalIcon;
    private readonly List<KillFeedRow> kills = new List<KillFeedRow>(), shown = new List<KillFeedRow>();
    private readonly List<RectTransform> feedRoom = new List<RectTransform>();
    private readonly Stack<KillFeedRow> spareKills = new Stack<KillFeedRow>();
    private readonly KillFeedLook look = new KillFeedLook();
    private int multiKill;
    private float totalPop, lastKillTime = -99f;
    // The money shown, rolling up to the balance (won; double, as a float can't hold big sums to the won).
    private double totalShown;

    private static CombatFeedHUD Get()
    {
        if (instance == null)
        {
            var host = new GameObject("Combat Feed", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = host.AddComponent<CombatFeedHUD>();
        }
        return instance;
    }

    /// <summary>
    /// Makes ahead, out of sight, what the first kill would otherwise make mid-fight (SceneWarmup's pools step, behind the
    /// loading screen): this HUD, the score popup and its kill feed rows up to the most it shows, each row with every piece
    /// its flairs draw (KillFeedRow.Prewarm: the CHARGE fire layer and its material, the bounty's stamp and chip, the
    /// skull's ring and the rest), the shapes the lines draw with, the tables the flairs read (KillFlairs.Prewarm) and every
    /// weapon picture (WeaponIcons.Prewarm). What is made stays made: calling it again only tops the pools up, and rows on
    /// screen are left alone.
    /// </summary>
    public static void Prewarm()
    {
        if (!Application.isPlaying) return;
        Get().FillPools();
        ScorePopupHUD.Prewarm();
        KillFlairs.Prewarm();
        WeaponIcons.Prewarm();
        // Drawn on first ask: a kill's skull, a headshot's burst, a counter's arrow, an execution's knife.
        _ = CombatUIAssets.Skull;
        _ = CombatUIAssets.CriticalBurst;
        _ = CombatUIAssets.CounterArrow;
        _ = CombatUIAssets.ExecutionKnife;
    }

    // Kill feed rows up to the most it shows, the spare rows with all their flair pieces made.
    private void FillPools()
    {
        for (int i = kills.Count + spareKills.Count; i < MaxKills; i++) spareKills.Push(new KillFeedRow(feedRoot));
        foreach (var row in spareKills) row.Prewarm();
    }

    /// <summary>distance: shooter to hit point, metres (0 when unknown); gun kills past Long Range Distance earn a bonus. feedRow is false when the host will send the row.
    /// pierced: the killing bullet went through or off something first (관통 사살). standout: the enemy was named or elite, so
    /// its row burns magenta (backlog 325). takedown: an execution of a zombie that never knew they were there (the
    /// stealth round's 암살, Stealth.csv Score.takedown on top of the execution's). bounty: points a Schießhund's own mark
    /// pays on top (현상금, ClassAbilities Schiesshund.bountyMoney / bountyElite), 0 for none. weaponIcon: the killer's id
    /// for its picture in the feed (WeaponIcons: a weapon's asset name, or fist, shove, execution ...); none, or one with no
    /// picture, shows the weapon's name instead. marks: what the kill happened under (KillConditions), for its flair in the
    /// feed (KillFlairs).</summary>
    public static void Kill(string weapon, string target, bool headshot, bool execution, bool counter, bool bullet, float distance, bool feedRow,
        bool pierced = false, bool standout = false, bool takedown = false, int bounty = 0, string weaponIcon = null,
        KillMarks marks = KillMarks.None)
    {
        if (!Application.isPlaying) return;
        var settings = GunFeelSettings.Instance;
        var self = Get();
        float now = Time.unscaledTime;
        // The kill feed's window, so the money's multi kill and the feed's numeral count the same kills.
        self.multiKill = now - self.lastKillTime <= StreakWindow ? self.multiKill + 1 : 1;
        self.lastKillTime = now;
        if (settings.scoreFeed)
        {
            // Money: the points a kill has always paid (Wallet turns them into money), whatever the popup shows.
            int gained = settings.killScore;
            bool far = bullet && distance >= settings.longRangeDistance, through = bullet && pierced;
            if (headshot) gained += settings.headshotBonus;
            if (far) gained += settings.longRangeBonus;
            if (through) gained += settings.piercedBonus;
            if (execution) gained += settings.executionBonus;
            if (takedown) gained += StealthScore.Takedown;
            if (bounty > 0) gained += bounty;
            if (counter) gained += settings.counterBonus;
            if (self.multiKill >= 2) gained += settings.multiKillBonus * (self.multiKill - 1);
            // Heriot (사망세), a perk card: a share of this kill's money on top (PlayerPerks; this PC's own kills only).
            int tax = PlayerPerks.DeathTax(gained);
            gained += tax;
            TotalScore += gained;
            self.totalPop = 1f;
            // Score: the kill's own row the addon's way, then a row for each extra of ours the addon has no row for.
            ScorePopupHUD.Kill(marks, headshot, execution, counter, takedown);
            if (far) ScorePopupHUD.Points("feed.score.long_range", settings.longRangeBonus);
            if (through) ScorePopupHUD.Points("feed.score.pierced", settings.piercedBonus);
            if (takedown) ScorePopupHUD.Points("feed.score.takedown", StealthScore.Takedown);
            if (bounty > 0) ScorePopupHUD.Points("feed.score.bounty", bounty);
            if (tax > 0) ScorePopupHUD.Points("feed.score.heriot", tax);
        }
        if (feedRow && settings.killFeed)
            self.AddKill(PlayerRoster.Local, weapon, weaponIcon, target, headshot, true, StreakOf(PlayerRoster.Local), standout, marks);
    }

    /// <summary>
    /// Points for something other than a kill (a parry, a special kill, a stealth stretch, a tower floor): money as a
    /// kill's points are, and a row of the score popup in its words (a Texts id, shown in English; ScorePopupHUD.Points).
    /// </summary>
    public static void Score(string textId, int points, params object[] values)
    {
        if (!Application.isPlaying || points <= 0) return;
        TotalScore += points;
        Get().totalPop = 1f;
        ScorePopupHUD.Points(textId, points, values);
    }

    /// <summary>A kill in a room, as the host counted it, in the feed only: the score is the killer's own. weaponIcon and
    /// marks as Kill's, the host's.</summary>
    public static void PlayerKill(PlayerMovement who, string weapon, string target, bool headshot, int streak, bool standout = false,
        string weaponIcon = null, KillMarks marks = KillMarks.None)
    {
        if (Application.isPlaying && GunFeelSettings.Instance.killFeed)
            Get().AddKill(who, weapon, weaponIcon, target, headshot, who != null && who.IsLocal, streak, standout, marks);
    }

    /// <summary>What just happened to a player, for the whole team to read.</summary>
    public static void Notice(PlayerMovement who, string what, bool grave)
    {
        if (!Application.isPlaying || !GunFeelSettings.Instance.killFeed) return;
        var settings = GunFeelSettings.Instance;
        Get().AddRow(PlayerTally.ColoredNameOf(who), grave ? CombatUIAssets.Skull : null, Color.white, what, grave ? KillRed : settings.scoreColor, false, 0, grave);
    }

    private static int StreakOf(PlayerMovement who) => who != null && who.TryGetComponent(out PlayerTally tally) ? tally.Streak : 0;

    /// <summary>How long a kill keeps a streak alive: the score's double-kill window with some grace on top (s).</summary>
    public static float StreakWindow => GunFeelSettings.Instance.multiKillWindow + Get().look.streakGrace;

    /// <summary>The streak whose kill first shows the blue-eyed skull instead of a numeral.</summary>
    public static int SkullStreak => Get().look.mostNumeral + 1;

    // Alone there's only one name it could be.
    private static string Credit(PlayerMovement who, string weapon)
        => GameSession.IsOnline && who != null ? PlayerTally.ColoredNameOf(who) + "  " + weapon : weapon;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelUI.SetupHud(gameObject, 27);
        GameTables.BindNested(TableName, TableRow, this, look);
        // The money total and the feed sit on the HUD's right plane with the ammo.
        var side = HudPlane.Create("Right Plane", transform, HudPlane.Side.Right);

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
            // A flair's stamp over a row and its chip under it push the rows round it apart (KillFlairFx Above, Below).
            float top = below + shown[rank].Above;
            shown[rank].Tick(dt, -top, KillLifetime, rank);
            below = top + shown[rank].Extent + 5f;
        }
        TakeRoom();
    }

    // The room the feed's rows take on the right edge now, so a passing piece (a callout window) steps clear of a long
    // feed rather than under it (HudRoom).
    private void TakeRoom()
    {
        feedRoom.Clear();
        foreach (Transform child in feedRoot)
            if (child.gameObject.activeSelf && child is RectTransform rect
                && (!child.TryGetComponent(out CanvasGroup shade) || shade.alpha > .02f)) feedRoom.Add(rect);
        if (feedRoom.Count > 0 && HudRoom.Measure(feedRoom, out float near, out float far, out float top, out float bottom))
            HudRoom.Take(this, near, far, top, bottom);
        else HudRoom.Free(this);
    }

    // A named or elite kill burns magenta round its row; a streak past the last numeral (its skull) burns blue (backlog 325).
    // With the weapon's picture the row reads killer, weapon, (headshot), victim, as kill feeds do, the killer's name even
    // alone; a weapon without one keeps the old row: its name (after the killer's in a room), the skull or burst, victim.
    // The kill's flair (KillFlairs) plays on its row, from its marks and its place in the killer's run of quick kills.
    private void AddKill(PlayerMovement who, string weapon, string weaponIcon, string target, bool headshot, bool mine, int streak, bool standout,
        KillMarks marks)
    {
        var settings = GunFeelSettings.Instance;
        var fire = standout ? FeedBorder.Standout : streak > look.mostNumeral ? FeedBorder.Skull : FeedBorder.None;
        Color mark = headshot ? settings.scoreColor : Color.white, accent = headshot ? settings.scoreColor : KillRed;
        var picture = WeaponIcons.For(weaponIcon);
        // The killer's class colour (ClassColours.csv) for the flairs drawn in it: an ultimate's light round the row.
        Color classColour = KillFlairs.ClassColourOf(who != null && who.TryGetComponent(out PlayerTally tally) ? tally.ClassId : null);
        var flairs = KillFlairs.Pick(marks, RunOf(who), mine, weaponIcon, classColour);
        if (picture != null)
        {
            // A headshot's mark after the weapon, Counter-Strike's way: the kill chain's shattered skull (CombatUIAssets.HeadshotSkull).
            AddRow(who != null ? PlayerTally.ColoredNameOf(who) : "", headshot ? CombatUIAssets.HeadshotSkull : null, mark,
                target, accent, mine, streak, headshot, fire, picture, flairs);
        }
        else
            AddRow(Credit(who, weapon), headshot ? CombatUIAssets.HeadshotSkull : CombatUIAssets.Skull, mark, target, accent, mine, streak,
                headshot, fire, null, flairs);
    }

    // Each killer's run of quick kills in the feed: a kill within the streak window of their last is the next of the run
    // (2 a double ...), as the score's multi kill counts this PC's own. Every PC sees the rows in the same order.
    private readonly Dictionary<PlayerMovement, (float at, int run)> runs = new Dictionary<PlayerMovement, (float, int)>();

    private int RunOf(PlayerMovement who)
    {
        if (who == null) return 1;
        float now = Time.unscaledTime;
        int run = runs.TryGetValue(who, out var last) && now - last.at <= StreakWindow ? last.run + 1 : 1;
        runs[who] = (now, run);
        return run;
    }

    private void AddRow(string left, Texture icon, Color iconColor, string right, Color accent, bool mine, int streak, bool emphatic,
        FeedBorder fire = FeedBorder.None, Sprite weapon = null, KillFlairs.Play flairs = default)
    {
        if (kills.Count >= MaxKills)
        {
            kills[0].Hide(); spareKills.Push(kills[0]); kills.RemoveAt(0);
        }
        var row = spareKills.Count > 0 ? spareKills.Pop() : new KillFeedRow(feedRoot);
        row.Show(left, icon, iconColor, right, accent, mine, streak, look, emphatic, fire, weapon, flairs);
        kills.Add(row);
    }
}
