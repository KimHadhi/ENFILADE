using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Field pickups (stage 6D): guns, ammo, medkits, shield cells and money lying in a map. What lies at each PickupSpot is
/// rolled from Tables/Pickups when the map starts, and a dying zombie sometimes drops something too (Tables/PickupRules:
/// dropChance; drops vanish after dropSeconds). The user's rules (2026-11-09): ammo and money are taken by walking over
/// them (touchReach), unless this player couldn't use the ammo; a gun, a medkit and a shield cell with [F] while looking
/// at it. Everything goes to whoever takes it first and is gone for everyone. Ammo adds `amount` magazines to every gun
/// held, up to its starting reserve. A gun goes into an empty gun slot, else replaces the gun in hand, which is left on
/// the floor for someone else; a gun already held refills its ammo instead. What was taken is listed at the bottom of
/// the screen (PickupFeedHUD), with a sound of its kind, a blink of the HUD part it went to and a dip of the gun in hand.
/// <para>The host, or the PC alone, keeps every item and tells the others (a named message on change and every couple
/// of seconds, so late joiners catch up); a guest asks the host to take one, and the host tells the taker's PC what it
/// got, which applies it there (its own guns, health, shield and money are its own). The [F] prompt here comes before
/// the campaign's own [F] prompts while a pickup is looked at (CampaignRun reads <see cref="Targeting"/>). Made for
/// every map in Tables/Maps. How they look: PickupView.</para>
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Campaign - 1)]
public sealed partial class FieldPickups : MonoBehaviour
{
    public const string TableName = "Pickups", RulesTable = "PickupRules";
    private const string StateMessage = "ENFILADE.Pickups", AskMessage = "ENFILADE.PickupAsk", GiveMessage = "ENFILADE.PickupGive",
        LeaveMessage = "ENFILADE.PickupLeave";
    // A guest's copy lags: the host lets them reach a little further (m); how far down a dropped thing looks for a floor (m).
    private const float ReachSlack = 1.2f, GroundSearch = 4f, LeaveAhead = .6f;
    // How often an unanswered touch asks again (s); how far above or below an item still counts as over it (m).
    private const float AskAgain = .5f, TouchHeight = 1.6f;
    // The [F] key's flash when pressed (s).
    private const float KeyFlash = .14f;

    public enum Kind : byte { Gun, Ammo, Heal, Shield, Money }

    [SerializeField, Range(0f, 1f), Tooltip("Chance a dying zombie drops something.")]
    private float dropChance = .07f;
    [SerializeField, Min(1f), Tooltip("How long a dropped thing lies before it vanishes (s).")]
    private float dropSeconds = 90f;
    [SerializeField, Min(.5f), Tooltip("How near a pickup must be to take it with [F] (m).")]
    private float reach = 2.2f;
    [SerializeField, Min(.3f), Tooltip("How near ammo or money must be to be taken by walking over it (m).")]
    private float touchReach = 1.3f;
    [SerializeField, Range(5f, 90f), Tooltip("How close to the middle of the view it must be to take it (°).")]
    private float lookAngle = 32f;
    [SerializeField, Min(.2f), Tooltip("How often the host sends everything again (s).")]
    private float shareSeconds = 2f;

    private sealed class Row
    {
        public string Name;
        public Kind Kind;
        public WeaponData Weapon;
        public float Amount, SpotWeight, DropWeight;
    }

    private struct Item : INetworkSerializeByMemcpy
    {
        public ushort Id;
        public byte Row;
        public Vector3 Position;
    }

    private static FieldPickups instance;

    /// <summary>This PC's player is looking at a pickup within reach ([F] is the pickup's).</summary>
    public static bool Targeting => instance != null && instance.targeted >= 0;

    private readonly List<Row> rows = new List<Row>();
    private readonly List<Item> items = new List<Item>();
    private readonly Dictionary<ushort, float> expiry = new Dictionary<ushort, float>();
    private readonly Dictionary<ushort, PickupView> views = new Dictionary<ushort, PickupView>();
    private readonly Dictionary<ushort, float> asked = new Dictionary<ushort, float>();
    private readonly HashSet<ushort> seen = new HashSet<ushort>();
    private readonly List<ushort> gone = new List<ushort>();
    private readonly System.Random random = new System.Random(Environment.TickCount);
    private NetworkManager network;
    private InputAction interact;
    private GameObject promptHost;
    private RectTransform promptRoot;
    private Image keyBox, underline, gunPicture;
    private TMP_Text keyLabel, nameLabel, amountLabel;
    private Transform eye;
    private ushort nextId = 1;
    private int targeted = -1;
    private string refusal;
    private bool changed = true, listening;
    private float nextShare, pressedAt = -99f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Every map gets one; the title screen none.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single || Array.IndexOf(MapTable.All(), scene.name) < 0) return;
        var host = new GameObject("Field Pickups");
        SceneManager.MoveGameObjectToScene(host, scene);
        RuntimeSceneHierarchy.Group(host, RuntimeSceneHierarchy.Systems, "Pickups");
        host.AddComponent<FieldPickups>();
    }

    private bool InRoom => network != null && network.IsListening;
    private bool IsHost => !InRoom || network.IsServer;
    private ulong Mine => InRoom ? network.LocalClientId : 0UL;

    private void Awake()
    {
        instance = this;
        GameTables.Bind(RulesTable, "Default", this);
        LoadRows();
    }

    private void Start()
    {
        network = GameSession.Network;
        interact = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Interact", false) : null;
        Listen();
        if (IsHost) RollSpots();
        CombatDamageEvents.Damaged += OnDamaged;
    }

    private void OnDestroy()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        if (instance == this) instance = null;
        if (promptHost != null) Destroy(promptHost);
        foreach (var view in views.Values) if (view != null) Destroy(view.gameObject);
        if (!listening || network == null || network.CustomMessagingManager == null) return;
        var messages = network.CustomMessagingManager;
        if (network.IsServer)
        {
            messages.UnregisterNamedMessageHandler(AskMessage);
            messages.UnregisterNamedMessageHandler(LeaveMessage);
        }
        else
        {
            messages.UnregisterNamedMessageHandler(StateMessage);
            messages.UnregisterNamedMessageHandler(GiveMessage);
        }
    }

    private void Listen()
    {
        if (!InRoom || network.CustomMessagingManager == null) return;
        listening = true;
        var messages = network.CustomMessagingManager;
        if (network.IsServer)
        {
            messages.RegisterNamedMessageHandler(AskMessage, ReceiveAsk);
            messages.RegisterNamedMessageHandler(LeaveMessage, ReceiveLeave);
        }
        else
        {
            messages.RegisterNamedMessageHandler(StateMessage, ReceiveState);
            messages.RegisterNamedMessageHandler(GiveMessage, ReceiveGive);
        }
    }

    /// <summary>Ammo and money are taken by walking over them; the rest with [F].</summary>
    private static bool Walked(Kind kind) => kind == Kind.Ammo || kind == Kind.Money;

    // ---- Helpers ----

    private int IndexOf(ushort id)
    {
        for (int i = 0; i < items.Count; i++) if (items[i].Id == id) return i;
        return -1;
    }

    // Down onto whatever is under it, players (layer 31) and enemies aside.
    private static Vector3 Ground(Vector3 at)
    {
        int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Enemy") & ~(1 << 31);
        return Physics.Raycast(at + Vector3.up * .5f, Vector3.down, out RaycastHit hit, GroundSearch, mask, QueryTriggerInteraction.Ignore)
            ? hit.point : at;
    }
}
