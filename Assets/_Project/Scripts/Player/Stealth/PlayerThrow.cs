using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// T: a stone thrown where the player looks, the 2026-11-11 stealth round's lure (after Advanced Stealth's thrown
/// objects and sound grenade, F:\Addons). Held, T aims: the arc it will fly is drawn to where it lands, with the ring of
/// how far its clack carries there, pink when a zombie that isn't awake will hear it, and those zombies' sight cones
/// show pink (StealthView); let go, it is thrown (a tap throws at once). It flies through zombies and players, clacks
/// where it lands, and that clack is a lure (Noise.Lure): every zombie that isn't awake and hears it turns to watch
/// that spot a good while with its mind on it, and the nearest walks over to look, none learning where it came from. A
/// zombie facing the way past is turned off it, or turned round for a takedown from behind. Stones never run out, one
/// every cooldown seconds: generous, so trying is cheap. Every PC throws its own copy of a friend's stone
/// (PlayerNetworkCombat), and the host's is the one its zombies hear. Numbers: Stealth.csv Throw.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerThrow : MonoBehaviour, ITableBound
{
    public const string TableName = "Stealth", TableRow = "Throw";

    [SerializeField, Min(0f), Tooltip("Seconds between two stones.")]
    private float cooldown = 2.5f;
    [SerializeField, Min(1f), Tooltip("How fast it leaves the hand, the way the player looks (m/s).")]
    private float speed = 15f;
    [SerializeField, Min(0f), Tooltip("And upward on top of that (m/s): an arc, not a line.")]
    private float lift = 2.5f;
    [SerializeField, Min(0f), Tooltip("How far its clack carries to a zombie (m), as a footstep's reach does (SoundCues.csv noise).")]
    private float noise = 16f;
    [SerializeField, Min(.01f), Tooltip("Its size (m).")]
    private float size = .075f;
    [SerializeField, Min(1f), Tooltip("How long it lies there before it sinks away (s).")]
    private float life = 7f;
    [SerializeField, Range(0f, 1f), Tooltip("Its clack's volume.")]
    private float volume = .9f;

    /// <summary>This PC's player threw one: where from and how fast, for friends' PCs (PlayerNetworkCombat).</summary>
    public event Action<Vector3, Vector3> Thrown;

    private PlayerMovement movement;
    private StealthView view;
    // The aim's arc, shared with the class throwable's G (ThrowArc, PlayerGrenades).
    private ThrowArc arc;
    private float readyAt;
    private bool aiming;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        GameTables.Bind(TableName, TableRow, this);
    }

    void ITableBound.OnTableApplied() { }

    private void Update()
    {
        // Player/Throw (T unless the key window says otherwise).
        var key = KeyBindings.Find("Player/Throw");
        bool able = movement != null && movement.InputAvailable && key != null && InputFocus.TakesInput() && !movement.IsDead && !movement.IsHeld
            && !movement.CombatInputLocked;
        var eye = Camera.main;
        if (!able || eye == null) { StopAim(); return; }
        if (KeyBindings.Pressed(key)) aiming = true;
        if (!aiming) return;
        if (KeyBindings.Held(key)) { Aim(eye.transform); return; }
        // Let go: thrown, if a stone is ready.
        StopAim();
        if (Time.time < readyAt) return;
        readyAt = Time.time + cooldown;
        Launch(eye.transform, out Vector3 from, out Vector3 velocity);
        ThrownStone.Throw(from, velocity, gameObject, noise, size, life, volume);
        Thrown?.Invoke(from, velocity);
    }

    // Where it leaves the hand and how fast, from the eyes now.
    private void Launch(Transform eye, out Vector3 from, out Vector3 velocity)
    {
        from = eye.position + eye.forward * .35f + eye.right * .12f - eye.up * .12f;
        // Face to a wall, it leaves from just this side of it.
        if (Physics.Linecast(eye.position, from, out RaycastHit wall, ThrownStone.WorldMask, QueryTriggerInteraction.Ignore))
            from = wall.point - (from - eye.position).normalized * .08f;
        Vector3 carried = movement.ActualVelocity;
        carried.y = 0f;
        velocity = eye.forward * speed + Vector3.up * lift + carried * .5f;
    }

    // Held: the arc it would fly, to where it would land, and there the reach of its clack (StealthView).
    private void Aim(Transform eye)
    {
        Launch(eye, out Vector3 at, out Vector3 velocity);
        if (arc == null) arc = new ThrowArc(transform);
        bool landed = arc.Trace(at, velocity, size * .5f);
        bool ready = Time.time >= readyAt;
        // Faint near the hand, solid along the flight and pink where it lands; grey while no stone is ready.
        Color body = ready ? KitStyle.Ice : KitStyle.With(KitStyle.Ice, .35f), end = ready ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f);
        arc.Draw(body, end, ready ? .85f : .4f);
        if (view == null) view = GetComponent<StealthView>();
        if (view != null && landed) view.ShowAim(arc.Landing, noise, ready);
    }

    private void StopAim()
    {
        aiming = false;
        arc?.Hide();
    }

    private void OnDisable() => StopAim();

    /// <summary>A friend's stone, seen here: this PC throws its own copy (the host's is the one its zombies hear).</summary>
    internal void ShowThrow(Vector3 from, Vector3 velocity) => ThrownStone.Throw(from, velocity, gameObject, noise, size, life, volume);
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
    private static AudioClip whoosh, clack;
    private static StealthSounds sounds;
    private static bool soundsLooked;
    private static int worldMask;

    /// <summary>What a throw leaves from and stops against: the world, not players, corpses or zombies.</summary>
    internal static int WorldMask => worldMask != 0 ? worldMask : worldMask = Physics.DefaultRaycastLayers & ~(1 << 31) & ~LayerMask.GetMask("Enemy");

    /// <summary>A thrown stone's collider: bullets go through it.</summary>
    public static bool IsStone(Collider collider) => collider != null && stones.Contains(collider);

    private GameObject thrower;
    private Collider box;
    private Rigidbody body;
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

    internal static ThrownStone Throw(Vector3 from, Vector3 velocity, GameObject thrower, float noise, float size, float life, float volume)
    {
        var host = new GameObject("Thrown Stone") { layer = GoreGibs.Layer };
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.World, "Debris");
        host.transform.SetPositionAndRotation(from, Random.rotation);
        host.transform.localScale = new Vector3(size, size * .78f, size * .9f);
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
        rigid.linearVelocity = velocity;
        rigid.angularVelocity = Random.insideUnitSphere * 18f;
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
        PlayAt(Whoosh(), from, volume * .45f, Random.Range(.92f, 1.08f));
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
        PlayAt(Clack(), point, volume * Mathf.Lerp(.4f, 1f, strength) * (landed ? .55f : 1f), Random.Range(.9f, 1.15f));
        if (landed) return;
        // The first landing is the lure, a hand's width up so the stone itself doesn't muffle it.
        landed = true;
        Noise.Make(point + Vector3.up * .15f, reach, thrower, true);
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
        var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
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

    // Its clack: the project's stone impacts (Resources/Stealth/StealthSounds), else one made here.
    private static AudioClip Clack()
    {
        if (!soundsLooked) { soundsLooked = true; sounds = Resources.Load<StealthSounds>(StealthSounds.ResourcePath); }
        var picked = sounds != null ? sounds.PickStone() : null;
        return picked != null ? picked : clack != null ? clack : clack = Made("Stone Clack", MakeClack());
    }

    private static AudioClip Whoosh() => whoosh != null ? whoosh : whoosh = Made("Stone Whoosh", MakeWhoosh());

    private static AudioClip Made(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, (int)Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    // A small hard thing on stone: a click and two short rings.
    private static float[] MakeClack()
    {
        var data = new float[(int)(Rate * .18f)];
        var random = new System.Random(311);
        float held = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / Rate;
            held += ((float)random.NextDouble() * 2f - 1f - held) * .6f;
            data[i] = held * Mathf.Exp(-t / .004f) * .8f
                + Mathf.Sin(2f * Mathf.PI * 2150f * t) * Mathf.Exp(-t / .018f) * .45f
                + Mathf.Sin(2f * Mathf.PI * 3420f * t) * Mathf.Exp(-t / .011f) * .3f;
        }
        return Normalize(data, .8f);
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
