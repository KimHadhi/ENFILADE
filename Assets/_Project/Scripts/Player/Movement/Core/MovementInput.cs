using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public sealed class MovementInput
{
    private readonly PlayerMovement ctx;
    internal readonly InputAction moveAction, sprintAction, jumpAction, crouchAction, diveAction;
    private readonly InputAction dodgeAction, leanLeftAction, leanRightAction, shoveAction;
    private bool crouchLatched;
    private int leanLatched, moveHeldSide;
    public bool JumpPressed { get; private set; }
    public bool JumpHeld { get; private set; }
    public bool CrouchPressed { get; private set; }
    /// <summary>Crouch intent: the key held, or latched by a press when the crouch toggle setting is on.</summary>
    public bool CrouchHeld { get; private set; }
    public bool DodgePressed { get; private set; }
    public bool ShovePressed { get; private set; }
    public bool SprintHeld { get; private set; }
    public bool LeanPressed { get; private set; }
    public int LeanSide { get; private set; }
    /// <summary>Independent horizontal press edges, including overlapping and same-frame opposing taps.</summary>
    public bool MoveLeftPressed { get; private set; }
    public bool MoveRightPressed { get; private set; }
    public int MoveSidePressed => (MoveRightPressed ? 1 : 0) - (MoveLeftPressed ? 1 : 0);
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
        // ButtonControl enables full press/release tracking on its first query. Arm it before the first fast tap,
        // otherwise a press followed by a release in one input update can be invisible on that first query.
        if (moveAction != null)
            foreach (var control in moveAction.controls)
                if (control is ButtonControl button) _ = button.wasPressedThisFrame;
    }
    internal void Sample(bool available)
    {
        var settings = GameSettings.Current;
        if (available) UpdateLatches(settings);
        JumpPressed = available && Pressed(jumpAction);
        JumpHeld = available && Held(jumpAction);
        CrouchPressed = available && Pressed(crouchAction);
        CrouchHeld = available && (settings.crouchToggle ? crouchLatched : Held(crouchAction));
        DodgePressed = available && Pressed(dodgeAction);
        ShovePressed = available && Pressed(shoveAction);
        SprintHeld = available && SprintButtonHeld;
        int heldSide = (Held(leanRightAction) ? 1 : 0) - (Held(leanLeftAction) ? 1 : 0);
        LeanSide = available ? (settings.leanToggle ? leanLatched : heldSide) : 0;
        LeanPressed = available && LeanSide != 0 && (Pressed(leanLeftAction) || Pressed(leanRightAction));
        SampleMoveSide(available);
    }

    private void SampleMoveSide(bool available)
    {
        float horizontal = Move.x;
        int side = horizontal > .5f ? 1 : horizontal < -.5f ? -1 : 0;
        MoveLeftPressed = MoveRightPressed = false;
        if (available && moveAction != null && moveAction.enabled)
        {
            bool buttonsUsed = false;
            foreach (var binding in moveAction.bindings)
            {
                if (!binding.isPartOfComposite || string.IsNullOrEmpty(binding.effectivePath) || !BindingEnabled(binding)) continue;
                bool isLeft = string.Equals(binding.name, "left", System.StringComparison.OrdinalIgnoreCase);
                bool isRight = string.Equals(binding.name, "right", System.StringComparison.OrdinalIgnoreCase);
                if (!isLeft && !isRight) continue;
                // A control can appear in several composites. The first binding alone can have another meaning.
                foreach (var control in moveAction.controls)
                {
                    if (!(control is ButtonControl button) || !InputControlPath.Matches(binding.effectivePath, control)) continue;
                    bool pressed = button.wasPressedThisFrame;
                    MoveLeftPressed |= isLeft && pressed;
                    MoveRightPressed |= isRight && pressed;
                    buttonsUsed |= button.isPressed || pressed || button.wasReleasedThisFrame;
                }
            }
            // Keep stick/other directional bindings, but never turn a key release into a press on its opposite.
            if (!buttonsUsed && side != moveHeldSide)
            {
                MoveLeftPressed = side < 0;
                MoveRightPressed = side > 0;
            }
        }
        // Sampling while input is unavailable must not create a new press when control returns.
        moveHeldSide = side;
    }

    private bool BindingEnabled(InputBinding binding)
    {
        var map = moveAction.actionMap;
        return Matches(moveAction.bindingMask, binding) && Matches(map?.bindingMask, binding)
            && Matches(map?.asset?.bindingMask, binding);
    }

    private static bool Matches(InputBinding? mask, InputBinding binding)
    {
        if (!mask.HasValue) return true;
        // InputSystem resolves ungrouped bindings under every control scheme (EmptyGroupMatchesAny).
        if (string.IsNullOrEmpty(binding.groups)) binding.groups = mask.Value.groups;
        return mask.Value.Matches(binding);
    }

    // Toggle mode: a press latches, a second press releases. Leaving the crouch, slide or air-tuck states (dive, prone,
    // parkour) or pressing sprint or jump releases a latched crouch, the way letting go of a held key would. Jump
    // releases it before the jump runs, so a toggled crouch never turns into a tucked crouch-jump. A press in the air
    // tucks the body and stays latched, so the landing stays crouched instead of standing straight back up.
    private void UpdateLatches(GameSettings settings)
    {
        bool sprintPressed = Pressed(sprintAction);
        bool crouchState = ctx.CurrentState == PlayerMovement.MovementState.Crouching || ctx.IsSliding
            || ctx.IsAirCrouching;
        if (!settings.crouchToggle || !crouchState || sprintPressed || Pressed(jumpAction)) crouchLatched = false;
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
