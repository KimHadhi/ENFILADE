using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Carries a player's name and scoreboard numbers to the other PCs. The owner writes them; each PC shows them through
/// PlayerTally. Alone it never spawns and does nothing.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerNetworkScore : NetworkBehaviour
{
    private struct Numbers : INetworkSerializeByMemcpy
    {
        public long Score;
        public ushort Kills, Executions, Downs, Deaths, Ping;
    }

    private const float PingInterval = 2f;

    private readonly NetworkVariable<FixedString64Bytes> displayName = new NetworkVariable<FixedString64Bytes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Numbers> numbers = new NetworkVariable<Numbers>(default,
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
        tally.ShowName(displayName.Value.ToString());
        Show(numbers.Value);
    }

    public override void OnNetworkDespawn()
    {
        displayName.OnValueChanged -= OnNameChanged;
        numbers.OnValueChanged -= OnNumbersChanged;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        displayName.Value = new FixedString64Bytes(Clip(tally.DisplayName));
        if (Time.unscaledTime >= nextPing)
        {
            nextPing = Time.unscaledTime + PingInterval;
            tally.ShowPing(MeasurePing());
        }
        numbers.Value = new Numbers
        {
            Score = tally.Score, Kills = (ushort)tally.Kills, Executions = (ushort)tally.Executions, Downs = (ushort)tally.Downs,
            Deaths = (ushort)tally.Deaths, Ping = (ushort)tally.Ping
        };
    }

    /// <summary>On the host: this player really killed a shared enemy. Counted once here, so every PC shows the same streak.
    /// standout: the enemy was named or elite (backlog 325).</summary>
    internal void AnnounceKill(string weapon, string target, bool headshot, bool standout)
    {
        if (!IsSpawned || !IsServer) return;
        tally.CountStreak();
        KillRpc(new FixedString64Bytes(Clip(weapon)), new FixedString64Bytes(Clip(target)), headshot, standout, tally.Streak);
    }

    /// <summary>The console set this player's streak: the host holds the count.</summary>
    internal void ShareStreak(int streak)
    {
        if (IsSpawned && !IsServer) StreakRpc(streak);
    }

    [Rpc(SendTo.Everyone)]
    private void KillRpc(FixedString64Bytes weapon, FixedString64Bytes target, bool headshot, bool standout, int streak)
    {
        CombatFeedHUD.PlayerKill(GetComponent<PlayerMovement>(), weapon.ToString(), target.ToString(), headshot, streak, standout);
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
    private static string Clip(string name) => name.Length > 20 ? name.Substring(0, 20) : name;

    private void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes current) => tally.ShowName(current.ToString());
    private void OnNumbersChanged(Numbers previous, Numbers current) => Show(current);
    private void Show(in Numbers now) => tally.ShowNumbers(now.Kills, now.Executions, now.Downs, now.Deaths, now.Score, now.Ping);
}
