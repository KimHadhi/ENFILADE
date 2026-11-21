using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// dev-text: file (log lines; nothing here is shown on screen)

/// <summary>
/// In a room the game's numbers are the host's (the user, 2026-11-21: "게임 수치를 바꾸는 모드는 호스트 기준으로"): the
/// tables a mod changes (its Tables/*.csv, GameMods) are the host's mods' on every PC, and a guest's own are left out while
/// it is in someone else's room. The moment a guest starts joining, its own mods' tables are taken away (none in the room
/// but what the host sends); the host sends its mods' tables as Netcode starts bringing the guest in (its scene
/// synchronisation, before the guest has loaded anything), in pieces of 4 KB, and the guest lays them where its own were
/// (GameMods.TableTexts) and reads the tables again (GameTables.Reload). Leaving, its own come back. If the host reads
/// its mods again mid-room (/mods reload) and its tables changed, every guest is sent them again. What a mod's avatars and
/// outfits lay by themselves (their sway, face and parts rows) stays each PC's own: they describe files this PC has.
/// A guest is told before it joins that the room is a modded one and asked (GameSession, the server browser).
/// </summary>
[DisallowMultipleComponent]
public sealed class RoomTables : MonoBehaviour
{
    private const string TablesMessage = "ENFILADE.RoomTables";
    private const int PieceBytes = 4096;
    // The most a room's tables may come to (bytes): more is not sent, and the guest plays on the game's own numbers.
    private const int MostBytes = 4 * 1024 * 1024;

    private struct Head : INetworkSerializeByMemcpy
    {
        public uint Sending, Total, Offset;
        public ushort Length;
    }

    private static RoomTables instance;
    private NetworkManager listened;
    private NetworkSceneManager scenes;
    // What the host sent last (its bytes' hash, so a reload that changed nothing sends nothing), and its send number.
    private uint sentHash, sending;
    // A guest's pieces as they come.
    private byte[] coming;
    private uint comingSending, got;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        if (instance != null) return;
        var host = new GameObject("Room Tables");
        RuntimeSceneHierarchy.Keep(host);
        instance = host.AddComponent<RoomTables>();
    }

    private void OnEnable() => GameTables.Reloaded += OnReloaded;

    private void OnDisable() => GameTables.Reloaded -= OnReloaded;

    private void Update()
    {
        var network = GameSession.Network;
        var now = network != null && network.IsListening && network.CustomMessagingManager != null ? network : null;
        if (now != listened) Listen(now);
        // The scene manager comes with the room: the host hears each newcomer's start from it.
        if (listened != null && listened.IsServer && listened.SceneManager != scenes)
        {
            if (scenes != null) scenes.OnSynchronize -= OnSynchronize;
            scenes = listened.SceneManager;
            if (scenes != null) scenes.OnSynchronize += OnSynchronize;
        }
    }

    private void Listen(NetworkManager now)
    {
        if (listened != null)
        {
            try { listened.CustomMessagingManager?.UnregisterNamedMessageHandler(TablesMessage); }
            catch (Exception) { }
            if (scenes != null) scenes.OnSynchronize -= OnSynchronize;
            scenes = null;
        }
        listened = now;
        coming = null;
        // In someone else's room: this PC's own mods' tables are out until the host's come. Out of it: back.
        bool guest = now != null && !now.IsServer;
        if (GameMods.InRoom != guest)
        {
            GameMods.UseRoomTables(guest ? new List<GameMods.RoomTable>() : null);
            GameTables.Reload();
        }
        if (now == null) return;
        sentHash = 0u;
        if (!now.IsServer) now.CustomMessagingManager.RegisterNamedMessageHandler(TablesMessage, Receive);
    }

    // ---- The host ----

    private void OnSynchronize(ulong client)
    {
        if (listened == null || !listened.IsServer || client == NetworkManager.ServerClientId) return;
        Send(Pack(), client);
    }

    // The host read its tables again: if its mods' tables are not what it sent, every guest has them again.
    private void OnReloaded()
    {
        if (listened == null || !listened.IsServer) return;
        var bytes = Pack();
        if (Hash(bytes) == sentHash) return;
        foreach (ulong client in listened.ConnectedClientsIds)
            if (client != NetworkManager.ServerClientId) Send(bytes, client);
    }

    // The host's mods' tables: how many, then each one's mod, table and text.
    private static byte[] Pack()
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, Encoding.UTF8, true))
        {
            var tables = GameMods.OwnTables();
            writer.Write(tables.Count);
            foreach (var table in tables)
            {
                writer.Write(table.Mod ?? "");
                writer.Write(table.Table ?? "");
                writer.Write(table.Text ?? "");
            }
        }
        return memory.ToArray();
    }

    private void Send(byte[] bytes, ulong client)
    {
        if (bytes.Length > MostBytes)
        {
            Debug.LogWarning($"[RoomTables] 모드 표가 너무 큽니다({bytes.Length / 1024} KB): 보내지 않습니다.");
            return;
        }
        sentHash = Hash(bytes);
        sending++;
        int offset = 0;
        do
        {
            int length = Mathf.Min(PieceBytes, bytes.Length - offset);
            var head = new Head { Sending = sending, Total = (uint)bytes.Length, Offset = (uint)offset, Length = (ushort)length };
            using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Head>() + length + 8, Allocator.Temp);
            writer.WriteValueSafe(head);
            if (length > 0) writer.WriteBytesSafe(bytes, length, offset);
            listened.CustomMessagingManager.SendNamedMessage(TablesMessage, client, writer, NetworkDelivery.ReliableFragmentedSequenced);
            offset += length;
        }
        while (offset < bytes.Length);
    }

    // ---- A guest ----

    private void Receive(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out Head head);
        if (head.Total > MostBytes || head.Length > PieceBytes || head.Offset + head.Length > head.Total) return;
        if (head.Offset == 0)
        {
            coming = new byte[head.Total];
            comingSending = head.Sending;
            got = 0;
        }
        if (coming == null || head.Sending != comingSending || head.Offset != got) return;
        if (head.Length > 0) reader.ReadBytesSafe(ref coming, head.Length, (int)head.Offset);
        got += head.Length;
        if (got < head.Total) return;
        var tables = Unpack(coming);
        coming = null;
        if (tables == null) return;
        GameMods.UseRoomTables(tables);
        GameTables.Reload();
        if (tables.Count > 0) Debug.Log($"[RoomTables] 방장의 모드 표 {tables.Count}개를 씁니다.");
    }

    private static List<GameMods.RoomTable> Unpack(byte[] bytes)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            int count = reader.ReadInt32();
            if (count < 0 || count > 4096) return null;
            var tables = new List<GameMods.RoomTable>(count);
            for (int i = 0; i < count; i++)
                tables.Add(new GameMods.RoomTable { Mod = reader.ReadString(), Table = reader.ReadString(), Text = reader.ReadString() });
            return tables;
        }
        catch (Exception error) when (error is IOException || error is ArgumentException)
        {
            Debug.LogWarning("[RoomTables] 방장의 모드 표를 읽지 못했습니다: " + error.Message);
            return null;
        }
    }

    private static uint Hash(byte[] bytes)
    {
        uint hash = 2166136261u;
        foreach (byte b in bytes) { hash ^= b; hash *= 16777619u; }
        return hash == 0u ? 1u : hash;
    }
}
