using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// dev-text: file (log lines for whoever plays with avatar mods; nothing here is shown on screen)

/// <summary>
/// Avatar mods passed round a room (Docs/Records/SyncAudit.md, item 20): a PC that hasn't the mod a friend's character or
/// outfit comes from showed the default character for them, its cloth standing still, so the two screens showed two
/// different people. Now the wearer's PC offers the files its look uses (each avatar file and its bundle, ModAvatars) to
/// the room as it puts the look on (PlayerNetworkPose), and any PC that hasn't the same file (by name, size and an FNV
/// hash of its bytes) asks for it. Every word goes through the host, as all of Netcode's named messages do: a guest's
/// files go up to the host first, which keeps them and hands them on. A file comes in 16 KB pieces, a few a frame,
/// written to Mods/&lt;mod&gt;~방/Avatars/ (its own folder, so a mod of the same name the player keeps is never written
/// over), checked against its hash, and the mods are read again (GameMods.Rescan) once nothing more is coming: the
/// friend's copy is dressed again in what they really wear.
/// Only avatar files (*.avatar.json) and bundles (*.bundle) travel, under a size cap, by bare file names (no folders), and
/// only files this PC asked for are taken. A mod's tables never travel: they change the game's own numbers. Network.csv
/// modShare 0 turns it off; modShareMostMB caps one file.
/// </summary>
[DisallowMultipleComponent]
public sealed class ModShare : MonoBehaviour
{
    private const string OfferMessage = "ENFILADE.ModOffer", WantMessage = "ENFILADE.ModWant", ChunkMessage = "ENFILADE.ModChunk";
    private const int ChunkBytes = 16 * 1024, ChunksPerFrame = 4;
    private const string RoomMark = "~방", BundleExtension = ".bundle";
    // Mods are read again this long after the last piece of anything came, so a json and its bundle land together (s).
    private const float RescanAfter = .5f;

    private struct Offer : INetworkSerializeByMemcpy
    {
        public FixedString64Bytes Mod;
        public FixedString128Bytes File;
        public uint Size, Hash;
        public ulong Owner;
    }

    private struct Want : INetworkSerializeByMemcpy
    {
        public FixedString64Bytes Mod;
        public FixedString128Bytes File;
        public uint Size, Hash;
        public ulong Asker;
    }

    private struct Piece : INetworkSerializeByMemcpy
    {
        public FixedString64Bytes Mod;
        public FixedString128Bytes File;
        public uint Size, Hash, Offset;
        public ushort Length;
    }

    // One file going out to one PC, a few pieces a frame.
    private sealed class Sending
    {
        public string Path, Mod, File;
        public uint Size, Hash, Offset;
        public ulong To;
        public FileStream Stream;
    }

    // One file coming in.
    private sealed class Coming
    {
        public string Mod, File, Temp, Final;
        public uint Size, Hash, Got;
        public ulong From;
        public FileStream Stream;
    }

    private static ModShare instance;
    private NetworkManager listened;
    // Files this PC has to give (its own mods' and what it was given), by key; the host's word of who offered what.
    private readonly Dictionary<string, string> sources = new Dictionary<string, string>();
    private readonly Dictionary<string, Offer> offers = new Dictionary<string, Offer>();
    // On the host: who waits for a file it hasn't yet, and which it has asked its owner for.
    private readonly Dictionary<string, List<ulong>> waiting = new Dictionary<string, List<ulong>>();
    private readonly HashSet<string> asked = new HashSet<string>();
    private readonly List<Sending> sending = new List<Sending>();
    private readonly Dictionary<string, Coming> coming = new Dictionary<string, Coming>();
    private readonly Dictionary<(string, long, long), uint> hashes = new Dictionary<(string, long, long), uint>();
    private readonly byte[] buffer = new byte[ChunkBytes];
    private float rescanAt = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        if (instance != null) return;
        var host = new GameObject("Mod Share");
        RuntimeSceneHierarchy.Keep(host);
        instance = host.AddComponent<ModShare>();
    }

    private static bool On => NetSmoothing.Number("modShare", 1f) > .5f;
    private static long MostBytes => (long)(Mathf.Max(1f, NetSmoothing.Number("modShareMostMB", 256f)) * 1024f * 1024f);

    // ---- The wearer's PC offers what its look uses ----

    /// <summary>
    /// This PC's player put this look on (PlayerNetworkPose): every avatar file it draws on (a character or an outfit item
    /// it names), and its bundle, is offered to the room.
    /// </summary>
    internal static void OfferLook(string look)
    {
        var network = GameSession.Network;
        if (instance == null || !On || string.IsNullOrEmpty(look) || network == null || !network.IsListening) return;
        var named = Ids(look);
        foreach (var read in ModAvatars.Files)
        {
            if (read == null || read.File == null || !Uses(read.File, named)) continue;
            string mod = read.Mod != null ? ModOf(read.Mod.Name) : "";
            instance.OfferFile(mod, read.Path, network);
            if (!string.IsNullOrEmpty(read.File.bundle))
                instance.OfferFile(mod, Path.Combine(Path.GetDirectoryName(read.Path) ?? "", read.File.bundle), network);
        }
    }

    // A room copy keeps its mod's own name: what it is offered by again.
    private static string ModOf(string folder) => folder.EndsWith(RoomMark, StringComparison.Ordinal) ? folder.Substring(0, folder.Length - RoomMark.Length) : folder;

    private static HashSet<string> Ids(string look)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var word = new StringBuilder();
        foreach (char c in look + " ")
        {
            if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_') { word.Append(c); continue; }
            if (word.Length > 0) { found.Add(word.ToString()); word.Clear(); }
        }
        return found;
    }

    private static bool Uses(ModAvatarFile file, HashSet<string> named)
    {
        foreach (var character in file.characters ?? Array.Empty<ModAvatarFile.Character>())
            if (character != null && named.Contains(character.id)) return true;
        foreach (var outfit in file.outfits ?? Array.Empty<ModAvatarFile.Outfit>())
        {
            if (outfit == null) continue;
            if (named.Contains(outfit.id)) return true;
            foreach (var item in outfit.items ?? Array.Empty<ModAvatarFile.Item>())
                if (item != null && named.Contains(item.id)) return true;
        }
        return false;
    }

    private void OfferFile(string mod, string path, NetworkManager network)
    {
        string file = Path.GetFileName(path);
        if (!Allowed(mod, file) || !File.Exists(path)) return;
        long length = new FileInfo(path).Length;
        if (length <= 0 || length > MostBytes) return;
        uint hash = HashOf(path);
        string key = Key(mod, file, hash);
        sources[key] = path;
        var offer = new Offer { Mod = mod, File = file, Size = (uint)length, Hash = hash, Owner = network.LocalClientId };
        offers[key] = offer;
        if (network.IsServer) SendToAll(network, OfferMessage, offer, NetworkManager.ServerClientId);
        else SendTo(network, OfferMessage, offer, NetworkManager.ServerClientId);
    }

    // ---- Words ----

    private void Update()
    {
        Listen();
        if (listened == null) return;
        Pump();
        if (rescanAt > 0f && Time.unscaledTime >= rescanAt && coming.Count == 0)
        {
            rescanAt = -1f;
            Rescan();
        }
    }

    private void Listen()
    {
        var network = GameSession.Network;
        var now = network != null && network.IsListening && network.CustomMessagingManager != null ? network : null;
        if (now == listened) return;
        if (listened != null && listened.CustomMessagingManager != null)
        {
            try
            {
                listened.CustomMessagingManager.UnregisterNamedMessageHandler(OfferMessage);
                listened.CustomMessagingManager.UnregisterNamedMessageHandler(WantMessage);
                listened.CustomMessagingManager.UnregisterNamedMessageHandler(ChunkMessage);
                listened.OnClientConnectedCallback -= OnJoined;
            }
            catch (Exception) { }
        }
        Stop();
        listened = now;
        if (now == null) return;
        now.CustomMessagingManager.RegisterNamedMessageHandler(OfferMessage, ReceiveOffer);
        now.CustomMessagingManager.RegisterNamedMessageHandler(WantMessage, ReceiveWant);
        now.CustomMessagingManager.RegisterNamedMessageHandler(ChunkMessage, ReceivePiece);
        if (now.IsServer) now.OnClientConnectedCallback += OnJoined;
    }

    // Out of a room: nothing more goes or comes; a half-come file is thrown away.
    private void Stop()
    {
        foreach (var going in sending) going.Stream?.Dispose();
        sending.Clear();
        foreach (var file in coming.Values) { file.Stream?.Dispose(); TryDelete(file.Temp); }
        coming.Clear();
        offers.Clear();
        waiting.Clear();
        asked.Clear();
    }

    // On the host: one who just came hears every offer made so far.
    private void OnJoined(ulong client)
    {
        if (listened == null || !listened.IsServer || client == NetworkManager.ServerClientId) return;
        foreach (var offer in offers.Values) SendTo(listened, OfferMessage, offer, client);
    }

    private void ReceiveOffer(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Offer offer);
        if (!On || listened == null) return;
        string mod = offer.Mod.ToString(), file = offer.File.ToString();
        if (!Allowed(mod, file) || offer.Size == 0 || offer.Size > MostBytes) return;
        string key = Key(mod, file, offer.Hash);
        if (listened.IsServer)
        {
            // A guest's offer: the room hears of it, and the host asks for it at once if it hasn't it (it gives it on).
            if (offer.Owner != sender) return;
            offers[key] = offer;
            SendToAll(listened, OfferMessage, offer, sender);
            if (!Has(key, mod, file, offer.Size, offer.Hash) && asked.Add(key)) AskFor(offer, sender);
            return;
        }
        if (sender != NetworkManager.ServerClientId) return;
        offers[key] = offer;
        if (!Has(key, mod, file, offer.Size, offer.Hash) && !coming.ContainsKey(key)) AskFor(offer, NetworkManager.ServerClientId);
    }

    // Asks <from> (the host, or on the host the owner) for a file, ready to take it in.
    private void AskFor(in Offer offer, ulong from)
    {
        string mod = offer.Mod.ToString(), file = offer.File.ToString();
        string key = Key(mod, file, offer.Hash);
        if (!coming.ContainsKey(key))
        {
            string folder = Path.Combine(GameMods.Root, mod + RoomMark, GameMods.AvatarsFolder);
            try { Directory.CreateDirectory(folder); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning("[ModShare] " + folder + " 폴더를 만들지 못했습니다: " + error.Message);
                return;
            }
            string final = Path.Combine(folder, file);
            coming[key] = new Coming
            {
                Mod = mod, File = file, Final = final, Temp = final + ".part", Size = offer.Size, Hash = offer.Hash, From = from,
            };
            Debug.Log($"[ModShare] {mod}/{file} ({offer.Size / 1024} KB)을 받습니다.");
        }
        SendTo(listened, WantMessage, new Want { Mod = mod, File = file, Size = offer.Size, Hash = offer.Hash, Asker = listened.LocalClientId }, from);
    }

    private void ReceiveWant(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Want want);
        if (!On || listened == null) return;
        string mod = want.Mod.ToString(), file = want.File.ToString();
        string key = Key(mod, file, want.Hash);
        if (sources.TryGetValue(key, out string path) && File.Exists(path))
        {
            Send(path, mod, file, want.Size, want.Hash, sender);
            return;
        }
        // The host hasn't it yet: the asker waits for it, and its owner is asked once.
        if (!listened.IsServer) return;
        if (!waiting.TryGetValue(key, out var list)) waiting[key] = list = new List<ulong>();
        if (!list.Contains(sender)) list.Add(sender);
        if (offers.TryGetValue(key, out var offer) && offer.Owner != NetworkManager.ServerClientId && asked.Add(key)) AskFor(offer, offer.Owner);
    }

    private void Send(string path, string mod, string file, uint size, uint hash, ulong to)
    {
        foreach (var going in sending) if (going.To == to && going.Path == path) return;
        try
        {
            sending.Add(new Sending { Path = path, Mod = mod, File = file, Size = size, Hash = hash, To = to, Stream = File.OpenRead(path) });
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("[ModShare] " + path + "을 열지 못했습니다: " + error.Message);
        }
    }

    // A few pieces a frame, all files together, so a big bundle never holds up the game's own words.
    private void Pump()
    {
        int left = ChunksPerFrame;
        for (int i = sending.Count - 1; i >= 0 && left > 0; i--)
        {
            var going = sending[i];
            bool done = false;
            try
            {
                for (; left > 0 && !done; left--)
                {
                    int read = going.Stream.Read(buffer, 0, ChunkBytes);
                    if (read <= 0) { done = true; break; }
                    var piece = new Piece { Mod = going.Mod, File = going.File, Size = going.Size, Hash = going.Hash, Offset = going.Offset, Length = (ushort)read };
                    using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Piece>() + read + 8, Allocator.Temp);
                    writer.WriteValueSafe(piece);
                    writer.WriteBytesSafe(buffer, read);
                    listened.CustomMessagingManager.SendNamedMessage(ChunkMessage, going.To, writer, NetworkDelivery.ReliableFragmentedSequenced);
                    going.Offset += (uint)read;
                    if (going.Offset >= going.Size) done = true;
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[ModShare] " + going.File + "을 보내다 멈췄습니다: " + error.Message);
                done = true;
            }
            if (!done) continue;
            going.Stream.Dispose();
            sending.RemoveAt(i);
        }
    }

    private void ReceivePiece(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Piece piece);
        string key = Key(piece.Mod.ToString(), piece.File.ToString(), piece.Hash);
        // Only what this PC asked for, from whom it asked, in order.
        if (!coming.TryGetValue(key, out var file) || file.From != sender || piece.Offset != file.Got || piece.Length > ChunkBytes) return;
        var bytes = buffer;
        if (piece.Length > 0) reader.ReadBytesSafe(ref bytes, piece.Length);
        try
        {
            file.Stream ??= new FileStream(file.Temp, FileMode.Create, FileAccess.Write);
            file.Stream.Write(buffer, 0, piece.Length);
            file.Got += piece.Length;
            rescanAt = Time.unscaledTime + RescanAfter;
            if (file.Got < file.Size) return;
            file.Stream.Dispose();
            file.Stream = null;
            coming.Remove(key);
            if (HashOf(file.Temp, false) != file.Hash)
            {
                Debug.LogWarning("[ModShare] " + file.File + "이 깨져서 왔습니다. 버립니다.");
                TryDelete(file.Temp);
                return;
            }
            TryDelete(file.Final);
            File.Move(file.Temp, file.Final);
            sources[key] = file.Final;
            Debug.Log("[ModShare] " + file.Mod + "/" + file.File + "을 받았습니다.");
            // On the host: those waiting for it get it now.
            if (waiting.TryGetValue(key, out var list))
            {
                waiting.Remove(key);
                foreach (ulong client in list) Send(file.Final, file.Mod, file.File, file.Size, file.Hash, client);
            }
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("[ModShare] " + file.File + "을 쓰지 못했습니다: " + error.Message);
            file.Stream?.Dispose();
            coming.Remove(key);
            TryDelete(file.Temp);
        }
    }

    // The mods read again, and every friend's copy dressed again in what they wear.
    private static void Rescan()
    {
        GameMods.Rescan();
        foreach (var player in PlayerRoster.All)
            if (player != null && !player.IsLocal && player.TryGetComponent(out PlayerNetworkPose pose)) pose.ShowSkinAgain();
    }

    // ---- What this PC has ----

    private bool Has(string key, string mod, string file, uint size, uint hash)
    {
        if (sources.TryGetValue(key, out string known) && File.Exists(known)) return true;
        foreach (string folder in new[] { mod, mod + RoomMark })
        {
            string path = Path.Combine(GameMods.Root, folder, GameMods.AvatarsFolder, file);
            if (!File.Exists(path) || new FileInfo(path).Length != size || HashOf(path) != hash) continue;
            sources[key] = path;
            return true;
        }
        return false;
    }

    private uint HashOf(string path, bool keep = true)
    {
        var info = new FileInfo(path);
        var stamp = (path, info.Length, info.LastWriteTimeUtc.Ticks);
        if (keep && hashes.TryGetValue(stamp, out uint known)) return known;
        uint hash = 2166136261u;
        using (var stream = File.OpenRead(path))
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                for (int i = 0; i < read; i++) { hash ^= buffer[i]; hash *= 16777619u; }
        }
        if (keep) hashes[stamp] = hash;
        return hash;
    }

    private static string Key(string mod, string file, uint hash) => mod + "/" + file + "#" + hash;

    // A mod's name and a file's are taken bare: no folders, no dots in front, only an avatar file or a bundle.
    private static bool Allowed(string mod, string file)
    {
        if (string.IsNullOrEmpty(mod) || string.IsNullOrEmpty(file) || mod.Length > 60 || file.Length > 120) return false;
        if (mod != Path.GetFileName(mod) || file != Path.GetFileName(file) || mod[0] == '.' || mod[0] == '_' || file[0] == '.') return false;
        if (mod.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return file.EndsWith(ModAvatarFile.Extension, StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(BundleExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
    }

    private static void SendTo<T>(NetworkManager network, string name, in T value, ulong to) where T : unmanaged, INetworkSerializeByMemcpy
    {
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<T>(), Allocator.Temp);
        writer.WriteValueSafe(value);
        network.CustomMessagingManager.SendNamedMessage(name, to, writer, NetworkDelivery.ReliableSequenced);
    }

    // On the host: to every guest but one.
    private static void SendToAll<T>(NetworkManager network, string name, in T value, ulong except) where T : unmanaged, INetworkSerializeByMemcpy
    {
        foreach (ulong client in network.ConnectedClientsIds)
            if (client != NetworkManager.ServerClientId && client != except) SendTo(network, name, value, client);
    }

    private void OnDestroy() => Stop();
}
