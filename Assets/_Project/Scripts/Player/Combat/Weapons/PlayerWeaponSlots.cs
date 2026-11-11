using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Zero-based slots: ranged, ranged, melee, special. Empty selections use fists. A Schießhund's special slot holds its
/// Desperado, a revolver loaded only by bounties (ClassKit; the user, 2026-11-11: "데스페라도는 헷갈리니까 4번 칸에").
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
    private WeaponController controller;
    private PlayerMovement player;
    private PlayerInput playerInput;
    private PlayerMeleeCombat melee;
    private bool sidearmOnly, handsFull;
    private int slotBeforeSidearm, slotBeforeHandsFull;
    // The slot to take out once the held gun is put away (WeaponController.BeginHolster), and how long that has left (s).
    private int holsterTo;
    private float holsterLeft;
    public int SelectedSlot => selectedSlot;
    /// <summary>The melee slot, the third: a melee weapon (MeleeWeapons, the katana) goes here.</summary>
    public const int MeleeSlot = 2;
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
            for (int i = 0; i < SlotCount; i++) RawWeapon(i)?.BindTable();
            if (player?.IsLocal != false && GetComponent<WeaponCrosshairSelector>() == null) gameObject.AddComponent<WeaponCrosshairSelector>();
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
    // This gun may go in this slot: the slot's own kind, or a gun in the special slot for a Schießhund's Desperado (and on
    // someone else's copy, whatever their own PC put there).
    private bool Fits(int index, WeaponData data) => IsCompatible(index, data) || (index == SpecialSlot && data != null
        && data.Category == WeaponCategory.Ranged && (SpecialHoldsGun || (player != null && player.IsNetworkDriven)));
    public WeaponRuntime GetRuntime(int index)
    {
        var data = GetWeapon(index);
        if (data == null) return null;
        if (weapons[index] == null || weapons[index].Data != data)
            weapons[index] = new WeaponRuntime(data, savedData[index] == data && savedAmmo[index] >= 0 ? savedAmmo[index] : (int?)null,
                savedData[index] == data && savedReserve[index] >= 0 ? savedReserve[index] : (int?)null);
        return weapons[index];
    }
    /// <summary>A gun's ammo changed outside a shot or a reload (a Point's gun put away reloading itself): the HUD reads again.</summary>
    internal void NotifyAmmo() => SlotActivity?.Invoke();

    public int RefillAmmo()
    {
        int count = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (GetWeapon(i)?.Category != WeaponCategory.Ranged || KeepsOwnRounds(i)) continue;
            GetRuntime(i).RefillAmmo();
            count++;
        }
        if (count > 0) SlotActivity?.Invoke();
        return count;
    }
    /// <summary>Whether RefillAmmo would add anything: a held gun's magazine isn't full, or its reserve is under what it starts with.</summary>
    public bool NeedsAmmo()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (GetWeapon(i)?.Category != WeaponCategory.Ranged || KeepsOwnRounds(i)) continue;
            var gun = GetRuntime(i);
            if (gun.Ammo < gun.Data.MagazineSize || gun.ReserveAmmo < gun.Data.InitialReserveAmmo) return true;
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
        var keyboard = Keyboard.current;
        // The perk cards take 1-3 while they are up (in a room the game runs on, so these keys still reach here): a card
        // picked must not also swap the gun and cut its reload (friend feedback C13).
        if (keyboard == null || !CanReadInput || PerkCardScreen.TakesKeyboard) return;
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) HandleSlotInput(0);
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) HandleSlotInput(1);
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) HandleSlotInput(2);
        else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) HandleSlotInput(3);
        // B switches the held gun's fire mode (Rainbow Six Siege's select fire).
        else if (keyboard.bKey.wasPressedThisFrame && !sidearmOnly) controller.CycleFireMode();
        // C opens the gun's customize menu, one key for its sight, muzzle and underbarrel parts, as ArcCW's (user,
        // 2026-11-09: "키를 늘릴 바에 ArcCW처럼 커스터마이징 키를 하나 따로").
        else if (keyboard.cKey.wasPressedThisFrame && !sidearmOnly && !GunCustomizeMenu.TakesKeyboard) GunCustomizeMenu.Open(player, controller);
        // Y looks the gun over; L switches its flashlight.
        else if (keyboard.yKey.wasPressedThisFrame) controller.Inspect();
        else if (keyboard.lKey.wasPressedThisFrame) controller.ToggleLight();
    }
    /// <summary>Someone downed holds only their pistol and can't change it. With no pistol, their hands stay empty. Afterwards they go back to what they held.</summary>
    internal void HoldSidearm(bool hold)
    {
        if (hold == sidearmOnly) return;
        sidearmOnly = hold;
        if (!hold) { SetSelection(slotBeforeSidearm); return; }
        // Going down drops whatever filled the hands (CampaignCans); getting up goes back to what was held before it.
        slotBeforeSidearm = handsFull ? slotBeforeHandsFull : selectedSlot;
        handsFull = false;
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

    /// <summary>Something fills both hands (a campaign fuel can, CampaignCans): nothing is held and the number keys do
    /// nothing until it is put down, then back to what was held.</summary>
    internal void HoldHandsFull(bool full)
    {
        if (full == handsFull || (full && sidearmOnly)) return;
        handsFull = full;
        if (full) { slotBeforeHandsFull = selectedSlot; SetSelection(-1); return; }
        SetSelection(slotBeforeHandsFull);
    }

    public bool HandleSlotInput(int index)
    {
        if (!CanReadInput || sidearmOnly || handsFull || index < 0 || index >= SlotCount) return false;
        return SelectSlot(selectedSlot == index ? -1 : index);
    }
    public void ToggleSlot(int index)
    {
        if (index >= 0 && index < SlotCount) SelectSlot(selectedSlot == index ? -1 : index);
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
    public bool TrySetWeapon(int index, WeaponData data)
    {
        if (!Fits(index, data) || (player != null && player.CombatInputLocked)) return false;
        weapons[index]?.Interrupt(); weapons[index] = null; savedAmmo[index] = -1; savedData[index] = null;
        savedReserve[index] = -1;
        switch (index) { case 0: slot1 = data; break; case 1: slot2 = data; break; case 2: slot3 = data; break; case 3: slot4 = data; break; }
        if (index == selectedSlot) SetSelection(index);
        SlotActivity?.Invoke();
        return true;
    }
    // Field object spawning/dropping belongs to the future pickup system.
    public bool TryAcquire(WeaponData data, out WeaponData replaced)
    {
        replaced = null;
        if (data == null || (player != null && player.CombatInputLocked)) return false;
        for (int i = 0; i < SlotCount; i++)
            if (IsCompatible(i, data) && RawWeapon(i) == null) return TrySetWeapon(i, data);
        if (!IsCompatible(selectedSlot, data)) return false;
        replaced = SelectedWeapon;
        return TrySetWeapon(selectedSlot, data);
    }
    private void SetSelection(int index)
    {
        // Any change of hands ends a put-away under way (its slot is taken now, or another reason took over).
        holsterLeft = 0f;
        if (controller == null) controller = GetComponent<WeaponController>();
        if (melee == null) melee = GetComponent<PlayerMeleeCombat>();
        melee?.CancelAction();
        player?.Abilities?.Combat.CancelShove();
        selectedSlot = index;
        controller.Equip(GetRuntime(index));
        SelectionChanged?.Invoke(index);
    }
    private void OnDisable()
    {
        for (int i = 0; i < SlotCount; i++)
            if (weapons[i] != null) { savedAmmo[i] = weapons[i].Ammo; savedReserve[i] = weapons[i].ReserveAmmo; savedData[i] = weapons[i].Data; }
        melee?.CancelAction();
        player?.Abilities?.Combat.CancelShove();
        if (controller != null) controller.Equip(null);
    }
}
