using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// KeyBindings: Finding and reading. One part of the class; the rest is in KeyBindings.cs and the files beside it.
public static partial class KeyBindings
{
    /// <summary>The project's action at "Map/Action", with the player's keys on it; null when the asset lacks it.</summary>
    public static InputAction Find(string path)
    {
        var actions = InputSystem.actions;
        if (actions == null || string.IsNullOrEmpty(path)) return null;
        EnsureRestored();
        if (cachedFor != actions) { found.Clear(); cachedFor = actions; }
        if (!found.TryGetValue(path, out var action) || action == null)
        {
            action = actions.FindAction(path, false);
            if (action != null) found[path] = action;
        }
        return action;
    }

    /// <summary>
    /// The action's key went down this frame, as the game reads it: never while a screen has the key (InputHold.Keeps:
    /// the chat or a menu holding what the action belongs to, a board's pointer out over a key that sits on a mouse
    /// button). Whose a press is is answered here, once, for every reader.
    /// Until 2026-11-18 a key whose map a screen had switched off was read straight from its bound controls, and each
    /// caller's own checks were to decide whose the press was. They asked about the body's keys, so with a board's
    /// pointer out (which takes the hands and leaves the legs) a click on the board was still the guard key's or the
    /// fire key's to every caller that read one.
    /// </summary>
    public static bool Pressed(InputAction action) => Read(action, null, true);

    /// <summary>The action's key is down, as the game reads it (see <see cref="Pressed(InputAction)"/>).</summary>
    public static bool Held(InputAction action) => Read(action, null, false);

    /// <summary>
    /// A screen's own key went down this frame, read by the screen that holds the player's keys: the key that closes
    /// the shop is the body's F, which the shop itself has taken. What <paramref name="screen"/> holds doesn't keep the
    /// key from it (its action is off then, and the bound controls are read straight); what another screen holds does,
    /// so a key typed into the console over the shop is not the shop's.
    /// </summary>
    public static bool Pressed(InputAction action, UnityEngine.Object screen) => Read(action, screen, true);

    public static bool Pressed(string path) => Pressed(Find(path));

    public static bool Held(string path) => Held(Find(path));

    public static bool Pressed(string path, UnityEngine.Object screen) => Pressed(Find(path), screen);

    private static bool Read(InputAction action, UnityEngine.Object screen, bool down)
    {
        if (action == null || InputHold.Keeps(action, screen)) return false;
        if (action.enabled) return down ? action.WasPressedThisFrame() : action.IsPressed();
        // Off, and no screen keeps it from the reader: the asking screen's own hold has it off, or the key window is
        // listening on it. The bound controls, read straight.
        foreach (var control in action.controls)
            if (control is ButtonControl button && (down ? button.wasPressedThisFrame : button.isPressed)) return true;
        return false;
    }
}
