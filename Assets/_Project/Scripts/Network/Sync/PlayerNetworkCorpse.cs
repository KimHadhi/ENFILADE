using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps a dead player's ragdoll the same on every PC. Their own PC runs the physics, because the camera rides it there
/// and can't wait for the network, and sends the pose; every other PC's corpse follows it. A hit on another PC's corpse
/// comes back here as a push. Physics at rest sends nothing. Alone it never spawns and does nothing.
/// Zombies this player killed fall the same way (ZombieCorpse): this PC runs them and sends their falls through here, so
/// if the player leaves mid-fall the others' corpses stop where they are.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerNetworkCorpse : NetworkBehaviour
{
    // Every network tick, as PlayerNetworkPose does: a falling body moves quickly.
    private const float SendInterval = .03f;

    private readonly NetworkVariable<CorpsePose> pose = new NetworkVariable<CorpsePose>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private PlayerRagdoll ragdoll;

    /// <summary>Someone on this PC hit this copy's corpse: the push happens where its physics runs.</summary>
    internal void Push(Vector3 point, Vector3 impulse) => PushRpc(point, impulse);

    public override void OnNetworkSpawn()
    {
        ragdoll = GetComponent<PlayerRagdoll>();
        pose.SetUpdateTraits(new NetworkVariableUpdateTraits { MinSecondsBetweenUpdates = SendInterval });
    }

    private void LateUpdate()
    {
        if (!IsSpawned || ragdoll == null) return;
        var corpse = ragdoll.Corpse;
        if (IsOwner) pose.Value = corpse != null ? corpse.Capture() : default;
        else if (corpse != null && pose.Value.Laid) corpse.Follow(pose.Value, Time.deltaTime);
    }

    [Rpc(SendTo.Owner)]
    private void PushRpc(Vector3 point, Vector3 impulse) => ragdoll.TakePush(point, impulse);

    /// <summary>A zombie corpse this PC runs, as it falls: every other PC's copy of it follows.</summary>
    internal void SendZombieFall(in ZombieCorpseFall fall)
    {
        if (IsSpawned && IsOwner) ZombieFallRpc(fall);
    }

    /// <summary>Someone on this PC hit a zombie corpse this player's PC runs.</summary>
    internal void PushZombie(ulong id, Vector3 point, Vector3 impulse)
    {
        if (IsSpawned) ZombiePushRpc(id, point, impulse);
    }

    [Rpc(SendTo.NotMe)]
    private void ZombieFallRpc(ZombieCorpseFall fall) => ZombieCorpse.Receive(fall, OwnerClientId);

    [Rpc(SendTo.Owner)]
    private void ZombiePushRpc(ulong id, Vector3 point, Vector3 impulse) => ZombieCorpse.TakePush(id, point, impulse);

    /// <summary>
    /// The shooter's PC burst a dead body's head (ZombieGore): a zombie's corpse, or a meal a calm zombie was eating
    /// (ZombieMealBody), each known on every PC by the zombie's network id. The others burst it too, along the same shot;
    /// nothing happens alone or for a body with no id.
    /// </summary>
    internal static void TellDeadHead(GameObject shooter, bool meal, ulong id, Vector3 direction)
    {
        if (id == 0UL || shooter == null) return;
        var sender = shooter.GetComponentInParent<PlayerNetworkCorpse>();
        if (sender != null && sender.IsSpawned) sender.DeadHeadRpc(meal, id, direction);
    }

    [Rpc(SendTo.NotMe)]
    private void DeadHeadRpc(bool meal, ulong id, Vector3 direction)
    {
        if (meal) ZombieMealBody.TakeBurst(id, direction);
        else ZombieCorpse.TakeHeadShot(id, direction);
    }
}
