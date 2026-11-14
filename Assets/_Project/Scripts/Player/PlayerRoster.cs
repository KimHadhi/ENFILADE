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
    /// <summary>Every player in play. A foreach over it makes no garbage (Players).</summary>
    public static Players All => new Players(players);

    /// <summary>
    /// The roster, read only. Every zombie walks it, its crowd every frame and its sight and its distance to the nearest
    /// one at every think, and a foreach over an IReadOnlyList boxes the list's enumerator: one piece of garbage per walk,
    /// thousands a second in a horde. Its own GetEnumerator hands foreach the list's struct enumerator instead. It is
    /// still an IReadOnlyList, for anything that wants one.
    /// </summary>
    public readonly struct Players : IReadOnlyList<PlayerMovement>
    {
        private readonly List<PlayerMovement> list;
        internal Players(List<PlayerMovement> list) => this.list = list;
        public int Count => list.Count;
        public PlayerMovement this[int index] => list[index];
        public List<PlayerMovement>.Enumerator GetEnumerator() => list.GetEnumerator();
        IEnumerator<PlayerMovement> IEnumerable<PlayerMovement>.GetEnumerator() => list.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => list.GetEnumerator();
    }

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
