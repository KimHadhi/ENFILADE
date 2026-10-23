using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class PlayerWeaponInput : MonoBehaviour, IWeaponInput
{
    [Tooltip("Optional. Uses the project's InputSystem.actions when empty; reads only the Combat action map.")]
    [SerializeField] private InputActionAsset inputActions;
    private InputActionMap ownedMap;
    private InputAction fire, reload, guard, shove;
    private bool meleeReleaseRequired = true;
    private bool acceptInputThisFrame, releaseRequired = true;

    private void OnEnable()
    {
        ownedMap?.Disable();
        ownedMap?.Dispose();
        ownedMap = null;
        fire = reload = null;
        var asset = inputActions != null ? inputActions : InputSystem.actions;
        var map = asset != null ? asset.FindActionMap("Combat", false) : null;
        if (map == null) { Debug.LogError("PlayerWeaponInput needs Combat/Fire and Combat/Reload in the input asset.", this); return; }
        ownedMap = map.Clone(); fire = ownedMap.FindAction("Fire", false); reload = ownedMap.FindAction("Reload", false);
        guard = ownedMap.FindAction("Guard", false);
        shove = asset.FindAction("Player/Shove", false);
        if (fire == null || reload == null)
        { Debug.LogError("Combat input map is missing Fire or Reload.", this); ownedMap.Dispose(); ownedMap = null; return; }
        ownedMap.Enable(); releaseRequired = meleeReleaseRequired = true; acceptInputThisFrame = false;
    }

    private void Update()
    {
        // Run before PlayerLook can relock the cursor with the very same mouse click.
        acceptInputThisFrame = Time.timeScale > 0f && Application.isFocused && Cursor.lockState == CursorLockMode.Locked;
        if (!acceptInputThisFrame) releaseRequired = meleeReleaseRequired = true;
    }

    public WeaponInputFrame ReadInput()
    {
        // Check again after PlayerLook.Update so Esc also blocks firing in the same frame.
        if (!isActiveAndEnabled || !acceptInputThisFrame || fire == null || reload == null
            || Time.timeScale <= 0f || !Application.isFocused || Cursor.lockState != CursorLockMode.Locked)
            return default;
        if (releaseRequired)
        {
            if (!fire.IsPressed()) releaseRequired = false;
            return default;
        }
        return new WeaponInputFrame(fire.WasPressedThisFrame(), fire.IsPressed(), reload.WasPressedThisFrame());
    }

    public bool TryReadMeleeInput(out MeleeInputFrame frame)
    {
        frame = default;
        var slots = GetComponent<PlayerWeaponSlots>();
        // The Player map is disabled while chat is open; never let the cloned map bypass it.
        bool available = isActiveAndEnabled && acceptInputThisFrame && slots != null && slots.CanReadInput
            && fire != null && guard != null && Cursor.lockState == CursorLockMode.Locked;
        if (!available) { meleeReleaseRequired = true; return false; }
        if (meleeReleaseRequired)
        {
            if (!fire.IsPressed() && !guard.IsPressed() && (shove == null || !shove.IsPressed())) meleeReleaseRequired = false;
            return false;
        }
        frame = new MeleeInputFrame(fire.WasPressedThisFrame(), fire.IsPressed(), fire.WasReleasedThisFrame(),
            guard.WasPressedThisFrame(), guard.IsPressed(), shove != null && shove.WasPressedThisFrame(),
            shove != null && shove.IsPressed(), shove != null && shove.WasReleasedThisFrame());
        return true;
    }

    public void RequireTriggerRelease() { releaseRequired = meleeReleaseRequired = true; }

    // A guard-break animation stays locked, but may remember a defensive press near its end.
    public bool TryReadGuardBuffer(out MeleeInputFrame frame)
    {
        frame = default;
        var movement = GetComponent<PlayerMovement>();
        var playerInput = GetComponent<PlayerInput>();
        if (!isActiveAndEnabled || !acceptInputThisFrame || guard == null || movement == null
            || !movement.InputAvailable || movement.Execution?.IsExecuting == true
            || (playerInput != null && playerInput.actions.FindActionMap("Player")?.enabled != true)) return false;
        frame = new MeleeInputFrame(guardPressed: guard.WasPressedThisFrame(), guardHeld: guard.IsPressed());
        return true;
    }

    private void OnApplicationFocus(bool focused) { if (!focused) { acceptInputThisFrame = false; releaseRequired = meleeReleaseRequired = true; } }
    private void OnDisable()
    {
        ownedMap?.Disable(); ownedMap?.Dispose(); ownedMap = null; fire = reload = null;
        acceptInputThisFrame = false; releaseRequired = meleeReleaseRequired = true; guard = shove = null;
    }
}
