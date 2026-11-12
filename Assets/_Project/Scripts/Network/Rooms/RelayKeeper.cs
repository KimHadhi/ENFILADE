using System;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using UnityEngine;

/// <summary>
/// Holds a friend's place on Unity's relay while their game runs over a direct connection (InternetRoom). Joining a room
/// reserves a relay allocation for the friend whether it is used or not, and the room's list is tied to it: an allocation
/// that stays silent for ten seconds counts as a player who dropped, and the lobby then removes them from the room, which
/// would show one player fewer and let a stranger into a full room. So a driver that carries nothing else binds the
/// allocation and pings it. Nothing ever connects through it; if it fails, only the list goes wrong, not the game.
/// </summary>
[DisallowMultipleComponent]
public sealed class RelayKeeper : MonoBehaviour
{
    // The relay forgets an allocation after 10 s of silence.
    private const int PingMs = 3000;
    // Binding takes a few exchanges, each needing an update; after that a ping now and then is all there is to send.
    private const float IdleStep = .25f;
    private NetworkDriver driver;
    private float wait;

    /// <summary>Starts holding the place this relay data is for. It is given up when `on` is destroyed.</summary>
    public static void Hold(GameObject on, RelayServerData relay)
    {
        var keeper = on.AddComponent<RelayKeeper>();
        try
        {
            var settings = new NetworkSettings(Allocator.Temp);
            settings.WithRelayParameters(ref relay, PingMs);
            keeper.driver = NetworkDriver.Create(settings);
            if (keeper.driver.Bind(NetworkEndpoint.AnyIpv4) != 0) keeper.Stop();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RelayKeeper] {e.Message}");
            keeper.Stop();
        }
    }

    private void Update()
    {
        if (!driver.IsCreated) return;
        var status = driver.GetRelayConnectionStatus();
        if (status == RelayConnectionStatus.AllocationInvalid) { Stop(); return; }
        if (status == RelayConnectionStatus.Established && (wait -= Time.unscaledDeltaTime) > 0f) return;
        wait = IdleStep;
        driver.ScheduleUpdate().Complete();
    }

    private void OnDestroy() => Stop();

    private void Stop()
    {
        if (driver.IsCreated) driver.Dispose();
        enabled = false;
    }
}
