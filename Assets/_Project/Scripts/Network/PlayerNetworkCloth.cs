using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps a player's cloth the same on every PC. Their own PC simulates it, and every other PC's copy puts its bones where
/// this says (PlayerBodyCloth). It goes unreliably, because a late pose is worth nothing once a newer one is out. Alone it
/// never spawns and does nothing.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerNetworkCloth : NetworkBehaviour
{
    // Cloth at rest needs only this: enough to mend a pose lost on the way and to show someone who just joined.
    private const float RestInterval = 1f;

    private PlayerBodyCloth cloth;
    private ClothPose sent;
    private float sentAt;

    public override void OnNetworkSpawn()
    {
        cloth = GetComponent<PlayerBodyCloth>();
        if (IsOwner && cloth != null) cloth.Simulated += Send;
    }

    public override void OnNetworkDespawn()
    {
        if (cloth != null) cloth.Simulated -= Send;
    }

    // Right after the simulation, while the bones still hold its result and before the arms press the skirts: every PC
    // presses its copy with the arms it shows. The next frame starts by putting the bones back.
    private void Send()
    {
        float since = Time.unscaledTime - sentAt;
        if (since < cloth.SendInterval) return;
        var pose = default(ClothPose);
        cloth.Capture(ref pose);
        if (pose.Count == 0 || (pose.Matches(ref sent) && since < RestInterval)) return;
        sent = pose;
        sentAt = Time.unscaledTime;
        ShowRpc(pose);
    }

    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    private void ShowRpc(ClothPose pose)
    {
        if (cloth != null) cloth.Show(ref pose);
    }
}
