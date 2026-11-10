using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// The UDP socket a direct internet connection is made through, without anybody opening a port (hole punching; user,
/// 2026-11-10: a neighbour's room went through the Tokyo relay at 100 ms because the router refused UPnP). A home router
/// lets a packet in only from an address this PC has just sent to, so both PCs must send to each other at once, and each
/// must know what the other looks like from outside:
///   1. Discover asks public STUN servers what address and port this socket has on the internet.
///   2. The room carries the host's (InternetRoom); a friend's travels to the host as they join.
///   3. The host sends Punch packets at the friend, which opens the host's router for them; the friend sends Probe packets
///      at the host, which opens the friend's router, and an Ack coming back proves the way works in both directions.
/// What happens then differs by side. A friend closes this socket and the game's transport binds the same port (Release):
/// routers remember the port, not the socket. The host keeps this socket for the whole room, because it must keep
/// punching for every newcomer, which the game's transport can't do: so here the socket stands in front of the transport,
/// which listens on this PC only, and each friend's packets are passed on through a socket of their own, so the transport
/// sees each friend as a separate address (Serve). Nothing here knows about Unity: it runs on its own threads.
/// </summary>
public sealed class PunchSocket : IDisposable
{
    // Everything that is not the game's own traffic: four bytes neither the transport nor STUN starts a packet with, what
    // the packet is, and the room's token, so only somebody the room told can ask to be let in.
    private const int SignalSize = 13;
    private const byte ProbeSignal = 1, AckSignal = 2, PunchSignal = 3;
    private static readonly byte[] Magic = { 0xE7, 0x4E, 0x46, 0x50 };
    private static readonly byte[] StunCookie = { 0x21, 0x12, 0xA4, 0x42 };
    private const int StunHeader = 20, StunId = 12;
    // The transport never sends more than 1472 bytes; a bigger buffer costs nothing.
    private const int BufferSize = 4096;
    // A map loading sends hundreds of packets in one frame; the system's default buffer holds about forty.
    private const int SocketBuffer = 1 << 20;
    // How long the socket's thread waits with nothing arriving, which is also how late a repeated send can be.
    private const int WaitMicroseconds = 20000, PeerWaitMicroseconds = 100000;
    private const int RepeatMs = 150, PunchMs = 5000, SweepMs = 1000;
    // Windows only: a packet sent to a closed port otherwise makes the next receive fail (SIO_UDP_CONNRESET).
    private const int IgnoreUnreachable = unchecked((int)0x9800000C);

    private readonly Socket socket;
    private readonly Thread thread;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ConcurrentQueue<IPEndPoint> punchRequests = new ConcurrentQueue<IPEndPoint>();
    // Owned by the socket's thread.
    private readonly Dictionary<IPEndPoint, Peer> peers = new Dictionary<IPEndPoint, Peer>();
    private readonly List<Punching> punching = new List<Punching>();
    private readonly byte[] signal = new byte[SignalSize];
    private byte[] keepAliveId;
    private long keepAliveAt, sweepAt;
    // Handed to the socket's thread by whoever asks. The two questions are swapped in and out whole, so an answer to an
    // old one never wipes out a new one.
    private Discovery discovery;
    private Probing probing;
    private volatile Serving serving;
    private volatile IPEndPoint outside, keepAliveServer;
    private volatile bool closed, reachable = true;
    private int peerCount;

    /// <summary>How often the host asks again what it looks like from outside, which also keeps the router remembering it.</summary>
    internal int KeepAliveMs = 15000;
    /// <summary>A friend this long silent has left, and their socket is closed.</summary>
    internal int PeerIdleMs = 20000;

    /// <summary>The port on this PC.</summary>
    public int Port { get; }
    /// <summary>What the internet sees this socket as, or null while nobody has told.</summary>
    public IPEndPoint Outside => outside;
    /// <summary>
    /// False when two servers saw this socket on two different ports: the router gives every destination a port of its
    /// own, so the address one server saw is no use to anybody else.
    /// </summary>
    public bool Reachable => reachable;
    /// <summary>Friends connected through this socket right now (the host's side).</summary>
    public int Peers => Volatile.Read(ref peerCount);
    /// <summary>The outside address changed under a running room. Raised on the socket's own thread.</summary>
    public event Action OutsideChanged;

    private PunchSocket(Socket socket)
    {
        this.socket = socket;
        Port = ((IPEndPoint)socket.LocalEndPoint).Port;
        Array.Copy(Magic, signal, Magic.Length);
        thread = new Thread(Run) { IsBackground = true, Name = "PunchSocket " + Port };
        thread.Start();
    }

    /// <summary>A socket on the first of `ports` that is free (0 is any free port), or null when none of them is.</summary>
    public static PunchSocket Open(IEnumerable<int> ports)
    {
        foreach (int port in ports)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                Prepare(socket);
                return new PunchSocket(socket);
            }
            catch (SocketException) { socket.Close(); }
        }
        return null;
    }

    /// <summary>A token only this room's members get to know.</summary>
    public static ulong NewToken()
    {
        var bytes = new byte[8];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        return BitConverter.ToUInt64(bytes, 0);
    }

    /// <summary>
    /// The addresses of STUN servers written as "name:port name:port". A name that doesn't resolve in time is left out.
    /// </summary>
    public static async Task<List<IPEndPoint>> Resolve(string servers, int timeoutMs)
    {
        var looking = new List<Task<IPEndPoint>>();
        foreach (string server in (servers ?? "").Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)) looking.Add(Resolve(server));
        var all = Task.WhenAll(looking);
        await Task.WhenAny(all, Task.Delay(timeoutMs)).ConfigureAwait(false);
        return looking.Where(task => task.Status == TaskStatus.RanToCompletion && task.Result != null).Select(task => task.Result).ToList();
    }

    private static async Task<IPEndPoint> Resolve(string server)
    {
        int colon = server.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(server.Substring(colon + 1), out int port) || port <= 0 || port > 65535) return null;
        string name = server.Substring(0, colon);
        try
        {
            if (IPAddress.TryParse(name, out IPAddress typed)) return typed.AddressFamily == AddressFamily.InterNetwork ? new IPEndPoint(typed, port) : null;
            var found = await Dns.GetHostAddressesAsync(name).ConfigureAwait(false);
            var address = found.FirstOrDefault(one => one.AddressFamily == AddressFamily.InterNetwork);
            return address != null ? new IPEndPoint(address, port) : null;
        }
        catch (SocketException) { return null; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>"1.2.3.4:5678" as an address, or null.</summary>
    public static IPEndPoint Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || !IPAddress.TryParse(text.Substring(0, colon), out IPAddress address) || address.AddressFamily != AddressFamily.InterNetwork) return null;
        return int.TryParse(text.Substring(colon + 1), out int port) && port > 0 && port <= 65535 ? new IPEndPoint(address, port) : null;
    }

    /// <summary>
    /// Asks the servers what this socket looks like from outside. True once one has answered; Outside and Reachable hold
    /// what they said. The first server that answers is the one a running room keeps asking.
    /// </summary>
    public Task<bool> Discover(IReadOnlyList<IPEndPoint> servers, int timeoutMs)
    {
        if (closed || servers == null || servers.Count == 0) return Task.FromResult(false);
        var asked = new Discovery(servers, clock.ElapsedMilliseconds + timeoutMs);
        Interlocked.Exchange(ref discovery, asked)?.Done.TrySetResult(false);
        return asked.Done.Task;
    }

    /// <summary>The router forwards a port to this socket (RouterPortMap), which tells the outside address without asking.</summary>
    public void Assume(IPEndPoint address)
    {
        outside = address;
        reachable = true;
        // Nothing to keep alive or to watch: the router holds the port for as long as it was asked to.
        keepAliveServer = null;
    }

    /// <summary>
    /// The host's side: from now on a Probe with the room's token is answered, and whoever sent it has their packets passed
    /// to the game's listener on this PC (and its answers passed back), up to `most` friends at once.
    /// </summary>
    public void Serve(ulong token, IPEndPoint listener, int most) => serving = new Serving(token, listener, most);

    /// <summary>The host's side: opens this PC's router for a friend who is about to probe from `toward`.</summary>
    public void Punch(IPEndPoint toward)
    {
        if (toward != null && !closed) punchRequests.Enqueue(toward);
    }

    /// <summary>
    /// A friend's side: probes every address the host may be reached at until one answers with the room's token, and
    /// gives that address; null when none did in time.
    /// </summary>
    public Task<IPEndPoint> Probe(ulong token, IReadOnlyList<IPEndPoint> candidates, int timeoutMs)
    {
        if (closed || candidates == null || candidates.Count == 0) return Task.FromResult<IPEndPoint>(null);
        var asked = new Probing(token, candidates, clock.ElapsedMilliseconds + timeoutMs);
        Interlocked.Exchange(ref probing, asked)?.Done.TrySetResult(null);
        return asked.Done.Task;
    }

    /// <summary>
    /// A friend's side, once a probe was answered: closes the socket and gives its port, which the game's transport binds
    /// next. Both routers go on letting that port's packets through.
    /// </summary>
    public int Release()
    {
        Dispose();
        return Port;
    }

    public void Dispose()
    {
        if (closed) return;
        closed = true;
        socket.Close();
        // The thread is inside a wait of 20 ms at most; the friends' sockets are its own until it has ended.
        if (Thread.CurrentThread != thread) thread.Join(500);
        foreach (var peer in peers.Values.ToArray()) peer.Close();
        Interlocked.Exchange(ref discovery, null)?.Done.TrySetResult(false);
        Interlocked.Exchange(ref probing, null)?.Done.TrySetResult(null);
    }

    private static void Prepare(Socket socket)
    {
        try { socket.ReceiveBufferSize = SocketBuffer; socket.SendBufferSize = SocketBuffer; }
        catch (SocketException) { }
        try { socket.IOControl(IgnoreUnreachable, new byte[] { 0, 0, 0, 0 }, null); }
        catch (Exception e) when (e is SocketException || e is PlatformNotSupportedException || e is NotSupportedException || e is NotImplementedException) { }
    }

    private void Run()
    {
        var buffer = new byte[BufferSize];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (!closed)
        {
            try
            {
                if (socket.Poll(WaitMicroseconds, SelectMode.SelectRead))
                {
                    int length = socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref from);
                    Received(buffer, length, (IPEndPoint)from);
                }
                Tick(clock.ElapsedMilliseconds);
            }
            // A packet somebody refused or one too long for the buffer says nothing about the socket itself. The pause
            // keeps a socket that fails every time from eating a whole core.
            catch (SocketException) { if (closed) break; Thread.Sleep(1); }
            catch (ObjectDisposedException) { break; }
        }
    }

    private void Received(byte[] data, int length, IPEndPoint from)
    {
        if (length == SignalSize && IsSignal(data)) { Signalled(data[Magic.Length], BitConverter.ToUInt64(data, Magic.Length + 1), from); return; }
        if (length >= StunHeader && Matches(data, 4, StunCookie, StunCookie.Length) && Answered(data, length)) return;
        if (peers.TryGetValue(from, out Peer peer)) peer.ToGame(data, length, clock.ElapsedMilliseconds);
    }

    private static bool IsSignal(byte[] data) => Matches(data, 0, Magic, Magic.Length);

    private static bool Matches(byte[] data, int at, byte[] with, int count)
    {
        for (int i = 0; i < count; i++)
            if (data[at + i] != with[i]) return false;
        return true;
    }

    private void Signalled(byte kind, ulong token, IPEndPoint from)
    {
        var host = serving;
        if (host != null && kind == ProbeSignal && token == host.Token)
        {
            if (Admit(from, host)) Send(AckSignal, token, from);
            return;
        }
        var asked = Volatile.Read(ref probing);
        if (asked == null || token != asked.Token) return;
        if (kind == AckSignal) { Interlocked.CompareExchange(ref probing, null, asked); asked.Done.TrySetResult(from); }
        // The host got through to here, so the next probe gets through to the host: no need to wait for the next round.
        else if (kind == PunchSignal) Send(ProbeSignal, token, from);
    }

    // True when this address has a socket of its own to the game's listener, made now if there was room.
    private bool Admit(IPEndPoint from, Serving host)
    {
        long now = clock.ElapsedMilliseconds;
        if (peers.TryGetValue(from, out Peer known)) { known.Heard(now); return true; }
        if (peers.Count >= host.Most) return false;
        Peer peer;
        try { peer = new Peer(this, from, host.Listener, now); }
        catch (SocketException) { return false; }
        peers.Add(from, peer);
        Volatile.Write(ref peerCount, peers.Count);
        // Their probe arrived, so this router is open for them already.
        punching.RemoveAll(one => one.Toward.Equals(from));
        return true;
    }

    private void Send(byte kind, ulong token, IPEndPoint toward)
    {
        signal[Magic.Length] = kind;
        for (int i = 0; i < 8; i++) signal[Magic.Length + 1 + i] = (byte)(token >> (8 * i));
        SendRaw(signal, signal.Length, toward);
    }

    private void SendRaw(byte[] data, int length, IPEndPoint toward)
    {
        try { socket.SendTo(data, 0, length, SocketFlags.None, toward); }
        // An address that can't be reached from this PC (a candidate on somebody else's home network) or a full buffer.
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    private void Tick(long now)
    {
        var asking = Volatile.Read(ref discovery);
        if (asking != null && asking.Tick(this, now)) Interlocked.CompareExchange(ref discovery, null, asking);

        var asked = Volatile.Read(ref probing);
        if (asked != null)
        {
            if (now >= asked.Until) { Interlocked.CompareExchange(ref probing, null, asked); asked.Done.TrySetResult(null); }
            else if (now >= asked.NextAt)
            {
                asked.NextAt = now + RepeatMs;
                foreach (var candidate in asked.Candidates) Send(ProbeSignal, asked.Token, candidate);
            }
        }

        var host = serving;
        while (punchRequests.TryDequeue(out IPEndPoint toward))
            if (host != null && !peers.ContainsKey(toward) && punching.Count < 64) punching.Add(new Punching { Toward = toward, Until = now + PunchMs });
        for (int i = punching.Count - 1; i >= 0; i--)
        {
            var one = punching[i];
            if (now >= one.Until) { punching.RemoveAt(i); continue; }
            if (now < one.NextAt) continue;
            one.NextAt = now + RepeatMs;
            if (host != null) Send(PunchSignal, host.Token, one.Toward);
        }

        var server = keepAliveServer;
        if (host != null && server != null && now >= keepAliveAt)
        {
            keepAliveAt = now + KeepAliveMs;
            keepAliveId = NewStunId();
            SendRaw(StunRequest(keepAliveId), StunHeader, server);
        }

        if (now < sweepAt) return;
        sweepAt = now + SweepMs;
        List<IPEndPoint> gone = null;
        foreach (var pair in peers)
            if (now - pair.Value.LastHeard > PeerIdleMs) (gone ?? (gone = new List<IPEndPoint>())).Add(pair.Key);
        if (gone == null) return;
        foreach (var address in gone) { peers[address].Close(); peers.Remove(address); }
        Volatile.Write(ref peerCount, peers.Count);
    }

    // True when the packet answers a question this socket asked.
    private bool Answered(byte[] data, int length)
    {
        var asking = Volatile.Read(ref discovery);
        if (asking != null && asking.Answered(data, length))
        {
            if (asking.Finish(this, false)) Interlocked.CompareExchange(ref discovery, null, asking);
            return true;
        }
        if (keepAliveId == null || !TryReadStun(data, length, keepAliveId, out IPEndPoint seen)) return false;
        keepAliveId = null;
        if (!seen.Equals(outside))
        {
            outside = seen;
            OutsideChanged?.Invoke();
        }
        return true;
    }

    private static byte[] NewStunId()
    {
        var id = new byte[StunId];
        Array.Copy(Guid.NewGuid().ToByteArray(), id, StunId);
        return id;
    }

    // A STUN binding request (RFC 5389): type 1, no attributes, the cookie, and an id the answer repeats.
    private static byte[] StunRequest(byte[] id)
    {
        var request = new byte[StunHeader];
        request[1] = 0x01;
        Array.Copy(StunCookie, 0, request, 4, StunCookie.Length);
        Array.Copy(id, 0, request, 8, StunId);
        return request;
    }

    // The address a STUN server saw the request come from, when this is the success answer to the request with this id.
    internal static bool TryReadStun(byte[] data, int length, byte[] id, out IPEndPoint seen)
    {
        seen = null;
        if (length < StunHeader || data[0] != 0x01 || data[1] != 0x01 || !Matches(data, 4, StunCookie, StunCookie.Length) || !Matches(data, 8, id, StunId)) return false;
        int end = Math.Min(length, StunHeader + ((data[2] << 8) | data[3]));
        IPEndPoint plain = null;
        for (int at = StunHeader; at + 4 <= end;)
        {
            int kind = (data[at] << 8) | data[at + 1], size = (data[at + 2] << 8) | data[at + 3], value = at + 4;
            if (value + size > end) break;
            // Only IPv4 addresses (family 1) are of use here: eight bytes with the port and the address.
            if (size >= 8 && data[value + 1] == 0x01)
            {
                int port = (data[value + 2] << 8) | data[value + 3];
                // XOR-MAPPED-ADDRESS, scrambled with the cookie so that routers which rewrite addresses inside packets leave it alone.
                if (kind == 0x0020)
                {
                    var address = new byte[4];
                    for (int i = 0; i < 4; i++) address[i] = (byte)(data[value + 4 + i] ^ StunCookie[i]);
                    seen = new IPEndPoint(new IPAddress(address), port ^ 0x2112);
                    return true;
                }
                // MAPPED-ADDRESS, the older plain form: used only when the other is missing.
                if (kind == 0x0001) plain = new IPEndPoint(new IPAddress(new[] { data[value + 4], data[value + 5], data[value + 6], data[value + 7] }), port);
            }
            at = value + ((size + 3) & ~3);
        }
        seen = plain;
        return seen != null;
    }

    private sealed class Serving
    {
        public readonly ulong Token;
        public readonly IPEndPoint Listener;
        public readonly int Most;
        public Serving(ulong token, IPEndPoint listener, int most) { Token = token; Listener = listener; Most = most; }
    }

    private sealed class Punching
    {
        public IPEndPoint Toward;
        public long Until, NextAt;
    }

    private sealed class Probing
    {
        public readonly ulong Token;
        public readonly IReadOnlyList<IPEndPoint> Candidates;
        public readonly long Until;
        public long NextAt;
        // The answer is picked up by whoever waits, not on the socket's thread.
        public readonly TaskCompletionSource<IPEndPoint> Done = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        public Probing(ulong token, IReadOnlyList<IPEndPoint> candidates, long until) { Token = token; Candidates = candidates; Until = until; }
    }

    // One round of asking STUN servers. Two that agree prove the router uses one port whoever it talks to.
    private sealed class Discovery
    {
        private readonly IReadOnlyList<IPEndPoint> servers;
        private readonly byte[][] ids;
        private readonly IPEndPoint[] seen;
        private readonly long until;
        private long nextAt;
        public readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Discovery(IReadOnlyList<IPEndPoint> servers, long until)
        {
            this.servers = servers;
            this.until = until;
            ids = servers.Select(_ => NewStunId()).ToArray();
            seen = new IPEndPoint[servers.Count];
        }

        // True when the round is over.
        public bool Tick(PunchSocket socket, long now)
        {
            if (now >= until) return Finish(socket, true);
            if (now < nextAt) return false;
            nextAt = now + RepeatMs;
            for (int i = 0; i < servers.Count; i++)
                if (seen[i] == null) socket.SendRaw(StunRequest(ids[i]), StunHeader, servers[i]);
            return false;
        }

        public bool Answered(byte[] data, int length)
        {
            for (int i = 0; i < ids.Length; i++)
                if (seen[i] == null && TryReadStun(data, length, ids[i], out IPEndPoint address)) { seen[i] = address; return true; }
            return false;
        }

        // Ends the round once every server has answered, or when time is up with whatever arrived.
        public bool Finish(PunchSocket socket, bool timeUp)
        {
            if (!timeUp && seen.Any(address => address == null)) return false;
            int first = Array.FindIndex(seen, address => address != null);
            if (first >= 0)
            {
                socket.outside = seen[first];
                socket.reachable = seen.All(address => address == null || address.Equals(seen[first]));
                socket.keepAliveServer = servers[first];
                socket.keepAliveAt = socket.clock.ElapsedMilliseconds + socket.KeepAliveMs;
            }
            Done.TrySetResult(first >= 0);
            return true;
        }
    }

    // One friend on the host's side: a socket of their own on this PC, so the game's listener tells friends apart by it.
    private sealed class Peer
    {
        private readonly PunchSocket owner;
        private readonly IPEndPoint remote;
        private readonly Socket inside;
        private long lastHeard;
        private volatile bool closed;

        public long LastHeard => Volatile.Read(ref lastHeard);

        public Peer(PunchSocket owner, IPEndPoint remote, IPEndPoint listener, long now)
        {
            this.owner = owner;
            this.remote = remote;
            lastHeard = now;
            inside = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                inside.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                Prepare(inside);
                inside.Connect(listener);
            }
            catch (SocketException) { inside.Close(); throw; }
            new Thread(Run) { IsBackground = true, Name = "PunchSocket peer " + remote }.Start();
        }

        public void Heard(long now) => Volatile.Write(ref lastHeard, now);

        public void ToGame(byte[] data, int length, long now)
        {
            Volatile.Write(ref lastHeard, now);
            try { inside.Send(data, 0, length, SocketFlags.None); }
            // The listener is not up (yet): the transport sends again whatever mattered.
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        public void Close()
        {
            closed = true;
            inside.Close();
        }

        // The game's answers, back out through the one socket the friend's router knows. It waits in short turns
        // rather than inside a receive, so it ends on every platform once the socket is closed under it.
        private void Run()
        {
            var buffer = new byte[BufferSize];
            while (!closed && !owner.closed)
            {
                try
                {
                    if (!inside.Poll(PeerWaitMicroseconds, SelectMode.SelectRead)) continue;
                    int length = inside.Receive(buffer, 0, buffer.Length, SocketFlags.None);
                    if (length > 0) owner.SendRaw(buffer, length, remote);
                }
                catch (SocketException) { if (closed || owner.closed) break; Thread.Sleep(1); }
                catch (ObjectDisposedException) { break; }
            }
        }
    }
}
