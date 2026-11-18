using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(FrameOrder.WeaponInput)]
public sealed class PlayerWeaponInput : MonoBehaviour, IWeaponInput
{
    [Tooltip("Optional. Uses the project's InputSystem.actions when empty; reads only the Combat action map.")]
    [SerializeField] private InputActionAsset inputActions;
    private InputActionMap ownedMap;
    private InputAction fire, reload, guard, shove;
    private bool meleeReleaseRequired = true;
    private bool acceptInputThisFrame, releaseRequired = true;
    // The guard button's present press went down while a screen had the hands: it is that screen's click (the right
    // click that puts the Tab board's pointer away), not an aim or a guard, until it has been let go. A guard held from
    // before the screen comes back with the hands, as a held walk key does.
    private bool guardIsScreens;

    private void OnEnable()
    {
        Release();
        fire = reload = null;
        var asset = inputActions != null ? inputActions : InputSystem.actions;
        var map = asset != null ? asset.FindActionMap("Combat", false) : null;
        if (map == null) { Debug.LogError("PlayerWeaponInput needs Combat/Fire and Combat/Reload in the input asset.", this); return; }
        // Its own copy, which still takes every key the player changes later in the key window (KeyBindings.Copy).
        ownedMap = KeyBindings.Copy(map); fire = ownedMap.FindAction("Fire", false); reload = ownedMap.FindAction("Reload", false);
        guard = ownedMap.FindAction("Guard", false);
        shove = asset.FindAction("Player/Shove", false);
        if (fire == null || reload == null)
        { Debug.LogError("Combat input map is missing Fire or Reload.", this); Release(); return; }
        ownedMap.Enable(); releaseRequired = meleeReleaseRequired = true; acceptInputThisFrame = false;
    }

    private void Update()
    {
        // Read early in the frame: a screen that closes on a click later this frame still had the hands when the click came.
        // Who holds the hands is asked (InputFocus.TakesCombat: a screen's hold of Combat, or of the pointer, whose
        // clicks are not the trigger's), not whether the pointer is locked; this map is this component's own copy, which
        // InputHold doesn't switch off.
        acceptInputThisFrame = Time.timeScale > 0f && InputFocus.TakesCombat();
        if (!acceptInputThisFrame) releaseRequired = meleeReleaseRequired = true;
        if (guard == null || !guard.IsPressed()) guardIsScreens = false;
        else if (!acceptInputThisFrame && guard.WasPressedThisFrame()) guardIsScreens = true;
    }

    // The guard button is down and the press is the player's own (see guardIsScreens).
    private bool GuardDown => guard != null && guard.IsPressed() && !guardIsScreens;

    public WeaponInputFrame ReadInput()
    {
        var slots = GetComponent<PlayerWeaponSlots>();
        if (slots != null && !slots.CanReadInput) { releaseRequired = true; return default; }
        // Asked again here, so a screen that took the hands since Update (Esc this frame) also blocks firing in that frame.
        if (!isActiveAndEnabled || !acceptInputThisFrame || fire == null || reload == null
            || Time.timeScale <= 0f || !InputFocus.TakesCombat())
            return default;
        if (releaseRequired)
        {
            if (!fire.IsPressed()) releaseRequired = false;
            return default;
        }
        return new WeaponInputFrame(fire.WasPressedThisFrame(), fire.IsPressed(), reload.WasPressedThisFrame(), GuardDown);
    }

    public bool TryReadMeleeInput(out MeleeInputFrame frame)
    {
        frame = default;
        var slots = GetComponent<PlayerWeaponSlots>();
        // The chat or a menu holds the hands (InputHold); this component's own copy of the map must not get round that.
        bool available = isActiveAndEnabled && acceptInputThisFrame && slots != null && slots.CanReadInput
            && fire != null && guard != null && InputFocus.TakesCombat();
        if (!available) { meleeReleaseRequired = true; return false; }
        if (meleeReleaseRequired)
        {
            if (!fire.IsPressed() && !guard.IsPressed() && (shove == null || !shove.IsPressed())) meleeReleaseRequired = false;
            return false;
        }
        frame = new MeleeInputFrame(fire.WasPressedThisFrame(), fire.IsPressed(), fire.WasReleasedThisFrame(),
            guard.WasPressedThisFrame(), GuardDown, shove != null && shove.WasPressedThisFrame(),
            shove != null && shove.IsPressed(), shove != null && shove.WasReleasedThisFrame());
        return true;
    }

    public void RequireTriggerRelease() { releaseRequired = meleeReleaseRequired = true; }

    /// <summary>The aim/guard button is held, for moves that read it (a slide ends lying on the back).</summary>
    public bool GuardHeld => isActiveAndEnabled && acceptInputThisFrame && GuardDown;

    // A guard-break animation stays locked, but may remember a defensive press near its end.
    public bool TryReadGuardBuffer(out MeleeInputFrame frame)
    {
        frame = default;
        var movement = GetComponent<PlayerMovement>();
        // acceptInputThisFrame already says no screen holds the hands; it used to look at the Player map being on for that.
        if (!isActiveAndEnabled || !acceptInputThisFrame || guard == null || movement == null
            || !movement.InputAvailable || movement.Execution.OrNull()?.IsExecuting == true) return false;
        frame = new MeleeInputFrame(guardPressed: guard.WasPressedThisFrame(), guardHeld: GuardDown);
        return true;
    }

    private void OnApplicationFocus(bool focused) { if (!focused) { acceptInputThisFrame = false; releaseRequired = meleeReleaseRequired = true; } }
    private void OnDisable()
    {
        Release(); fire = reload = null;
        acceptInputThisFrame = false; releaseRequired = meleeReleaseRequired = true; guard = shove = null; guardIsScreens = false;
    }

    private void Release()
    {
        if (ownedMap == null) return;
        KeyBindings.Forget(ownedMap);
        ownedMap.Disable();
        ownedMap.Dispose();
        ownedMap = null;
    }
}
