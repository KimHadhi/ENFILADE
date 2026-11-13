using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
public static partial class KeyBindings
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

    private static readonly Dictionary<InputAction, string> names = new Dictionary<InputAction, string>(),
        bracketed = new Dictionary<InputAction, string>();

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
    private static readonly Dictionary<string, string> relabeled = new Dictionary<string, string>();
}
