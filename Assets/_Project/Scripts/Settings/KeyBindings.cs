using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keys the player picks for themself (Settings → 조작; the class key first, the user's "마우스 휠 클릭으로 해 두고 키
/// 설정에서 바꿀 수 있게", 2026-11-11): the Input System's binding overrides on the project's actions, kept as JSON in
/// GameSettings.keyBindings and put back on every start. Also the name a key reads by on screen, mouse buttons in
/// Korean.
/// </summary>
public static class KeyBindings
{
    private static InputActionRebindingExtensions.RebindingOperation running;

    private static bool restored;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Restore()
    {
        restored = false;
        EnsureRestored();
    }

    /// <summary>The saved keys are on the actions (again at first use, should the actions not have been there at start).</summary>
    public static void EnsureRestored()
    {
        if (restored || InputSystem.actions == null) return;
        restored = true;
        Apply(GameSettings.Current.keyBindings);
    }

    /// <summary>Puts these overrides on the actions (none: every key as the asset has it).</summary>
    public static void Apply(string json)
    {
        var actions = InputSystem.actions;
        if (actions == null) return;
        actions.RemoveAllBindingOverrides();
        if (string.IsNullOrEmpty(json)) return;
        try { actions.LoadBindingOverridesFromJson(json); }
        catch (Exception problem) { Debug.LogWarning("[Keys] The saved keys couldn't be read (" + problem.Message + "); every key is back to its default."); }
    }

    /// <summary>
    /// Waits for the next key or mouse button and binds the action's first binding to it (Esc cancels); done is told
    /// the name it reads by now. The new keys are saved with the settings.
    /// </summary>
    public static void Rebind(InputAction action, Action<string> done)
    {
        if (action == null) return;
        running?.Cancel();
        bool wasOn = action.enabled;
        action.Disable();
        running = action.PerformInteractiveRebinding(0)
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnComplete(operation => Finish(operation, action, wasOn, done, true))
            .OnCancel(operation => Finish(operation, action, wasOn, done, false))
            .Start();
    }

    private static void Finish(InputActionRebindingExtensions.RebindingOperation operation, InputAction action, bool wasOn,
        Action<string> done, bool keep)
    {
        operation.Dispose();
        if (running == operation) running = null;
        if (wasOn) action.Enable();
        if (keep)
        {
            GameSettings.Current.keyBindings = InputSystem.actions != null ? InputSystem.actions.SaveBindingOverridesAsJson() : "";
            GameSettings.Save();
        }
        done?.Invoke(Name(action));
    }

    /// <summary>What the action's key reads as on screen: 휠 for the wheel button, the key's own short name otherwise.</summary>
    public static string Name(InputAction action)
    {
        if (action == null || action.bindings.Count == 0) return "?";
        string path = action.bindings[0].effectivePath ?? "";
        switch (path)
        {
            case "<Mouse>/middleButton": return Texts.Get("key.mouse.middle");
            case "<Mouse>/leftButton": return Texts.Get("key.mouse.left");
            case "<Mouse>/rightButton": return Texts.Get("key.mouse.right");
            case "<Mouse>/forwardButton": return Texts.Get("key.mouse.forward");
            case "<Mouse>/backButton": return Texts.Get("key.mouse.back");
        }
        string name = action.GetBindingDisplayString(0);
        return string.IsNullOrEmpty(name) ? "?" : name.ToUpperInvariant();
    }
}
