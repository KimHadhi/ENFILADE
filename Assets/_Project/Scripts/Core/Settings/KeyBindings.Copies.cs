using UnityEngine;
using UnityEngine.InputSystem;

// KeyBindings: Copies of a map. One part of the class; the rest is in KeyBindings.cs and the files beside it.
public static partial class KeyBindings
{
    /// <summary>
    /// A component's own copy of an action map (PlayerWeaponInput, PlayerSpectate keep one so another's enabling never
    /// touches theirs). A plain Clone keeps the keys of the moment it was made; this one takes every later change too.
    /// Hand it to <see cref="Forget"/> before disposing of it.
    /// </summary>
    public static InputActionMap Copy(InputActionMap source)
    {
        if (source == null) return null;
        EnsureRestored();
        var copy = source.Clone();
        copies.Add((source, copy));
        return copy;
    }

    public static void Forget(InputActionMap copy)
    {
        for (int i = copies.Count - 1; i >= 0; i--)
            if (copies[i].copy == copy) copies.RemoveAt(i);
    }

    // The copies' bindings stand in the source's order (Clone keeps it), so each takes its source's override by index.
    private static void SyncCopies()
    {
        for (int i = copies.Count - 1; i >= 0; i--)
        {
            var (source, copy) = copies[i];
            if (source == null || copy == null) { copies.RemoveAt(i); continue; }
            var from = source.bindings;
            int count = Mathf.Min(from.Count, copy.bindings.Count);
            for (int b = 0; b < count; b++)
            {
                var have = copy.bindings[b];
                if (have.overridePath == from[b].overridePath && have.overrideInteractions == from[b].overrideInteractions
                    && have.overrideProcessors == from[b].overrideProcessors) continue;
                copy.ApplyBindingOverride(b, from[b]);
            }
        }
    }
}
