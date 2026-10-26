using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Zero-based slots: ranged, ranged, melee, special. Empty selections use fists.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(WeaponController), typeof(PlayerWeaponInput))]
[DefaultExecutionOrder(-175)]
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
    public int SelectedSlot => selectedSlot;
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
    public WeaponData GetWeapon(int index) => IsCompatible(index, RawWeapon(index)) ? RawWeapon(index) : null;
    public WeaponRuntime GetRuntime(int index)
    {
        var data = GetWeapon(index);
        if (data == null) return null;
        if (weapons[index] == null || weapons[index].Data != data)
            weapons[index] = new WeaponRuntime(data, savedData[index] == data && savedAmmo[index] >= 0 ? savedAmmo[index] : (int?)null,
                savedData[index] == data && savedReserve[index] >= 0 ? savedReserve[index] : (int?)null);
        return weapons[index];
    }
    public int RefillAmmo()
    {
        int count = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (GetWeapon(i)?.Category != WeaponCategory.Ranged) continue;
            GetRuntime(i).RefillAmmo();
            count++;
        }
        if (count > 0) SlotActivity?.Invoke();
        return count;
    }
    public bool CanReadInput => isActiveAndEnabled && player != null && player.InputAvailable && !player.CombatInputLocked
        && (playerInput == null || playerInput.actions.FindActionMap("Player")?.enabled == true);

    private void Update()
    {
        if (controller.EquippedData != SelectedWeapon) SetSelection(selectedSlot);
        var keyboard = Keyboard.current;
        if (keyboard == null || !CanReadInput) return;
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) HandleSlotInput(0);
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) HandleSlotInput(1);
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) HandleSlotInput(2);
        else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) HandleSlotInput(3);
    }
    public bool HandleSlotInput(int index)
    {
        if (!CanReadInput || index < 0 || index >= SlotCount) return false;
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
        if (selectedSlot != index) SetSelection(index);
        SlotActivity?.Invoke();
        return true;
    }
    public bool TrySetWeapon(int index, WeaponData data)
    {
        if (!IsCompatible(index, data) || (player != null && player.CombatInputLocked)) return false;
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
