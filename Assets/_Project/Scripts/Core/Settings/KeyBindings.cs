using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// The keys the player picks for themself. Every key the game reads is an action of the project's actions
/// (Input/Player.inputactions, whose keys are the defaults), so the key window (Settings → 조작 → 키 설정,
/// <see cref="KeyBindingsView"/>; user 2026-11-12: "키 설정 창을 아예 따로 만들었으면 좋겠음. 그래서 키들 싹 다
/// 커스텀할 수 있게끔") lists exactly what the game listens to. A changed key is an Input System binding override, kept
/// as JSON in GameSettings.keyBindings and put back on every start. Each line of the window (<see cref="Entries"/>) has a
/// first and a second key: the second is an empty binding in the asset until the player fills it.
/// Code that reads a key a menu holds (its action map is off while a shop or the perk cards are up) asks
/// <see cref="Pressed"/>/<see cref="Held"/>, which then read the bound controls straight, as a hard-coded key read used to.
/// Maps cloned for a component's own use (<see cref="Copy"/>) follow every change. Words in Texts.csv that name a
/// default key in brackets ("[F] 문 열기") show the key the player chose instead (<see cref="Relabel"/>).
/// </summary>
public static class KeyBindings
{
    /// <summary>Lines in the same group can't share a key; the chat's own key is only read while typing in the chat.</summary>
    public enum Group { Play, Chat }

    /// <summary>One line of the key window: an action, or one direction of the move keys (<see cref="Part"/>).</summary>
    public sealed class Entry
    {
        public readonly string Section, Id, Action, Part;
        public readonly Group Group;

        public Entry(string section, string id, string action, string part = null, Group group = Group.Play)
        {
            Section = section; Id = id; Action = action; Part = part; Group = group;
        }

        /// <summary>The line's name (Texts settings.keys.&lt;id&gt;).</summary>
        public string Name => Texts.Get("settings.keys." + Id);
    }

    /// <summary>
    /// The key window's lines in order, under their sections (Texts settings.keys.section.&lt;section&gt;). An action read
    /// anywhere in the game belongs here; one that isn't here can't be changed by the player.
    /// </summary>
    public static readonly Entry[] Entries =
    {
        new Entry("move", "move_up", "Player/Move", "up"),
        new Entry("move", "move_down", "Player/Move", "down"),
        new Entry("move", "move_left", "Player/Move", "left"),
        new Entry("move", "move_right", "Player/Move", "right"),
        new Entry("move", "sprint", "Player/Sprint"),
        new Entry("move", "jump", "Player/Jump"),
        new Entry("move", "crouch", "Player/Crouch"),
        new Entry("move", "dive", "Player/Dive"),
        new Entry("move", "dodge", "Player/Dodge"),
        new Entry("move", "lean_left", "Player/LeanLeft"),
        new Entry("move", "lean_right", "Player/LeanRight"),
        new Entry("move", "interact", "Player/Interact"),
        new Entry("combat", "fire", "Combat/Fire"),
        new Entry("combat", "guard", "Combat/Guard"),
        new Entry("combat", "reload", "Combat/Reload"),
        new Entry("combat", "shove", "Player/Shove"),
        new Entry("combat", "fire_mode", "Player/FireMode"),
        new Entry("combat", "inspect", "Player/Inspect"),
        new Entry("combat", "flashlight", "Player/Flashlight"),
        new Entry("combat", "customize", "Player/Customize"),
        new Entry("combat", "throw", "Player/Throw"),
        new Entry("combat", "grenade", "Player/Grenade"),
        new Entry("combat", "knock", "Player/Knock"),
        new Entry("items", "slot1", "Combat/Slot1"),
        new Entry("items", "slot2", "Combat/Slot2"),
        new Entry("items", "slot3", "Combat/Slot3"),
        new Entry("items", "slot4", "Combat/Slot4"),
        new Entry("items", "drop_money", "Player/DropMoney"),
        new Entry("class", "class", "Player/ClassAbility"),
        new Entry("class", "perk", "Player/PerkCards"),
        new Entry("class", "callout", "Player/Callout"),
        new Entry("screen", "scoreboard", "UI/OpenObjectiveBoard"),
        new Entry("screen", "chat", "UI/OpenChat"),
        new Entry("screen", "chat_channel", "UI/CycleChatChannel", null, Group.Chat),
        new Entry("screen", "third_person", "Player/ThirdPerson"),
        new Entry("screen", "console", "UI/Console"),
    };

    /// <summary>A key went in, came out or went back to its default: everything showing a key reads it again.</summary>
    public static event Action Changed;

    /// <summary>A key is being listened for, or the listening ended this frame (its Esc or click must not do more).</summary>
    public static bool Busy => running != null || endedFrame == Time.frameCount;

    private static InputActionRebindingExtensions.RebindingOperation running;
    private static int endedFrame = -1;
    private static bool restored;
    private static InputActionAsset cachedFor;
    private static readonly Dictionary<string, InputAction> found = new Dictionary<string, InputAction>();
    private static readonly List<(InputActionMap source, InputActionMap copy)> copies = new List<(InputActionMap, InputActionMap)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        running = null;
        endedFrame = -1;
        restored = false;
        cachedFor = null;
        found.Clear();
        copies.Clear();
        relabeled.Clear();
        names.Clear();
        bracketed.Clear();
        anyMoved = false;
        Changed = null;
    }

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
        if (!string.IsNullOrEmpty(json))
        {
            try { actions.LoadBindingOverridesFromJson(json); }
            catch (Exception problem) { Debug.LogWarning("[Keys] The saved keys couldn't be read (" + problem.Message + "); every key is back to its default."); }
        }
        Announce();
    }

    // ---- Finding and reading ----

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
    /// The action's key went down this frame. While its map is off (a menu holds the player's keys), its bound controls
    /// are read straight, so a menu's own close key still works and a caller's checks decide as they did before.
    /// </summary>
    public static bool Pressed(InputAction action)
    {
        if (action == null) return false;
        if (action.enabled) return action.WasPressedThisFrame();
        foreach (var control in action.controls)
            if (control is ButtonControl button && button.wasPressedThisFrame) return true;
        return false;
    }

    /// <summary>The action's key is down (read straight from its controls while its map is off, as <see cref="Pressed"/>).</summary>
    public static bool Held(InputAction action)
    {
        if (action == null) return false;
        if (action.enabled) return action.IsPressed();
        foreach (var control in action.controls)
            if (control is ButtonControl button && button.isPressed) return true;
        return false;
    }

    public static bool Pressed(string path) => Pressed(Find(path));
    public static bool Held(string path) => Held(Find(path));

    // ---- Copies of a map ----

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

    // ---- Lines, slots and names ----

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

    private static readonly Dictionary<InputAction, string> names = new Dictionary<InputAction, string>(),
        bracketed = new Dictionary<InputAction, string>();

    // ---- Changing keys ----

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
        InputSystem.actions?.RemoveAllBindingOverrides();
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

    // ---- Words that name a key ----

    // The default keys Texts.csv writes in brackets, and whose key each stands for.
    private static readonly (string label, string action)[] Labels =
    {
        ("F", "Player/Interact"), ("V", "Player/Shove"), ("C", "Player/Customize"), ("L", "Player/Flashlight"),
        ("G", "Player/Grenade"), ("J", "Player/DropMoney"), ("T", "Player/Throw"), ("H", "Player/Knock"), ("X", "Player/PerkCards"),
        ("B", "Player/FireMode"), ("Y", "Player/Inspect"), ("R", "Combat/Reload"), ("Q", "Player/LeanLeft"),
        ("E", "Player/LeanRight"), ("Z", "Player/Dive"), ("좌클릭", "Combat/Fire"), ("LMB", "Combat/Fire"),
        ("우클릭", "Combat/Guard"), ("RMB", "Combat/Guard"), ("Space", "Player/Jump"), ("Shift", "Player/Sprint"),
        ("Ctrl", "Player/Crouch"), ("휠", "Player/ClassAbility"), ("Tab", "UI/OpenObjectiveBoard"),
    };
    private const string MoveLabel = "WASD";
    private static readonly Dictionary<string, string> relabeled = new Dictionary<string, string>();
    private static bool anyMoved;

    /// <summary>
    /// The words with each bracketed default key ("[F]", "[좌클릭·F]", "[WASD]") read as the key the player put on that
    /// action, so every prompt follows the key window (Texts.Get calls it). Untouched while every such key is its default.
    /// </summary>
    public static string Relabel(string words)
    {
        if (!anyMoved || !Application.isPlaying || string.IsNullOrEmpty(words) || words.IndexOf('[') < 0) return words;
        if (relabeled.TryGetValue(words, out string done)) return done;
        var built = new StringBuilder(words.Length + 8);
        int at = 0;
        while (at < words.Length)
        {
            int open = words.IndexOf('[', at);
            int close = open >= 0 ? words.IndexOf(']', open + 1) : -1;
            if (open < 0 || close < 0) { built.Append(words, at, words.Length - at); break; }
            built.Append(words, at, open + 1 - at);
            var pieces = words.Substring(open + 1, close - open - 1).Split('·');
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i > 0) built.Append('·');
                built.Append(LabelFor(pieces[i].Trim()) ?? pieces[i]);
            }
            built.Append(']');
            at = close + 1;
        }
        string result = built.ToString();
        if (relabeled.Count < 2048) relabeled[words] = result;
        return result;
    }

    // The key now on the action this default key's label stands for, or null to leave the label (unknown, or unchanged).
    private static string LabelFor(string label)
    {
        if (label == MoveLabel)
        {
            var move = Find("Player/Move");
            if (move == null) return null;
            // W, A, S, D are up, left, down, right.
            var order = new[] { 0, 2, 1, 3 };
            var keys = new string[4];
            bool moved = false, single = true;
            for (int i = 0; i < 4; i++)
            {
                int index = Slot(Entries[order[i]], 0);
                moved |= !IsDefault(move, index);
                keys[i] = KeyName(move, index);
                single &= keys[i].Length == 1;
            }
            return moved ? string.Join(single ? "" : "/", keys) : null;
        }
        foreach (var (key, path) in Labels)
        {
            if (!string.Equals(key, label, StringComparison.OrdinalIgnoreCase)) continue;
            var action = Find(path);
            return action != null && !IsDefault(action, FirstPlain(action)) ? Name(action) : null;
        }
        return null;
    }
}
