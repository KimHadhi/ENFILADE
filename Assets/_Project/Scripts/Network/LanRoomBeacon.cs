using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>While this PC hosts, answers anyone on the local network asking which rooms exist.</summary>
[DisallowMultipleComponent]
public sealed class LanRoomBeacon : MonoBehaviour
{
    public const int Port = 7778;
    public const string Question = "ENFILADE_ROOMS";
    private UdpClient socket;

    private void OnEnable()
    {
        try
        {
            socket = new UdpClient(AddressFamily.InterNetwork);
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        }
        catch (SocketException e)
        {
            Debug.LogWarning($"[LanRoomBeacon] Port {Port} is unavailable, so this room won't appear on the LAN list. {e.Message}");
            socket = null;
        }
    }

    private void Update()
    {
        while (socket != null && socket.Available > 0)
        {
            try
            {
                var asker = new IPEndPoint(IPAddress.Any, 0);
                if (Encoding.ASCII.GetString(socket.Receive(ref asker)) != Question) continue;
                var room = GameSession.DescribeRoom();
                if (room == null) continue;
                byte[] reply = Encoding.UTF8.GetBytes(JsonUtility.ToJson(room));
                socket.Send(reply, reply.Length, asker);
            }
            catch (SocketException) { return; } // an asker that went away; the next question still arrives
        }
    }

    private void OnDisable()
    {
        socket?.Close();
        socket = null;
    }
}
