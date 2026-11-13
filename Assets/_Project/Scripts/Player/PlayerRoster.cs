using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Every player in play, and the one this PC controls. Anything that needs "the player" asks here instead of searching
/// the scene: screens use Local, enemies use Nearest.
/// </summary>
public static class PlayerRoster
{
    private static readonly List<PlayerMovement> players = new List<PlayerMovement>();
    public static IReadOnlyList<PlayerMovement> All => players;

    /// <summary>The network owner id of a player's object; 0 alone or before it is spawned.</summary>
    public static ulong IdOf(PlayerMovement player)
        => player.TryGetComponent(out NetworkObject network) && network.IsSpawned ? network.OwnerClientId : 0UL;

    /// <summary>The player a network owner id stands for among those playing on this PC; null when there is none.</summary>
    public static PlayerMovement ById(ulong id)
    {
        foreach (var someone in All)
            if (someone != null && (someone.IsLocal || someone.IsNetworkDriven) && IdOf(someone) == id) return someone;
        return null;
    }
    public static PlayerMovement Local { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear() { players.Clear(); Local = null; }

    internal static void Add(PlayerMovement player)
    {
        if (!players.Contains(player)) players.Add(player);
        if (player.IsLocal) Local = player;
    }

    internal static void Remove(PlayerMovement player)
    {
        players.Remove(player);
        if (Local == player) Local = null;
    }

    /// <summary>The closest player still alive, or null when nobody is.</summary>
    public static PlayerMovement Nearest(Vector3 position)
    {
        PlayerMovement nearest = null;
        float best = float.PositiveInfinity;
        foreach (var player in players)
        {
            if (!player.IsInitialized || !player.DamageReceiver.IsAlive) continue;
            float distance = (player.transform.position - position).sqrMagnitude;
            if (distance < best) { best = distance; nearest = player; }
        }
        return nearest;
    }
}
