using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class PlayerWeaponInput : MonoBehaviour, IWeaponInput
{
    [Tooltip("Optional. Uses the project's InputSystem.actions when empty; reads only the Combat action map.")]
    [SerializeField] private InputActionAsset inputActions;
    private InputActionMap ownedMap;
    private InputAction fire, reload;
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
        if (fire == null || reload == null)
        { Debug.LogError("Combat input map is missing Fire or Reload.", this); ownedMap.Dispose(); ownedMap = null; return; }
        ownedMap.Enable(); releaseRequired = true; acceptInputThisFrame = false;
    }

    private void Update()
    {
        // Run before PlayerLook can relock the cursor with the very same mouse click.
        acceptInputThisFrame = Time.timeScale > 0f && Application.isFocused && Cursor.lockState == CursorLockMode.Locked;
        if (!acceptInputThisFrame) releaseRequired = true;
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

    private void OnApplicationFocus(bool focused) { if (!focused) { acceptInputThisFrame = false; releaseRequired = true; } }
    private void OnDisable()
    {
        ownedMap?.Disable(); ownedMap?.Dispose(); ownedMap = null; fire = reload = null;
        acceptInputThisFrame = false; releaseRequired = true;
    }
}
