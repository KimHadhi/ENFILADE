using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every player in play, and the one this PC controls. Anything that needs "the player" asks here instead of searching
/// the scene: screens use Local, enemies use Nearest.
/// </summary>
public static class PlayerRoster
{
    private static readonly List<PlayerMovement> players = new List<PlayerMovement>();
    public static IReadOnlyList<PlayerMovement> All => players;
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
