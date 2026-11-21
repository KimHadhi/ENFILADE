using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Zero-based slots: ranged, ranged, melee, special. Empty selections use fists. A Schießhund's special slot holds its
/// Desperado, a revolver loaded only by bounties (ClassKit; the user, 2026-11-11: "데스페라도는 헷갈리니까 4번 칸에").
/// <para>Who owns what of the ammunition (the user, 2026-11-18, "필드 무기 교체 및 탄약 시스템 개편"): the rounds in a
/// gun are the gun's and go with it (GunState: Put takes a particular gun in, Drop lets one go); the reserve is this
/// player's, kept by the ammunition a gun takes (ReserveOf, Weapons.csv ammo) whether or not a gun that takes it is in
/// a slot, so guns of one ammunition share it, and it is filled to what the guns held that take it carry between them
/// (ReserveCap). Before, a slot's gun was only its kind: swapping it threw its magazine and its reserve away, and
/// taking it up again made both anew and full, so rounds earned were lost and an empty gun came back full.</para>
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(WeaponController), typeof(PlayerWeaponInput))]
[DefaultExecutionOrder(FrameOrder.WeaponSlots)]
public sealed class PlayerWeaponSlots : MonoBehaviour
{
    public const int SlotCount = 4;
    [SerializeField] private WeaponData slot1, slot2, slot3, slot4;
    [SerializeField, Range(0, 4), Tooltip("0 = no slot selected; 1..4 = starting slot.")]
    private int startingSlot;
    [SerializeField, HideInInspector] private int selectedSlot = -1;
    [SerializeField, HideInInspector] private bool selectionInitialized;
    [SerializeField, HideInInspector] private int[] savedAmmo = { -1, -1, -1, -1 };
    [SerializeField, HideInInspector] private int[] savedReserve = { -1, -1, -1, -1 };
    [SerializeField, HideInInspector] private WeaponData[] savedData = new WeaponData[SlotCount];
    private readonly WeaponRuntime[] weapons = new WeaponRuntime[SlotCount];
    // The rounds this player carries for each kind of gun, outside the guns (ReserveOf), by ReserveKey.
    private readonly Dictionary<string, AmmoReserve> reserves = new Dictionary<string, AmmoReserve>();
    // A particular gun put in a slot as it was (Put), until its runtime is made; null for a slot whose gun is a new one.
    private readonly GunState?[] arrived = new GunState?[SlotCount];
    private WeaponController controller;
    private PlayerMovement player;
    private PlayerInput playerInput;
    private PlayerMeleeCombat melee;
    private bool sidearmOnly, handsFull, gunsAway;
    private int slotBeforeSidearm, slotBeforeHandsFull;
    // The slot to take out once the held gun is put away (WeaponController.BeginHolster), and how long that has left (s).
    private int holsterTo;
    private float holsterLeft;
    public int SelectedSlot => selectedSlot;
    // The special slot, the fourth.
    private const int SpecialSlot = 3;
    /// <summary>
    /// A Schießhund's (ClassKit): the special slot holds a gun, its Desperado, whose rounds come only from its bounties;
    /// no ammo pickup, refill, resupply or execution tops it up (KeepsOwnRounds).
    /// </summary>
    internal bool SpecialHoldsGun { get; set; }
    /// <summary>This slot's gun is loaded only from outside (a Schießhund's Desperado): leave its rounds alone.</summary>
    public bool KeepsOwnRounds(int index) => index == SpecialSlot && SpecialHoldsGun;
    public WeaponData SelectedWeapon => GetWeapon(selectedSlot);
    public bool UsesFists => isActiveAndEnabled && selectedSlot >= 0 && SelectedWeapon == null;
    public event Action SlotActivity;
    public event Action<int> SelectionChanged;

    private void OnEnable()
    {
        controller = GetComponent<WeaponController>();
        player = GetComponentInParent<PlayerMovement>();
        playerInput = GetComponentInParent<PlayerInput>();
        // Slots run before movement input; initialize health/services before equipping the controller.
        if (player != null) player.EnsureInitialized();
        if (!selectionInitialized) { selectedSlot = Mathf.Clamp(startingSlot - 1, -1, SlotCount - 1); selectionInitialized = true; }
        if (Application.isPlaying)
        {
            melee = GetComponent<PlayerMeleeCombat>();
            if (melee == null) melee = gameObject.AddComponent<PlayerMeleeCombat>();
            GameTables.Bind(PlayerMovement.TableName, "Fists", melee);
            for (int i = 0; i < SlotCount; i++) RawWeapon(i).OrNull()?.BindTable();
            // Every gun the slots start with is made now, as one put in later is (Place): its share is in the reserve
            // before any gun's count is shown.
            for (int i = 0; i < SlotCount; i++) GetRuntime(i);
            if (player.OrNull()?.IsLocal != false && GetComponent<WeaponCrosshairSelector>() == null) gameObject.AddComponent<WeaponCrosshairSelector>();
        }
        SetSelection(Mathf.Clamp(selectedSlot, -1, SlotCount - 1));
    }
    public static WeaponCategory GetSlotCategory(int index)
    {
        if (index < 0 || index >= SlotCount) throw new ArgumentOutOfRangeException(nameof(index));
        return index < 2 ? WeaponCategory.Ranged : index == 2 ? WeaponCategory.Melee : WeaponCategory.Special;
    }
    public static bool IsCompatible(int index, WeaponData data) => index >= 0 && index < SlotCount
        && (data == null || data.Category == GetSlotCategory(index));
    private WeaponData RawWeapon(int index) => index == 0 ? slot1 : index == 1 ? slot2 : index == 2 ? slot3 : index == 3 ? slot4 : null;
    public WeaponData GetWeapon(int index) => Fits(index, RawWeapon(index)) ? RawWeapon(index) : null;

    /// <summary>Some slot of these holds a gun.</summary>
    public static bool HoldsAnyGun(PlayerWeaponSlots slots)
    {
        for (int i = 0; i < SlotCount; i++) if (slots.GetWeapon(i).OrNull()?.Category == WeaponCategory.Ranged) return true;
        return false;
    }
    // This gun may go in this slot: the slot's own kind, or a gun in the special slot for a Schießhund's Desperado (and on
    // someone else's copy, whatever their own PC put there).
    private bool Fits(int index, WeaponData data) => IsCompatible(index, data) || (index == SpecialSlot && data != null
        && data.Category == WeaponCategory.Ranged && (SpecialHoldsGun || (player != null && player.IsNetworkDriven)));
    public WeaponRuntime GetRuntime(int index)
    {
        var data = GetWeapon(index);
        if (data == null) return null;
        if (weapons[index] == null || weapons[index].Data != data)
        {
            // The ammunition it takes is the table's (Weapons.csv ammo), read before its reserve is asked for.
            data.BindTable();
            // A gun loaded only from outside (a Schießhund's Desperado) reloads from nothing its holder carries: a reserve
            // of its own, whatever ammunition it names, so emptying it (LoadOwnRounds) can never empty another gun's.
            bool own = KeepsOwnRounds(index);
            var reserve = own ? new AmmoReserve() : ReserveOf(data);
            GunState gun;
            if (arrived[index] is GunState put && put.Weapon == data)
            {
                // A particular gun put here as it was (Put): what is in it is its own, and the reserve is as it stands.
                gun = put;
            }
            else if (savedData[index] == data && savedAmmo[index] >= 0)
            {
                // The gun this slot held before the component was switched off and its guns forgotten (a script reload).
                gun = new GunState(data, savedAmmo[index]);
                if (savedReserve[index] >= 0) reserve.Rounds = savedReserve[index];
            }
            else
            {
                // A new gun (the shop's, a class's kit, the rack's): its magazine full, and with it what it carries by
                // itself (OwnCap), added to its holder's reserve of its ammunition as far as that is filled to with the
                // guns now held (ReserveCap). So a second gun of an ammunition already carried adds its own share and
                // never refills the first's; more than the cap already carried is kept.
                gun = GunState.New(data);
                if (!own)
                {
                    int before = reserve.Rounds, cap = ReserveCap(index);
                    reserve.Rounds = Mathf.Max(before, Mathf.Min(cap, before + OwnCap(data)));
                    if (Logged && data.Category == WeaponCategory.Ranged)
                        GunAmmoLog.Note($"{data.name,-15} {ReserveKey(data),-8} new in slot {index + 1}: {gun.Rounds} in it; reserve {before} -> {reserve.Rounds} (filled to {cap})");
                }
            }
            arrived[index] = null;
            weapons[index] = new WeaponRuntime(gun, reserve);
            // What moves this PC's own player's rounds is written down (Logs/gun-ammo.txt); a friend's copy counts nothing.
            if (Logged) weapons[index].Told = GunAmmoLog.Gun;
        }
        return weapons[index];
    }

    // This PC's own player's rounds are written down as they move (GunAmmoLog).
    private bool Logged => GunAmmoLog.On && player != null && player.IsLocal && !player.IsNetworkDriven;

    /// <summary>
    /// What a player's reserve is kept by: the ammunition a gun takes (WeaponData.Ammunition, Weapons.csv ammo), so
    /// every gun that takes it reloads from the one reserve, and a gun swapped for another of its ammunition finds the
    /// rounds its player carried (the user, 2026-11-18: "총 종류별로 두면 너무 빡셀 것 같으니, 탄 종류끼리 공유하는 편이
    /// 좋을 것 같습니다"). The one place that says so: until then it was the gun's own name, a reserve for each gun.
    /// Asked of a gun whose table has been read, as every gun in a slot is (Place, OnEnable).
    /// </summary>
    public static string ReserveKey(WeaponData gun) => gun != null ? gun.Ammunition : "";

    /// <summary>
    /// The rounds this player carries of the ammunition that gun takes, outside any gun (AmmoReserve): theirs whether or
    /// not a gun that takes it is in a slot now, so they are there again when one is (the user, 2026-11-18: "암살 등으로
    /// 모은 탄약이 길가의 총기와 교체 후 원래 총을 다시 주울 때 초기화되는 문제"). Not what a gun loaded only from
    /// outside reloads from: that one has a reserve of its own (GetRuntime).
    /// </summary>
    public AmmoReserve ReserveOf(WeaponData gun)
    {
        string key = ReserveKey(gun);
        if (!reserves.TryGetValue(key, out var reserve)) reserves[key] = reserve = new AmmoReserve();
        return reserve;
    }

    /// <summary>
    /// The slot holding this very gun (the same one of the game's guns, not another that takes its ammunition), other
    /// than one that keeps its own rounds; -1 when none does. Such a gun on the ground is taken for its rounds alone
    /// (TakeRounds); another gun of the same ammunition is a gun to swap for.
    /// </summary>
    public int SlotOf(WeaponData gun)
    {
        if (gun == null) return -1;
        for (int i = 0; i < SlotCount; i++)
            if (GetWeapon(i) == gun && !KeepsOwnRounds(i)) return i;
        return -1;
    }

    /// <summary>
    /// Puts this particular gun in the slot as it is (one taken from the ground): the rounds in it are its own, the
    /// rounds lying with it join this player's reserve of its ammunition, and nothing else is filled. <paramref name="before"/>
    /// is the gun the slot held, as it was, for whoever lays it on the ground; null when the slot was empty or held no
    /// gun. False when the slot can't take it now.
    /// </summary>
    public bool Put(int index, in GunState gun, out GunState? before)
    {
        before = null;
        if (gun.Weapon == null || !Fits(index, gun.Weapon) || KeepsOwnRounds(index) || (player != null && player.CombatInputLocked)) return false;
        if (Leaving(index, out GunState old)) before = old;
        // Its numbers are the table's (the ammunition it takes, what it carries), read before they are asked.
        gun.Weapon.BindTable();
        var reserve = ReserveOf(gun.Weapon);
        // What lies with the gun joins the reserve of its ammunition as far as that is filled to with this gun in the
        // slot (CapWith), as an ammo pile does; the rest stays the gun's and lies with it again when it is put down. So a
        // gun put down and another taken up in its place never brings more than the reserve holds: no round is lost, and
        // none is made past the cap. The reserve itself is untouched by the swap: a gun that takes the same ammunition as
        // the one put down goes on with it as it stood.
        int cap = CapWith(index, gun.Weapon);
        int reserveBefore = reserve.Rounds, taken = Mathf.Min(gun.Spare, Mathf.Max(0, cap - reserve.Rounds));
        reserve.Rounds += taken;
        Place(index, gun.Weapon, gun.Holding(gun.Rounds, gun.Spare - taken));
        if (Logged)
            GunAmmoLog.Note($"{gun.Weapon.name,-15} {ReserveKey(gun.Weapon),-8} taken into slot {index + 1}: {gun.Rounds} in it, {gun.Spare} lying with it "
                + $"({taken} taken); reserve {reserveBefore} -> {reserve.Rounds} of {cap}" + (before is GunState left
                    ? $"; {left.Weapon.name} put down with {left.Rounds} in it, its {ReserveKey(left.Weapon)} reserve {ReserveOf(left.Weapon).Rounds} kept" : ""));
        return true;
    }

    /// <summary>
    /// The slot's gun leaves it as it is: a reload under way is cut, what is in the gun goes with it, and the reserve
    /// stays with this player. The one call that puts a gun down, for the Tab drop to come (the user, 2026-11-18:
    /// "드랍된 무기는 현재 장탄수 그대로 유지되고 예비 탄약은 플레이어 본인 기준으로 관리"); FieldPickups lays it on the
    /// ground. False for a slot with no gun, or one that keeps its own rounds (a Schießhund's Desperado is never dropped).
    /// </summary>
    public bool Drop(int index, out GunState gun)
    {
        gun = default;
        if ((player != null && player.CombatInputLocked) || !Leaving(index, out gun)) return false;
        Place(index, null, null);
        if (Logged)
            GunAmmoLog.Note($"{gun.Weapon.name,-15} {ReserveKey(gun.Weapon),-8} put down from slot {index + 1} with {gun.Rounds} in it; "
                + $"reserve {ReserveOf(gun.Weapon).Rounds} kept");
        return true;
    }

    // The gun in the slot as it would leave it now (a reload under way is cut); false when the slot has none to give up.
    private bool Leaving(int index, out GunState gun)
    {
        gun = default;
        var data = GetWeapon(index);
        if (data == null || data.Category != WeaponCategory.Ranged || KeepsOwnRounds(index)) return false;
        var runtime = GetRuntime(index);
        runtime.Interrupt();
        gun = runtime.State;
        return true;
    }

    /// <summary>
    /// How many rounds of a gun lying on the ground this player's reserve would take now, when it is the same gun as the
    /// one in <paramref name="index"/>: up to what the reserve of its ammunition is filled to (ReserveCap), the rounds
    /// lying with the gun first, then those in it.
    /// </summary>
    public int RoomFor(int index, in GunState lying)
    {
        var data = GetWeapon(index);
        if (data == null || lying.Weapon != data || KeepsOwnRounds(index)) return 0;
        int room = Mathf.Max(0, ReserveCap(index) - ReserveOf(data).Rounds);
        return Mathf.Min(room, lying.Spare + (lying.MagazineOut ? 0 : lying.Rounds));
    }

    /// <summary>
    /// A gun on the ground that is the same gun as one already held gives up its rounds and stays where it lies: as many
    /// as this player's reserve has room for (RoomFor) go into it, and <paramref name="left"/> is the gun as it then is
    /// (the user, 2026-11-18: "같은 종류의 총이라면 탄만 빼오게 하신 건 잘하신 판단입니다"). Nothing else is filled:
    /// before, taking a gun already held refilled every gun the player had. Returns how many were taken.
    /// </summary>
    public int TakeRounds(int index, in GunState lying, out GunState left)
    {
        left = lying;
        int taken = RoomFor(index, lying);
        if (taken <= 0) return 0;
        int fromSpare = Mathf.Min(taken, lying.Spare);
        left = lying.Holding(lying.Rounds - (taken - fromSpare), lying.Spare - fromSpare);
        GetRuntime(index).AddReserve(taken);
        SlotActivity?.Invoke();
        return taken;
    }

    /// <summary>
    /// How much the reserve this slot's gun reloads from is filled to by a pickup, the shop or a refill, and
    /// <paramref name="overPercent"/> per cent past it (a Support's resupply, Fully Loaded): what every gun held that
    /// takes its ammunition carries by itself (OwnCap), added up. One rifle with 90: 90. Two rifles with 90 and 120: 210
    /// in the one reserve both reload from, as much as they held apart before reserves were shared. When one of them
    /// leaves, the cap is the other's again; rounds already carried past it are kept and only nothing more is added.
    /// The ammo pouch is this PC's own player's (PlayerPerks.ReserveFactor), so a friend's copy keeps the guns' own.
    /// </summary>
    public int ReserveCap(int index, float overPercent = 0f)
    {
        var data = GetWeapon(index);
        if (data == null) return 0;
        return KeepsOwnRounds(index) ? OwnCap(data, overPercent) : SharedCap(ReserveKey(data), -1, null, overPercent);
    }

    /// <summary>
    /// As ReserveCap, were <paramref name="gun"/> in the slot in place of what it holds now: what a gun about to be taken
    /// up there finds its ammunition's reserve filled to.
    /// </summary>
    public int CapWith(int index, WeaponData gun, float overPercent = 0f)
        => gun == null ? 0 : KeepsOwnRounds(index) ? OwnCap(gun, overPercent) : SharedCap(ReserveKey(gun), index, gun, overPercent);

    /// <summary>
    /// What one gun carries in reserve by itself: its starting reserve (Weapons.csv initialReserveAmmo), times this PC's
    /// own player's ammo pouch, and <paramref name="overPercent"/> per cent past it. What a new gun brings its holder.
    /// </summary>
    public int OwnCap(WeaponData data, float overPercent = 0f)
    {
        if (data == null) return 0;
        float factor = player != null && player.IsLocal ? PlayerPerks.ReserveFactor : 1f;
        // A hair under, so float error never rounds a whole number up (120 × 1.2 comes out as 144.00001).
        return Mathf.Max(0, Mathf.CeilToInt(data.InitialReserveAmmo * factor * (1f + Mathf.Max(0f, overPercent) / 100f) - .001f));
    }

    // What the reserve of one ammunition is filled to: what each gun held that takes it carries by itself, added up. So
    // two guns of one ammunition hold together what they held apart, in the one reserve both reload from, and sharing
    // never leaves a player with less than before it. A gun loaded only from outside is no part of it. The slot
    // `replaced` is counted as holding `incoming` (-1 and null: the slots as they are).
    private int SharedCap(string ammunition, int replaced, WeaponData incoming, float overPercent)
    {
        int cap = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (KeepsOwnRounds(i)) continue;
            var held = i == replaced ? incoming : GetWeapon(i);
            if (held != null && ReserveKey(held) == ammunition) cap += OwnCap(held, overPercent);
        }
        return cap;
    }
    /// <summary>A gun's ammo changed outside a shot or a reload (a Point's gun put away reloading itself): the HUD reads again.</summary>
    internal void NotifyAmmo() => SlotActivity?.Invoke();

    public int RefillAmmo()
    {
        int count = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (GetWeapon(i).OrNull()?.Category != WeaponCategory.Ranged || KeepsOwnRounds(i)) continue;
            GetRuntime(i).RefillAmmo(ReserveCap(i));
            count++;
        }
        if (count > 0) SlotActivity?.Invoke();
        return count;
    }
    /// <summary>
    /// Every gun held comes up with its magazine full at once and its reserve untouched (WeaponRuntime.TopUp), the one in
    /// hand and the ones put away alike: what the room's slow motion gives each player as it begins (SkullSlowMotion; the
    /// user, 2026-11-18: "모든 인원의 주무기 및 보조무기 현재 장탄수만 최대치로 충전"). Not a gun that keeps its own rounds
    /// (a Schießhund's Desperado is loaded by bounties alone). Returns how many guns it filled.
    /// </summary>
    public int TopUpMagazines()
    {
        int count = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (GetWeapon(i).OrNull()?.Category != WeaponCategory.Ranged || KeepsOwnRounds(i)) continue;
            if (GetRuntime(i).TopUp()) count++;
        }
        if (count > 0) SlotActivity?.Invoke();
        return count;
    }

    /// <summary>Whether RefillAmmo would add anything: a held gun's magazine isn't full, or its reserve is under its cap (ReserveCap).</summary>
    public bool NeedsAmmo()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (GetWeapon(i).OrNull()?.Category != WeaponCategory.Ranged || KeepsOwnRounds(i)) continue;
            var gun = GetRuntime(i);
            if (gun.Ammo < gun.Data.MagazineSize || gun.ReserveAmmo < ReserveCap(i)) return true;
        }
        return false;
    }
    public bool CanReadInput => isActiveAndEnabled && player != null && player.InputAvailable && !player.CombatInputLocked && !player.IsHeld
        && (playerInput == null || playerInput.actions.FindActionMap("Player")?.enabled == true);

    private void Update()
    {
        // The held gun is put away: the next comes out.
        if (holsterLeft > 0f)
        {
            holsterLeft -= Time.deltaTime;
            if (holsterLeft <= 0f) SetSelection(holsterTo);
        }
        if (controller.EquippedData != SelectedWeapon) SetSelection(selectedSlot);
        // The perk cards take 1-3 while they are up (in a room the game runs on, so these keys still reach here): a card
        // picked must not also swap the gun and cut its reload (friend feedback C13).
        if (!CanReadInput || PerkCardScreen.TakesKeyboard) return;
        // Every key here is the key window's (KeyBindings): slots 1-4 with the number pad's as their second keys.
        if (KeyBindings.Pressed("Combat/Slot1")) HandleSlotInput(0);
        else if (KeyBindings.Pressed("Combat/Slot2")) HandleSlotInput(1);
        else if (KeyBindings.Pressed("Combat/Slot3")) HandleSlotInput(2);
        else if (KeyBindings.Pressed("Combat/Slot4")) HandleSlotInput(3);
        // B switches the held gun's fire mode (Rainbow Six Siege's select fire).
        else if (KeyBindings.Pressed("Player/FireMode") && !sidearmOnly) controller.CycleFireMode();
        // C opens the gun's customize menu, one key for its sight, muzzle and underbarrel parts, as ArcCW's (user,
        // 2026-11-09: "키를 늘릴 바에 ArcCW처럼 커스터마이징 키를 하나 따로").
        else if (KeyBindings.Pressed("Player/Customize") && !sidearmOnly && !GunCustomizeMenu.TakesKeyboard) GunCustomizeMenu.Open(player, controller);
        // Y looks the gun over; L switches its flashlight.
        else if (KeyBindings.Pressed("Player/Inspect")) controller.Inspect();
        else if (KeyBindings.Pressed("Player/Flashlight")) controller.ToggleLight();
    }
    /// <summary>Someone downed holds only their pistol and can't change it. With no pistol, their hands stay empty. Afterwards they go back to what they held.</summary>
    internal void HoldSidearm(bool hold)
    {
        if (hold == sidearmOnly) return;
        sidearmOnly = hold;
        if (!hold) { SetSelection(slotBeforeSidearm); return; }
        // Going down drops whatever filled the hands (CampaignCans) and brings the pistol out even with the guns on an
        // x-ray belt (GimmickTray); getting up goes back to what was held before it.
        slotBeforeSidearm = handsFull || gunsAway ? slotBeforeHandsFull : selectedSlot;
        handsFull = gunsAway = false;
        for (int i = 0; i < SlotCount; i++)
        {
            var data = GetWeapon(i);
            if (data == null || data.Category != WeaponCategory.Ranged || data.SoundProfile != GunSoundProfile.Pistol) continue;
            SetSelection(i);
            return;
        }
        SetSelection(-1);
    }
    /// <summary>Both hands hold something that isn't a weapon (HoldHandsFull).</summary>
    public bool HandsFull => handsFull;

    /// <summary>The guns are elsewhere (GimmickTray: on an airport's x-ray belt), so nothing is held (HoldGunsAway).</summary>
    public bool GunsAway => gunsAway;

    // Whatever fills both hands now, each said every frame by its own owner (a fuel can: CampaignCans; a carried prop:
    // PropCarry), so one owner letting go never empties the hands another still fills.
    private readonly HashSet<object> fillers = new HashSet<object>();

    /// <summary>
    /// Something of <paramref name="by"/>'s fills both hands (a fuel can, a carried prop), or no longer does: nothing is
    /// held and the number keys do nothing while anything does, then back to what was held.
    /// </summary>
    internal void HoldHandsFull(object by, bool full)
    {
        if (by == null) return;
        if (full) fillers.Add(by);
        else fillers.Remove(by);
        Empty(ref handsFull, fillers.Count > 0);
    }

    /// <summary>The guns ride an x-ray belt (GimmickTray): nothing is held and the number keys do nothing until they are
    /// taken back, then back to what was held. A fuel can still fits the empty hands; downed, the pistol comes out.</summary>
    internal void HoldGunsAway(bool away) => Empty(ref gunsAway, away);

    // Hands emptied for one reason or more (a can, the guns away): what was held before the first goes back after the
    // last. Each reason is set every frame by its own owner, so one never undoes another's.
    private void Empty(ref bool reason, bool on)
    {
        if (on == reason || (on && sidearmOnly)) return;
        bool before = handsFull || gunsAway;
        reason = on;
        bool after = handsFull || gunsAway;
        if (before == after) return;
        if (after) { slotBeforeHandsFull = selectedSlot; SetSelection(-1); return; }
        SetSelection(slotBeforeHandsFull);
    }

    public bool HandleSlotInput(int index)
    {
        if (!CanReadInput || sidearmOnly || handsFull || gunsAway || index < 0 || index >= SlotCount) return false;
        return SelectSlot(selectedSlot == index ? -1 : index);
    }
    // Explicit selection is idempotent; -1 holsters everything. Number keys use ToggleSlot semantics.
    public bool SelectSlot(int index)
    {
        if (index < -1 || index >= SlotCount || (player != null && player.CombatInputLocked)) return false;
        if (holsterLeft > 0f) holsterTo = index;
        else if (selectedSlot != index && !Holster(index)) SetSelection(index);
        SlotActivity?.Invoke();
        return true;
    }

    // This PC's own player puts the held gun away over its holster time before the next comes out, as Siege's swap does;
    // someone else's copy follows their PC's slot at once (it already waited there). False when it goes at once.
    private bool Holster(int index)
    {
        if (player == null || !player.IsLocal || player.IsNetworkDriven || controller == null) return false;
        float seconds = controller.BeginHolster();
        if (seconds <= 0f) return false;
        holsterTo = index;
        holsterLeft = seconds;
        return true;
    }
    /// <summary>
    /// A new gun of this kind in the slot (the shop's, a class's kit, the rack's; on a friend's copy, what their PC says
    /// they hold), or nothing with null: its magazine full and its holder's reserve for its kind filled to their cap
    /// (GetRuntime). The gun the slot held is gone, not laid down: a particular gun is put in with Put and taken out
    /// with Drop.
    /// </summary>
    public bool TrySetWeapon(int index, WeaponData data)
    {
        if (!Fits(index, data) || (player != null && player.CombatInputLocked)) return false;
        Place(index, data, null);
        return true;
    }

    // The slot now holds this kind of gun: the particular one given as it was, else a new one (GetRuntime makes it).
    private void Place(int index, WeaponData data, GunState? asItWas)
    {
        weapons[index]?.Interrupt(); weapons[index] = null; savedAmmo[index] = -1; savedData[index] = null;
        savedReserve[index] = -1;
        arrived[index] = asItWas;
        switch (index) { case 0: slot1 = data; break; case 1: slot2 = data; break; case 2: slot3 = data; break; case 3: slot4 = data; break; }
        // Made now, not when first asked for: what a new gun brings is in its holder's reserve from the moment the gun is
        // theirs. Two guns of one ammunition read one reserve, so the second's share must not turn up in the first's
        // count only when the second is first drawn.
        GetRuntime(index);
        if (index == selectedSlot) SetSelection(index);
        SlotActivity?.Invoke();
    }
    private void SetSelection(int index)
    {
        // Any change of hands ends a put-away under way (its slot is taken now, or another reason took over).
        holsterLeft = 0f;
        if (controller == null) controller = GetComponent<WeaponController>();
        if (melee == null) melee = GetComponent<PlayerMeleeCombat>();
        melee.OrNull()?.CancelAction();
        player.OrNull()?.Abilities?.Combat.CancelShove();
        selectedSlot = index;
        controller.Equip(GetRuntime(index));
        SelectionChanged?.Invoke(index);
    }
    private void OnDisable()
    {
        for (int i = 0; i < SlotCount; i++)
            if (weapons[i] != null) { savedAmmo[i] = weapons[i].Ammo; savedReserve[i] = weapons[i].ReserveAmmo; savedData[i] = weapons[i].Data; }
        melee.OrNull()?.CancelAction();
        player.OrNull()?.Abilities?.Combat.CancelShove();
        if (controller != null) controller.Equip(null);
    }
}
