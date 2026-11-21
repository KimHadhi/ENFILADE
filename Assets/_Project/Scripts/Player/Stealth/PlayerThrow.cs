using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// T: a stone thrown where the player looks, the 2026-11-11 stealth round's lure (after Advanced Stealth's thrown
/// objects and sound grenade, F:\Addons). Since 2026-11-21 the left hand throws it as it throws the class throwable
/// (the user: "T를 꾹 누를 때 애초에 돌을 왼손에 드는 모션이 없어서 Ready 소리가 안 나고"): PlayerGrenades owns the key, the
/// hand (HandOwner grenade.throw), the clips (draw, hold, throw, tap) and the arc, with the stone as Throwable.Stone
/// (Tables/Grenades stone: its flight and how the hand holds it). The draw brings a stone into the hand, and as it closes
/// on it (the draw's pin moment) StoneReady sounds; held, the arc it will fly is drawn to where it lands, with the ring of
/// how far its clack carries there, pink when a zombie that isn't awake will hear it, and those zombies' sight cones show
/// pink (StealthView); let go, the throw clip lets it go (a tap throws at once).
/// What stays here is the stone's own: it never runs out, one every cooldown seconds (generous, so trying is cheap); it
/// flies through zombies and players, clacks where it lands, and that clack is a lure (Noise.Lure): every zombie that
/// isn't awake and hears it turns to watch that spot a good while with its mind on it, and the nearest walks over to
/// look, none learning where it came from. A zombie facing the way past is turned off it, or turned round for a takedown
/// from behind. Every PC throws its own copy of a friend's stone (PlayerNetworkCombat), and the host's is the one its
/// zombies hear. Numbers: Stealth.csv Throw (cooldown, how far it carries, how long it lies, its clack).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerThrow : MonoBehaviour, ITableBound
{
    public const string TableName = "Stealth", TableRow = "Throw";
    /// <summary>The key that throws a stone (KeyBindings; T unless the key window says otherwise).</summary>
    public const string KeyName = "Player/Throw";
    /// <summary>The stealth sound bank (Art/Audio/Stealth), its keys StoneReady and StoneHit; volumes in Sounds.csv "Stealth/…".</summary>
    public const string BankId = "Stealth";
    internal const string ReadyKey = "StoneReady", HitKey = "StoneHit";

    [SerializeField, Min(0f), Tooltip("Seconds between two stones.")]
    private float cooldown = 2.5f;
    [SerializeField, Min(0f), Tooltip("How far its clack carries to a zombie (m), as a footstep's reach does (SoundCues.csv noise).")]
    private float noise = 16f;
    [SerializeField, Min(1f), Tooltip("How long it lies there before it sinks away (s).")]
    private float life = 7f;
    [SerializeField, Range(0f, 1f), Tooltip("Its clack's volume.")]
    private float volume = .9f;

    /// <summary>This PC's player threw one: where from, how fast and its number, for friends' PCs (PlayerNetworkCombat).</summary>
    public event Action<Vector3, Vector3, ushort> Thrown;
    /// <summary>
    /// This PC's player's stone first landed there: the lure, for friends' PCs, the host's zombies among them
    /// (PlayerNetworkCombat). Their own copies of it never lure.
    /// </summary>
    public event Action<Vector3> Lured;

    // This player's stones so far, for their names between PCs (ThrownStone).
    private ushort thrownCount;

    private StealthView view;
    private float readyAt;

    /// <summary>A stone can come into the hand: the last one's cooldown is over.</summary>
    public bool Ready => Time.time >= readyAt;

    private void Awake() => GameTables.Bind(TableName, TableRow, this);

    void ITableBound.OnTableApplied() { }

    // Its size and look come from its throwable row (Tables/Grenades stone), the one place the hand and the flight read.
    private static float Size => Throwables.Of(Throwable.Stone) is ThrowableInfo stone ? stone.Size : .075f;

    /// <summary>
    /// The hand let go of a stone (PlayerGrenades, at its throw clip's release): it flies from there, the next one is a
    /// cooldown away, and friends' PCs are told.
    /// </summary>
    internal void ThrowFrom(Vector3 from, Vector3 velocity)
    {
        readyAt = Time.time + cooldown;
        ushort id = ++thrownCount;
        var stone = ThrownStone.Throw(from, velocity, gameObject, noise, Size, life, volume, true, id);
        if (stone != null) stone.Landed = Lure;
        Thrown?.Invoke(from, velocity, id);
    }

    // This PC's own stone landed: the lure is there, here and on every other PC (SyncAudit.md, item 6: the host's copy used
    // to lure where it landed, which was not where the thrower's did).
    private void Lure(Vector3 at)
    {
        Noise.Make(at, noise, gameObject, true);
        Lured?.Invoke(at);
    }

    /// <summary>A friend's stone first landed there on their PC: its lure, here (the host's zombies hear it).</summary>
    internal void ShowLure(Vector3 at) => Noise.Make(at, noise, gameObject, true);

    /// <summary>The held stone would land there (PlayerGrenades' arc, this frame): the reach of its clack and who would hear it (StealthView).</summary>
    internal void ShowAim(Vector3 landing)
    {
        if (view == null) view = GetComponent<StealthView>();
        if (view != null) view.ShowAim(landing, noise, true);
    }

    /// <summary>
    /// A friend's stone, seen here: this PC throws its copy, which follows their PC's stone (PropNet) and never lures
    /// (ShowLure does, from where theirs landed).
    /// </summary>
    internal void ShowThrow(Vector3 from, Vector3 velocity, ushort id)
        => ThrownStone.Throw(from, velocity, gameObject, noise, Size, life, volume, false, id);
}

/// <summary>
/// A thrown stone (PlayerThrow): a small body that flies with a soft whoosh, clacks where it first lands (the lure the
/// zombies hear, once) and on a bounce or two after, then lies a while and sinks away. On the players' own layer, as a
/// dropped magazine is: players walk through it and bullets pass it (AttackTrace).
/// </summary>
[DisallowMultipleComponent]
public sealed class ThrownStone : MonoBehaviour
{
    private const float ClackSpeed = 1.5f, ClackGap = .15f, SinkSeconds = 1.2f, SinkDepth = .15f, Rate = 44100;
    private const int MostClacks = 3;
    private static readonly HashSet<Collider> stones = new HashSet<Collider>();
    private static Mesh shape;
    private static Material look;
    private static AudioClip whoosh;
    private static SoundBank sounds;
    private static bool soundsLooked;

    /// <summary>What a throw leaves from and stops against: the world, not players, corpses or zombies.</summary>
    internal static int WorldMask => WorldLayers.Built;

    /// <summary>The stone taken in hand (PlayerThrow): flat, on the thrower's PC only. Silent with no StoneReady clip.</summary>
    internal static void PlayReady()
    {
        var bank = Sounds();
        var clip = bank != null ? bank.Pick(PlayerThrow.ReadyKey) : null;
        if (clip == null) return;
        var settings = bank.SettingsOf(PlayerThrow.ReadyKey);
        FeelAudio.PlayClip(clip, settings.Volume, settings.RandomPitch());
    }

    private static SoundBank Sounds()
    {
        if (!soundsLooked) { soundsLooked = true; sounds = SoundBank.Load(PlayerThrow.BankId); }
        return sounds;
    }

    /// <summary>
    /// The stone as the hand holds it (Throwables.Dress for Throwable.Stone): the same pebble that flies, its size from
    /// its throwable row, no collider. The one place a stone's look is made.
    /// </summary>
    internal static GameObject Held(Transform parent, float size)
    {
        var held = new GameObject("Look");
        held.transform.SetParent(parent, false);
        held.transform.localScale = Scale(size);
        held.AddComponent<MeshFilter>().sharedMesh = Shape();
        var renderer = held.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Look();
        return held;
    }

    // A pebble: the sphere, flattened a little.
    private static Vector3 Scale(float size) => new Vector3(size, size * .78f, size * .9f);

    /// <summary>A thrown stone's collider: bullets go through it.</summary>
    public static bool IsStone(Collider collider) => collider != null && stones.Contains(collider);

    /// <summary>On the thrower's PC: where it first landed, the lure's place (a hand's width up).</summary>
    internal Action<Vector3> Landed;

    private GameObject thrower;
    private Collider box;
    private Rigidbody body;
    // The thrower's PC's stone (theirs runs its flight and lures), or another PC's copy of it.
    private bool mine;
    private PhysicsProp prop;
    private float reach, lifeSeconds, volume, age, lastClack = -1f, sinking = -1f;
    private int clacks;
    private bool landed;
    private Vector3 sinkFrom;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        stones.Clear();
        sounds = null;
        soundsLooked = false;
    }

    /// <summary>
    /// Throws one. Mine: this PC's player threw it, and this PC runs its flight; else it is a friend's copy. Id: the
    /// thrower's number for it. In a room it is a shared prop named by its thrower and number (PropNet): the thrower's PC
    /// runs its flight and every copy follows it, so it bounces, rolls and stops in one place on every PC, and its spin
    /// and sounds draw from that name (SharedRandom).
    /// </summary>
    internal static ThrownStone Throw(Vector3 from, Vector3 velocity, GameObject thrower, float noise, float size, float life, float volume,
        bool mine, ushort id)
    {
        ulong client = thrower != null && thrower.TryGetComponent(out NetworkObject network) && network.IsSpawned ? network.OwnerClientId : 0UL;
        using var drawn = SharedRandom.Scope(SharedRandom.Seed(client, id, 0x570E5u));
        var host = new GameObject("Thrown Stone") { layer = GoreGibs.Layer };
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.World, "Debris");
        host.transform.SetPositionAndRotation(from, SharedRandom.Rotation());
        host.transform.localScale = Scale(size);
        host.AddComponent<MeshFilter>().sharedMesh = Shape();
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Look();
        var collider = host.AddComponent<SphereCollider>();
        collider.radius = .5f;
        // It flies through zombies and players (layer 31) and lands behind them: one hit square in the face only learnt to
        // look at its own feet.
        collider.excludeLayers = LayerMask.GetMask("Enemy") | (1 << 31);
        var stone = host.AddComponent<ThrownStone>();
        stone.box = collider;
        stones.Add(collider);
        var rigid = stone.body = host.AddComponent<Rigidbody>();
        rigid.mass = .25f;
        rigid.linearDamping = .02f;
        rigid.angularDamping = .4f;
        rigid.interpolation = RigidbodyInterpolation.Interpolate;
        // Small and fast: swept, so it never passes through a thin wall.
        rigid.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        stone.mine = mine;
        if (PropNet.Shared)
        {
            // Taking the word (or waiting on the thrower's) before it is thrown: it sets the body free on a guest's PC.
            var prop = stone.prop = host.AddComponent<PhysicsProp>();
            prop.OwnCarry = true;
            prop.Epoch = 0;
            prop.Driving = false;
            prop.Following = !mine;
            PropNet.Name(prop, "stone/" + client + "/" + id);
            if (mine) PropNet.Drive(prop);
            else prop.ApplyMode();
        }
        rigid.linearVelocity = velocity;
        rigid.angularVelocity = SharedRandom.InsideUnitSphere() * 18f;
        // Players pass through it, as through magazines and corpses; the thrower's own body above all.
        foreach (var player in PlayerRoster.All)
        {
            if (player == null) continue;
            foreach (var part in player.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(collider, part, true);
        }
        stone.thrower = thrower;
        stone.reach = noise;
        stone.lifeSeconds = life;
        stone.volume = volume;
        PlayAt(Whoosh(), from, volume * .45f, SharedRandom.Range(.92f, 1.08f));
        return stone;
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (sinking < 0f)
        {
            if (age >= lifeSeconds) Sink();
            return;
        }
        sinking += Time.deltaTime;
        float share = Mathf.Clamp01(sinking / SinkSeconds);
        transform.position = sinkFrom - Vector3.up * (SinkDepth * share);
        if (share >= 1f) Destroy(gameObject);
    }

    private void Sink()
    {
        if (sinking >= 0f) return;
        sinking = 0f;
        sinkFrom = transform.position;
        // Its sinking is its own on each PC: no word moves it from here.
        if (prop != null) { PropNet.Forget(prop); prop.Driving = prop.Following = false; }
        if (body != null) { body.isKinematic = true; body.detectCollisions = false; }
    }

    private void OnCollisionEnter(Collision hit)
    {
        float speed = hit.relativeVelocity.magnitude;
        if (speed < ClackSpeed || clacks >= MostClacks || age - lastClack < ClackGap) return;
        clacks++;
        lastClack = age;
        Vector3 point = hit.contactCount > 0 ? hit.GetContact(0).point : transform.position;
        float strength = Mathf.InverseLerp(ClackSpeed, 12f, speed);
        // Its clack: the stealth bank's StoneHit (the user's Stone hit, 2026-11-21), its pitch spread in Sounds.csv.
        var bank = Sounds();
        var clip = bank != null ? bank.Pick(PlayerThrow.HitKey) : null;
        if (clip != null)
        {
            var settings = bank.SettingsOf(PlayerThrow.HitKey);
            PlayAt(clip, point, volume * settings.Volume * Mathf.Lerp(.4f, 1f, strength) * (landed ? .55f : 1f), settings.RandomPitch());
        }
        if (landed) return;
        // The first landing is the lure, a hand's width up so the stone itself doesn't muffle it: the thrower's stone's,
        // told to every PC from there (PlayerThrow). A copy's landing only clacks.
        landed = true;
        if (mine) Landed?.Invoke(point + Vector3.up * .15f);
    }

    private void OnDestroy()
    {
        if (!ReferenceEquals(box, null)) stones.Remove(box);
    }

    // A pebble: the sphere, flattened a little (Throw's scale).
    private static Mesh Shape()
    {
        if (shape != null) return shape;
        shape = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        if (shape == null)
        {
            var made = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shape = made.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(made);
        }
        return shape;
    }

    // Dull grey stone.
    private static Material Look()
    {
        if (look != null) return look;
        var lit = Shader.Find("Universal Render Pipeline/Lit").OrNull() ?? Shader.Find("Universal Render Pipeline/Simple Lit");
        look = new Material(lit) { hideFlags = HideFlags.DontSave, name = "Thrown Stone" };
        var grey = new Color(.33f, .32f, .3f);
        if (look.HasProperty("_BaseColor")) look.SetColor("_BaseColor", grey);
        if (look.HasProperty("_Smoothness")) look.SetFloat("_Smoothness", .12f);
        return look;
    }

    // A sound out in the world, at the combat channel's volume.
    private static void PlayAt(AudioClip clip, Vector3 at, float level, float pitch)
    {
        if (clip == null || level <= 0f) return;
        var host = new GameObject("Stone Sound");
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.Effects, "Audio");
        host.transform.position = at;
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 1.2f;
        source.maxDistance = 40f;
        source.dopplerLevel = 0f;
        source.pitch = pitch;
        GameAudio.Bind(source, AudioChannel.Combat);
        source.PlayOneShot(clip, Mathf.Clamp01(level));
        Destroy(host, clip.length / Mathf.Max(.1f, pitch) + .1f);
    }

    // Still made here until a file is chosen for it (SoundRebuild.md 5.6).
    private static AudioClip Whoosh() => whoosh != null ? whoosh : whoosh = Made("Stone Whoosh", MakeWhoosh());

    private static AudioClip Made(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, (int)Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    // Air past a thrown stone: noise swelling and falling as it leaves the hand.
    private static float[] MakeWhoosh()
    {
        var data = new float[(int)(Rate * .28f)];
        var random = new System.Random(57);
        float low = 0f, band = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / Rate, share = t / .28f;
            float white = (float)random.NextDouble() * 2f - 1f;
            low += (white - low) * Mathf.Lerp(.08f, .25f, share);
            band += (low - band) * .5f;
            data[i] = (low - band) * Mathf.Sin(Mathf.PI * share) * Mathf.Sin(Mathf.PI * share);
        }
        return Normalize(data, .5f);
    }

    private static float[] Normalize(float[] data, float peak)
    {
        float top = 0f;
        foreach (float sample in data) top = Mathf.Max(top, Mathf.Abs(sample));
        if (top > 0f) for (int i = 0; i < data.Length; i++) data[i] *= peak / top;
        return data;
    }
}
