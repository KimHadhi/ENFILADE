using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

/// <summary>
/// Opens a port on the home router for an internet room by UPnP (the Internet Gateway Device protocol most home routers
/// speak), so friends reach the host straight instead of through Unity's relay. It finds the router by SSDP, asks it to
/// forward a UDP port to this PC and for its outside address, and sends itself a packet through that address: only one
/// that comes back in proves the router forwards it and the firewall lets it through. The mapping is leased and renewed
/// while the room stays open, so a crash leaves the port open an hour at most. Anything that fails gives null, and the
/// room goes through the relay as before.
/// </summary>
public sealed class RouterPortMap
{
    private const string SsdpAddress = "239.255.255.250";
    private const int SsdpPort = 1900, LeaseSeconds = 3600;
    private const string Description = "ENFILADE";
    // UPnP error codes: another PC holds the port; the router takes only leases that never end.
    private const int Conflict = 718, PermanentOnly = 725;
    private static readonly string[] Searches =
    {
        "urn:schemas-upnp-org:device:InternetGatewayDevice:1", "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1"
    };
    // In order of preference: the kind most routers have, then the one dial-up style links use.
    private static readonly string[] Services =
    {
        "urn:schemas-upnp-org:service:WANIPConnection:2", "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1"
    };
    private static readonly TimeSpan Search = TimeSpan.FromSeconds(2), Echo = TimeSpan.FromMilliseconds(400);
    private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

    private readonly string control, service;
    private readonly IPAddress inside;
    private readonly int lease;
    private readonly CancellationTokenSource renewing = new CancellationTokenSource();

    /// <summary>The router's address on the internet, which friends connect to.</summary>
    public IPAddress Outside { get; }
    public ushort Port { get; }

    private RouterPortMap(string control, string service, IPAddress inside, IPAddress outside, ushort port, int lease)
    {
        this.control = control; this.service = service; this.inside = inside; this.lease = lease;
        Outside = outside; Port = port;
        if (lease > 0) Renew();
    }

    /// <summary>The first of `ports` the router forwards to this PC, checked from outside; null when there is none.</summary>
    public static async Task<RouterPortMap> Open(ushort[] ports, CancellationToken cancel)
    {
        try
        {
            Uri location = await FindRouter(cancel).ConfigureAwait(false);
            if (location == null) return null;
            (string control, string service) = await FindService(location, cancel).ConfigureAwait(false);
            if (control == null) return null;
            IPAddress router = (await Dns.GetHostAddressesAsync(location.Host).ConfigureAwait(false))
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            if (router == null) return null;
            IPAddress inside = InsideAddress(router);
            string reply = await Soap(control, service, "GetExternalIPAddress", "", cancel).ConfigureAwait(false);
            // A private or shared outside address means another router or the provider's NAT stands in front: nobody on the
            // internet can reach it.
            if (!IPAddress.TryParse(Value(reply, "NewExternalIPAddress"), out IPAddress outside) || !Reachable(outside)) return null;
            foreach (ushort port in ports)
            {
                int lease = await Map(control, service, inside, port, cancel).ConfigureAwait(false);
                if (lease < 0) continue;
                var map = new RouterPortMap(control, service, inside, outside, port, lease);
                bool back;
                try { back = await ComesBack(outside, port, cancel).ConfigureAwait(false); }
                catch (Exception e) when (Expected(e)) { back = false; }
                if (back) return map;
                map.Close();
                return null;
            }
            return null;
        }
        catch (Exception e) when (Expected(e))
        {
            return null;
        }
    }

    // What a router that doesn't answer, answers badly or refuses throws; anything else is a bug and is let through.
    private static bool Expected(Exception e) => e is SocketException || e is HttpRequestException || e is XmlException
        || e is OperationCanceledException || e is UpnpError || e is ObjectDisposedException || e is System.IO.IOException;

    /// <summary>Gives the port back to the router. Nothing waits for it; the lease ends it anyway.</summary>
    public async void Close()
    {
        renewing.Cancel();
        try { await Soap(control, service, "DeletePortMapping", Mapping(Port), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception e) when (Expected(e)) { }
    }

    // Asks again at half the lease, as long as the room is open.
    private async void Renew()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(lease / 2), renewing.Token).ConfigureAwait(false);
                await Soap(control, service, "AddPortMapping", Mapping(Port, inside, lease), renewing.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (Expected(e)) { }
    }

    // The lease the router took, or -1 when the port is someone else's.
    private static async Task<int> Map(string control, string service, IPAddress inside, ushort port, CancellationToken cancel)
    {
        try
        {
            await Soap(control, service, "AddPortMapping", Mapping(port, inside, LeaseSeconds), cancel).ConfigureAwait(false);
            return LeaseSeconds;
        }
        catch (UpnpError error) when (error.Code == PermanentOnly)
        {
            await Soap(control, service, "AddPortMapping", Mapping(port, inside, 0), cancel).ConfigureAwait(false);
            return 0;
        }
        catch (UpnpError error) when (error.Code == Conflict)
        {
            return -1;
        }
    }

    private static string Mapping(ushort port)
        => "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port + "</NewExternalPort><NewProtocol>UDP</NewProtocol>";

    private static string Mapping(ushort port, IPAddress inside, int lease)
        => Mapping(port) + "<NewInternalPort>" + port + "</NewInternalPort><NewInternalClient>" + inside + "</NewInternalClient>"
            + "<NewEnabled>1</NewEnabled><NewPortMappingDescription>" + Description + "</NewPortMappingDescription>"
            + "<NewLeaseDuration>" + lease + "</NewLeaseDuration>";

    // SSDP: a search sent to the multicast group, answered by the router with where its description lives.
    private static async Task<Uri> FindRouter(CancellationToken cancel)
    {
        using (var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
        {
            var group = new IPEndPoint(IPAddress.Parse(SsdpAddress), SsdpPort);
            foreach (string target in Searches)
            {
                byte[] search = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: " + SsdpAddress + ":" + SsdpPort
                    + "\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + target + "\r\n\r\n");
                await udp.SendAsync(search, search.Length, group).ConfigureAwait(false);
            }
            DateTime until = DateTime.UtcNow + Search;
            while (true)
            {
                TimeSpan left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return null;
                var reply = Received(udp);
                if (await Task.WhenAny(reply, Task.Delay(left, cancel)).ConfigureAwait(false) != reply) return null;
                string location = Header(Encoding.ASCII.GetString(reply.Result.Buffer), "LOCATION");
                if (location != null && Uri.TryCreate(location, UriKind.Absolute, out Uri uri)) return uri;
            }
        }
    }

    // The control address of the router's connection service, from its description.
    private static async Task<(string, string)> FindService(Uri location, CancellationToken cancel)
    {
        using (var response = await http.GetAsync(location, cancel).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode) return (null, null);
            var description = new XmlDocument { XmlResolver = null };
            description.LoadXml(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            Uri root = location;
            string urlBase = description.GetElementsByTagName("URLBase").Cast<XmlNode>().FirstOrDefault()?.InnerText;
            if (!string.IsNullOrWhiteSpace(urlBase) && Uri.TryCreate(urlBase.Trim(), UriKind.Absolute, out Uri declared)) root = declared;
            var offered = description.GetElementsByTagName("service").Cast<XmlNode>()
                .Select(node => (type: Child(node, "serviceType"), url: Child(node, "controlURL"))).ToList();
            foreach (string wanted in Services)
                foreach (var (type, url) in offered)
                    if (type == wanted && url != null && Uri.TryCreate(root, url, out Uri control))
                        return (control.ToString(), type);
            return (null, null);
        }
    }

    private static async Task<string> Soap(string control, string service, string action, string arguments, CancellationToken cancel)
    {
        string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" "
            + "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:" + action + " xmlns:u=\"" + service + "\">"
            + arguments + "</u:" + action + "></s:Body></s:Envelope>";
        using (var request = new HttpRequestMessage(HttpMethod.Post, control) { Content = new StringContent(body, Encoding.UTF8, "text/xml") })
        {
            request.Headers.TryAddWithoutValidation("SOAPAction", "\"" + service + "#" + action + "\"");
            using (var response = await http.SendAsync(request, cancel).ConfigureAwait(false))
            {
                string reply = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new UpnpError(int.TryParse(Value(reply, "errorCode"), out int code) ? code : 0);
                return reply;
            }
        }
    }

    // A packet to the outside address on this port must arrive on this port, as a friend's would.
    private static async Task<bool> ComesBack(IPAddress outside, ushort port, CancellationToken cancel)
    {
        using (var listen = new UdpClient(new IPEndPoint(IPAddress.Any, port)))
        using (var send = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
        {
            byte[] token = Guid.NewGuid().ToByteArray();
            var arrived = Received(listen);
            for (int tries = 0; tries < 3; tries++)
            {
                await send.SendAsync(token, token.Length, new IPEndPoint(outside, port)).ConfigureAwait(false);
                if (await Task.WhenAny(arrived, Task.Delay(Echo, cancel)).ConfigureAwait(false) == arrived) break;
            }
            return arrived.Status == TaskStatus.RanToCompletion && arrived.Result.Buffer.SequenceEqual(token);
        }
    }

    // A receive whose failure (the socket closing under it) is seen to, so it never surfaces later as unobserved.
    private static Task<UdpReceiveResult> Received(UdpClient udp)
    {
        var receive = udp.ReceiveAsync();
        receive.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        return receive;
    }

    // The address this PC has on the router's side: the one its packets to the router leave from.
    private static IPAddress InsideAddress(IPAddress router)
    {
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            probe.Connect(router, SsdpPort);
            return ((IPEndPoint)probe.LocalEndPoint).Address;
        }
    }

    private static bool Reachable(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        if (b.Length != 4) return false;
        return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || (b[0] == 100 && b[1] >= 64 && b[1] < 128) || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168) || b[0] >= 224);
    }

    private static string Header(string reply, string name)
    {
        foreach (string line in reply.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && line.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return line.Substring(colon + 1).Trim();
        }
        return null;
    }

    private static string Child(XmlNode node, string name)
        => node.ChildNodes.Cast<XmlNode>().FirstOrDefault(child => child.LocalName == name)?.InnerText?.Trim();

    // The text inside the first element of this name in a SOAP reply, whatever its namespace prefix.
    private static string Value(string reply, string element)
    {
        try
        {
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml(reply);
            foreach (XmlNode node in document.GetElementsByTagName("*"))
                if (node.LocalName == element) return node.InnerText.Trim();
        }
        catch (XmlException) { }
        return null;
    }

    private sealed class UpnpError : Exception
    {
        public readonly int Code;
        public UpnpError(int code) : base("UPnP error " + code) { Code = code; }
    }
}
