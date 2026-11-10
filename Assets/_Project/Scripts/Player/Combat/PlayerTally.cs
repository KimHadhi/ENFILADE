using UnityEngine;

/// <summary>
/// One player's line on the scoreboard, and what the team hears about them. This PC's player counts their own; another
/// player's numbers arrive through PlayerNetworkScore, so nobody's count depends on having seen it happen.
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

    private PlayerMovement player;
    private bool wasDowned, wasDead;
    private float lastKillTime = float.NegativeInfinity;

    public string DisplayName { get; private set; } = "";
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

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        if (player.IsLocal) DisplayName = GameSettings.Current.DisplayName;
    }

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
    }

    /// <summary>For trying out a long streak from the console, with time to go and make the next kill.</summary>
    internal void SetStreak(int streak)
    {
        Streak = Mathf.Max(0, streak);
        lastKillTime = Time.unscaledTime + ConsoleHold;
        if (TryGetComponent(out PlayerNetworkScore score)) score.ShareStreak(Streak);
    }

    internal void ShowSeat(ulong client) => Color = SeatColors[client % (ulong)SeatColors.Length];
    internal void ShowName(string name) => DisplayName = name;
    internal void ShowPing(int ping) => Ping = ping;

    /// <summary>On someone else's copy: the numbers their own PC reports.</summary>
    internal void ShowNumbers(int kills, int executions, int downs, int deaths, long score, int ping)
    {
        Kills = kills; Executions = executions; Downs = downs; Deaths = deaths; Score = score; Ping = ping;
    }

    private void LateUpdate()
    {
        if (!player.IsInitialized) return;
        if (player.IsLocal)
        {
            DisplayName = GameSettings.Current.DisplayName;
            Score = Wallet.Balance;
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
            if (dead && !wasDead) CombatFeedHUD.Notice(player, "사망", true);
            else if (downed && !wasDowned) CombatFeedHUD.Notice(player, "쓰러짐", false);
            else if (wasDowned && !downed && !dead) CombatFeedHUD.Notice(player, "일어남", false);
            // A teammate going down or dying is also said at the top of the screen, from the side they are on (AlertHUD);
            // the downed keep their own mark (TeammateMarkerHUD).
            if (!player.IsLocal && (dead && !wasDead || downed && !wasDowned))
            {
                string name = "<color=#" + ColorUtility.ToHtmlStringRGB(Color) + ">" + DisplayName + "</color>";
                if (dead && !wasDead) AlertHUD.Danger(name + " 사망", transform);
                else AlertHUD.Team(name + " 쓰러짐", transform, Color);
                KitSound.Play(KitSound.Cue.Alert);
            }
        }
        wasDowned = downed;
        wasDead = dead;
    }
}
