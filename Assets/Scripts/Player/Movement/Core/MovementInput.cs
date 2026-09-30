using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public sealed class MovementInput
{
    internal readonly InputAction moveAction, sprintAction, jumpAction, crouchAction, diveAction;
    private readonly InputAction dodgeAction, leanLeftAction, leanRightAction, shoveAction;
    public bool JumpPressed { get; private set; }
    public bool JumpHeld { get; private set; }
    public bool CrouchPressed { get; private set; }
    public bool CrouchHeld { get; private set; }
    public bool DodgePressed { get; private set; }
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
    internal MovementInput()
    {
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
    }
    internal void Sample(bool available)
    {
        JumpPressed = available && Pressed(jumpAction);
        JumpHeld = available && Held(jumpAction);
        CrouchPressed = available && Pressed(crouchAction);
        CrouchHeld = available && Held(crouchAction);
        DodgePressed = available && Pressed(dodgeAction);
        ShovePressed = available && Pressed(shoveAction);
        SprintHeld = available && SprintButtonHeld;
        LeanSide = available ? (Held(leanRightAction) ? 1 : 0) - (Held(leanLeftAction) ? 1 : 0) : 0;
        LeanPressed = available && LeanSide != 0 && (Pressed(leanLeftAction) || Pressed(leanRightAction));
    }
    private static bool Held(InputAction action) => action != null && action.IsPressed();
    private static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();
    public Vector2 Move => moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
}
