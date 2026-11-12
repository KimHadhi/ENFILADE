using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Carries a player's name and scoreboard numbers to the other PCs. The owner writes them; each PC shows them through
/// PlayerTally. Alone it never spawns and does nothing.
/// The chapter's results (memo 37) ride along: what the owner's PC counted of their damage, healing, headshots, revives,
/// best streak, takedowns, farthest headshot and money made, and the class they play, so every PC can put the same
/// results on the screen when the chapter ends (MatchResults).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerNetworkScore : NetworkBehaviour
{
    private struct Numbers : INetworkSerializeByMemcpy
    {
        // The results' own (Earned, Damage, Healing and the second row of counts): money in won, damage and healing in
        // whole health points, the farthest headshot in tenths of a metre. Widest first, so the struct packs tight.
        public long Score, Earned;
        public uint Damage, Healing;
        public ushort Kills, Executions, Downs, Deaths, Ping;
        public ushort Headshots, Revives, BestStreak, Takedowns, Farthest;
    }

    private const float PingInterval = 2f;

    private readonly NetworkVariable<FixedString64Bytes> displayName = new NetworkVariable<FixedString64Bytes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Numbers> numbers = new NetworkVariable<Numbers>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<FixedString32Bytes> classId = new NetworkVariable<FixedString32Bytes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private PlayerTally tally;
    private float nextPing;

    public override void OnNetworkSpawn()
    {
        tally = GetComponent<PlayerTally>();
        tally.ShowSeat(OwnerClientId);
        if (IsOwner) return;
        displayName.OnValueChanged += OnNameChanged;
        numbers.OnValueChanged += OnNumbersChanged;
        classId.OnValueChanged += OnClassChanged;
        tally.ShowName(displayName.Value.ToString());
        tally.ShowClass(classId.Value.ToString());
        Show(numbers.Value);
    }

    public override void OnNetworkDespawn()
    {
        displayName.OnValueChanged -= OnNameChanged;
        numbers.OnValueChanged -= OnNumbersChanged;
        classId.OnValueChanged -= OnClassChanged;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        displayName.Value = new FixedString64Bytes(Clip(tally.DisplayName));
        classId.Value = new FixedString32Bytes(Clip(tally.ClassId));
        if (Time.unscaledTime >= nextPing)
        {
            nextPing = Time.unscaledTime + PingInterval;
            tally.ShowPing(MeasurePing());
        }
        numbers.Value = new Numbers
        {
            Score = tally.Score, Kills = (ushort)tally.Kills, Executions = (ushort)tally.Executions, Downs = (ushort)tally.Downs,
            Deaths = (ushort)tally.Deaths, Ping = (ushort)tally.Ping,
            Damage = (uint)Mathf.Max(0f, tally.Damage), Healing = (uint)Mathf.Max(0f, tally.Healing), Earned = tally.Earned,
            Headshots = Small(tally.Headshots), Revives = Small(tally.Revives), BestStreak = Small(tally.BestStreak),
            Takedowns = Small(tally.Takedowns), Farthest = Small(Mathf.RoundToInt(tally.FarthestHeadshot * 10f))
        };
    }

    /// <summary>On the host: this player really killed a shared enemy. Counted once here, so every PC shows the same streak.
    /// standout: the enemy was named or elite (backlog 325). icon: the killer's picture id for the feed (WeaponIcons), so every
    /// PC draws the same weapon whatever its own language calls it.</summary>
    internal void AnnounceKill(string weapon, string target, bool headshot, bool standout, string icon = null)
    {
        if (!IsSpawned || !IsServer) return;
        tally.CountStreak();
        KillRpc(new FixedString64Bytes(Clip(weapon)), new FixedString64Bytes(Clip(target)), headshot, standout, tally.Streak,
            new FixedString32Bytes(ClipId(icon)));
    }

    /// <summary>The console set this player's streak: the host holds the count.</summary>
    internal void ShareStreak(int streak)
    {
        if (IsSpawned && !IsServer) StreakRpc(streak);
    }

    [Rpc(SendTo.Everyone)]
    private void KillRpc(FixedString64Bytes weapon, FixedString64Bytes target, bool headshot, bool standout, int streak, FixedString32Bytes icon)
    {
        CombatFeedHUD.PlayerKill(GetComponent<PlayerMovement>(), weapon.ToString(), target.ToString(), headshot, streak, standout, icon.ToString());
        // The host's count is the one: the killer's own PC keeps the best of it for the chapter's results.
        tally.NoteStreak(streak);
        // Every PC hears of this kill, so the first skull of a streak slows the whole room (item 18), and a named or
        // elite kill briefly (backlog 325).
        if (streak == CombatFeedHUD.SkullStreak) SkullSlowMotion.Begin();
        else if (standout) SkullSlowMotion.BeginBrief();
    }

    [Rpc(SendTo.Server)]
    private void StreakRpc(int streak) => tally.SetStreak(streak);

    private int MeasurePing()
    {
        if (IsServer) return 0;
        ulong measured = NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        return measured > 0 ? PingDisplay.Shown((int)measured) : 0;
    }

    // A long Korean name in UTF-8 could overrun the fixed string.
    private static string Clip(string name) => string.IsNullOrEmpty(name) ? "" : name.Length > 20 ? name.Substring(0, 20) : name;

    // Icon ids are asset names in ASCII: a 32-byte fixed string holds 29 of them.
    private static string ClipId(string id) => string.IsNullOrEmpty(id) ? "" : id.Length > 29 ? id.Substring(0, 29) : id;

    private static ushort Small(int value) => (ushort)Mathf.Clamp(value, 0, ushort.MaxValue);

    private void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes current) => tally.ShowName(current.ToString());
    private void OnClassChanged(FixedString32Bytes previous, FixedString32Bytes current) => tally.ShowClass(current.ToString());
    private void OnNumbersChanged(Numbers previous, Numbers current) => Show(current);

    private void Show(in Numbers now)
    {
        tally.ShowNumbers(now.Kills, now.Executions, now.Downs, now.Deaths, now.Score, now.Ping);
        tally.ShowResults(now.Damage, now.Healing, now.Headshots, now.Revives, now.BestStreak, now.Takedowns, now.Farthest * .1f, now.Earned);
    }
}
