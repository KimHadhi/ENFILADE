using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public sealed class MovementInput
{
    private readonly PlayerMovement ctx;
    internal readonly InputAction moveAction, sprintAction, jumpAction, crouchAction, diveAction;
    // Interact is not sampled here: it is shared by everything the key does, and whose a press is belongs to its one
    // owner (InteractKey), which a ladder's top asks like the rest (MovementFixtures).
    private readonly InputAction dodgeAction, leanLeftAction, leanRightAction, shoveAction, walkModeAction;
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
    public bool LeanPressed { get; private set; }
    public int LeanSide { get; private set; }
    /// <summary>
    /// Walk mode is on (Player/WalkMode, Caps Lock: a press turns it on, the next off): the keys walk, and jog with Shift,
    /// instead of running and sprinting (MovementGait). The game's own switch, not the keyboard's Caps Lock light, which
    /// another window can change behind the game's back.
    /// </summary>
    public bool WalkMode { get; private set; }
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
        walkModeAction = actions.FindAction("Player/WalkMode", false);
    }
    /// <summary>
    /// The input frame the movement reads. available false (the keys are a screen's or another window's, control is
    /// locked) makes every button read as let go and the stick as zero: that is all "whose the keys are" ever decides,
    /// and the states go on from that frame by their own rules (a hang hangs on, a slide slides out, a held key's move
    /// ends as if the key were released).
    /// moving false (getting up off the back) keeps the body's own moves (jump, crouch press, dodge, dive, sprint, lean)
    /// from starting, while the hands stay free: the shove and the aim still read, and a crouch held from before still
    /// counts, since the body rose into it.
    /// </summary>
    internal void Sample(bool available, bool moving = true)
    {
        var settings = GameSettings.Current;
        bool move = available && moving;
        if (move) UpdateLatches(settings);
        JumpPressed = move && InputFocus.Pressed(jumpAction);
        JumpHeld = move && Held(jumpAction);
        CrouchPressed = move && InputFocus.Pressed(crouchAction);
        // A crouch taken on toggle is the body's posture, not a key held down: it stays while a screen has the keys (a
        // word typed crouched behind a zombie stood the player up in its sight). A key held down reads as let go then.
        CrouchHeld = settings.crouchToggle ? crouchLatched : available && Held(crouchAction);
        DodgePressed = move && InputFocus.Pressed(dodgeAction);
        DivePressed = move && InputFocus.Pressed(diveAction);
        ShovePressed = available && InputFocus.Pressed(shoveAction);
        // A switch, not a move: it flips whenever the keys are the game's, getting up off the back included.
        if (available && InputFocus.Pressed(walkModeAction)) WalkMode = !WalkMode;
        if (!weaponInputSought) { weaponInput = ctx.GetComponentInChildren<PlayerWeaponInput>(); weaponInputSought = true; }
        AimHeld = available && weaponInput != null && weaponInput.GuardHeld;
        
        int heldSide = (Held(leanRightAction) ? 1 : 0) - (Held(leanLeftAction) ? 1 : 0);
        // As the crouch: a lean on toggle stays put while a screen has the keys; a lean on a held key reads as let go.
        LeanSide = settings.leanToggle ? leanLatched : move ? heldSide : 0;
        LeanPressed = move && LeanSide != 0 && (InputFocus.Pressed(leanLeftAction) || InputFocus.Pressed(leanRightAction));
    }

    // Toggle mode: a press latches, a second press releases. Leaving the crouch, slide or air-tuck states (dive,
    // parkour) or pressing sprint, jump or dive releases a latched crouch, the way letting go of a held key would. Jump
    // releases it before the jump runs, so a toggled crouch never turns into a tucked crouch-jump. A press in the air
    // tucks the body and stays latched, so the landing stays crouched instead of standing straight back up. A crawl
    // under a low roof keeps it, so leaving the passage comes back to the crouch; going prone with dive lets it go.
    // A step dodge keeps it too (user, 2026-11-11: crouch on toggle exactly so as not to hold the key, and every dodge
    // stood them up): one taken crouched comes back down to the crouch (MovementAbilities.ReturnToLocomotion), and a
    // press during a dodge flips it for when the dodge ends.
    private void UpdateLatches(GameSettings settings)
    {
        bool sprintPressed = InputFocus.Pressed(sprintAction);
        // On a ladder, and through the climb off its top, a latched crouch is kept too: the body comes back down to the
        // crouch at the top, so a sneak that goes by a ladder stays a sneak.
        bool onLadder = ctx.CurrentState == PlayerMovement.MovementState.Ladder
            || (ctx.CurrentState == PlayerMovement.MovementState.Mantle && ctx.Abilities != null && ctx.Abilities.Traversal.Obstacle.ExitLadder != null);
        bool crouchState = ctx.CurrentState == PlayerMovement.MovementState.Crouching || ctx.IsSliding
            || ctx.IsAirCrouching || ctx.CurrentState == PlayerMovement.MovementState.Proning
            || ctx.CurrentState == PlayerMovement.MovementState.StepDodge || onLadder;
        if (!settings.crouchToggle || !crouchState || sprintPressed || InputFocus.Pressed(jumpAction) || InputFocus.Pressed(diveAction))
            crouchLatched = false;
        if (settings.crouchToggle && InputFocus.Pressed(crouchAction)) crouchLatched = !crouchLatched;

        bool leanState = ctx.CurrentState == PlayerMovement.MovementState.Standing
            || ctx.CurrentState == PlayerMovement.MovementState.Crouching;
        if (!settings.leanToggle || !leanState || sprintPressed) leanLatched = 0;
        int pressedSide = (InputFocus.Pressed(leanRightAction) ? 1 : 0) - (InputFocus.Pressed(leanLeftAction) ? 1 : 0);
        if (settings.leanToggle && pressedSide != 0) leanLatched = leanLatched == pressedSide ? 0 : pressedSide;
    }
    private static bool Held(InputAction action) => action != null && action.IsPressed();

    /// <summary>
    /// The shove key went down this frame, whether or not control is locked: pinned by a runner the player lies locked
    /// in a heavy landing, and ShovePressed stays false all the while, but V still bursts the shield (PlayerHold).
    /// </summary>
    internal bool ShoveKeyPressed => InputFocus.Pressed(shoveAction);
    public Vector2 Move => moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
}
