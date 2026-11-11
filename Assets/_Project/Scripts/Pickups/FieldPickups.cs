using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using Unity.Collections;
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
public sealed class FieldPickups : MonoBehaviour
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

    /// <summary>This PC's player is looking at a pickup within reach ([F] is the pickup's), a thrown knife's included (ThrowingKnives).</summary>
    public static bool Targeting => OwnTarget || ThrowingKnives.Targeting;
    /// <summary>A pickup of this list is looked at: a thrown knife waits its turn behind it.</summary>
    internal static bool OwnTarget => instance != null && instance.targeted >= 0;

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
    private Image keyBox, underline;
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

    // ---- The table ----

    private void LoadRows()
    {
        var guns = new Dictionary<string, WeaponData>(StringComparer.OrdinalIgnoreCase);
        var assets = FPSBodyAssets.Instance;
        if (assets != null)
            foreach (var entry in assets.guns)
                if (entry.weapon != null && !guns.ContainsKey(entry.weapon.name)) guns[entry.weapon.name] = entry.weapon;
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row) || !Enum.TryParse(Text(row, "kind"), true, out Kind kind)) continue;
            WeaponData weapon = null;
            if (kind == Kind.Gun && !guns.TryGetValue(Text(row, "weapon"), out weapon)) continue;
            string name = Text(row, "name");
            if (string.IsNullOrEmpty(name)) name = weapon != null ? weapon.DisplayName : id;
            rows.Add(new Row
            {
                Name = name, Kind = kind, Weapon = weapon, Amount = Number(row, "amount", 0f),
                SpotWeight = Mathf.Max(0f, Number(row, "spot", 0f)), DropWeight = Mathf.Max(0f, Number(row, "drop", 0f))
            });
            if (rows.Count >= 255) break;
        }
    }

    private int Pick(Func<Row, bool> allowed, Func<Row, float> weight)
    {
        float sum = 0f;
        for (int i = 0; i < rows.Count; i++) if (allowed(rows[i])) sum += weight(rows[i]);
        if (sum <= 0f) return -1;
        float pick = (float)random.NextDouble() * sum;
        for (int i = 0; i < rows.Count; i++)
        {
            if (!allowed(rows[i])) continue;
            pick -= weight(rows[i]);
            if (pick <= 0f) return i;
        }
        return -1;
    }

    private int RowOf(WeaponData weapon)
    {
        for (int i = 0; i < rows.Count; i++) if (rows[i].Kind == Kind.Gun && rows[i].Weapon == weapon) return i;
        return -1;
    }

    // ---- The host's side ----

    private void RollSpots()
    {
        foreach (var spot in FindObjectsByType<PickupSpot>(FindObjectsSortMode.None))
        {
            var roll = spot.Kind;
            int row = Pick(r => r.SpotWeight > 0f && (roll == PickupSpot.Roll.Any || (roll == PickupSpot.Roll.Gun) == (r.Kind == Kind.Gun)),
                r => r.SpotWeight);
            if (row >= 0) Add(row, spot.transform.position, 0f);
        }
    }

    private void Add(int row, Vector3 at, float life)
    {
        var item = new Item { Id = nextId++, Row = (byte)row, Position = at };
        if (nextId == 0) nextId = 1;
        items.Add(item);
        if (life > 0f) expiry[item.Id] = Time.time + life;
        changed = true;
    }

    // A real enemy's death on the host sometimes leaves something where it fell.
    private void OnDamaged(CombatDamageReport report)
    {
        if (!IsHost || !report.IsKillingBlow || !(report.Target is EnemyHealth) || random.NextDouble() >= dropChance) return;
        int row = Pick(r => r.DropWeight > 0f, r => r.DropWeight);
        if (row >= 0) Add(row, Ground(report.Target.transform.position), dropSeconds);
    }

    private void Expire()
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (!expiry.TryGetValue(items[i].Id, out float until) || Time.time < until) continue;
            expiry.Remove(items[i].Id);
            items.RemoveAt(i);
            changed = true;
        }
    }

    /// <summary>
    /// On the host: <paramref name="who"/> (client <paramref name="client"/>) takes item <paramref name="id"/> if it is
    /// still there and they are near it. First come, first served.
    /// </summary>
    private bool Take(ushort id, ulong client, PlayerMovement who)
    {
        int index = IndexOf(id);
        if (index < 0 || who == null || who.IsDead || who.IsDowned) return false;
        var item = items[index];
        float far = reach + ReachSlack;
        if ((item.Position - who.transform.position).sqrMagnitude > far * far) return false;
        items.RemoveAt(index);
        expiry.Remove(id);
        changed = true;
        if (client == Mine) Give(item.Row);
        else SendGive(client, item.Row);
        return true;
    }

    private void Share()
    {
        if (!InRoom || !network.IsServer || network.CustomMessagingManager == null) return;
        if (!changed && Time.unscaledTime < nextShare) return;
        changed = false;
        nextShare = Time.unscaledTime + shareSeconds;
        using var writer = new FastBufferWriter(sizeof(ushort) + items.Count * FastBufferWriter.GetWriteSize<Item>(), Allocator.Temp);
        writer.WriteValueSafe((ushort)items.Count);
        foreach (var item in items) writer.WriteValueSafe(item);
        network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer);
    }

    private void ReceiveAsk(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ushort id);
        Take(id, sender, PlayerOf(sender));
    }

    private void ReceiveLeave(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte row);
        reader.ReadValueSafe(out Vector3 at);
        if (row < rows.Count) Add(row, Ground(at), 0f);
    }

    private void SendGive(ulong client, byte row)
    {
        if (network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe(row);
        network.CustomMessagingManager.SendNamedMessage(GiveMessage, client, writer);
    }

    // ---- A guest's side ----

    private void ReceiveState(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ushort count);
        items.Clear();
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out Item item);
            if (item.Row < rows.Count) items.Add(item);
        }
    }

    private void ReceiveGive(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte row);
        Give(row);
    }

    private void SendAsk(ushort id)
    {
        if (network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(sizeof(ushort), Allocator.Temp);
        writer.WriteValueSafe(id);
        network.CustomMessagingManager.SendNamedMessage(AskMessage, NetworkManager.ServerClientId, writer);
    }

    // A gun swapped out goes back on the floor for anyone.
    private void Leave(int row, Vector3 at)
    {
        if (row < 0) return;
        if (IsHost) { Add(row, Ground(at), 0f); return; }
        if (network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(sizeof(byte) + FastBufferWriter.GetWriteSize<Vector3>(), Allocator.Temp);
        writer.WriteValueSafe((byte)row);
        writer.WriteValueSafe(at);
        network.CustomMessagingManager.SendNamedMessage(LeaveMessage, NetworkManager.ServerClientId, writer);
    }

    // ---- This PC's player ----

    private void Update()
    {
        if (IsHost)
        {
            Expire();
            Share();
        }
        Touch();
        Aim();
        ShowPrompt();
        if (targeted < 0 || refusal != null || interact == null || !interact.WasPressedThisFrame()) return;
        var item = items[targeted];
        pressedAt = Time.unscaledTime;
        if (IsHost) Take(item.Id, Mine, PlayerRoster.Local);
        else SendAsk(item.Id);
    }

    // Ammo and money under this PC's player: ask for them (the host just takes them), again now and then until answered.
    private void Touch()
    {
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.IsInitialized || mine.IsDead || mine.IsDowned) return;
        Vector3 at = mine.transform.position;
        float now = Time.unscaledTime;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var row = rows[item.Row];
            if (!Walked(row.Kind)) continue;
            Vector3 off = item.Position - at;
            if (Mathf.Abs(off.y) > TouchHeight) continue;
            off.y = 0f;
            if (off.sqrMagnitude > touchReach * touchReach) continue;
            if (asked.TryGetValue(item.Id, out float when) && now - when < AskAgain) continue;
            // Ammo this player can't use stays for someone who can.
            if (Refuse(mine, row) != null) continue;
            asked[item.Id] = now;
            if (!IsHost) { SendAsk(item.Id); continue; }
            // The host takes it at once, which changes the list: the rest wait for the next frame.
            if (Take(item.Id, Mine, mine)) return;
        }
    }

    // The [F] pickup within reach nearest the middle of the view, unless something else has [F] or the hands.
    private void Aim()
    {
        targeted = -1;
        refusal = null;
        eye = null;
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.TryGetComponent(out PlayerBody body) || body.View == null) return;
        eye = body.View;
        if (!Free(mine)) return;
        float best = lookAngle;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (Walked(rows[item.Row].Kind)) continue;
            if ((item.Position - mine.transform.position).sqrMagnitude > reach * reach) continue;
            float angle = Vector3.Angle(eye.forward, item.Position + Vector3.up * .25f - eye.position);
            if (angle > best) continue;
            best = angle;
            targeted = i;
        }
        if (targeted >= 0) refusal = Refuse(mine, rows[items[targeted].Row]);
    }

    private static bool Free(PlayerMovement mine)
    {
        if (!mine.IsInitialized || mine.IsDead || mine.IsDowned || Cursor.lockState != CursorLockMode.Locked) return false;
        if (ShopMenu.TakesKeyboard || PerkCardScreen.TakesKeyboard || GunCustomizeMenu.TakesKeyboard || DevConsole.TakesKeyboard) return false;
        if (mine.Execution != null && mine.Execution.Candidate != null) return false;
        if (mine.TryGetComponent(out PlayerRevive revive) && revive.Candidate != null) return false;
        return !(mine.TryGetComponent(out PlayerWeaponSlots slots) && slots.HandsFull);
    }

    // What stops this player using it now, or null.
    private static string Refuse(PlayerMovement player, Row row)
    {
        var receiver = player.DamageReceiver;
        player.TryGetComponent(out PlayerWeaponSlots slots);
        switch (row.Kind)
        {
            case Kind.Gun:
                if (slots == null || player.CombatInputLocked) return Texts.Get("pickup.refuse.now");
                // Horde's weight limit: the load with this gun where GiveGun would put it (one already held only refills).
                return Heavy(slots, row.Weapon) ? Texts.Format("pickup.refuse.heavy",
                    CarryWeight.Show(CarryWeight.With(slots, GunSlotFor(slots), row.Weapon)), CarryWeight.Show(CarryWeight.Limit)) : null;
            case Kind.Ammo:
                if (slots == null || !HoldsAnyGun(slots)) return Texts.Get("pickup.refuse.nogun");
                // Short of throwing knives, a pile is worth taking for them alone (PlayerKnives.FromAmmo).
                return RoomForAmmo(slots) || PlayerKnives.Short(player) ? null : Texts.Get("pickup.refuse.ammofull");
            case Kind.Heal: return receiver.Health >= receiver.MaximumHealth - .5f ? Texts.Get("pickup.refuse.healthfull") : null;
            case Kind.Shield: return receiver.Shield >= row.Amount - .5f ? Texts.Get("pickup.refuse.shield") : null;
        }
        return null;
    }

    // On the taker's own PC: what it got.
    private void Give(int index)
    {
        var player = PlayerRoster.Local;
        if (player == null || index < 0 || index >= rows.Count) return;
        var row = rows[index];
        var receiver = player.DamageReceiver;
        player.TryGetComponent(out PlayerWeaponSlots slots);
        switch (row.Kind)
        {
            case Kind.Gun:
                GiveGun(player, slots, row);
                break;
            case Kind.Ammo:
                int magazines = Mathf.Max(1, Mathf.RoundToInt(row.Amount));
                if (slots != null) AddMagazines(slots, magazines);
                Pop(row.Kind, row.Name, Texts.Format("pickup.gives.ammo", magazines));
                PlayerKnives.FromAmmo(player);
                break;
            case Kind.Heal:
                receiver.Heal(row.Amount);
                Pop(row.Kind, row.Name, Texts.Format("pickup.gives.heal", Mathf.RoundToInt(row.Amount)));
                break;
            case Kind.Shield:
                receiver.SetShield(Mathf.Max(receiver.Shield, row.Amount));
                Pop(row.Kind, row.Name, Texts.Format("pickup.gives.shield", Mathf.RoundToInt(row.Amount)));
                break;
            case Kind.Money:
                Wallet.Receive((long)row.Amount);
                Pop(row.Kind, row.Name, "+" + Wallet.Format((long)row.Amount));
                break;
        }
    }

    // Into an empty gun slot, else in place of the gun in hand (left on the floor), else the first gun slot. A gun
    // already held fills its ammo instead.
    private void GiveGun(PlayerMovement player, PlayerWeaponSlots slots, Row row)
    {
        if (slots == null || row.Weapon == null) return;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            // Held twice as a dual pistol (AkimboGuns) counts as held.
            var held = slots.GetWeapon(i);
            if (held != row.Weapon && (held == null || AkimboGuns.SingleOf(held) != row.Weapon)) continue;
            slots.RefillAmmo();
            Pop(row.Kind, row.Name, Texts.Get("pickup.gives.refill"));
            return;
        }
        int slot = GunSlotFor(slots);
        // Asked for within the weight limit, but the load grew before the answer came: it goes back on the floor.
        if (!CarryWeight.Fits(slots, slot, row.Weapon))
        {
            KitSound.Play(KitSound.Cue.Refuse, .5f);
            Leave(RowOf(row.Weapon), player.transform.position + player.transform.forward * LeaveAhead);
            return;
        }
        var before = slots.GetWeapon(slot);
        if (!slots.TrySetWeapon(slot, row.Weapon)) return;
        if (!slots.HandsFull) slots.SelectSlot(slot);
        Pop(row.Kind, row.Name, Texts.Get("pickup.gives.gun"));
        // A dual pistol has no floor row of its own: it leaves its single gun (AkimboGuns), so a bought pair isn't lost.
        if (before != null && before.Category == WeaponCategory.Ranged)
            Leave(RowOf(AkimboGuns.SingleOf(before) ?? before), player.transform.position + player.transform.forward * LeaveAhead);
    }

    // Each gun held gets this many magazines more, up to the reserve it started with.
    private static void AddMagazines(PlayerWeaponSlots slots, int magazines)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            var data = slots.GetWeapon(i);
            // A Schießhund's Desperado is loaded by bounties alone.
            if (data == null || data.Category != WeaponCategory.Ranged || slots.KeepsOwnRounds(i)) continue;
            var runtime = slots.GetRuntime(i);
            int room = data.InitialReserveAmmo - runtime.ReserveAmmo;
            if (room > 0) runtime.AddReserve(Mathf.Min(room, data.MagazineSize * magazines));
        }
    }

    private static bool RoomForAmmo(PlayerWeaponSlots slots)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            var data = slots.GetWeapon(i);
            if (data != null && data.Category == WeaponCategory.Ranged && !slots.KeepsOwnRounds(i)
                && slots.GetRuntime(i).ReserveAmmo < data.InitialReserveAmmo) return true;
        }
        return false;
    }

    private static int GunSlotFor(PlayerWeaponSlots slots)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
            if (PlayerWeaponSlots.GetSlotCategory(i) == WeaponCategory.Ranged && slots.GetWeapon(i) == null) return i;
        int held = slots.SelectedSlot;
        if (held >= 0 && PlayerWeaponSlots.GetSlotCategory(held) == WeaponCategory.Ranged) return held;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
            if (PlayerWeaponSlots.GetSlotCategory(i) == WeaponCategory.Ranged) return i;
        return 0;
    }

    private static bool HoldsAnyGun(PlayerWeaponSlots slots)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++) if (slots.GetWeapon(i)?.Category == WeaponCategory.Ranged) return true;
        return false;
    }

    // A gun not held yet that would take the load past Horde's weight limit where it would go (CarryWeight).
    private static bool Heavy(PlayerWeaponSlots slots, WeaponData gun)
    {
        if (gun == null) return false;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            // Held, or held twice as a dual pistol (AkimboGuns): GiveGun only refills it, so it weighs nothing new.
            var held = slots.GetWeapon(i);
            if (held == gun || (held != null && AkimboGuns.SingleOf(held) == gun)) return false;
        }
        return !CarryWeight.Fits(slots, GunSlotFor(slots), gun);
    }

    // ---- The [F] prompt (the HUD's kit, beside the crosshair) and what a pickup shows ----

    private void ShowPrompt()
    {
        bool shown = targeted >= 0;
        if (!shown && promptHost == null) return;
        Build();
        if (promptRoot.gameObject.activeSelf != shown) promptRoot.gameObject.SetActive(shown);
        if (!shown) return;
        var row = rows[items[targeted].Row];
        var tint = ColorOf(row.Kind);
        bool flash = Time.unscaledTime - pressedAt < KeyFlash;
        keyBox.color = refusal != null ? KitStyle.With(KitStyle.Ice, .45f) : flash ? KitStyle.Accent : KitStyle.Ice;
        SetText(nameLabel, row.Name);
        SetText(amountLabel, refusal ?? AmountOf(row));
        amountLabel.color = refusal != null ? KitStyle.Soft : tint;
        underline.color = refusal != null ? KitStyle.Faint : tint;
    }

    private static void SetText(TMP_Text label, string text)
    {
        if (label.text != text) label.text = text;
    }

    private static string AmountOf(Row row) => row.Kind switch
    {
        Kind.Gun => Texts.Format("pickup.gives.gun.weight", CarryWeight.Show(CarryWeight.Of(row.Weapon))),
        Kind.Ammo => Texts.Format("pickup.gives.ammo", Mathf.RoundToInt(row.Amount)),
        Kind.Heal => Texts.Format("pickup.gives.heal", Mathf.RoundToInt(row.Amount)),
        Kind.Shield => Texts.Format("pickup.gives.shield", Mathf.RoundToInt(row.Amount)),
        _ => "+" + Wallet.Format((long)row.Amount),
    };

    // What was taken, on the taker's own PC (the user's pick of the pickup drafts, 2026-11-10, draft C, after "줍는 소리,
    // 화면 효과, 줍는 동작, 주울 때 뜨는 글자 다 부족"): a row in the list at the bottom of the screen, its kind's own
    // sound, a blink of the HUD part it went to, and the gun in hand dipping as the body goes for it. Money has no part
    // to blink: its number counts up by itself.
    private static void Pop(Kind kind, string name, string amount)
    {
        Color tint = ColorOf(kind);
        PickupFeedHUD.Show(IconOf(kind), tint, name, amount);
        KitSound.Play(kind switch
        {
            Kind.Gun => KitSound.Cue.PickGun,
            Kind.Ammo => KitSound.Cue.PickAmmo,
            Kind.Heal => KitSound.Cue.PickHeal,
            Kind.Shield => KitSound.Cue.PickShield,
            _ => KitSound.Cue.PickMoney
        }, .8f);
        if (kind == Kind.Gun || kind == Kind.Ammo) AmmoHUD.Blink(KitStyle.Ice);
        else if (kind == Kind.Heal || kind == Kind.Shield) AdaptiveHealthHUD.Blink(KitStyle.Ice, kind == Kind.Shield);
        var player = PlayerRoster.Local;
        var weapons = player != null ? player.GetComponentInChildren<WeaponController>() : null;
        if (weapons != null) FPSBody.Holding(weapons)?.Reach();
    }

    // The list's icon for a kind; a gun has none and shows a gun's outline.
    private static Texture IconOf(Kind kind) => kind switch
    {
        Kind.Ammo => KitButtons.Icon("Bullet"),
        Kind.Heal => KitButtons.Icon("Heart"),
        Kind.Shield => KitButtons.Icon("Armor"),
        Kind.Money => MoneyIcon.Texture,
        _ => null
    };

    private void Build()
    {
        if (promptHost != null) return;
        promptHost = new GameObject("Pickup Prompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        promptHost.transform.SetParent(transform, false);
        var canvas = promptHost.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 85;
        var scaler = promptHost.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        // The [F] prompt: a key square, then a dark plate with the name and what it gives over a line of its colour,
        // leaning at its own angle just below and right of the crosshair.
        promptRoot = Rect("Prompt", promptHost.transform);
        promptRoot.anchoredPosition = new Vector2(70f, -50f);
        promptRoot.localRotation = Quaternion.Euler(0f, 0f, 5f);
        keyBox = Panel("Key", promptRoot, new Vector2(0f, 0f), new Vector2(52f, 52f), KitStyle.Ice);
        keyLabel = FeelUI.ScoreLabel("F", keyBox.rectTransform, 34f, TextAlignmentOptions.Center);
        keyLabel.rectTransform.anchorMin = Vector2.zero; keyLabel.rectTransform.anchorMax = Vector2.one;
        keyLabel.rectTransform.offsetMin = keyLabel.rectTransform.offsetMax = Vector2.zero;
        keyLabel.text = "F";
        keyLabel.color = KitStyle.Ink;
        var plate = Panel("Plate", promptRoot, new Vector2(58f, -4f), new Vector2(270f, 44f), KitStyle.With(KitStyle.Night, .82f));
        underline = Panel("Line", promptRoot, new Vector2(58f, -44f), new Vector2(270f, 4f), KitStyle.Ice);
        nameLabel = FeelUI.ScoreLabel("Name", plate.rectTransform, 24f, TextAlignmentOptions.Left);
        Fill(nameLabel.rectTransform, new Vector2(14f, 0f), new Vector2(-110f, 0f));
        amountLabel = FeelUI.ScoreLabel("Gives", plate.rectTransform, 20f, TextAlignmentOptions.Right);
        Fill(amountLabel.rectTransform, new Vector2(120f, 0f), new Vector2(-12f, 0f));
        promptRoot.gameObject.SetActive(false);
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = Vector2.zero;
        return rect;
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
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = min; rect.offsetMax = max;
    }

    // ---- In the world ----

    private void LateUpdate()
    {
        seen.Clear();
        float time = Time.unscaledTime;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var row = rows[item.Row];
            seen.Add(item.Id);
            if (!views.TryGetValue(item.Id, out var view) || view == null)
                views[item.Id] = view = PickupView.Make(transform, row.Name, row.Kind, row.Weapon, ColorOf(row.Kind));
            view.transform.position = item.Position;
            view.Show(i == targeted, eye, time);
        }
        if (views.Count == seen.Count && asked.Count == 0) return;
        // Gone (taken, or its time ran out): it plays its take and goes.
        gone.Clear();
        foreach (var pair in views) if (!seen.Contains(pair.Key)) gone.Add(pair.Key);
        foreach (ushort id in gone)
        {
            if (views[id] != null) views[id].Take();
            views.Remove(id);
        }
        gone.Clear();
        foreach (var pair in asked) if (!seen.Contains(pair.Key)) gone.Add(pair.Key);
        foreach (ushort id in gone) asked.Remove(id);
    }

    private static Color ColorOf(Kind kind) => kind switch
    {
        Kind.Gun => KitStyle.Text,
        Kind.Ammo => new Color(1f, .8f, .3f),
        Kind.Heal => KitStyle.Red,
        Kind.Shield => new Color(.45f, .7f, 1f),
        _ => new Color(.49f, .86f, .54f),
    };

    // ---- Helpers ----

    private int IndexOf(ushort id)
    {
        for (int i = 0; i < items.Count; i++) if (items[i].Id == id) return i;
        return -1;
    }

    private static ulong IdOf(PlayerMovement player)
        => player.TryGetComponent(out NetworkObject network) && network.IsSpawned ? network.OwnerClientId : 0UL;

    private static PlayerMovement PlayerOf(ulong client)
    {
        foreach (var someone in PlayerRoster.All)
            if (someone != null && (someone.IsLocal || someone.IsNetworkDriven) && IdOf(someone) == client) return someone;
        return null;
    }

    // Down onto whatever is under it, players (layer 31) and enemies aside.
    private static Vector3 Ground(Vector3 at)
    {
        int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Enemy") & ~(1 << 31);
        return Physics.Raycast(at + Vector3.up * .5f, Vector3.down, out RaycastHit hit, GroundSearch, mask, QueryTriggerInteraction.Ignore)
            ? hit.point : at;
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string key) => row.TryGetValue(key, out string text) ? text.Trim() : "";

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;
}
