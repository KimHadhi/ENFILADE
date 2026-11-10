using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// The console's /net: the room's true ping (not PingDisplay's), data per second and frame times, measured until it is
/// switched off, which reports the whole measurement and copies it, since chat text can't be selected. A clean start
/// each time, and gone with the room.
/// </summary>
[DisallowMultipleComponent]
public sealed class NetworkStatsHUD : MonoBehaviour
{
    private const float SampleTime = 1f, Width = 300f, LineHeight = 24f, Margin = 20f;

    private readonly List<string> lines = new List<string>();
    private NetworkManager network;
    private NetworkTransport transport;
    // One of the two counts the room's data: a plain transport, or a host's two as one.
    private CountingTransport single;
    private DualTransport dual;
    private long lastSent, lastReceived, sentAtStart, receivedAtStart;
    private float sampleLeft = SampleTime, worstFrame, worstInSample, elapsed, sentPeak, receivedPeak;
    private int frames, framesInSample, pingSamples;
    private long pingTotal;
    private ulong pingPeak;

    /// <summary>Starts a measurement, or ends the one running and returns its result.</summary>
    public static string Toggle()
    {
        var network = GameSession.Network;
        if (network == null) return "방에 있을 때만 잴 수 있습니다.";
        if (network.TryGetComponent(out NetworkStatsHUD running))
        {
            string result = running.Result();
            Destroy(running);
            GUIUtility.systemCopyBuffer = result;
            return result + " (복사됨)";
        }
        network.gameObject.AddComponent<NetworkStatsHUD>();
        return "측정을 시작했습니다. 뛰어다닌 뒤 /net을 다시 입력하면 결과가 나옵니다.";
    }

    private void Awake()
    {
        network = GetComponent<NetworkManager>();
        transport = network.NetworkConfig.NetworkTransport;
        single = transport as CountingTransport;
        dual = transport as DualTransport;
        if (single == null && dual == null) { enabled = false; return; }
        lastSent = sentAtStart = Sent;
        lastReceived = receivedAtStart = Received;
    }

    private long Sent => dual != null ? dual.BytesSent : single.BytesSent;
    private long Received => dual != null ? dual.BytesReceived : single.BytesReceived;

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        elapsed += dt;
        frames++;
        framesInSample++;
        worstInSample = Mathf.Max(worstInSample, dt);
        sampleLeft -= dt;
        if (sampleLeft > 0f) return;
        Sample(SampleTime - sampleLeft);
        sampleLeft = SampleTime;
    }

    private void Sample(float time)
    {
        float sent = (Sent - lastSent) / 1024f / time;
        float received = (Received - lastReceived) / 1024f / time;
        lastSent = Sent;
        lastReceived = Received;
        sentPeak = Mathf.Max(sentPeak, sent);
        receivedPeak = Mathf.Max(receivedPeak, received);
        worstFrame = Mathf.Max(worstFrame, worstInSample);

        lines.Clear();
        // A host that takes friends both ways says which way each came, next to their ping.
        string way = dual != null ? "Relay + direct" : single.Protocol == UnityTransport.ProtocolType.RelayUnityTransport ? "Relay" : "direct";
        lines.Add($"{(network.IsHost ? "Host" : "Client")} on {way}, {network.ConnectedClientsIds.Count} players");
        lines.Add(GameSession.LinkName);
        if (network.IsHost)
        {
            foreach (ulong client in network.ConnectedClientsIds)
                if (client != network.LocalClientId) AddPing($"Player {client}" + (dual == null ? "" : GameSession.CameDirect(client) ? " (direct)" : " (relay)"), client);
        }
        else AddPing("Host", NetworkManager.ServerClientId);
        lines.Add($"Up {sent:F1} kB/s, down {received:F1} kB/s");
        lines.Add($"{framesInSample / time:F0} fps, worst {worstInSample * 1000f:F0} ms");
        framesInSample = 0;
        worstInSample = 0f;
    }

    private void AddPing(string who, ulong client)
    {
        ulong ping = transport.GetCurrentRtt(client);
        lines.Add($"{who}: true ping {ping} ms");
        // Zero means the transport has no reading yet, not a perfect connection.
        if (ping == 0) return;
        pingTotal += (long)ping;
        pingSamples++;
        if (ping > pingPeak) pingPeak = ping;
    }

    private string Result()
    {
        if (elapsed <= 0f) return "측정한 시간이 없습니다.";
        string ping = pingSamples > 0 ? $"실제 핑 평균 {pingTotal / pingSamples} ms, 최고 {pingPeak} ms" : "실제 핑 없음";
        float sent = (Sent - sentAtStart) / 1024f / elapsed, received = (Received - receivedAtStart) / 1024f / elapsed;
        // Whose room and how it is carried, so a ping can be read for what it is (friend feedback C16).
        string link = (network.IsHost ? "방장" : "참가") + " · " + GameSession.LinkName;
        return $"측정 {elapsed:F0}초 ({link}): {ping} / 보냄 평균 {sent:F1}, 최고 {sentPeak:F1} kB/s / 받음 평균 {received:F1}, 최고 {receivedPeak:F1} kB/s / "
            + $"평균 {frames / elapsed:F0} fps, 가장 느린 프레임 {worstFrame * 1000f:F0} ms";
    }

    private void OnGUI()
    {
        if (lines.Count == 0) return;
        GUILayout.BeginArea(new Rect(Screen.width - Width - Margin, Margin, Width, LineHeight * (lines.Count + 1)), GUI.skin.box);
        foreach (string line in lines) GUILayout.Label(line);
        GUILayout.EndArea();
    }
}
