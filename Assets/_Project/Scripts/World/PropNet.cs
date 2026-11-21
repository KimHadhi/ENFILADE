using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Loose physics props the same on every PC in a room: carried, thrown, kicked, shot. The game's rule for every hit is that
/// the PC that acts decides (the user, 2026-11-14), and a prop's motion follows it: the PC whose own player last touched
/// a prop (picked it up, threw it, kicked, shoved or shot it, walked it along: PhysicsProp) runs its physics and tells
/// everyone where it is, about 30 times a second while it moves and once more when it comes to rest; every other PC's copy
/// follows that word, moved by hand, and lies still again where it came to rest. Its strikes on zombies are judged on that
/// PC, by its player (PropStrike). Two players touching one prop at once: the newer claim wins (each touch counts one up),
/// and on a tie the lower client id. A carried prop hangs from its carrier's hand on the others' screens (CarryHold).
/// Each prop's name between PCs is its place in its scene (the same scene on every PC), or, for one made at run time
/// (/prop), the number its maker gave it and sent with it. The host passes guests' words on to the others. Alone, none of
/// this runs. Not here: the campaign's fuel cans, whose every move the host runs (CampaignCans, PhysicsProp.HostJudged).
/// Not yet: a player who joins later does not hear of props made before (/prop) or moved before.
/// </summary>
[DisallowMultipleComponent]
public sealed class PropNet : MonoBehaviour
{
    public const string PoseMessage = "ENFILADE.PropPose", MadeMessage = "ENFILADE.PropMade", GoneMessage = "ENFILADE.PropGone";
    private enum Phase : byte { Moving, Held, Rest }
    // How often the PC running a moving prop tells the others where it is (s), a carried one (s: the others hang it from
    // the carrier's hand, so this is only a reminder), how long it lies still before it says it rests (s), and how still
    // is still (m/s, rad/s).
    private const float MovingEvery = 1f / 30f, HeldEvery = .5f, QuietSeconds = .4f, StillSpeed = .05f, StillSpin = .1f;
    // A follower closes this share of the gap to the word every 1/60 s, and jumps across anything farther (m).
    private const float FollowRate = .45f, FollowJump = 3f;

    private struct Pose : INetworkSerializeByMemcpy
    {
        public uint Id;
        public ushort Epoch;
        public byte Phase;
        // The client whose PC runs it, and its carrier's client id + 1 (0: nobody carries it).
        public ulong Driver, Holder;
        public Vector3 Position, Velocity, Spin;
        public Quaternion Rotation;
    }

    private struct Made : INetworkSerializeByMemcpy
    {
        public uint Id;
        public ushort Item;
        public ulong Maker;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private struct Gone : INetworkSerializeByMemcpy
    {
        public uint Id;
        public ulong Maker;
    }

    private static PropNet instance;
    private static readonly Dictionary<uint, PhysicsProp> byId = new Dictionary<uint, PhysicsProp>();
    private static readonly List<PhysicsProp> all = new List<PhysicsProp>();
    private static readonly List<ulong> others = new List<ulong>();
    // What /prop made, on this PC and the others, by the name its maker gave it (a fixed item has no PhysicsProp to ask).
    private static readonly Dictionary<uint, GameObject> made = new Dictionary<uint, GameObject>();
    private static uint madeCount;
    private CustomMessagingManager listened;

    /// <summary>In a room: props are shared and only their driving PC moves them.</summary>
    public static bool Shared
    {
        get
        {
            var network = GameSession.Network;
            return network != null && network.IsListening;
        }
    }

    private static ulong LocalClient
    {
        get
        {
            var network = GameSession.Network;
            return network != null && network.IsListening ? network.LocalClientId : 0UL;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        instance = null;
        byId.Clear();
        all.Clear();
        made.Clear();
        madeCount = 0;
    }

    // The props' keeper and this PC's player's hands on them, for the whole run from the first scene on.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        if (instance != null) return;
        var host = new GameObject("Props");
        RuntimeSceneHierarchy.Keep(host);
        instance = host.AddComponent<PropNet>();
        host.AddComponent<PropCarry>();
    }

    // ---- Every prop's name ----

    /// <summary>A prop came to be (PhysicsProp.OnEnable): known by its id, its place in its scene unless it has one.</summary>
    internal static void Register(PhysicsProp prop)
    {
        if (prop == null || prop.HostJudged) return;
        if (prop.NetId == 0) prop.NetId = PlaceId(prop.transform);
        byId[prop.NetId] = prop;
        if (!all.Contains(prop)) all.Add(prop);
    }

    internal static void Forget(PhysicsProp prop)
    {
        if (prop == null) return;
        if (byId.TryGetValue(prop.NetId, out var known) && known == prop) byId.Remove(prop.NetId);
        all.Remove(prop);
    }

    // Its place in its scene, the same on every PC: the scene and each step down to it (name and sibling number), hashed
    // (FNV-1a). The top bit marks a place id, so it never meets a made one.
    private static uint PlaceId(Transform at)
    {
        var path = new StringBuilder(at.gameObject.scene.name);
        var steps = new List<Transform>();
        for (var step = at; step != null; step = step.parent) steps.Add(step);
        for (int i = steps.Count - 1; i >= 0; i--) path.Append('/').Append(steps[i].name).Append('#').Append(steps[i].GetSiblingIndex());
        uint hash = 2166136261;
        foreach (char c in path.ToString()) { hash ^= c; hash *= 16777619; }
        return hash | 0x80000000u;
    }

    private static void Rename(PhysicsProp prop, uint id)
    {
        Forget(prop);
        prop.NetId = id;
        byId[id] = prop;
        all.Add(prop);
    }

    // ---- This PC's own player acts on a prop ----

    /// <summary>
    /// This PC's own player touched it (PhysicsProp: a push, a kick, a shot, a lean): this PC runs its motion from now on,
    /// a newer claim than any other PC's.
    /// </summary>
    internal static void Drive(PhysicsProp prop)
    {
        if (prop == null || prop.HostJudged) return;
        prop.QuietFor = 0f;
        if (prop.Driving) return;
        prop.Epoch++;
        prop.Driving = true;
        prop.Following = false;
        prop.ApplyMode();
        prop.SentAt = -99f;
    }

    /// <summary>This PC's player picks it up (PropCarry): theirs to carry, and this PC tells the others at once.</summary>
    internal static void Hold(PhysicsProp prop, PlayerMovement carrier)
    {
        Drive(prop);
        prop.SetHolder(carrier);
        Send(prop, Phase.Held);
    }

    /// <summary>
    /// This PC's player lets go of it (PropCarry: put down, or thrown): loose at <paramref name="at"/>, moving at
    /// <paramref name="velocity"/> and turning at <paramref name="spin"/>, its motion still this PC's until it rests.
    /// </summary>
    internal static void Release(PhysicsProp prop, Vector3 at, Quaternion turn, Vector3 velocity, Vector3 spin)
    {
        if (prop == null) return;
        Drive(prop);
        prop.SetHolder(null);
        prop.Teleport(at, turn, velocity, spin);
        Send(prop, Phase.Moving);
    }

    /// <summary>
    /// Makes shelf item <paramref name="item"/> (PropShelf) here and on every PC of the room (/prop), with a name this PC
    /// gives it. Returns it, or null.
    /// </summary>
    internal static GameObject Make(int item, Vector3 at, Quaternion turn)
    {
        uint id = ((uint)(LocalClient & 0x7FF) << 20) | (++madeCount & 0xFFFFF);
        var thing = Spawn(item, id, at, turn);
        if (thing == null) return null;
        DriveMade(thing);
        var message = new Made { Id = id, Item = (ushort)item, Maker = LocalClient, Position = at, Rotation = turn };
        SendValue(MadeMessage, message, NetworkDelivery.ReliableSequenced, LocalClient);
        return thing;
    }

    /// <summary>Takes away a thing /prop made on this PC, here and on every PC of the room.</summary>
    internal static void Unmake(GameObject thing)
    {
        if (thing == null) return;
        foreach (var pair in made)
        {
            if (pair.Value != thing) continue;
            SendValue(GoneMessage, new Gone { Id = pair.Key, Maker = LocalClient }, NetworkDelivery.ReliableSequenced, LocalClient);
            made.Remove(pair.Key);
            break;
        }
        Destroy(thing);
    }

    private static GameObject Spawn(int item, uint id, Vector3 at, Quaternion turn)
    {
        var shelf = PropShelf.Get();
        if (shelf == null || item < 0 || item >= shelf.Items.Count || shelf.Items[item] == null) return null;
        CollisionEffects.Ensure();
        var prefab = shelf.Items[item];
        var thing = Instantiate(prefab, at, turn);
        thing.name = prefab.name;
        RuntimeSceneHierarchy.Group(thing, RuntimeSceneHierarchy.World, "Props");
        made[id] = thing;
        if (thing.TryGetComponent(out Rigidbody _))
        {
            if (!thing.TryGetComponent(out PhysicsProp prop)) prop = thing.AddComponent<PhysicsProp>();
            Rename(prop, id);
        }
        return thing;
    }

    // Its maker's PC runs its first fall, so it lands in one place on every PC.
    private static void DriveMade(GameObject thing)
    {
        if (thing != null && thing.TryGetComponent(out PhysicsProp prop)) Drive(prop);
    }

    // ---- Each frame ----

    private void Update()
    {
        Listen();
        if (!Shared) return;
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var prop = all[i];
            if (prop == null) { all.RemoveAt(i); continue; }
            if (!prop.Driving) continue;
            if (prop.Holder != null)
            {
                if (now - prop.SentAt >= HeldEvery) Send(prop, Phase.Held);
                continue;
            }
            var body = prop.Body;
            bool still = body.isKinematic || body.IsSleeping()
                || body.linearVelocity.sqrMagnitude < StillSpeed * StillSpeed && body.angularVelocity.sqrMagnitude < StillSpin * StillSpin;
            prop.QuietFor = still ? prop.QuietFor + dt : 0f;
            if (prop.QuietFor >= QuietSeconds)
            {
                Send(prop, Phase.Rest);
                prop.Driving = false;
            }
            else if (now - prop.SentAt >= MovingEvery) Send(prop, Phase.Moving);
        }
    }

    // Copies that follow another PC's word: carried ones hang from their carrier's hand, moving ones ease toward the word.
    private void LateUpdate()
    {
        if (!Shared) return;
        var mine = PlayerRoster.Local;
        float share = 1f - Mathf.Pow(1f - FollowRate, Time.deltaTime * 60f);
        foreach (var prop in all)
        {
            if (prop == null || !prop.Following) continue;
            if (prop.Holder != null) { CarryHold.Place(prop.transform, prop.Holder, mine, null, prop.HalfSize); continue; }
            var body = prop.transform;
            if ((body.position - prop.FollowPosition).sqrMagnitude > FollowJump * FollowJump)
                body.SetPositionAndRotation(prop.FollowPosition, prop.FollowRotation);
            else
                body.SetPositionAndRotation(Vector3.Lerp(body.position, prop.FollowPosition, share),
                    Quaternion.Slerp(body.rotation, prop.FollowRotation, share));
        }
    }

    // ---- Words between PCs ----

    private static void Send(PhysicsProp prop, Phase phase)
    {
        prop.SentAt = Time.unscaledTime;
        if (!Shared || prop.NetId == 0) return;
        var body = prop.Body;
        bool moving = !body.isKinematic;
        var pose = new Pose
        {
            Id = prop.NetId, Epoch = prop.Epoch, Phase = (byte)phase, Driver = LocalClient,
            Holder = prop.Holder != null ? PlayerRoster.IdOf(prop.Holder) + 1UL : 0UL,
            Position = prop.transform.position, Rotation = prop.transform.rotation,
            Velocity = moving ? body.linearVelocity : Vector3.zero, Spin = moving ? body.angularVelocity : Vector3.zero
        };
        SendValue(PoseMessage, pose, phase == Phase.Moving ? NetworkDelivery.UnreliableSequenced : NetworkDelivery.ReliableSequenced, LocalClient);
    }

    // To the host, or from the host to every guest but the one it came from.
    private static void SendValue<T>(string name, T value, NetworkDelivery delivery, ulong from) where T : unmanaged, INetworkSerializeByMemcpy
    {
        var network = GameSession.Network;
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<T>(), Allocator.Temp);
        writer.WriteValueSafe(value);
        if (!network.IsServer)
        {
            network.CustomMessagingManager.SendNamedMessage(name, NetworkManager.ServerClientId, writer, delivery);
            return;
        }
        others.Clear();
        foreach (ulong client in network.ConnectedClientsIds)
            if (client != NetworkManager.ServerClientId && client != from) others.Add(client);
        if (others.Count > 0) network.CustomMessagingManager.SendNamedMessage(name, others, writer, delivery);
    }

    private void Listen()
    {
        var network = GameSession.Network;
        var messages = network != null && network.IsListening ? network.CustomMessagingManager : null;
        if (messages == listened) return;
        if (listened != null)
        {
            try
            {
                listened.UnregisterNamedMessageHandler(PoseMessage);
                listened.UnregisterNamedMessageHandler(MadeMessage);
                listened.UnregisterNamedMessageHandler(GoneMessage);
            }
            catch (System.Exception) { }
        }
        listened = messages;
        if (messages == null) return;
        messages.RegisterNamedMessageHandler(PoseMessage, ReceivePose);
        messages.RegisterNamedMessageHandler(MadeMessage, ReceiveMade);
        messages.RegisterNamedMessageHandler(GoneMessage, ReceiveGone);
    }

    private static void ReceivePose(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Pose pose);
        if (pose.Driver == LocalClient) return;
        Relay(PoseMessage, pose, (Phase)pose.Phase == Phase.Moving ? NetworkDelivery.UnreliableSequenced : NetworkDelivery.ReliableSequenced, sender);
        if (!byId.TryGetValue(pose.Id, out var prop) || prop == null) return;
        // The newer claim wins; on a tie, the lower client id.
        short ahead = (short)(pose.Epoch - prop.Epoch);
        if (ahead < 0 || ahead == 0 && prop.Driving && pose.Driver > LocalClient) return;
        prop.Epoch = pose.Epoch;
        prop.Driving = false;
        var phase = (Phase)pose.Phase;
        if (phase == Phase.Rest)
        {
            prop.Following = false;
            prop.SetHolder(null);
            prop.Teleport(pose.Position, pose.Rotation, Vector3.zero, Vector3.zero);
            return;
        }
        prop.Following = true;
        prop.SetHolder(phase == Phase.Held && pose.Holder > 0 ? PlayerRoster.ById(pose.Holder - 1) : null);
        prop.FollowPosition = pose.Position;
        prop.FollowRotation = pose.Rotation;
    }

    private static void ReceiveMade(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Made word);
        if (word.Maker == LocalClient) return;
        Relay(MadeMessage, word, NetworkDelivery.ReliableSequenced, sender);
        if (!made.ContainsKey(word.Id)) Spawn(word.Item, word.Id, word.Position, word.Rotation);
    }

    private static void ReceiveGone(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Gone gone);
        if (gone.Maker == LocalClient) return;
        Relay(GoneMessage, gone, NetworkDelivery.ReliableSequenced, sender);
        if (made.TryGetValue(gone.Id, out var thing) && thing != null) Destroy(thing);
        made.Remove(gone.Id);
    }

    // On the host: a guest's word goes on to the other guests.
    private static void Relay<T>(string name, T value, NetworkDelivery delivery, ulong sender) where T : unmanaged, INetworkSerializeByMemcpy
    {
        var network = GameSession.Network;
        if (network != null && network.IsServer) SendValue(name, value, delivery, sender);
    }
}
