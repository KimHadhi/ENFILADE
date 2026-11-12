using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shows the host's waves on the other PCs. The scene's WaveManager isn't a network object, so this sends a named
/// message whenever what the screen shows changes, and to anyone who has just arrived.
/// </summary>
[DisallowMultipleComponent]
public sealed class WaveSync : MonoBehaviour
{
    private const string Message = "ENFILADE.Waves";
    private WaveManager waves;
    private NetworkManager network;
    private WaveManager.Status sent;

    private void Start()
    {
        waves = GetComponent<WaveManager>();
        network = GameSession.Network;
        if (network == null) { enabled = false; return; }
        if (network.IsServer) network.SceneManager.OnSynchronizeComplete += SendTo;
        else network.CustomMessagingManager.RegisterNamedMessageHandler(Message, Receive);
    }

    private void OnDestroy()
    {
        if (network == null) return;
        if (network.SceneManager != null) network.SceneManager.OnSynchronizeComplete -= SendTo;
        network.CustomMessagingManager?.UnregisterNamedMessageHandler(Message);
    }

    private void Update()
    {
        if (!network.IsServer) return;
        var now = waves.CurrentStatus;
        if (now.Equals(sent)) return;
        sent = now;
        using var writer = Write(now);
        network.CustomMessagingManager.SendNamedMessageToAll(Message, writer);
    }

    private void SendTo(ulong client)
    {
        using var writer = Write(waves.CurrentStatus);
        network.CustomMessagingManager.SendNamedMessage(Message, client, writer);
    }

    private static FastBufferWriter Write(in WaveManager.Status status)
    {
        var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<WaveManager.Status>(), Allocator.Temp);
        writer.WriteValueSafe(status);
        return writer;
    }

    private void Receive(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out WaveManager.Status status);
        waves.ShowHostStatus(status);
    }
}
