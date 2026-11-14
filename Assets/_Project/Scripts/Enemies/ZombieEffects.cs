using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What class cards and ultimates do to zombies, in one place: the door the card code calls, so it never reaches into the
/// zombies' own parts. Each call is made on the PC of the player who did the thing (the user's rule of 2026-11-14: the
/// acting player's PC decides and sends only the result). It takes effect there at once, and this carries it to
/// whichever PCs need it: the host, where zombies think and walk, and every PC for what is seen. Alone there is nobody
/// to tell and it only takes effect.
/// - Suppress: one zombie pinned down by fire walks and winds up slower for a while, and cowers.
/// - Unseen: for a while no zombie sees a player (their sounds and their touch still count).
/// - Blind: gas over an area. Zombies in it lose whoever they were after, cough and stagger, and walk slower.
/// - Move: one zombie is put somewhere else at once (a duel's arena, and back).
/// Two more things cards lean on live with the hit itself: a silent kill (DamageHit.Silent, EnemyHealth.SilentWhen) and
/// zombies noticing a death beside them (ZombieMind.DeathSeen), which needs no call.
/// </summary>
public static class ZombieEffects
{
    private const string Message = "ENFILADE.ZombieEffects";
    private const byte KindUnseen = 1, KindBlind = 2;

    private static CustomMessagingManager listening;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => listening = null;

    // After Netcode has cleared its own static events for the play (it does at SubsystemRegistration): every room's
    // NetworkManager is heard of as it is made, and this PC listens from the moment it starts, before any player or
    // zombie is there to call in.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        NetworkManager.OnInstantiated -= RoomMade;
        NetworkManager.OnInstantiated += RoomMade;
    }

    private static void RoomMade(NetworkManager network)
    {
        network.OnServerStarted += () => Listen(network);
        network.OnClientStarted += () => Listen(network);
    }

    /// <summary>
    /// Makes sure this PC listens in the room it is in. It does from the room's start (RoomMade); every zombie that
    /// appears asks again (EnemyNetwork), should that start have been missed.
    /// </summary>
    internal static void Listen() => Listen(GameSession.Network);

    private static void Listen(NetworkManager network)
    {
        var messages = network != null ? network.CustomMessagingManager : null;
        if (messages == null || messages == listening) return;
        listening = messages;
        messages.RegisterNamedMessageHandler(Message, Receive);
    }

    // ---- One zombie ------------------------------------------------------------------------------------------

    /// <summary>
    /// Pins this zombie down for <paramref name="seconds"/>: it walks <paramref name="walkSlow"/> slower (0.35: at 65%
    /// of its speed), its windups take <paramref name="windupSlow"/> longer (0.3: 30% longer), and it cowers a moment on
    /// every PC. Called again while it lasts it is kept up, the longer time and the stronger slow standing; calling it
    /// for every round is fine, the word goes out only a few times a second. The caller picks the numbers for the zombie
    /// (half for a brute or a named one, say): they are applied as given.
    /// </summary>
    public static void Suppress(EnemyHealth zombie, float seconds, float walkSlow, float windupSlow)
    {
        if (zombie == null || !zombie.IsAlive || seconds <= 0f) return;
        // In a room the zombie's own word reaches every PC, this one at once; alone it only takes effect.
        if (zombie.TryGetComponent(out EnemyNetwork network) && network.IsSpawned) network.ShareSuppress(seconds, walkSlow, windupSlow);
        else Suppressed(zombie, seconds, walkSlow, windupSlow);
    }

    /// <summary>Every PC (EnemyNetwork's word of it, or the caller's own PC alone): the zombie is pinned down.</summary>
    internal static void Suppressed(EnemyHealth zombie, float seconds, float walkSlow, float windupSlow)
    {
        if (zombie == null || !zombie.IsAlive || seconds <= 0f) return;
        bool copy = zombie.TryGetComponent(out EnemyNetwork network) && network.IsCopy;
        // Its windup is timed on the PC of whoever it swings at, so every PC slows it; its walk is the host's.
        if (zombie.TryGetComponent(out EnemyCombatAI combat)) combat.SlowWindup(seconds, windupSlow);
        if (!copy && zombie.TryGetComponent(out EnemyHitReaction reaction)) reaction.Slow(seconds, walkSlow);
        if (zombie.TryGetComponent(out ZombieBody body)) body.Cower();
    }

    /// <summary>
    /// Puts this zombie at <paramref name="position"/>, facing <paramref name="rotation"/>, at once: a duel's arena and
    /// the way back. The host moves it (told by the caller's PC), and its copies jump there with it instead of gliding
    /// across the map. The spot needs walk mesh under it for the zombie to walk and fight on there; without any it is
    /// only set down and stands.
    /// </summary>
    public static void Move(EnemyHealth zombie, Vector3 position, Quaternion rotation)
    {
        if (zombie == null || !zombie.IsAlive) return;
        if (zombie.TryGetComponent(out EnemyNetwork network) && network.IsCopy) network.ShareMove(position, rotation);
        else Moved(zombie, position, rotation);
    }

    /// <summary>The host, or alone: the zombie is put there.</summary>
    internal static void Moved(EnemyHealth zombie, Vector3 position, Quaternion rotation)
    {
        if (zombie == null || !zombie.IsAlive) return;
        // Lying knocked down it is carried as it lies: it stands first, where it is going.
        if (zombie.TryGetComponent(out MeleeCombatTarget melee) && melee.IsKnockedDown) melee.ResetPosture();
        bool walks = zombie.TryGetComponent(out NavMeshAgent agent) && agent.isActiveAndEnabled && agent.Warp(position);
        if (!walks) zombie.transform.position = position;
        zombie.transform.rotation = rotation;
        if (zombie.TryGetComponent(out EnemyNetwork network)) network.Jump();
    }

    // ---- Players and places ----------------------------------------------------------------------------------

    /// <summary>
    /// For <paramref name="seconds"/> no zombie sees this player: whoever was after them loses them at once and goes to
    /// look where it last knew them, and nothing picks them as its quarry by sight. Their sounds are still heard, and a
    /// zombie they come close enough to touch, before its eyes, still has them.
    /// </summary>
    public static void Unseen(PlayerMovement player, float seconds)
    {
        if (player == null || seconds <= 0f) return;
        ZombieMind.Unsee(player, seconds);
        if (player.TryGetComponent(out NetworkObject known) && known.IsSpawned) Send(KindUnseen, known.NetworkObjectId, default, seconds, 0f, 0f);
    }

    /// <summary>No zombie's eyes make this player out now.</summary>
    public static bool IsUnseen(PlayerMovement player) => ZombieMind.IsUnseen(player);

    /// <summary>
    /// Blinding gas at <paramref name="at"/>, <paramref name="radius"/> metres round, for <paramref name="seconds"/>:
    /// zombies in it lose whoever they were after and make nobody out while they stay in it, cough and stagger (each
    /// cough stops them a moment, and one holding a player lets go), and walk <paramref name="walkSlow"/> slower. This
    /// is what the gas does; what it looks and sounds like is the caller's to show.
    /// </summary>
    public static void Blind(Vector3 at, float radius, float seconds, float walkSlow)
    {
        if (radius <= 0f || seconds <= 0f) return;
        ZombieMind.AddGas(at, radius, seconds, walkSlow);
        Send(KindBlind, 0UL, at, radius, seconds, walkSlow);
    }

    /// <summary>This spot is in blinding gas now.</summary>
    public static bool InGas(Vector3 point) => ZombieMind.AnyGas && ZombieMind.InGas(point, out _);

    // ---- The word between PCs --------------------------------------------------------------------------------
    // Neither is about one zombie, so they ride a named message, not a zombie's own RPC. A guest tells the host; the
    // host tells every other guest (not the one it heard it from, whose PC took it up as it was called).

    private static void Send(byte kind, ulong id, Vector3 at, float a, float b, float c, ulong heardFrom = ulong.MaxValue)
    {
        var network = GameSession.Network;
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        Listen(network);
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteValueSafe(kind);
        writer.WriteValueSafe(id);
        writer.WriteValueSafe(at);
        writer.WriteValueSafe(a);
        writer.WriteValueSafe(b);
        writer.WriteValueSafe(c);
        if (!network.IsServer)
        {
            network.CustomMessagingManager.SendNamedMessage(Message, NetworkManager.ServerClientId, writer);
            return;
        }
        foreach (ulong client in network.ConnectedClientsIds)
            if (client != network.LocalClientId && client != heardFrom) network.CustomMessagingManager.SendNamedMessage(Message, client, writer);
    }

    private static void Receive(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kind);
        reader.ReadValueSafe(out ulong id);
        reader.ReadValueSafe(out Vector3 at);
        reader.ReadValueSafe(out float a);
        reader.ReadValueSafe(out float b);
        reader.ReadValueSafe(out float c);
        var network = GameSession.Network;
        if (kind == KindUnseen)
        {
            if (network != null && network.SpawnManager != null && network.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject found)
                && found.TryGetComponent(out PlayerMovement player)) ZombieMind.Unsee(player, a);
        }
        else if (kind == KindBlind) ZombieMind.AddGas(at, a, b, c);
        if (network != null && network.IsServer) Send(kind, id, at, a, b, c, sender);
    }
}
