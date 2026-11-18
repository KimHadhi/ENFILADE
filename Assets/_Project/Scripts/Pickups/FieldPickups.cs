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
/// dropChance; drops vanish after dropSeconds). In a campaign assault the dead drop more often (Phases.csv drops) and
/// their drops' time runs only once it is over (CampaignPhases). The user's rules (2026-11-09): ammo and money are taken by walking over
/// them (touchReach), unless this player couldn't use the ammo; a gun, a medkit and a shield cell with [F] while looking
/// at it. Everything goes to whoever takes it first and is gone for everyone. Ammo adds `amount` magazines to every gun
/// held, up to the reserve it is filled to. A gun on the ground is a particular gun (GunState: the user, 2026-11-18,
/// "필드 무기 교체 및 탄약 시스템 개편"): it lies there with the rounds in it, and one the map laid out or a zombie
/// dropped also with its starting reserve beside it for its first taker. Taken, it goes into an empty gun slot, else
/// in place of the gun in hand, which is left on the floor as it is for someone else; the reserve is the player's and
/// stays with them (PlayerWeaponSlots.Put). A gun of a kind already held is not taken: its rounds go into that kind's
/// reserve as far as there is room, and it stays where it lay with the rest. What was taken is listed at the bottom of
/// the screen (PickupFeedHUD), with a sound of its kind, a blink of the HUD part it went to and a dip of the gun in hand.
/// <para>The host, or the PC alone, keeps every item and tells the others (a named message on change and every couple
/// of seconds, so late joiners catch up); a guest asks the host to take one, and the host hands the taker's PC the item
/// as it lay, which applies it there (its own guns, rounds, health, shield and money are its own to count; a gun it
/// puts down in turn is told back to the host as it is). The [F] prompt here comes before
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
        // A gun the map lays out or a zombie drops: the rounds in it and the rounds lying with it (below 0: a full
        // magazine, the gun's own starting reserve).
        public int Rounds = -1, Spare = -1;
    }

    // What lies somewhere. A supply is a row of Tables/Pickups. A gun is a particular gun (GunState): which one (its
    // number in the game's gun list, so any gun can lie there, with or without a row), the rounds in it, the rounds
    // lying with it that are nobody's yet, and how a cut-short reload left it. Before, a gun on the ground was only its
    // row, so what was in it was lost when it was put down and made anew, full, when it was taken up.
    private struct Item : INetworkSerializeByMemcpy
    {
        public ushort Id;
        public byte Row;
        public Vector3 Position;
        public ushort Gun, Rounds, Spare;
        public byte Flags;
    }

    // Item.Row of a gun: it is told by Item.Gun. Item.Flags of a gun: its magazine is out, its bolt is still to work,
    // and the reload that left it so was one from empty.
    private const byte NoRow = 255, MagazineOutFlag = 1, ChamberDueFlag = 2, ResumeEmptyFlag = 4;

    private static FieldPickups instance;

    /// <summary>This PC's player is looking at a pickup within reach ([F] is the pickup's).</summary>
    public static bool Targeting => instance != null && instance.targeted >= 0;

    private readonly List<Row> rows = new List<Row>();
    // What is shown and said of each kind of gun: its row of the table when it has one, else one made of the gun itself.
    private readonly Dictionary<WeaponData, Row> gunRows = new Dictionary<WeaponData, Row>();
    private readonly List<Item> items = new List<Item>();
    private readonly Dictionary<ushort, float> expiry = new Dictionary<ushort, float>();
    // The host's clock the drops run out by (Expire): it stands still through a campaign assault.
    private float dropClock;
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

    // What an item is: a gun's own row (GunRow), a supply's row of the table; null for one this PC can't make sense of
    // (a gun its list doesn't have, a row its table doesn't have).
    private Row RowOf(in Item item)
    {
        if (item.Gun != 0) return GunRow(GunList.Of(item.Gun));
        return item.Row < rows.Count ? rows[item.Row] : null;
    }

    // The gun an item is, as it lies there.
    private static GunState StateOf(in Item item) => new GunState(GunList.Of(item.Gun), item.Rounds, item.Spare,
        (item.Flags & MagazineOutFlag) != 0, (item.Flags & ChamberDueFlag) != 0, (item.Flags & ResumeEmptyFlag) != 0);

    // A gun as an item to lie at a place (its id is the host's to give).
    private static Item ItemOf(in GunState gun, Vector3 at) => new Item
    {
        Row = NoRow, Position = at, Gun = GunList.IdOf(gun.Weapon),
        Rounds = (ushort)Mathf.Clamp(gun.Rounds, 0, ushort.MaxValue), Spare = (ushort)Mathf.Clamp(gun.Spare, 0, ushort.MaxValue),
        Flags = (byte)((gun.MagazineOut ? MagazineOutFlag : 0) | (gun.ChamberDue ? ChamberDueFlag : 0) | (gun.ResumeEmpty ? ResumeEmptyFlag : 0)),
    };

    /// <summary>
    /// This PC's own player puts the gun in that slot down where they stand, as it is: the rounds in it go with it, the
    /// reserve stays theirs (PlayerWeaponSlots.Drop), and it lies there for anyone. The one call a drop is, for the Tab
    /// menu's drop to come (the user, 2026-11-18: "추후 Tab 메뉴에서 주무기/보조무기 드랍(Drop) 기능을 추가할 예정");
    /// the console's /drop uses it now. False when there is no such gun to put down, or no map to put it in.
    /// </summary>
    public static bool Drop(PlayerMovement player, int slot)
    {
        if (instance == null || player == null || !player.TryGetComponent(out PlayerWeaponSlots slots) || !slots.Drop(slot, out GunState gun)) return false;
        instance.Leave(gun, player.transform.position + player.transform.forward * LeaveAhead);
        return true;
    }

    // Down onto whatever is under it, players (layer 31) and enemies aside.
    private static Vector3 Ground(Vector3 at)
    {
        int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Enemy") & ~(1 << 31);
        return Physics.Raycast(at + Vector3.up * .5f, Vector3.down, out RaycastHit hit, GroundSearch, mask, QueryTriggerInteraction.Ignore)
            ? hit.point : at;
    }
}
