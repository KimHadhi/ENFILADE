using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps a dead player's ragdoll the same on every PC. Their own PC runs the physics, because the camera rides it there
/// and can't wait for the network, and sends the pose; every other PC's corpse follows it. A hit on another PC's corpse
/// comes back here as a push. Physics at rest sends nothing. Alone it never spawns and does nothing.
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
}
