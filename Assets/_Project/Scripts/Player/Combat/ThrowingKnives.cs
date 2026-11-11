using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The thrown knives of a map (porting bundle 6: [MW] Quick Throwing Knife 3650480912 for the throw, TFA Base 2840031720
/// and ArcCW Gunsmith Offensive 2257255110 for sticking and taking back). It holds the knife's numbers (Tables/
/// ThrowingKnife), every knife flying, stuck or lying on this PC (ThrownKnife), what they look and sound like
/// (ThrowingKnifeLibrary), and taking one back: F while looking at one within reach, which comes before the campaign's
/// own F and the ping, after a field pickup (FieldPickups.Targeting covers both).
/// <para>In a room every PC sees every knife. The thrower's PC says what its player's knife does as it happens (a note:
/// drawn into the hand, thrown, into a body or a wall, glanced off, lying still) and every other PC plays its own copy
/// from that; a guest's notes go through the host, which passes them on. Taking one back is the host's to decide, so
/// two players can't both take one knife: a guest asks, the host gives it to whoever asked first and tells everyone,
/// and the taker's PC adds it to their count. Alone nothing is sent. Someone joining later doesn't see knives already
/// lying (as a barricade isn't).</para>
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Campaign - 1)]
public sealed class ThrowingKnives : MonoBehaviour
{
    public const string TableName = "ThrowingKnife", TableRow = "Default";
    private const string Message = "ENFILADE.Knife";
    // A guest's copy lags: the host lets them reach a little further (m). An unanswered ask is sent again after (s).
    private const float ReachSlack = 1.5f, AskAgain = .5f, KeyFlash = .14f;

    [Header("Carried")]
    [SerializeField, Min(0), Tooltip("Most knives a player carries.")]
    internal int carry = 3;
    [SerializeField, Min(0), Tooltip("Knives a player starts a chapter with, and has again after a respawn.")]
    internal int start = 3;
    [SerializeField, Min(0), Tooltip("Knives an ammo pile adds.")]
    internal int ammoKnives = 1;
    [Header("The blow")]
    [SerializeField, Min(0f), Tooltip("Damage of a knife that goes in. 3650480912: 80, whatever it hits.")]
    internal float damage = 80f;
    [SerializeField, Min(1f), Tooltip("Times the damage in the head.")]
    internal float headFactor = 2f;
    [SerializeField, Min(1f), Tooltip("Times the damage into a zombie that doesn't know anyone is there.")]
    internal float unawareFactor = 1.5f;
    [SerializeField, Min(0f), Tooltip("Posture it takes off what it goes into.")]
    internal float postureDamage = 12f;
    [SerializeField, Min(0f), Tooltip("How long what it goes into staggers (s): a blow on its way is cut off.")]
    internal float stagger = .4f;
    [SerializeField, Min(0f), Tooltip("Slower than this it doesn't go in (m/s). 3650480912: 300 units/s.")]
    internal float minHitSpeed = 7.5f;
    [Header("The flight")]
    [SerializeField, Min(1f), Tooltip("How fast it leaves the hand (m/s). 3650480912 and 2257255110: 2000 units/s.")]
    internal float speed = 50f;
    [SerializeField, Tooltip("How fast it turns end over end (degrees/s). 3650480912: 3000.")]
    internal float spin = 3000f;
    [SerializeField, Min(0f), Tooltip("How fast it falls in flight (m/s²): a little.")]
    internal float gravity = 3f;
    [SerializeField, Min(0f), Tooltip("How fat it is to a body in its way (m).")]
    internal float hitRadius = .06f;
    [Header("Sticking")]
    [SerializeField, Min(0f), Tooltip("Slower than this it never sticks in a wall (m/s). TFA: 250 units/s.")]
    internal float stickSpeed = 6.25f;
    [SerializeField, Min(0f), Tooltip("How much more slanted it may come in for each m/s more (TFA: speed/1000 × 0.3 in units/s).")]
    internal float stickSlope = .012f;
    [SerializeField, Min(0f), Tooltip("How deep its tip goes into a wall (m).")]
    internal float wallDepth = .05f;
    [SerializeField, Min(0f), Tooltip("How deep its tip goes into a body (m).")]
    internal float fleshDepth = .09f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of its speed it keeps glancing off a wall.")]
    internal float bounce = .3f;
    [Header("The throw")]
    [SerializeField, Min(0f), Tooltip("From letting go of the key to the swing's sound and view punch (s).")]
    internal float swingTime = .1f;
    [SerializeField, Min(0f), Tooltip("From letting go of the key to the knife leaving the hand (s).")]
    internal float releaseTime = .3f;
    [SerializeField, Min(0f), Tooltip("From letting go of the key to the gun being ready again (s).")]
    internal float recoverTime = .5f;
    [SerializeField, Tooltip("The swing's view punch (degrees): down; and a roll either way.")]
    internal Vector2 viewPunch = new Vector2(2f, 2f);
    [Header("On the ground")]
    [SerializeField, Min(1f), Tooltip("How long a knife lies before it goes (s). TFA: 30.")]
    internal float looseLife = 30f;
    [SerializeField, Min(1f), Tooltip("How long a stuck knife stays before it goes (s). TFA: 60.")]
    internal float stuckLife = 60f;
    [SerializeField, Min(1), Tooltip("Most knives lying about at once; past it the oldest goes.")]
    internal int mostLying = 30;
    [SerializeField, Min(.5f), Tooltip("How near a knife must be to take it back with F (m, from the eyes).")]
    internal float reach = 2.2f;
    [SerializeField, Range(5f, 90f), Tooltip("How close to the middle of the view it must be to take it back (degrees).")]
    internal float lookAngle = 25f;
    [Header("Sound")]
    [SerializeField, Min(0f), Tooltip("How far zombies hear it go into a wall (m).")]
    internal float wallNoise = 5f;
    [SerializeField, Min(0f), Tooltip("How far zombies hear it clatter off something (m).")]
    internal float bounceNoise = 3f;
    [SerializeField, Range(0f, 1f), Tooltip("Its sounds' volume.")]
    internal float volume = .9f;
    [Header("Model")]
    [SerializeField, Min(.05f), Tooltip("The knife's length (m); the model is scaled to it.")]
    internal float length = .28f;
    [SerializeField, Tooltip("The model's tip is at the other end: turn it round.")]
    internal bool flipModel;

    private enum NoteKind : byte { Hand, Throw, Land, Bounce, Rest, Ask, Gone }
    private enum LandOn : byte { World, Body }

    /// <summary>One thing a knife or a hand did, for the other PCs.</summary>
    private struct Note : INetworkSerializeByMemcpy
    {
        public byte Kind, Flag, Bone;
        public uint Id;
        /// <summary>Whose hand, who threw it, or who took it.</summary>
        public ulong Who;
        /// <summary>The network id of the enemy a knife went into (0: none).</summary>
        public ulong Target;
        public Vector3 Position, Velocity, Spin, Local;
        public Quaternion Rotation, LocalTurn;
    }

    private static ThrowingKnives instance;
    private static Mesh shapeMesh;
    private static Material[] shapeMaterials;
    private static Quaternion shapeTurn = Quaternion.identity;
    private static Vector3 shapeCenter;
    private static float shapeLength;
    private static bool shapeLooked;
    private static Material steel, grip;

    private readonly Dictionary<uint, ThrownKnife> live = new Dictionary<uint, ThrownKnife>();
    private readonly Dictionary<uint, float> asked = new Dictionary<uint, float>();
    private readonly List<ulong> targets = new List<ulong>();
    private NetworkManager network;
    private InputAction interact;
    private ThrownKnife targeted;
    private string refusal;
    private uint serial;
    private bool listening;
    private float pressedAt = -99f;
    private GameObject promptHost;
    private RectTransform promptRoot;
    private Image keyBox, underline;
    private TMP_Text keyLabel, nameLabel, amountLabel;

    /// <summary>This map's knives: made with every map, and on first use anywhere else.</summary>
    public static ThrowingKnives Instance
    {
        get
        {
            if (instance == null && Application.isPlaying) Make(SceneManager.GetActiveScene());
            return instance;
        }
    }

    /// <summary>This map's knives if there are any yet (none made by asking).</summary>
    public static ThrowingKnives Current => instance;

    /// <summary>This PC's player is looking at a knife they can take back within reach ([F] is the knife's).</summary>
    public static bool Targeting => instance != null && instance.targeted != null;

    /// <summary>What a knife flies into and leaves from: everything but the players' own layer (players, corpses, gore).</summary>
    internal static int Mask => Physics.DefaultRaycastLayers & ~(1 << GoreGibs.Layer);

    /// <summary>How a knife sits as it leaves the hand: tip first along its way.</summary>
    internal static Quaternion LaunchTurn(Vector3 way)
    {
        if (way.sqrMagnitude < 1e-6f) way = Vector3.forward;
        return Quaternion.LookRotation(way, Mathf.Abs(Vector3.Dot(way.normalized, Vector3.up)) > .98f ? Vector3.forward : Vector3.up);
    }

    internal float HalfLength => Mathf.Max(.05f, length) * .5f;
    internal ThrowingKnifeLibrary Sounds => ThrowingKnifeLibrary.Instance;

    private bool InRoom => network != null && network.IsListening;
    private bool IsHost => !InRoom || network.IsServer;
    private ulong Mine => InRoom ? network.LocalClientId : 0UL;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        shapeLooked = false;
        shapeMesh = null;
        shapeMaterials = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Every map gets one as it loads, so a guest listens for notes before anyone throws; the title screen none.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single || Array.IndexOf(MapTable.All(), scene.name) < 0) return;
        Make(scene);
    }

    private static void Make(Scene scene)
    {
        if (instance != null) return;
        var host = new GameObject("Throwing Knives");
        if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<ThrowingKnives>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        GameTables.Bind(TableName, TableRow, this);
    }

    private void Start()
    {
        network = GameSession.Network;
        interact = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Interact", false) : null;
        Listen();
        // The sounds' data is read in now, off the main thread, not at the first throw.
        var library = Sounds;
        if (library == null) return;
        foreach (var clip in library.All())
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
    }

    private void Listen()
    {
        if (!InRoom || network.CustomMessagingManager == null) return;
        listening = true;
        network.CustomMessagingManager.RegisterNamedMessageHandler(Message, Receive);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (promptHost != null) Destroy(promptHost);
        if (listening && network != null && network.CustomMessagingManager != null)
            network.CustomMessagingManager.UnregisterNamedMessageHandler(Message);
    }

    // ---- This PC's player's knives ----

    // Unique across the room: the thrower's client in the top bits, a count of their throws below.
    private uint NextId()
    {
        serial = (serial + 1) & 0xFFFFF;
        if (serial == 0) serial = 1;
        return ((uint)(Mine + 1) & 0xFFF) << 20 | serial;
    }

    /// <summary>
    /// This PC's player's knife leaves the hand at <paramref name="from"/> at <paramref name="velocity"/>, checked from
    /// their eyes on (PlayerKnives). Every other PC throws its copy.
    /// </summary>
    internal ThrownKnife Throw(PlayerMovement thrower, Vector3 eyes, Vector3 from, Vector3 velocity)
    {
        uint id = NextId();
        var knife = ThrownKnife.Make(this, id, thrower, true, eyes, from, velocity);
        live[id] = knife;
        Share(new Note { Kind = (byte)NoteKind.Throw, Id = id, Who = Mine, Position = from, Velocity = velocity });
        return knife;
    }

    /// <summary>This PC's player drew a knife into the hand, or put it back unthrown.</summary>
    internal void ShareHand(bool drawn) => Share(new Note { Kind = (byte)NoteKind.Hand, Flag = (byte)(drawn ? 1 : 0), Who = Mine });

    /// <summary>A knife this PC judged went into a body: which, and where on which bone.</summary>
    internal void ShareBody(ThrownKnife knife, Transform bone, byte boneIndex, ulong target)
    {
        var note = About(NoteKind.Land, knife);
        note.Flag = (byte)LandOn.Body;
        note.Target = target;
        note.Bone = boneIndex;
        if (bone != null)
        {
            note.Local = bone.InverseTransformPoint(knife.transform.position);
            note.LocalTurn = Quaternion.Inverse(bone.rotation) * knife.transform.rotation;
        }
        Share(note);
    }

    /// <summary>A knife this PC judged stuck into a wall.</summary>
    internal void ShareWorld(ThrownKnife knife)
    {
        var note = About(NoteKind.Land, knife);
        note.Flag = (byte)LandOn.World;
        Share(note);
    }

    /// <summary>A knife this PC judged glanced off something, at this speed and spin.</summary>
    internal void ShareBounce(ThrownKnife knife, Vector3 linear, Vector3 angular)
    {
        var note = About(NoteKind.Bounce, knife);
        note.Velocity = linear;
        note.Spin = angular;
        Share(note);
    }

    /// <summary>A knife this PC judged has come to lie still.</summary>
    internal void ShareRest(ThrownKnife knife) => Share(About(NoteKind.Rest, knife));

    private Note About(NoteKind kind, ThrownKnife knife) => new Note
    {
        Kind = (byte)kind, Id = knife.Id, Who = Mine, Bone = ThrownKnife.NoBone,
        Position = knife.transform.position, Rotation = knife.transform.rotation
    };

    // ---- Notes between PCs ----

    // This PC's own: a guest's goes to the host, the host's to every guest.
    private void Share(in Note note)
    {
        if (!InRoom || network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Note>(), Allocator.Temp);
        writer.WriteValueSafe(note);
        if (!network.IsServer) { network.CustomMessagingManager.SendNamedMessage(Message, NetworkManager.ServerClientId, writer); return; }
        ToGuests(writer, NetworkManager.ServerClientId);
    }

    // On the host: every guest but the one it came from (the host itself never sends to itself).
    private void ToGuests(FastBufferWriter writer, ulong except)
    {
        targets.Clear();
        foreach (ulong client in network.ConnectedClientsIds)
            if (client != network.LocalClientId && client != except) targets.Add(client);
        if (targets.Count > 0) network.CustomMessagingManager.SendNamedMessage(Message, targets, writer);
    }

    private void TellOne(ulong client, in Note note)
    {
        if (!InRoom || network.CustomMessagingManager == null || client == network.LocalClientId) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Note>(), Allocator.Temp);
        writer.WriteValueSafe(note);
        network.CustomMessagingManager.SendNamedMessage(Message, client, writer);
    }

    private void Receive(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Note note);
        if (network == null) return;
        if (network.IsServer)
        {
            // A guest asking for a knife, or telling what its player's knife did: the host plays it and passes it on.
            if ((NoteKind)note.Kind == NoteKind.Ask) { Take(note.Id, sender); return; }
            if ((NoteKind)note.Kind == NoteKind.Gone) return;
            note.Who = sender;
            Apply(note);
            using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Note>(), Allocator.Temp);
            writer.WriteValueSafe(note);
            ToGuests(writer, sender);
            return;
        }
        if (sender == NetworkManager.ServerClientId) Apply(note);
    }

    private void Apply(in Note note)
    {
        switch ((NoteKind)note.Kind)
        {
            case NoteKind.Hand:
            {
                var who = PlayerKnives.Of(PlayerOf(note.Who));
                if (who != null) who.ShowDrawn(note.Flag != 0);
                break;
            }
            case NoteKind.Throw:
            {
                var thrower = PlayerOf(note.Who);
                var hand = PlayerKnives.Of(thrower);
                if (hand != null) hand.ShowThrown(note.Position);
                if (!live.TryGetValue(note.Id, out var known) || known == null)
                    live[note.Id] = ThrownKnife.Make(this, note.Id, thrower, false, note.Position, note.Position, note.Velocity);
                break;
            }
            case NoteKind.Land:
                if (note.Flag == (byte)LandOn.Body) PutInBody(Copy(note), note);
                else Copy(note).PutInWall(note.Position, note.Rotation);
                break;
            case NoteKind.Bounce:
                Copy(note).LetGo(note.Position, note.Rotation, note.Velocity, note.Spin);
                break;
            case NoteKind.Rest:
                Copy(note).LayDown(note.Position, note.Rotation);
                break;
            case NoteKind.Gone:
                Remove(note.Id, note.Flag != 0, note.Who);
                break;
        }
    }

    // This PC's copy of the knife a note is about; one is made where the note says if it never flew here.
    private ThrownKnife Copy(in Note note)
    {
        if (live.TryGetValue(note.Id, out var knife) && knife != null) return knife;
        knife = ThrownKnife.Make(this, note.Id, PlayerOf(note.Who), false, note.Position, note.Position, Vector3.zero);
        knife.transform.rotation = note.Rotation;
        live[note.Id] = knife;
        return knife;
    }

    // The thrower's word that it went into a body: the same bone of this PC's copy of that body, living or a corpse.
    private void PutInBody(ThrownKnife knife, in Note note)
    {
        Transform root = null, bone = null;
        EnemyHealth enemy = null;
        Animator animator = null;
        if (note.Target != 0UL && network != null && network.SpawnManager != null
            && network.SpawnManager.SpawnedObjects.TryGetValue(note.Target, out NetworkObject found) && found != null)
        {
            root = found.transform;
            enemy = found.GetComponent<EnemyHealth>();
            var zombie = found.GetComponent<ZombieBody>();
            if (zombie != null && zombie.Model != null) animator = zombie.Model.GetComponentInChildren<Animator>();
        }
        // Dead here already: its body lies as its corpse.
        if (animator == null && note.Target != 0UL) animator = ZombieCorpse.AnimatorOf(note.Target);
        if (note.Bone != ThrownKnife.NoBone && animator != null && animator.isHuman) bone = animator.GetBoneTransform((HumanBodyBones)note.Bone);
        if (bone != null) { knife.PutInBody(bone, bone.TransformPoint(note.Local), bone.rotation * note.LocalTurn, enemy); return; }
        if (root != null) { knife.PutInBody(root, note.Position, note.Rotation, enemy); return; }
        // Its body is gone here: the knife drops from where the thrower saw it go in.
        knife.LetGo(note.Position, note.Rotation, Vector3.zero, Vector3.zero);
    }

    // ---- Taking one back ----

    private void Update()
    {
        Aim();
        ShowPrompt();
        if (targeted == null || refusal != null || interact == null || !interact.WasPressedThisFrame()) return;
        pressedAt = Time.unscaledTime;
        uint id = targeted.Id;
        if (IsHost) { Take(id, Mine); return; }
        if (asked.TryGetValue(id, out float when) && Time.unscaledTime - when < AskAgain) return;
        asked[id] = Time.unscaledTime;
        if (network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Note>(), Allocator.Temp);
        writer.WriteValueSafe(new Note { Kind = (byte)NoteKind.Ask, Id = id, Who = Mine });
        network.CustomMessagingManager.SendNamedMessage(Message, NetworkManager.ServerClientId, writer);
    }

    // The knife within reach nearest the middle of the view, unless something else has F or the hands.
    private void Aim()
    {
        targeted = null;
        refusal = null;
        var mine = PlayerRoster.Local;
        if (mine == null || !Free(mine) || FieldPickups.OwnTarget) return;
        var eyes = PlayerView.EyesOf(mine);
        if (eyes == null) return;
        float best = lookAngle;
        foreach (var knife in live.Values)
        {
            if (knife == null || !knife.CanBeTaken) continue;
            Vector3 toward = knife.GripPoint - eyes.position;
            if (toward.sqrMagnitude > reach * reach) continue;
            float angle = Vector3.Angle(eyes.forward, toward);
            if (angle > best) continue;
            best = angle;
            targeted = knife;
        }
        var knives = PlayerKnives.Of(mine);
        if (targeted != null && knives != null && knives.Full) refusal = Texts.Get("pickup.refuse.knivesfull");
    }

    private static bool Free(PlayerMovement mine)
    {
        if (!mine.IsInitialized || mine.IsDead || mine.IsDowned || Cursor.lockState != CursorLockMode.Locked) return false;
        if (ShopMenu.TakesKeyboard || PerkCardScreen.TakesKeyboard || GunCustomizeMenu.TakesKeyboard || DevConsole.TakesKeyboard) return false;
        if (mine.Execution != null && mine.Execution.Candidate != null) return false;
        if (mine.TryGetComponent(out PlayerRevive revive) && revive.Candidate != null) return false;
        // At a ladder's top F takes the player down it (LadderPromptHUD).
        if (mine.Abilities != null && mine.Abilities.Fixtures.ClimbDownLadder != null) return false;
        return !(mine.TryGetComponent(out PlayerWeaponSlots slots) && slots.HandsFull);
    }

    // On the host: client takes knife id if it is still there to take and they are near it. First come, first served.
    private void Take(uint id, ulong client)
    {
        if (!live.TryGetValue(id, out var knife) || knife == null)
        {
            // Gone here already (taken, its time up, gone with its body): the asker's copy goes too.
            TellOne(client, new Note { Kind = (byte)NoteKind.Gone, Id = id });
            return;
        }
        var who = PlayerOf(client);
        if (!knife.CanBeTaken || who == null || who.IsDead || who.IsDowned) return;
        float far = reach + (client == Mine ? 0f : ReachSlack);
        if ((knife.GripPoint - EyesOf(who)).sqrMagnitude > far * far) return;
        var gone = new Note { Kind = (byte)NoteKind.Gone, Id = id, Flag = 1, Who = client };
        Apply(gone);
        Share(gone);
    }

    // Taken (by taker), or gone on the host: off every PC; the taker's own PC adds it to their count.
    private void Remove(uint id, bool taken, ulong taker)
    {
        asked.Remove(id);
        bool fromWall = false;
        if (live.TryGetValue(id, out var knife))
        {
            live.Remove(id);
            if (knife != null)
            {
                fromWall = knife.IsStuck;
                if (taken && taker != Mine)
                    Play(Sounds != null ? (fromWall ? Sounds.draw : Sounds.pickup) : null, knife.transform.position, .4f);
                knife.Taken();
            }
        }
        if (!taken || taker != Mine) return;
        var knives = PlayerKnives.Local;
        if (knives != null) knives.TakeBack(fromWall);
    }

    /// <summary>A knife came to rest: past the most lying about, the oldest goes.</summary>
    internal void Landed(ThrownKnife knife)
    {
        int lying = 0;
        ThrownKnife oldest = null;
        foreach (var other in live.Values)
        {
            if (other == null || other.Now == ThrownKnife.State.Flying || other.IsGoing) continue;
            lying++;
            if (other != knife && (oldest == null || other.LandedAt < oldest.LandedAt)) oldest = other;
        }
        if (lying > Mathf.Max(1, mostLying) && oldest != null) oldest.Go();
    }

    /// <summary>A knife is gone from this PC (taken, its time up, or gone with what it was in).</summary>
    internal void Forget(ThrownKnife knife)
    {
        if (knife != null && live.TryGetValue(knife.Id, out var known) && ReferenceEquals(known, knife)) live.Remove(knife.Id);
        if (ReferenceEquals(targeted, knife)) targeted = null;
    }

    // ---- The [F] prompt, as a field pickup's (FieldPickups) ----

    private void ShowPrompt()
    {
        bool shown = targeted != null;
        if (!shown && promptHost == null) return;
        Build();
        if (promptRoot.gameObject.activeSelf != shown) promptRoot.gameObject.SetActive(shown);
        if (!shown) return;
        bool flash = Time.unscaledTime - pressedAt < KeyFlash;
        keyBox.color = refusal != null ? KitStyle.With(KitStyle.Ice, .45f) : flash ? KitStyle.Accent : KitStyle.Ice;
        string key = KeyBindings.Name(interact);
        if (keyLabel.text != key) keyLabel.text = key;
        SetText(nameLabel, Texts.Get("knife.name"));
        SetText(amountLabel, refusal ?? Texts.Format("pickup.gives.knife", 1));
        amountLabel.color = refusal != null ? KitStyle.Soft : KitStyle.Cyan;
        underline.color = refusal != null ? KitStyle.Faint : KitStyle.Cyan;
    }

    private static void SetText(TMP_Text label, string text)
    {
        if (label.text != text) label.text = text;
    }

    private void Build()
    {
        if (promptHost != null) return;
        promptHost = new GameObject("Knife Prompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        promptHost.transform.SetParent(transform, false);
        var canvas = promptHost.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 85;
        var scaler = promptHost.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        // A key square, then a dark plate with the name and what it gives over a line of its colour, just below and
        // right of the crosshair: where a field pickup's prompt stands.
        promptRoot = FeelUI.Rect("Prompt", promptHost.transform);
        promptRoot.anchoredPosition = new Vector2(70f, -50f);
        promptRoot.localRotation = Quaternion.Euler(0f, 0f, 5f);
        keyBox = Panel("Key", promptRoot, new Vector2(0f, 0f), new Vector2(52f, 52f), KitStyle.Ice);
        keyLabel = FeelUI.ScoreLabel("Key Name", keyBox.rectTransform, 34f, TextAlignmentOptions.Center);
        Fill(keyLabel.rectTransform, Vector2.zero, Vector2.zero);
        keyLabel.color = KitStyle.Ink;
        var plate = Panel("Plate", promptRoot, new Vector2(58f, -4f), new Vector2(270f, 44f), KitStyle.With(KitStyle.Night, .82f));
        underline = Panel("Line", promptRoot, new Vector2(58f, -44f), new Vector2(270f, 4f), KitStyle.Cyan);
        nameLabel = FeelUI.ScoreLabel("Name", plate.rectTransform, 24f, TextAlignmentOptions.Left);
        Fill(nameLabel.rectTransform, new Vector2(14f, 0f), new Vector2(-110f, 0f));
        amountLabel = FeelUI.ScoreLabel("Gives", plate.rectTransform, 20f, TextAlignmentOptions.Right);
        Fill(amountLabel.rectTransform, new Vector2(120f, 0f), new Vector2(-12f, 0f));
        promptRoot.gameObject.SetActive(false);
    }

    // A box with its top-left corner at `at` (y down from the parent's middle).
    private static Image Panel(string name, RectTransform parent, Vector2 at, Vector2 size, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        var rect = image.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(at.x, -at.y);
        rect.sizeDelta = size;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Fill(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }

    // ---- What a knife looks like ----

    /// <summary>
    /// Puts the knife's model under <paramref name="root"/>, its middle on the root and its tip along the root's +Z, the
    /// flat of its blade facing +X, at the table's length: the library's model, else one built from boxes.
    /// </summary>
    internal void Dress(Transform root)
    {
        var made = new GameObject("Model").transform;
        made.gameObject.layer = root.gameObject.layer;
        made.SetParent(root, false);
        if (!Shape())
        {
            Boxes(made);
            return;
        }
        float scale = Mathf.Max(.05f, length) / shapeLength;
        Quaternion turn = flipModel ? Quaternion.AngleAxis(180f, Vector3.up) * shapeTurn : shapeTurn;
        made.localRotation = turn;
        made.localScale = Vector3.one * scale;
        made.localPosition = -(turn * (shapeCenter * scale));
        made.gameObject.AddComponent<MeshFilter>().sharedMesh = shapeMesh;
        made.gameObject.AddComponent<MeshRenderer>().sharedMaterials = shapeMaterials;
    }

    // The library model's mesh, read once: its longest side becomes the length (+Z), its thinnest the flat (+X).
    private static bool Shape()
    {
        if (shapeLooked) return shapeMesh != null;
        shapeLooked = true;
        var library = ThrowingKnifeLibrary.Instance;
        var filter = library != null && library.model != null ? library.model.GetComponentInChildren<MeshFilter>(true) : null;
        var renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
        if (filter == null || filter.sharedMesh == null || renderer == null) return false;
        var bounds = filter.sharedMesh.bounds;
        Vector3 size = bounds.size;
        int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        int thinnest = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        if (thinnest == longest) thinnest = (longest + 1) % 3;
        int across = 3 - longest - thinnest;
        if (size[longest] <= 1e-5f) return false;
        Vector3 along = Vector3.zero, wide = Vector3.zero;
        along[longest] = 1f;
        wide[across] = 1f;
        shapeTurn = Quaternion.Inverse(Quaternion.LookRotation(along, wide));
        shapeCenter = bounds.center;
        shapeLength = size[longest];
        shapeMesh = filter.sharedMesh;
        shapeMaterials = renderer.sharedMaterials;
        return true;
    }

    // No model: a blade, a guard and a grip from boxes, at the table's length.
    private void Boxes(Transform made)
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        float total = Mathf.Max(.05f, length);
        Part("Blade", made, cube, Steel, new Vector3(.005f, .032f, total * .6f), new Vector3(0f, 0f, total * .2f));
        Part("Guard", made, cube, Steel, new Vector3(.012f, .05f, .012f), new Vector3(0f, 0f, -total * .1f));
        Part("Grip", made, cube, Grip, new Vector3(.018f, .026f, total * .38f), new Vector3(0f, 0f, -total * .31f));
    }

    private static void Part(string name, Transform parent, Mesh mesh, Material material, Vector3 size, Vector3 at)
    {
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.layer = parent.gameObject.layer;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = at;
        part.transform.localScale = size;
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static Material Steel => steel != null ? steel : steel = Paint("Knife Steel", new Color(.62f, .64f, .66f), .8f, .7f);
    private static Material Grip => grip != null ? grip : grip = Paint("Knife Grip", new Color(.08f, .08f, .09f), 0f, .25f);

    private static Material Paint(string name, Color color, float metal, float smooth)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metal);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smooth);
        return material;
    }

    // ---- Sound ----

    /// <summary>One of these clips out in the world, at the combat channel's volume.</summary>
    internal void Play(AudioClip[] clips, Vector3 at, float level, float pitch = 1f)
    {
        var library = Sounds;
        var clip = library != null ? library.Pick(clips) : null;
        if (clip == null || level <= 0f) return;
        var host = new GameObject("Knife Sound");
        host.transform.position = at;
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 1.5f;
        source.maxDistance = 45f;
        source.dopplerLevel = 0f;
        source.pitch = pitch;
        GameAudio.Bind(source, AudioChannel.Combat);
        source.PlayOneShot(clip, Mathf.Clamp01(level * volume));
        Destroy(host, clip.length / Mathf.Max(.1f, pitch) + .1f);
    }

    // ---- Helpers ----

    private PlayerMovement PlayerOf(ulong client)
    {
        if (!InRoom) return PlayerRoster.Local;
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.TryGetComponent(out NetworkObject owner) && owner.IsSpawned && owner.OwnerClientId == client) return someone;
        return null;
    }

    private static Vector3 EyesOf(PlayerMovement player)
    {
        var eyes = PlayerView.EyesOf(player);
        return eyes != null ? eyes.position : player.transform.position + Vector3.up * 1.5f;
    }
}
