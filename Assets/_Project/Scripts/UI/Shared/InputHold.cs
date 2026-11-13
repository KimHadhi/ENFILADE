using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>What a screen takes from the player while it is up.</summary>
[System.Flags]
public enum Held
{
    None = 0,
    /// <summary>The combat keys (the asset's Combat map).</summary>
    Combat = 1,
    /// <summary>Moving and looking (the asset's Player map).</summary>
    Move = 2,
    /// <summary>The mouse pointer: free and shown instead of locked to the view.</summary>
    Cursor = 4,
    Keys = Combat | Move,
    All = Combat | Move | Cursor,
}

/// <summary>
/// Who has the controls. The chat, the console, the shop, the perk cards, the gun bench, the hub's mirror, the results
/// show, the settings and the title screen each take the player's keys or the pointer while they are up. Each used to
/// switch things off and remember for itself what to switch back on, and each knew by name which other screens to ask
/// first, so two of them up at once undid each other: close the chat under an open shop and the player walked and shot
/// behind the shop; close the shop under the console and the pointer locked under it.
/// Here a screen only says what it holds (Take) and that it is done (Release). A thing is held while any screen holds
/// it: the input maps that were on come back on, and the pointer locks to the view again, when the last one lets go. A
/// screen destroyed while it held (a scene change) lets go by being gone.
/// </summary>
public static class InputHold
{
    private struct Holder
    {
        public Object Who;
        public Held What;
    }

    private static readonly List<Holder> holders = new List<Holder>();
    // The maps this switched off, to switch back on: only those that were on.
    private static readonly List<InputActionMap> combatOff = new List<InputActionMap>(), moveOff = new List<InputActionMap>();
    private static Held applied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        holders.Clear();
        combatOff.Clear();
        moveOff.Clear();
        applied = Held.None;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    // The screens of a scene that was left are gone without a word: what only they held comes back now.
    private static void OnSceneUnloaded(Scene scene) => Apply();

    /// <summary>Some screen that is still there holds this (any part of it).</summary>
    public static bool Any(Held what = Held.All) => (Apply() & what) != 0;

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

    // Brings the input maps and the pointer in line with what is held now; returns what is held.
    private static Held Apply()
    {
        Held wanted = Held.None;
        for (int i = holders.Count - 1; i >= 0; i--)
        {
            if (holders[i].Who == null) holders.RemoveAt(i);
            else wanted |= holders[i].What;
        }
        if (wanted == applied) return wanted;
        Switch(wanted, Held.Combat, "Combat", combatOff);
        Switch(wanted, Held.Move, "Player", moveOff);
        bool free = (wanted & Held.Cursor) != 0;
        if (free != ((applied & Held.Cursor) != 0))
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
        applied = wanted;
        return wanted;
    }

    private static void Switch(Held wanted, Held one, string mapName, List<InputActionMap> off)
    {
        bool hold = (wanted & one) != 0;
        if (hold == ((applied & one) != 0)) return;
        if (hold)
        {
            off.Clear();
            foreach (var map in Maps(mapName))
            {
                if (!map.enabled) continue;
                map.Disable();
                off.Add(map);
            }
            return;
        }
        foreach (var map in off) map.Enable();
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
