using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// An enemy in a room. The host runs it; on every other PC it is a copy that shows the host's position and health. A
/// player's PC judges their own hits on its copy, shows them at once and tells the host, whose health is the true one.
/// Alone it never spawns and does nothing.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyNetwork : NetworkBehaviour
{
    private readonly NetworkVariable<float> health = new NetworkVariable<float>();
    private EnemyHealth enemy;

    /// <summary>This PC only shows the enemy; the host runs it.</summary>
    public bool IsCopy => IsSpawned && !IsServer;

    /// <summary>Puts a new enemy into the room, if there is one. Call it on the host, right after making the enemy.</summary>
    public static void SpawnInRoom(EnemyHealth enemy)
    {
        if (GameSession.IsOnline && enemy.TryGetComponent(out NetworkObject network)) network.Spawn(destroyWithScene: true);
    }

    public override void OnNetworkSpawn()
    {
        enemy = GetComponent<EnemyHealth>();
        if (IsServer)
        {
            health.Value = enemy.Health;
            return;
        }
        if (TryGetComponent(out EnemyChaseAI chase)) chase.enabled = false;
        if (TryGetComponent(out EnemyCombatAI combat)) combat.enabled = false;
        if (TryGetComponent(out NavMeshAgent agent)) agent.enabled = false;
        health.OnValueChanged += OnHealthChanged;
        enemy.ShowHealth(health.Value);
    }

    public override void OnNetworkDespawn() => health.OnValueChanged -= OnHealthChanged;

    private void Update()
    {
        if (IsSpawned && IsServer) health.Value = enemy.Health;
    }

    private void OnHealthChanged(float previous, float current) => enemy.ShowHealth(current);

    /// <summary>This PC's player hit its copy: the host applies it for real.</summary>
    internal void SendHit(in DamageHit hit) => HitRpc(new EnemyHitMessage(hit));

    /// <summary>The host took a hit: everyone but the one who landed it sees it now. They saw it when they fired.</summary>
    internal void AnnounceHit(in DamageHit hit, float before, float after)
    {
        if (!IsSpawned) return;
        ulong attacker = hit.Source != null && hit.Source.TryGetComponent(out NetworkObject player) ? player.OwnerClientId : NetworkManager.ServerClientId;
        ShowHitRpc(new EnemyHitMessage(hit), attacker, before, after);
    }

    /// <summary>A kill this PC judged: the corpse is out, and the host removes the enemy a moment later.</summary>
    internal void HideUntilRemoved()
    {
        foreach (var part in GetComponentsInChildren<Renderer>()) part.enabled = false;
        foreach (var part in GetComponentsInChildren<Collider>()) part.enabled = false;
    }

    [Rpc(SendTo.Server)]
    private void HitRpc(EnemyHitMessage message, RpcParams rpc = default) => enemy.ReceiveHit(message.ToHit(PlayerOf(rpc.Receive.SenderClientId)));

    [Rpc(SendTo.NotServer)]
    private void ShowHitRpc(EnemyHitMessage message, ulong attacker, float before, float after)
    {
        if (attacker == NetworkManager.LocalClientId) return;
        enemy.ShowHit(message.ToHit(PlayerOf(attacker)), before, after);
    }

    private static GameObject PlayerOf(ulong client)
    {
        foreach (var player in PlayerRoster.All)
            if (player.TryGetComponent(out NetworkObject network) && network.OwnerClientId == client) return player.gameObject;
        return null;
    }
}
