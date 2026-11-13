using System;
using UnityEngine.InputSystem;

// KeyBindings: Lines, slots and names. One part of the class; the rest is in KeyBindings.cs and the files beside it.
public static partial class KeyBindings
{
    /// <summary>
    /// Where a line's first (<paramref name="which"/> 0) or second (1) key is among its action's bindings; -1 when the
    /// asset has no such slot. A move direction's slots are that part in the first and the second WASD set.
    /// </summary>
    public static int Slot(Entry entry, int which)
    {
        var action = entry != null ? Find(entry.Action) : null;
        if (action == null) return -1;
        var bindings = action.bindings;
        if (entry.Part == null)
        {
            int seen = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isComposite || bindings[i].isPartOfComposite) continue;
                if (seen++ == which) return i;
            }
            return -1;
        }
        int set = -1;
        for (int i = 0; i < bindings.Count; i++)
        {
            if (bindings[i].isComposite) { set++; continue; }
            if (bindings[i].isPartOfComposite && set == which && string.Equals(bindings[i].name, entry.Part, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>The key in this slot now ("" for none).</summary>
    public static string PathOf(InputAction action, int index) =>
        action != null && index >= 0 && index < action.bindings.Count ? action.bindings[index].effectivePath ?? "" : "";

    public static bool IsDefault(InputAction action, int index) =>
        action == null || index < 0 || index >= action.bindings.Count || !action.bindings[index].hasOverrides;

    /// <summary>Both of a line's keys are as the asset has them.</summary>
    public static bool IsDefault(Entry entry)
    {
        var action = Find(entry.Action);
        return IsDefault(action, Slot(entry, 0)) && IsDefault(action, Slot(entry, 1));
    }

    /// <summary>How many lines have a key the player changed.</summary>
    public static int ChangedCount()
    {
        int count = 0;
        foreach (var entry in Entries) if (!IsDefault(entry)) count++;
        return count;
    }

    /// <summary>The key of one slot as it reads on screen: mouse buttons in Korean, a key by its own short name, "" for none.</summary>
    public static string KeyName(InputAction action, int index) => PathName(action, index, PathOf(action, index));

    private static string PathName(InputAction action, int index, string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        switch (path)
        {
            case "<Mouse>/middleButton": return Texts.Get("key.mouse.middle");
            case "<Mouse>/leftButton": return Texts.Get("key.mouse.left");
            case "<Mouse>/rightButton": return Texts.Get("key.mouse.right");
            case "<Mouse>/forwardButton": return Texts.Get("key.mouse.forward");
            case "<Mouse>/backButton": return Texts.Get("key.mouse.back");
        }
        string name = action != null && index >= 0 && PathOf(action, index) == path
            ? action.GetBindingDisplayString(index, InputBinding.DisplayStringOptions.DontIncludeInteractions)
            : InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        return string.IsNullOrEmpty(name) ? path : name.ToUpperInvariant();
    }

    /// <summary>
    /// What the action's key reads as on screen (prompts, HUD chips): its first key, else its second, else "없음". Kept
    /// until a key changes, so a prompt can ask every frame without making garbage.
    /// </summary>
    public static string Name(InputAction action)
    {
        if (action == null) return "?";
        if (names.TryGetValue(action, out string known)) return known;
        string name = null;
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count && name == null; i++)
        {
            if (bindings[i].isComposite || bindings[i].isPartOfComposite) continue;
            string key = KeyName(action, i);
            if (key.Length > 0) name = key;
        }
        name ??= Texts.Get("key.none");
        names[action] = name;
        return name;
    }

    public static string Name(string path) => Name(Find(path));

    /// <summary>The action's key in brackets, "[F]", the way prompts write it (kept like <see cref="Name(InputAction)"/>).</summary>
    public static string Bracketed(string path)
    {
        var action = Find(path);
        if (action == null) return "[?]";
        if (bracketed.TryGetValue(action, out string known)) return known;
        string words = "[" + Name(action) + "]";
        bracketed[action] = words;
        return words;
    }
}
