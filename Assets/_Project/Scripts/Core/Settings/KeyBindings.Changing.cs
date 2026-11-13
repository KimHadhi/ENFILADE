using System;
using UnityEngine;
using UnityEngine.InputSystem;

// KeyBindings: Changing keys. One part of the class; the rest is in KeyBindings.cs and the files beside it.
public static partial class KeyBindings
{
    /// <summary>
    /// Waits for the next key or mouse button and puts it in the line's first (<paramref name="which"/> 0) or second (1)
    /// slot; Esc keeps the old key. A key another line of the same group already uses is swapped: that line gets this
    /// slot's old key. <paramref name="done"/> is told what was swapped (null when nothing was, or it was cancelled).
    /// False when the slot doesn't exist.
    /// </summary>
    public static bool Listen(Entry entry, int which, Action<string> done)
    {
        var action = Find(entry.Action);
        int index = Slot(entry, which);
        if (action == null || index < 0) return false;
        Cancel();
        bool wasOn = action.enabled;
        if (wasOn) action.Disable();
        string picked = null;
        running = action.PerformInteractiveRebinding(index)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsHavingToMatchPath("<Mouse>")
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithControlsExcluding("<Mouse>/press")
            .WithControlsExcluding("<Pointer>/press")
            .WithControlsExcluding("<Keyboard>/anyKey")
            .WithCancelingThrough("<Keyboard>/escape")
            // The swap rules decide where the key goes, not the operation.
            .OnApplyBinding((operation, path) => picked = path)
            .OnComplete(operation => Finish(operation, entry, which, picked, action, wasOn, done))
            .OnCancel(operation => Finish(operation, entry, which, null, action, wasOn, done))
            .Start();
        return true;
    }

    /// <summary>Stops listening for a key (the old one stays).</summary>
    public static void Cancel() => running?.Cancel();

    private static void Finish(InputActionRebindingExtensions.RebindingOperation operation, Entry entry, int which, string picked,
        InputAction action, bool wasOn, Action<string> done)
    {
        operation.Dispose();
        if (running == operation) running = null;
        endedFrame = Time.frameCount;
        if (wasOn) action.Enable();
        string swapped = picked != null ? Assign(entry, which, picked) : null;
        if (picked != null) Save();
        done?.Invoke(swapped);
    }

    /// <summary>Empties one slot of a line.</summary>
    public static void Clear(Entry entry, int which)
    {
        var action = Find(entry.Action);
        int index = Slot(entry, which);
        if (action == null || index < 0) return;
        Put(action, index, "");
        Save();
    }

    /// <summary>Puts a line's keys back to the asset's; a line that held one of them gives it up for this line's old one.</summary>
    public static string Reset(Entry entry)
    {
        var action = Find(entry.Action);
        if (action == null) return null;
        // Both slots first, so a default that sat in the line's other slot isn't taken for a clash with itself.
        var index = new[] { Slot(entry, 0), Slot(entry, 1) };
        var before = new[] { PathOf(action, index[0]), PathOf(action, index[1]) };
        for (int which = 0; which < 2; which++)
            if (index[which] >= 0 && !IsDefault(action, index[which])) action.RemoveBindingOverride(index[which]);
        string swapped = null;
        for (int which = 0; which < 2; which++)
        {
            if (index[which] < 0 || Same(before[which], PathOf(action, index[which]))) continue;
            // An old key the line still has after the reset isn't handed on.
            string give = Overlaps(before[which], PathOf(action, index[0])) || Overlaps(before[which], PathOf(action, index[1]))
                ? "" : before[which];
            swapped = GiveWay(entry, action, index[which], give) ?? swapped;
        }
        Save();
        return swapped;
    }

    /// <summary>Every key back to the asset's.</summary>
    public static void ResetAll()
    {
        Cancel();
        InputSystem.actions.OrNull()?.RemoveAllBindingOverrides();
        Save();
    }

    // Puts the key in the slot, then the other lines make way (GiveWay). Returns the swap notice, or null.
    private static string Assign(Entry entry, int which, string path)
    {
        var action = Find(entry.Action);
        int index = Slot(entry, which);
        if (action == null || index < 0) return null;
        path ??= "";
        string before = PathOf(action, index);
        if (Same(before, path)) return null;
        // The line's other slot already has it: one key twice on a line does nothing, so nothing changes.
        if (Overlaps(PathOf(action, Slot(entry, 1 - which)), path)) return null;
        Put(action, index, path);
        return GiveWay(entry, action, index, before);
    }

    // Another line of the same group whose slot overlaps the key now in this slot takes <give>, this slot's old key (the
    // first such slot), or is emptied (any after). Returns the notice for the first, or null when none clashed.
    private static string GiveWay(Entry entry, InputAction action, int index, string give)
    {
        string path = PathOf(action, index);
        if (path.Length == 0) return null;
        string notice = null;
        bool given = false;
        foreach (var other in Entries)
        {
            if (other == entry || other.Group != entry.Group) continue;
            var otherAction = Find(other.Action);
            for (int otherWhich = 0; otherWhich < 2; otherWhich++)
            {
                int otherIndex = Slot(other, otherWhich);
                if (otherAction == null || otherIndex < 0 || !Overlaps(PathOf(otherAction, otherIndex), path)) continue;
                string handed = given ? "" : give ?? "";
                Put(otherAction, otherIndex, handed);
                if (!given)
                    notice = handed.Length > 0
                        ? Texts.Format("settings.keys.swapped", PathName(action, index, path), other.Name, PathName(otherAction, otherIndex, handed))
                        : Texts.Format("settings.keys.taken", PathName(action, index, path), other.Name);
                given = true;
            }
        }
        return notice;
    }

    // An override equal to the asset's key is dropped, so the saved JSON holds only real changes.
    private static void Put(InputAction action, int index, string path)
    {
        if (Same(action.bindings[index].path ?? "", path)) action.RemoveBindingOverride(index);
        else action.ApplyBindingOverride(index, path);
    }

    private static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

    // Two keys clash when they share a physical key: "Shift" is both shift keys, so it clashes with "Left Shift".
    private static bool Overlaps(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (Same(a, b)) return true;
        return Same(Side(a), b) || Same(a, Side(b));
    }

    // The either-side form of a left or right modifier ("<Keyboard>/leftShift" → "<Keyboard>/shift"), else the key.
    private static string Side(string path)
    {
        string lower = path.ToLowerInvariant();
        foreach (var key in new[] { "shift", "ctrl", "alt" })
            if (lower == "<keyboard>/left" + key || lower == "<keyboard>/right" + key) return "<Keyboard>/" + key;
        return path;
    }

    private static void Save()
    {
        var actions = InputSystem.actions;
        GameSettings.Current.keyBindings = actions != null ? actions.SaveBindingOverridesAsJson() : "";
        GameSettings.Save();
        Announce();
    }

    private static void Announce()
    {
        SyncCopies();
        relabeled.Clear();
        names.Clear();
        bracketed.Clear();
        anyMoved = false;
        foreach (var (_, path) in Labels)
        {
            var action = Find(path);
            if (action != null && !IsDefault(action, FirstPlain(action))) anyMoved = true;
        }
        for (int i = 0; i < 4 && !anyMoved; i++)
        {
            var move = Find("Player/Move");
            if (move != null && !IsDefault(move, Slot(Entries[i], 0))) anyMoved = true;
        }
        Changed?.Invoke();
    }

    private static int FirstPlain(InputAction action)
    {
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
            if (!bindings[i].isComposite && !bindings[i].isPartOfComposite) return i;
        return -1;
    }
}
