using System.Globalization;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Backlog 721: whether players' attacks hurt each other. It is the room's rule: the host sets it with the `ff` console
/// command, every PC hears it, and anyone who joins later hears the rule as it stands. Each room starts as Network.csv
/// says (friendlyFire, off). With it on, a blow this PC's player lands on a teammate's copy goes to that teammate's own PC
/// (PlayerDamageReceiver, PlayerNetworkCombat.FriendlyHit), which takes it like any other: dodge, parry, shield, going
/// down and all, at friendlyFireShare of the damage. Alone there is nobody to hit, and the command only flips the flag.
/// </summary>
public static class FriendlyFire
{
    private const string Message = "ENFILADE.FriendlyFire", Table = "Network", Column = "value";
    private static CustomMessagingManager listening;
    private static bool? rule;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        listening = null;
        rule = null;
    }

    /// <summary>Players' attacks hurt each other.</summary>
    public static bool Enabled => rule ?? Number("friendlyFire", 0f) > .5f;
    /// <summary>The share of a blow's damage a teammate takes.</summary>
    public static float Share => Mathf.Max(0f, Number("friendlyFireShare", 1f));
    private static string State => Enabled ? "켜짐" : "꺼짐";

    /// <summary>Hooks this PC up to the room's rule, once a room. Each room starts from the table's rule.</summary>
    internal static void Listen()
    {
        var network = GameSession.Network;
        var messages = network != null ? network.CustomMessagingManager : null;
        if (messages == null || messages == listening) return;
        listening = messages;
        rule = null;
        messages.RegisterNamedMessageHandler(Message, Receive);
        if (network.IsServer && network.SceneManager != null) network.SceneManager.OnSynchronizeComplete += SendTo;
    }

    /// <summary>The `ff` command: on, off, or nothing to flip it. Returns what the console prints.</summary>
    internal static string Command(string args)
    {
        bool want;
        switch ((args ?? "").Trim().ToLowerInvariant())
        {
            case "": want = !Enabled; break;
            case "on": case "1": case "켜기": want = true; break;
            case "off": case "0": case "끄기": want = false; break;
            default: return "사용법: /ff 1 (켜기) · /ff 0 (끄기) · /ff (바꾸기). 지금: " + State;
        }
        if (GameSession.IsGuest) return "아군 사격은 방장만 바꿀 수 있어요. 지금: " + State;
        // Before the rule is set: the room's first Listen starts it from the table.
        Listen();
        rule = want;
        var network = GameSession.Network;
        if (network == null || !network.IsListening) return "아군 사격: " + State;
        using var writer = Write(want);
        network.CustomMessagingManager.SendNamedMessageToAll(Message, writer);
        return "아군 사격: " + State + " (방 전체)";
    }

    /// <summary>
    /// A hit landed on a teammate's copy on this PC. With the rule on, a blow from this PC's own player goes on to them;
    /// anything else (an enemy's swing struck on their own PC, a fall) is theirs to judge already.
    /// </summary>
    internal static void PassOn(PlayerMovement victim, in DamageHit hit)
    {
        if (!Enabled || hit.Kind != DamageKind.Attack || hit.Amount <= 0f || hit.Source == null) return;
        var mine = PlayerRoster.Local;
        if (mine == null || mine == victim || mine.IsDead || victim.IsDead || hit.Source.GetComponentInParent<PlayerMovement>() != mine) return;
        if (victim.TryGetComponent(out PlayerNetworkCombat network)) network.FriendlyHit(hit, Share, mine.transform.position);
    }

    /// <summary>On the host, someone's player just arrived: the rule as it stands, sent after their player so their PC is
    /// listening by then (it starts listening as players spawn there).</summary>
    internal static void Greet(ulong client)
    {
        var network = GameSession.Network;
        if (network != null && network.IsServer && client != NetworkManager.ServerClientId) SendTo(client);
    }

    // Someone joined: the rule as it stands. Sent twice to a newcomer (here and Greet); only a change is announced.
    private static void SendTo(ulong client)
    {
        var network = GameSession.Network;
        if (network == null) return;
        using var writer = Write(Enabled);
        network.CustomMessagingManager.SendNamedMessage(Message, client, writer);
    }

    // Only the host's word counts; the host set it itself.
    private static void Receive(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out bool on);
        var network = GameSession.Network;
        if (network == null || network.IsServer || sender != NetworkManager.ServerClientId) return;
        bool changed = on != Enabled;
        rule = on;
        if (!changed) return;
        var chat = Object.FindFirstObjectByType<SinglePlayerChatManager>();
        if (chat != null) chat.AddBroadcast("방장이 아군 사격을 " + (on ? "켰습니다" : "껐습니다"));
    }

    private static FastBufferWriter Write(bool on)
    {
        var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<bool>(), Allocator.Temp);
        writer.WriteValueSafe(on);
        return writer;
    }

    private static float Number(string row, float fallback)
        => GameTables.TryGetRow(Table, row, out var values) && values.TryGetValue(Column, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
}
