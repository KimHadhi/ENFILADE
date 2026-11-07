using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public sealed class MovementInput
{
    private readonly PlayerMovement ctx;
    internal readonly InputAction moveAction, sprintAction, jumpAction, crouchAction, diveAction;
    private readonly InputAction dodgeAction, leanLeftAction, leanRightAction, shoveAction, interactAction;
    /// <summary>Interact (Player/Interact) was pressed this frame: also takes hold of a ladder from its top (backlog 141).</summary>
    public bool InteractPressed { get; private set; }
    private bool crouchLatched, weaponInputSought;
    private int leanLatched;
    private PlayerWeaponInput weaponInput;
    public bool JumpPressed { get; private set; }
    /// <summary>The aim/guard button (Combat/Guard) is held.</summary>
    public bool AimHeld { get; private set; }
    public bool JumpHeld { get; private set; }
    public bool CrouchPressed { get; private set; }
    /// <summary>Crouch intent: the key held, or latched by a press when the crouch toggle setting is on.</summary>
    public bool CrouchHeld { get; private set; }
    public bool DodgePressed { get; private set; }
    /// <summary>Dive / prone (Player/Dive) was pressed this frame.</summary>
    public bool DivePressed { get; private set; }
    public bool ShovePressed { get; private set; }
    public bool SprintHeld { get; private set; }
    public bool LeanPressed { get; private set; }
    public int LeanSide { get; private set; }
    internal bool SprintButtonHeld
    {
        get
        {
            if (sprintAction == null || !sprintAction.enabled) return false;
            if (sprintAction.IsPressed()) return true;
            // Sprint is a held intent. A re-enabled Button action can wait for a new press
            // even though its bound Shift/trigger is still down. Read the bound button in
            // that case; never hard-code Shift or bypass a composite modifier binding.
            foreach (var binding in sprintAction.bindings)
                if (binding.isComposite) return false;
            foreach (var control in sprintAction.controls)
                if (control is ButtonControl button && button.isPressed) return true;
            return false;
        }
    }
    internal MovementInput(PlayerMovement context)
    {
        ctx = context;
        var actions = InputSystem.actions;
        if (actions == null) return;
        moveAction = actions.FindAction("Player/Move", false);
        sprintAction = actions.FindAction("Player/Sprint", false);
        jumpAction = actions.FindAction("Player/Jump", false);
        crouchAction = actions.FindAction("Player/Crouch", false);
        diveAction = actions.FindAction("Player/Dive", false) ?? actions.FindAction("Player/Prone", false);
        dodgeAction = actions.FindAction("Player/Dodge", false);
        leanLeftAction = actions.FindAction("Player/LeanLeft", false);
        leanRightAction = actions.FindAction("Player/LeanRight", false);
        shoveAction = actions.FindAction("Player/Shove", false);
        interactAction = actions.FindAction("Player/Interact", false);
    }
    /// <summary>
    /// moving false (getting up off the back) keeps the body's own moves (jump, crouch press, dodge, dive, sprint, lean)
    /// from starting, while the hands stay free: the shove and the aim still read, and a crouch held from before still
    /// counts, since the body rose into it.
    /// </summary>
    internal void Sample(bool available, bool moving = true)
    {
        var settings = GameSettings.Current;
        bool move = available && moving;
        if (move) UpdateLatches(settings);
        JumpPressed = move && Pressed(jumpAction);
        JumpHeld = move && Held(jumpAction);
        CrouchPressed = move && Pressed(crouchAction);
        CrouchHeld = available && (settings.crouchToggle ? crouchLatched : Held(crouchAction));
        DodgePressed = move && Pressed(dodgeAction);
        DivePressed = move && Pressed(diveAction);
        InteractPressed = move && Pressed(interactAction);
        ShovePressed = available && Pressed(shoveAction);
        if (!weaponInputSought) { weaponInput = ctx.GetComponentInChildren<PlayerWeaponInput>(); weaponInputSought = true; }
        AimHeld = available && weaponInput != null && weaponInput.GuardHeld;
        SprintHeld = move && SprintButtonHeld;
        int heldSide = (Held(leanRightAction) ? 1 : 0) - (Held(leanLeftAction) ? 1 : 0);
        LeanSide = move ? (settings.leanToggle ? leanLatched : heldSide) : 0;
        LeanPressed = move && LeanSide != 0 && (Pressed(leanLeftAction) || Pressed(leanRightAction));
    }

    // Toggle mode: a press latches, a second press releases. Leaving the crouch, slide or air-tuck states (dive,
    // parkour) or pressing sprint, jump or dive releases a latched crouch, the way letting go of a held key would. Jump
    // releases it before the jump runs, so a toggled crouch never turns into a tucked crouch-jump. A press in the air
    // tucks the body and stays latched, so the landing stays crouched instead of standing straight back up. A crawl
    // under a low roof keeps it, so leaving the passage comes back to the crouch; going prone with dive lets it go.
    private void UpdateLatches(GameSettings settings)
    {
        bool sprintPressed = Pressed(sprintAction);
        bool crouchState = ctx.CurrentState == PlayerMovement.MovementState.Crouching || ctx.IsSliding
            || ctx.IsAirCrouching || ctx.CurrentState == PlayerMovement.MovementState.Proning;
        if (!settings.crouchToggle || !crouchState || sprintPressed || Pressed(jumpAction) || Pressed(diveAction))
            crouchLatched = false;
        if (settings.crouchToggle && Pressed(crouchAction)) crouchLatched = !crouchLatched;

        bool leanState = ctx.CurrentState == PlayerMovement.MovementState.Standing
            || ctx.CurrentState == PlayerMovement.MovementState.Crouching;
        if (!settings.leanToggle || !leanState || sprintPressed) leanLatched = 0;
        int pressedSide = (Pressed(leanRightAction) ? 1 : 0) - (Pressed(leanLeftAction) ? 1 : 0);
        if (settings.leanToggle && pressedSide != 0) leanLatched = leanLatched == pressedSide ? 0 : pressedSide;
    }
    private static bool Held(InputAction action) => action != null && action.IsPressed();
    private static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();
    public Vector2 Move => moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
}
