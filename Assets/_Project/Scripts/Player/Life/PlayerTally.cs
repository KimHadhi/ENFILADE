using UnityEngine;

/// <summary>
/// One player's line on the scoreboard, and what the team hears about them. This PC's player counts their own; another
/// player's numbers arrive through PlayerNetworkScore, so nobody's count depends on having seen it happen.
/// The chapter's results (memo 37, MatchResults) read the rest from here too: the damage they did, the friends they healed
/// and got up, their headshots, best streak, farthest headshot, takedowns, the money they made this chapter and their
/// class. A player is made again for every scene, so all of it counts one chapter.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerTally : MonoBehaviour
{
    // A colour for each seat in the room, so a name reads the same in the feed and on the board.
    private static readonly Color[] SeatColors =
    {
        new Color(.45f, .78f, 1f), new Color(1f, .5f, .45f), new Color(.55f, .95f, .5f), new Color(1f, .78f, .4f),
        new Color(.8f, .6f, 1f), new Color(.4f, .95f, .85f), new Color(1f, .6f, .85f), new Color(.85f, .9f, .5f)
    };

    private const float ConsoleHold = 15f;
    // The class pick is read again this often (s): it only changes in the hub or the title screen.
    private const float ClassEvery = 1f;

    private PlayerMovement player;
    private bool wasDowned, wasDead;
    private float lastKillTime = float.NegativeInfinity, nextClassLook;
    // What the wallet had earned when this chapter began, so Earned is this chapter's alone.
    private long earnedAtStart;

    public string DisplayName { get; private set; } = "";

    /// <summary>The name a player goes by on screen: the one they chose, else their object's; "" for no one.</summary>
    public static string NameOf(PlayerMovement player)
        => player != null && player.TryGetComponent(out PlayerTally tally) && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName
            : player != null ? player.name : "";

    /// <summary>A player's name in their seat's colour, as rich text.</summary>
    public static string ColoredNameOf(PlayerMovement someone)
        => someone.TryGetComponent(out PlayerTally theirs)
            ? "<color=#" + ColorUtility.ToHtmlStringRGB(theirs.Color) + ">" + theirs.DisplayName + "</color>" : someone.name;
    public int Kills { get; private set; }
    public int Executions { get; private set; }
    public int Downs { get; private set; }
    public int Deaths { get; private set; }
    /// <summary>Kills chained in quick succession, as the score popup's double kills are but with more time between. In a room the host counts it for everyone (PlayerNetworkScore), so it reads the same on every PC.</summary>
    public int Streak { get; private set; }
    /// <summary>The money they hold (won, Wallet.Balance on their own PC), the one number the scoreboard shows of it.</summary>
    public long Score { get; private set; }
    /// <summary>The ping players see (PingDisplay). The host's is 0.</summary>
    public int Ping { get; private set; }
    public Color Color { get; private set; } = SeatColors[0];

    /// <summary>Health taken off enemies this chapter: what really came off, not the overkill past zero.</summary>
    public float Damage { get; private set; }
    /// <summary>Health given back to teammates (a Détente's doses and treatment), as far as they were missing it.</summary>
    public float Healing { get; private set; }
    /// <summary>Kills by a bullet in the head.</summary>
    public int Headshots { get; private set; }
    /// <summary>Teammates got up from going down, or back from the dead (a Détente's Resurrection).</summary>
    public int Revives { get; private set; }
    /// <summary>The longest chain of kills this chapter (Streak at its highest).</summary>
    public int BestStreak { get; private set; }
    /// <summary>Executions of a zombie that never knew they were there (암살).</summary>
    public int Takedowns { get; private set; }
    /// <summary>The farthest headshot kill this chapter (m).</summary>
    public float FarthestHeadshot { get; private set; }
    /// <summary>The money this chapter brought in (won), the same kind of number as the money held.</summary>
    public long Earned { get; private set; }
    /// <summary>The class they play (Tables/Classes id).</summary>
    public string ClassId { get; private set; } = "";

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        if (player.IsLocal) DisplayName = GameSettings.Current.DisplayName;
        earnedAtStart = Wallet.Earned;
    }

    private void OnEnable() => CombatDamageEvents.Damaged += OnDamaged;
    private void OnDisable() => CombatDamageEvents.Damaged -= OnDamaged;

    /// <summary>This PC's player finished someone.</summary>
    internal void CountKill(bool execution)
    {
        Kills++;
        if (execution) Executions++;
    }

    /// <summary>A kill in time carries the chain on, and a late one starts it again.</summary>
    internal void CountStreak()
    {
        float now = Time.unscaledTime;
        Streak = now - lastKillTime <= CombatFeedHUD.StreakWindow ? Streak + 1 : 1;
        lastKillTime = now;
        NoteStreak(Streak);
    }

    /// <summary>For trying out a long streak from the console, with time to go and make the next kill.</summary>
    internal void SetStreak(int streak)
    {
        Streak = Mathf.Max(0, streak);
        lastKillTime = Time.unscaledTime + ConsoleHold;
        NoteStreak(Streak);
        if (TryGetComponent(out PlayerNetworkScore score)) score.ShareStreak(Streak);
    }

    /// <summary>A streak this player reached (the host's count reaches every PC through PlayerNetworkScore).</summary>
    internal void NoteStreak(int streak) => BestStreak = Mathf.Max(BestStreak, streak);

    internal void ShowSeat(ulong client) => Color = SeatColors[client % (ulong)SeatColors.Length];
    internal void ShowName(string name) => DisplayName = name;
    internal void ShowPing(int ping) => Ping = ping;
    internal void ShowClass(string id) => ClassId = id ?? "";

    /// <summary>On someone else's copy: the numbers their own PC reports.</summary>
    internal void ShowNumbers(int kills, int executions, int downs, int deaths, long score, int ping)
    {
        Kills = kills; Executions = executions; Downs = downs; Deaths = deaths; Score = score; Ping = ping;
    }

    /// <summary>On someone else's copy: the results' numbers their own PC reports (memo 37).</summary>
    internal void ShowResults(float damage, float healing, int headshots, int revives, int bestStreak, int takedowns, float farthest, long earned)
    {
        Damage = damage; Healing = healing; Headshots = headshots; Revives = revives; BestStreak = bestStreak;
        Takedowns = takedowns; FarthestHeadshot = farthest; Earned = earned;
    }

    /// <summary>
    /// This PC's player gave <paramref name="patient"/> health back (PlayerNetworkCombat.Heal, a Détente's dose or
    /// treatment): counted as far as the patient was missing it, as this PC sees their health.
    /// </summary>
    internal static void Healed(Component patient, float amount)
    {
        var mine = Mine();
        if (mine == null || patient == null || amount <= 0f || !patient.TryGetComponent(out PlayerMovement who) || who.DamageReceiver == null) return;
        float missing = Mathf.Max(0f, who.DamageReceiver.MaximumHealth - who.DamageReceiver.Health);
        mine.Healing += Mathf.Min(amount, missing);
    }

    /// <summary>This PC's player got a teammate up, or back from the dead (PlayerNetworkCombat.Revive and Resurrect).</summary>
    internal static void Raised()
    {
        var mine = Mine();
        if (mine != null) mine.Revives++;
    }

    private static PlayerTally Mine()
        => PlayerRoster.Local != null && PlayerRoster.Local.TryGetComponent(out PlayerTally tally) ? tally : null;

    // This PC's player's own hits on enemies: the damage, and of the kills the headshots (how far) and the takedowns.
    // Practice targets and other players' hits don't count; their own PCs count theirs.
    private void OnDamaged(CombatDamageReport report)
    {
        if (player == null || !player.IsLocal || report.ByOtherPlayer || !(report.Target is EnemyHealth)) return;
        var hit = report.Hit;
        if (hit.Kind == DamageKind.Fall || hit.Source == null || hit.Source.GetComponentInParent<PlayerMovement>() != player) return;
        Damage += report.Applied;
        if (!report.IsKillingBlow) return;
        if (hit.Method == DamageMethod.Bullet && report.IsCritical)
        {
            Headshots++;
            if (hit.Point != Vector3.zero) FarthestHeadshot = Mathf.Max(FarthestHeadshot, Vector3.Distance(hit.Source.transform.position, hit.Point));
        }
        if (hit.Kind == DamageKind.Execution && report.Target.TryGetComponent(out ZombieMind mind) && mind.Unaware) Takedowns++;
    }

    private void LateUpdate()
    {
        if (!player.IsInitialized) return;
        if (player.IsLocal)
        {
            DisplayName = GameSettings.Current.DisplayName;
            Score = Wallet.Balance;
            Earned = System.Math.Max(0L, Wallet.Earned - earnedAtStart);
            if (Time.unscaledTime >= nextClassLook)
            {
                nextClassLook = Time.unscaledTime + ClassEvery;
                ClassId = ClassGrowth.CurrentId;
            }
        }
        bool downed = player.IsDowned, dead = player.IsDead;
        if (downed == wasDowned && dead == wasDead) return;
        if (player.IsLocal)
        {
            if (downed && !wasDowned) Downs++;
            if (dead && !wasDead) Deaths++;
        }
        if (GameSession.IsOnline)
        {
            if (dead && !wasDead) CombatFeedHUD.Notice(player, Texts.Get("feed.notice.dead"), true);
            else if (downed && !wasDowned) CombatFeedHUD.Notice(player, Texts.Get("feed.notice.downed"), false);
            else if (wasDowned && !downed && !dead) CombatFeedHUD.Notice(player, Texts.Get("feed.notice.up"), false);
            // A teammate going down or dying is also said at the top of the screen, from the side they are on (AlertHUD);
            // the downed keep their own mark (TeammateMarkerHUD).
            if (!player.IsLocal && (dead && !wasDead || downed && !wasDowned))
            {
                string name = "<color=#" + ColorUtility.ToHtmlStringRGB(Color) + ">" + DisplayName + "</color>";
                if (dead && !wasDead) AlertHUD.Danger(Texts.Format("team.alert.dead", name), transform);
                else AlertHUD.Team(Texts.Format("team.alert.downed", name), transform, Color);
                KitSound.Play(KitSound.Cue.Alert);
            }
        }
        wasDowned = downed;
        wasDead = dead;
    }
}
