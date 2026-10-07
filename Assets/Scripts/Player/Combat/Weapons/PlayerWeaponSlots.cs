using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Three independent magazines. SelectedSlot -1 is the unarmed state.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(WeaponController))]
[DefaultExecutionOrder(250)]
public sealed class PlayerWeaponSlots : MonoBehaviour
{
    [SerializeField] private WeaponData slot1;
    [SerializeField] private WeaponData slot2;
    [SerializeField] private WeaponData slot3;
    [SerializeField, Range(0,3), Tooltip("0 = unarmed; 1..3 = initial equipped slot.")]
    private int startingSlot;
    public int SelectedSlot { get; private set; } = -1;
    public event Action SlotActivity;
    public event Action<int> SelectionChanged;
    private readonly WeaponRuntime[] weapons = new WeaponRuntime[3];
    private WeaponController controller;
    private PlayerMovement player;
    private PlayerInput playerInput;

    private void Start()
    {
        controller = GetComponent<WeaponController>();
        player = GetComponentInParent<PlayerMovement>();
        playerInput = GetComponentInParent<PlayerInput>();
        SetSelection(startingSlot - 1);
    }

    public WeaponData GetWeapon(int index) => index == 0 ? slot1 : index == 1 ? slot2 : index == 2 ? slot3 : null;
    public WeaponRuntime GetRuntime(int index) => index >= 0 && index < 3 ? weapons[index] : null;
    public bool CanReadInput => isActiveAndEnabled && player != null && player.InputAvailable
        && (playerInput == null || playerInput.actions.FindActionMap("Player")?.enabled == true);

    private void Update()
    {
        if (SelectedSlot >= 0 && (GetWeapon(SelectedSlot) == null || weapons[SelectedSlot]?.Data != GetWeapon(SelectedSlot)))
            SetSelection(SelectedSlot);
        var keyboard = Keyboard.current;
        if (keyboard == null || !CanReadInput) return;
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) HandleSlotInput(0);
        else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) HandleSlotInput(1);
        else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) HandleSlotInput(2);
    }

    public bool HandleSlotInput(int index)
    {
        if (index < 0 || index > 2 || !CanReadInput) return false;
        ToggleSlot(index);
        return true;
    }

    public void ToggleSlot(int index)
    {
        if (index < 0 || index > 2) return;
        SetSelection(SelectedSlot == index ? -1 : index);
        SlotActivity?.Invoke();
    }

    private void SetSelection(int index)
    {
        if (controller == null) controller = GetComponent<WeaponController>();
        var data = GetWeapon(index);
        int next = data != null ? index : -1;
        if (next >= 0 && (weapons[next] == null || weapons[next].Data != data))
            weapons[next] = new WeaponRuntime(data);
        controller.Equip(next < 0 ? null : weapons[next]);
        SelectedSlot = next;
        SelectionChanged?.Invoke(next);
    }

    private void OnDisable()
    {
        if (controller != null) controller.Equip(null);
        SelectedSlot = -1;
        SelectionChanged?.Invoke(-1);
    }
}
