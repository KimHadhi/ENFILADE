using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

/// <summary>
/// What a screen takes from the player while it is up. Four things, because the player has four: the legs (Move), the
/// view (Look), the gun in the hands (Combat) and the mouse pointer (Cursor). Walking and looking were one flag until
/// 2026-11-18, and every gate asked whether the pointer was locked, so a screen could not show the pointer without also
/// stopping the walk (the user's list of that day, the Tab board: a pointer there while the character keeps moving).
/// </summary>
[System.Flags]
public enum Held
{
    None = 0,
    /// <summary>The gun in the hands: the trigger, the sights and the reload (PlayerWeaponInput), and the asset's Combat map.</summary>
    Combat = 1,
    /// <summary>The body's keys: walking, jumping, crouching and every other action of the asset's Player map but the look.</summary>
    Move = 2,
    /// <summary>
    /// The mouse pointer: free and shown instead of locked to the view. A mouse that points can't turn the view, and its
    /// click on a screen is not the trigger's, so a held pointer holds Look and Combat with it (InputHold adds them). It
    /// does not hold Move: a screen can free the pointer while the player walks on.
    /// </summary>
    Cursor = 4,
    /// <summary>Turning the view (the Player map's Look).</summary>
    Look = 8,
    Keys = Combat | Move | Look,
    All = Combat | Move | Look | Cursor,
}

/// <summary>
/// Who has the controls. The chat, the console, the shop, the perk cards, the gun bench, the hub's mirror, the results
/// show, the settings, the loading screen and the title screen each take the player's keys or the pointer while they are
/// up. Each used to switch things off and remember for itself what to switch back on, and each knew by name which other
/// screens to ask first, so two of them up at once undid each other: close the chat under an open shop and the player
/// walked and shot behind the shop; close the shop under the console and the pointer locked under it.
/// Here a screen only says what it holds (Take) and that it is done (Release). A thing is held while any screen holds
/// it: the input actions that were on come back on, and the pointer locks to the view again, when the last one lets go. A
/// screen destroyed while it held (a scene change) lets go by being gone.
/// This is the one owner: whoever reads the player's input asks here what is held (InputFocus puts the questions), never
/// whether the pointer happens to be locked. The lock is only what this does about Held.Cursor.
/// A key read by its action asks by the action (<see cref="Keeps"/>; KeyBindings.Pressed and Held do, and InputFocus.Takes):
/// the reader then needs no check of its own for whose the press is, and a click on a screen is never the game's.
/// </summary>
public static class InputHold
{
    private struct Holder
    {
        public Object Who;
        public Held What;
    }

    // The asset's two maps a screen can take keys of: the hands' and the body's. The body's one action that is the
    // view's; every other action of that map is the body's.
    private const string CombatMap = "Combat", PlayerMap = "Player", LookAction = "Look";

    private static readonly List<Holder> holders = new List<Holder>();
    // The actions this switched off, to switch back on: only those that were on.
    private static readonly List<InputAction> combatOff = new List<InputAction>(), moveOff = new List<InputAction>(),
        lookOff = new List<InputAction>();
    private static Held applied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        holders.Clear();
        combatOff.Clear();
        moveOff.Clear();
        lookOff.Clear();
        applied = Held.None;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    // The screens of a scene that was left are gone without a word: what only they held comes back now.
    private static void OnSceneUnloaded(Scene scene) => Apply();

    /// <summary>Some screen that is still there holds this (any part of it).</summary>
    public static bool Any(Held what = Held.All) => (Apply() & what) != 0;

    /// <summary>
    /// Some screen other than <paramref name="who"/> holds this: for a screen that must give way to the others while
    /// what it holds itself doesn't count (the Tab board stays up under its own pointer, the callout wheel under its own
    /// hold of the view).
    /// </summary>
    public static bool AnyBesides(Object who, Held what = Held.All)
    {
        Apply();
        return (Wanted(who) & what) != 0;
    }

    /// <summary>
    /// What a key of this action is: the hands' (the Combat map), the view's (the Player map's Look) or the body's (the
    /// rest of the Player map). Nothing for an action of any other map: no screen takes those (the UI's own keys).
    /// </summary>
    public static Held PartOf(InputAction action)
    {
        var map = action != null ? action.actionMap : null;
        if (map == null) return Held.None;
        if (map.name == CombatMap) return Held.Combat;
        if (map.name == PlayerMap) return action.name == LookAction ? Held.Look : Held.Move;
        return Held.None;
    }

    /// <summary>
    /// Some screen has this action's key, so the key is not the game's to read: a screen holds what the action belongs
    /// to (<see cref="PartOf"/>), or a screen has the pointer out and the action sits on a mouse button. A mouse that
    /// points clicks on the screen under it whatever its button is bound to, the class key on the middle button as much
    /// as the trigger on the left; such an action is the pointer's as a whole then, its second key too.
    /// <paramref name="besides"/> is a screen asking about a key of its own (the key that closes it): what it holds
    /// itself doesn't keep the key from it, what another screen holds does.
    /// </summary>
    public static bool Keeps(InputAction action, Object besides = null)
    {
        if (action == null) return false;
        Apply();
        Held held = Wanted(besides);
        if ((held & PartOf(action)) != 0) return true;
        return (held & Held.Cursor) != 0 && OnPointer(action);
    }

    // One of the action's keys is a button of the mouse (or of a pen or a touch screen, which point as it does).
    private static bool OnPointer(InputAction action)
    {
        foreach (var control in action.controls)
            if (control is ButtonControl && control.device is Pointer) return true;
        return false;
    }

    /// <summary>The screen holds these from now on. Taking again adds to what it holds.</summary>
    public static void Take(Object who, Held what = Held.All)
    {
        if (who == null || what == Held.None) return;
        int at = holders.FindIndex(h => ReferenceEquals(h.Who, who));
        if (at >= 0) holders[at] = new Holder { Who = who, What = holders[at].What | what };
        else holders.Add(new Holder { Who = who, What = what });
        Apply();
    }

    /// <summary>The screen lets go of all it held. Nothing happens when it held nothing.</summary>
    public static void Release(Object who)
    {
        // By reference: a screen in the middle of being destroyed is already "null" to Unity's ==.
        if (holders.RemoveAll(h => ReferenceEquals(h.Who, who)) > 0) Apply();
    }

    /// <summary>What is held now and by whom, a screen a line (the console's /input).</summary>
    public static string Describe()
    {
        Held all = Apply();
        var text = new StringBuilder("held: ").Append(all == Held.None ? "nothing" : all.ToString());
        foreach (var holder in holders)
            text.Append('\n').Append(holder.Who.name).Append(" (").Append(holder.Who.GetType().Name).Append("): ").Append(holder.What);
        return text.ToString();
    }

    // What the screens that are still there hold together, leaving one out when asked. A free pointer has the mouse, so
    // the view and the trigger go with it (Held.Cursor).
    private static Held Wanted(Object without = null)
    {
        Held wanted = Held.None;
        for (int i = holders.Count - 1; i >= 0; i--)
        {
            if (holders[i].Who == null) holders.RemoveAt(i);
            else if (!ReferenceEquals(holders[i].Who, without)) wanted |= holders[i].What;
        }
        if ((wanted & Held.Cursor) != 0) wanted |= Held.Look | Held.Combat;
        return wanted;
    }

    // Brings the input actions and the pointer in line with what is held now; returns what is held.
    private static Held Apply()
    {
        Held wanted = Wanted();
        if (wanted == applied) return wanted;
        Switch(wanted, Held.Combat, CombatMap, null, combatOff);
        Switch(wanted, Held.Move, PlayerMap, false, moveOff);
        Switch(wanted, Held.Look, PlayerMap, true, lookOff);
        bool free = (wanted & Held.Cursor) != 0;
        if (free != ((applied & Held.Cursor) != 0))
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
        applied = wanted;
        return wanted;
    }

    // look: null takes every action of the map, true the look alone, false every action but the look.
    private static void Switch(Held wanted, Held one, string mapName, bool? look, List<InputAction> off)
    {
        bool hold = (wanted & one) != 0;
        if (hold == ((applied & one) != 0)) return;
        if (hold)
        {
            off.Clear();
            foreach (var map in Maps(mapName))
                foreach (var action in map.actions)
                {
                    if (!action.enabled || (look.HasValue && look.Value != (action.name == LookAction))) continue;
                    action.Disable();
                    off.Add(action);
                }
            return;
        }
        foreach (var action in off)
        {
            // A player's own copy of the asset goes with that player (a scene change): nothing of it is left to switch on.
            var map = action.actionMap;
            if (map != null && map.asset != null) action.Enable();
        }
        off.Clear();
    }

    // The project's input asset, and the local player's own copy of it when Unity gave them one (PlayerInput does for a
    // second player on one asset).
    private static IEnumerable<InputActionMap> Maps(string name)
    {
        var project = InputSystem.actions;
        var shared = project != null ? project.FindActionMap(name, false) : null;
        if (shared != null) yield return shared;
        var local = PlayerRoster.Local;
        if (local == null || !local.TryGetComponent(out PlayerInput input) || input.actions == null || input.actions == project) yield break;
        var own = input.actions.FindActionMap(name, false);
        if (own != null) yield return own;
    }
}
