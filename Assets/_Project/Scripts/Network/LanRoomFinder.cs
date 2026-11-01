using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>Asks the local network which rooms exist and times the answers, which gives a true ping.</summary>
public sealed class LanRoomFinder : IRoomList
{
    private const float AskInterval = 2f, ForgetAfter = 5f;
    private readonly Dictionary<string, RoomInfo> rooms = new Dictionary<string, RoomInfo>();
    private readonly Stopwatch sinceAsked = new Stopwatch();
    private readonly UdpClient socket;
    private float nextAsk;

    public IEnumerable<RoomInfo> Rooms => rooms.Values;
    public string Problem => "";

    public LanRoomFinder()
    {
        socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
    }

    public void Refresh() => nextAsk = 0f;

    public void Tick()
    {
        if (Time.unscaledTime >= nextAsk) Ask();
        Listen();
        foreach (string gone in rooms.Where(room => Time.unscaledTime - room.Value.lastSeen > ForgetAfter).Select(room => room.Key).ToArray())
            rooms.Remove(gone);
    }

    private void Ask()
    {
        nextAsk = Time.unscaledTime + AskInterval;
        byte[] question = Encoding.ASCII.GetBytes(LanRoomBeacon.Question);
        sinceAsked.Restart();
        foreach (var target in Targets())
        {
            try { socket.Send(question, question.Length, new IPEndPoint(target, LanRoomBeacon.Port)); }
            catch (SocketException) { } // a network card that can't send this; the others still ask
        }
    }

    private void Listen()
    {
        while (socket.Available > 0)
        {
            try
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                var room = JsonUtility.FromJson<RoomInfo>(Encoding.UTF8.GetString(socket.Receive(ref from)));
                room.address = from.Address.ToString();
                room.ping = (int)sinceAsked.ElapsedMilliseconds;
                room.lastSeen = Time.unscaledTime;
                rooms[room.id] = room;
            }
            catch (SocketException) { return; }
            catch (ArgumentException) { } // not one of our replies
        }
    }

    // This PC, the general broadcast, and each network card's own broadcast, which also covers virtual LANs.
    private static IEnumerable<IPAddress> Targets()
    {
        yield return IPAddress.Loopback;
        yield return IPAddress.Broadcast;
        foreach (var card in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (card.OperationalStatus != OperationalStatus.Up || card.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var unicast in card.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                byte[] address = unicast.Address.GetAddressBytes(), mask = unicast.IPv4Mask.GetAddressBytes();
                for (int i = 0; i < 4; i++) address[i] |= (byte)~mask[i];
                yield return new IPAddress(address);
            }
        }
    }

    public void Dispose() => socket.Close();
}
