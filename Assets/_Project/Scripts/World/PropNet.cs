using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loose physics props the same on every PC in a room (the user, 2026-11-21: "what I see is what others see. I want
/// everything synchronizes with everyone"): where they lie, who carries them, how they fly, tumble, roll and clatter.
/// Built on the shipped way of sharing physics (Glenn Fiedler's state synchronization, with the authority rules of his
/// "Networked Physics in VR"):
/// - One PC has the word on each prop's motion at a time. By default the host, which runs the world (a zombie walking into
///   a crate, a blast); the moment a player touches a prop (picks it up, throws, kicks, shoves or shoots it, walks it along:
///   PhysicsProp) their own PC takes the word, as every hit here is the acting PC's (the user, 2026-11-14), until the prop
///   comes to rest. Claims are counted (Epoch): a newer one wins; on a tie the lower client id, so the host.
/// - The PC with the word sends the prop's place, turn, speed and spin: at once when it is thrown, pushed or hits
///   something, else ten times a second while it moves, once more when it rests, and now and then while it is carried.
///   Packed small: a turn in 32 bits (smallest three), speeds as half floats, 42 bytes a word.
/// - Every other PC's copy keeps simulating itself between words (its own physics is the prediction) and is pulled to each
///   word: half the gap at once, all of it when it is far. So it hits the same floor at the same moment, and Impact CFX
///   plays its knocks, scrapes and rolls on every PC from its own collisions (CollisionEffects), and a carried one hangs
///   from its carrier's hand (CarryHold).
/// - On a guest's PC a prop nobody has the word on lies frozen: only a word moves it, so it never drifts on one screen
///   alone (a zombie copy brushing it). Its own player's touch still takes it at once.
/// - A player who joins, or loads the next scene, asks the host for every prop as it is (Hello) and for what /prop made.
/// Strikes on zombies are judged where the word is, by its player (PropStrike). Names between PCs: a prop's place in its
/// scene, a run's own name (CampaignCans: each fuel can), or for one made at run time (/prop) the number its maker gave it.
/// The host passes guests' words on to the others. Alone, none of this runs and every prop is simply physics.
/// </summary>
[DisallowMultipleComponent]
public sealed class PropNet : MonoBehaviour
{
    public const string PoseMessage = "ENFILADE.PropPose", MadeMessage = "ENFILADE.PropMade", GoneMessage = "ENFILADE.PropGone",
        HelloMessage = "ENFILADE.PropHello";
    private enum Phase : byte { Moving, Held, Rest }
    // While a prop moves its word goes out at least this often (s) and at once after a jolt; a carried one's this often
    // (s: the others hang it from the carrier's hand, so this only reminds); still this long, it rests (s); how still is
    // still (m/s, rad/s).
    private const float MovingEvery = .1f, HeldEvery = 1f, QuietSeconds = .4f, StillSpeed = .05f, StillSpin = .1f;
    // A jolt (a push, a hit) sends at once, but no more often than this (s): a body leaning on a prop pushes it every frame.
    private const float JoltEvery = 1f / 30f;
    // The host takes the word on a prop that starts moving of itself, once it is this fast (m/s).
    private const float StirSpeed = .3f;
    // A copy further than this from the word jumps to it (m); nearer, it closes half the gap at each word.
    private const float SnapDistance = .5f;

    private struct Pose : INetworkSerializeByMemcpy
    {
        public uint Id;
        public uint Turn;
        public Vector3 Position;
        public ushort Epoch, Driver, Holder;
        public ushort VX, VY, VZ, WX, WY, WZ;
        public byte Phase;
    }

    private struct Made : INetworkSerializeByMemcpy
    {
        public uint Id;
        public ushort Item, Maker;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private struct Gone : INetworkSerializeByMemcpy
    {
        public uint Id;
        public ushort Maker;
    }

    private struct Hello : INetworkSerializeByMemcpy
    {
        public ushort Asker;
    }

    // What /prop made, on this PC and the others: the thing, which shelf item, and whose.
    private struct MadeThing
    {
        public GameObject Thing;
        public ushort Item, Maker;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private static PropNet instance;
    private static readonly Dictionary<uint, PhysicsProp> byId = new Dictionary<uint, PhysicsProp>();
    private static readonly List<PhysicsProp> all = new List<PhysicsProp>();
    private static readonly List<ulong> others = new List<ulong>();
    private static readonly Dictionary<uint, MadeThing> made = new Dictionary<uint, MadeThing>();
    private static readonly List<uint> gone = new List<uint>();
    private static uint madeCount;
    private CustomMessagingManager listened;
    private bool greeted;

    /// <summary>In a room: props are shared, and each moves where its word is.</summary>
    public static bool Shared
    {
        get
        {
            var network = GameSession.Network;
            return network != null && network.IsListening;
        }
    }

    private static bool IsHost
    {
        get
        {
            var network = GameSession.Network;
            return network == null || !network.IsListening || network.IsServer;
        }
    }

    private static ushort LocalClient
    {
        get
        {
            var network = GameSession.Network;
            return network != null && network.IsListening ? (ushort)network.LocalClientId : (ushort)0;
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

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    // A new scene's props are asked for again once this PC's player stands in it.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => greeted = false;

    // ---- Every prop's name ----

    /// <summary>A prop came to be (PhysicsProp.OnEnable): known by its id, its place in its scene unless it has one.</summary>
    internal static void Register(PhysicsProp prop)
    {
        if (prop == null) return;
        if (prop.NetId == 0) prop.NetId = PlaceId(prop.transform);
        byId[prop.NetId] = prop;
        if (!all.Contains(prop)) all.Add(prop);
        prop.ApplyMode();
    }

    internal static void Forget(PhysicsProp prop)
    {
        if (prop == null) return;
        if (byId.TryGetValue(prop.NetId, out var known) && known == prop) byId.Remove(prop.NetId);
        all.Remove(prop);
    }

    /// <summary>
    /// Gives a prop the name the same thing has on every PC, where its place in the scene would not do (a fuel can made at
    /// run time: CampaignCans names it by its run and number). The top bit marks a given name, as it does a place id.
    /// </summary>
    internal static void Name(PhysicsProp prop, string name)
    {
        if (prop == null || string.IsNullOrEmpty(name)) return;
        Rename(prop, Hash(name) | 0x80000000u);
    }

    // Its place in its scene, the same on every PC: the scene and each step down to it (name and sibling number), hashed
    // (FNV-1a). The top bit marks a place id, so it never meets a made one.
    private static uint PlaceId(Transform at)
    {
        var path = new StringBuilder(at.gameObject.scene.name);
        var steps = new List<Transform>();
        for (var step = at; step != null; step = step.parent) steps.Add(step);
        for (int i = steps.Count - 1; i >= 0; i--) path.Append('/').Append(steps[i].name).Append('#').Append(steps[i].GetSiblingIndex());
        return Hash(path.ToString()) | 0x80000000u;
    }

    private static uint Hash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text) { hash ^= c; hash *= 16777619; }
        return hash;
    }

    private static void Rename(PhysicsProp prop, uint id)
    {
        Forget(prop);
        prop.NetId = id;
        byId[id] = prop;
        all.Add(prop);
    }

    /// <summary>
    /// On a guest's PC a prop nobody has the word on lies frozen, moved by hand: only a word moves it (PhysicsProp.ApplyMode).
    /// </summary>
    internal static bool Frozen(PhysicsProp prop) => !IsHost && !prop.Driving && !prop.Following;

    // ---- Taking the word on a prop ----

    /// <summary>
    /// This PC takes the word on its motion: its own player touched it (PhysicsProp: a push, a kick, a shot, a lean), or, on
    /// the host, it started moving of itself. A newer claim than any other PC's.
    /// </summary>
    internal static void Drive(PhysicsProp prop)
    {
        if (prop == null) return;
        prop.QuietFor = 0f;
        prop.Jolted = true;
        if (prop.Driving) return;
        prop.Epoch++;
        prop.Driving = true;
        prop.Following = false;
        prop.ApplyMode();
    }

    /// <summary>This PC's player picks it up (PropCarry): theirs to carry, and this PC tells the others at once.</summary>
    internal static void Hold(PhysicsProp prop, PlayerMovement carrier)
    {
        Drive(prop);
        prop.SetHolder(carrier);
        Send(prop, Phase.Held);
    }

    /// <summary>
    /// This PC lets go of it or sets it somewhere (PropCarry: put down or thrown; CampaignCans: a can dropped, thrown or
    /// back where it started): loose at <paramref name="at"/>, moving at <paramref name="velocity"/> and turning at
    /// <paramref name="spin"/>, its motion this PC's word until it rests.
    /// </summary>
    internal static void Release(PhysicsProp prop, Vector3 at, Quaternion turn, Vector3 velocity, Vector3 spin)
    {
        if (prop == null) return;
        Drive(prop);
        prop.SetHolder(null);
        prop.Teleport(at, turn, velocity, spin);
        Send(prop, Phase.Moving);
    }

    // ---- What /prop makes ----

    /// <summary>
    /// Makes shelf item <paramref name="item"/> (PropShelf) here and on every PC of the room (/prop), with a name this PC
    /// gives it; this PC runs its first fall, so it lands in one place on every PC. Returns it, or null.
    /// </summary>
    internal static GameObject Make(int item, Vector3 at, Quaternion turn)
    {
        ushort maker = LocalClient;
        uint id = ((uint)(maker & 0x7FF) << 20) | (++madeCount & 0xFFFFF);
        var thing = Spawn(item, id, maker, at, turn);
        if (thing == null) return null;
        if (thing.TryGetComponent(out PhysicsProp prop)) Drive(prop);
        SendValue(MadeMessage, new Made { Id = id, Item = (ushort)item, Maker = maker, Position = at, Rotation = turn },
            NetworkDelivery.ReliableSequenced, maker);
        return thing;
    }

    /// <summary>Takes away a thing /prop made on this PC, here and on every PC of the room.</summary>
    internal static void Unmake(GameObject thing)
    {
        if (thing == null) return;
        foreach (var pair in made)
        {
            if (pair.Value.Thing != thing) continue;
            SendValue(GoneMessage, new Gone { Id = pair.Key, Maker = LocalClient }, NetworkDelivery.ReliableSequenced, LocalClient);
            made.Remove(pair.Key);
            break;
        }
        Destroy(thing);
    }

    private static GameObject Spawn(int item, uint id, ushort maker, Vector3 at, Quaternion turn)
    {
        var shelf = PropShelf.Get();
        if (shelf == null || item < 0 || item >= shelf.Items.Count || shelf.Items[item] == null) return null;
        CollisionEffects.Ensure();
        var prefab = shelf.Items[item];
        var thing = Instantiate(prefab, at, turn);
        thing.name = prefab.name;
        RuntimeSceneHierarchy.Group(thing, RuntimeSceneHierarchy.World, "Props");
        made[id] = new MadeThing { Thing = thing, Item = (ushort)item, Maker = maker, Position = at, Rotation = turn };
        if (thing.TryGetComponent(out Rigidbody _))
        {
            if (!thing.TryGetComponent(out PhysicsProp prop)) prop = thing.AddComponent<PhysicsProp>();
            Rename(prop, id);
            prop.ApplyMode();
        }
        return thing;
    }

    // ---- Each frame ----

    private void Update()
    {
        Listen();
        if (!Shared) return;
        Greet();
        bool host = IsHost;
        var mine = PlayerRoster.Local;
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var prop = all[i];
            if (prop == null) { all.RemoveAt(i); continue; }
            var body = prop.Body;
            // The host runs the world: a prop nobody has the word on that starts moving (a zombie walked into it, a blast)
            // is the host's to tell about.
            if (host && !prop.Driving && !prop.Following && prop.Holder == null && !body.isKinematic
                && body.linearVelocity.sqrMagnitude > StirSpeed * StirSpeed)
                Drive(prop);
            if (!prop.Driving) continue;
            if (prop.Holder != null)
            {
                // Carried by someone else here (a fuel can the run hands over): theirs, not this PC's word.
                if (prop.Holder != mine) { prop.Driving = false; continue; }
                if (now - prop.SentAt >= HeldEvery) Send(prop, Phase.Held);
                continue;
            }
            bool still = body.isKinematic || body.IsSleeping()
                || body.linearVelocity.sqrMagnitude < StillSpeed * StillSpeed && body.angularVelocity.sqrMagnitude < StillSpin * StillSpin;
            prop.QuietFor = still ? prop.QuietFor + dt : 0f;
            if (prop.QuietFor >= QuietSeconds)
            {
                Send(prop, Phase.Rest);
                prop.Driving = false;
                prop.ApplyMode();
            }
            else if (prop.Jolted && now - prop.SentAt >= JoltEvery || now - prop.SentAt >= MovingEvery) Send(prop, Phase.Moving);
        }
    }

    // A prop carried by anyone but this PC's own player hangs from their hand (this PC's own carry is placed by its carrier:
    // PropCarry, CampaignCans).
    private void LateUpdate()
    {
        if (!Shared) return;
        var mine = PlayerRoster.Local;
        foreach (var prop in all)
            if (prop != null && prop.Holder != null && prop.Holder != mine)
                CarryHold.Place(prop.transform, prop.Holder, mine, null, prop.HalfSize);
    }

    // A guest that stands in a scene asks the host once for every prop as it is.
    private void Greet()
    {
        if (greeted || IsHost || PlayerRoster.Local == null) return;
        greeted = true;
        SendValue(HelloMessage, new Hello { Asker = LocalClient }, NetworkDelivery.ReliableSequenced, LocalClient);
    }

    // ---- Words between PCs ----

    private static void Send(PhysicsProp prop, Phase phase)
    {
        prop.SentAt = Time.unscaledTime;
        prop.Jolted = false;
        if (!Shared || prop.NetId == 0) return;
        SendValue(PoseMessage, PoseOf(prop, phase), phase == Phase.Moving ? NetworkDelivery.UnreliableSequenced : NetworkDelivery.ReliableSequenced,
            LocalClient);
    }

    private static Pose PoseOf(PhysicsProp prop, Phase phase)
    {
        var body = prop.Body;
        bool moving = !body.isKinematic;
        Vector3 velocity = moving ? body.linearVelocity : Vector3.zero, spin = moving ? body.angularVelocity : Vector3.zero;
        return new Pose
        {
            Id = prop.NetId, Epoch = prop.Epoch, Phase = (byte)phase, Driver = LocalClient,
            Holder = prop.Holder != null ? (ushort)(PlayerRoster.IdOf(prop.Holder) + 1UL) : (ushort)0,
            Position = prop.transform.position, Turn = PackTurn(prop.transform.rotation),
            VX = Mathf.FloatToHalf(velocity.x), VY = Mathf.FloatToHalf(velocity.y), VZ = Mathf.FloatToHalf(velocity.z),
            WX = Mathf.FloatToHalf(spin.x), WY = Mathf.FloatToHalf(spin.y), WZ = Mathf.FloatToHalf(spin.z)
        };
    }

    // A turn in 32 bits (smallest three): which part is largest (2 bits), the other three in 10 bits each over ±1/√2.
    private static uint PackTurn(Quaternion q)
    {
        float x = q.x, y = q.y, z = q.z, w = q.w;
        float length = Mathf.Sqrt(x * x + y * y + z * z + w * w);
        if (length < 1e-6f) return 3u << 30 | Part(0f) << 20 | Part(0f) << 10 | Part(0f);
        x /= length; y /= length; z /= length; w /= length;
        uint largest = 0;
        float most = Mathf.Abs(x);
        if (Mathf.Abs(y) > most) { largest = 1; most = Mathf.Abs(y); }
        if (Mathf.Abs(z) > most) { largest = 2; most = Mathf.Abs(z); }
        if (Mathf.Abs(w) > most) largest = 3;
        float sign = (largest == 0 ? x : largest == 1 ? y : largest == 2 ? z : w) < 0f ? -1f : 1f;
        x *= sign; y *= sign; z *= sign; w *= sign;
        float a, b, c;
        switch (largest)
        {
            case 0: a = y; b = z; c = w; break;
            case 1: a = x; b = z; c = w; break;
            case 2: a = x; b = y; c = w; break;
            default: a = x; b = y; c = z; break;
        }
        return largest << 30 | Part(a) << 20 | Part(b) << 10 | Part(c);
    }

    private const float PartRange = .70710678f;

    private static uint Part(float value) => (uint)Mathf.Clamp(Mathf.RoundToInt((value + PartRange) / (2f * PartRange) * 1023f), 0, 1023);

    private static float PartOf(uint bits) => bits / 1023f * 2f * PartRange - PartRange;

    private static Quaternion UnpackTurn(uint packed)
    {
        uint largest = packed >> 30;
        float a = PartOf(packed >> 20 & 1023u), b = PartOf(packed >> 10 & 1023u), c = PartOf(packed & 1023u);
        float d = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));
        switch (largest)
        {
            case 0: return new Quaternion(d, a, b, c);
            case 1: return new Quaternion(a, d, b, c);
            case 2: return new Quaternion(a, b, d, c);
            default: return new Quaternion(a, b, c, d);
        }
    }

    // To the host, or from the host to every guest but the one it came from (or to one guest only).
    private static void SendValue<T>(string name, T value, NetworkDelivery delivery, ulong from, ulong only = ulong.MaxValue)
        where T : unmanaged, INetworkSerializeByMemcpy
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
        if (only != ulong.MaxValue)
        {
            network.CustomMessagingManager.SendNamedMessage(name, only, writer, delivery);
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
                listened.UnregisterNamedMessageHandler(HelloMessage);
            }
            catch (System.Exception) { }
        }
        listened = messages;
        greeted = false;
        // Into a room or out of one: who has the word changes, so every prop's body is set again (frozen on a guest).
        foreach (var prop in all)
        {
            if (prop == null) continue;
            prop.Driving = prop.Following = false;
            prop.ApplyMode();
        }
        if (messages == null) return;
        messages.RegisterNamedMessageHandler(PoseMessage, ReceivePose);
        messages.RegisterNamedMessageHandler(MadeMessage, ReceiveMade);
        messages.RegisterNamedMessageHandler(GoneMessage, ReceiveGone);
        messages.RegisterNamedMessageHandler(HelloMessage, ReceiveHello);
    }

    private static void ReceivePose(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Pose pose);
        if (pose.Driver == LocalClient) return;
        Relay(PoseMessage, pose, (Phase)pose.Phase == Phase.Moving ? NetworkDelivery.UnreliableSequenced : NetworkDelivery.ReliableSequenced, sender);
        if (!byId.TryGetValue(pose.Id, out var prop) || prop == null) return;
        // The newer claim wins; on a tie, the lower client id (the host's).
        short ahead = (short)(pose.Epoch - prop.Epoch);
        if (ahead < 0 || ahead == 0 && prop.Driving && pose.Driver > LocalClient) return;
        prop.Epoch = pose.Epoch;
        prop.Driving = false;
        var turn = UnpackTurn(pose.Turn);
        var phase = (Phase)pose.Phase;
        if (phase == Phase.Rest)
        {
            prop.Following = false;
            prop.SetHolder(null);
            prop.Teleport(pose.Position, turn, Vector3.zero, Vector3.zero);
            return;
        }
        prop.Following = true;
        if (phase == Phase.Held)
        {
            prop.SetHolder(pose.Holder > 0 ? PlayerRoster.ById((ulong)(pose.Holder - 1)) : null);
            return;
        }
        prop.SetHolder(null);
        prop.Correct(pose.Position, turn,
            new Vector3(Mathf.HalfToFloat(pose.VX), Mathf.HalfToFloat(pose.VY), Mathf.HalfToFloat(pose.VZ)),
            new Vector3(Mathf.HalfToFloat(pose.WX), Mathf.HalfToFloat(pose.WY), Mathf.HalfToFloat(pose.WZ)), SnapDistance);
    }

    private static void ReceiveMade(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Made word);
        if (word.Maker == LocalClient) return;
        Relay(MadeMessage, word, NetworkDelivery.ReliableSequenced, sender);
        if (!made.ContainsKey(word.Id)) Spawn(word.Item, word.Id, word.Maker, word.Position, word.Rotation);
    }

    private static void ReceiveGone(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Gone word);
        if (word.Maker == LocalClient) return;
        Relay(GoneMessage, word, NetworkDelivery.ReliableSequenced, sender);
        if (made.TryGetValue(word.Id, out var thing) && thing.Thing != null) Destroy(thing.Thing);
        made.Remove(word.Id);
    }

    // On the host: a guest standing in a scene gets every made thing and every prop as it is now, to them alone.
    private static void ReceiveHello(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Hello _);
        if (!IsHost) return;
        gone.Clear();
        foreach (var pair in made)
        {
            if (pair.Value.Thing == null) { gone.Add(pair.Key); continue; }
            var at = pair.Value.Thing.transform;
            SendValue(MadeMessage, new Made { Id = pair.Key, Item = pair.Value.Item, Maker = pair.Value.Maker, Position = at.position, Rotation = at.rotation },
                NetworkDelivery.ReliableSequenced, LocalClient, sender);
        }
        foreach (uint id in gone) made.Remove(id);
        foreach (var prop in all)
        {
            if (prop == null || prop.NetId == 0 || !prop.isActiveAndEnabled) continue;
            var body = prop.Body;
            var phase = prop.Holder != null ? Phase.Held
                : !body.isKinematic && !body.IsSleeping() && body.linearVelocity.sqrMagnitude > StillSpeed * StillSpeed ? Phase.Moving : Phase.Rest;
            var pose = PoseOf(prop, phase);
            SendValue(PoseMessage, pose, NetworkDelivery.ReliableSequenced, LocalClient, sender);
        }
    }

    // On the host: a guest's word goes on to the other guests.
    private static void Relay<T>(string name, T value, NetworkDelivery delivery, ulong sender) where T : unmanaged, INetworkSerializeByMemcpy
    {
        var network = GameSession.Network;
        if (network != null && network.IsServer) SendValue(name, value, delivery, sender);
    }
}
