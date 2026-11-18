using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The callout wheel's slices (Tables/Player/CalloutSlots.csv). A row is one slice: the page it is on, its place on
/// the wheel (0 at the top, clockwise), when it is there (a CalloutWhen condition) and its name. Several rows may ask
/// for one place: the one that fits with the highest order has it, so a class or a moment can have a slice of its own
/// in place of the usual one ("class:detente" over the plain class slice, "friend.down" over thanks). A slice says the
/// line of Callouts.csv whose slot is its id, or opens another page (opens), as 말 opens the page of short words.
/// The eight slices and the eight words were two arrays in PlayerCallouts until 2026-11-18; as rows a mod adds a slice,
/// a page or a class's own wheel the same way the game's own are written (the user's list of that day: "클래스·상황별
/// 동적 콜아웃으로 개편", and its standing principle that every system takes new content from tables).
/// </summary>
public static class CalloutSlots
{
    public const string TableName = "CalloutSlots";
    /// <summary>The page the wheel opens on, and how many places a page has (the wheel's slices: CalloutWheel).</summary>
    public const string FirstPage = "wheel";
    public const int Places = 8;

    public sealed class Slot
    {
        public string Id, Page, When, Name, Opens;
        public int Place, Order;
        /// <summary>The slice's name on the wheel: the Texts row its name cell holds, a modder's plain words as written.</summary>
        public string Title => string.IsNullOrEmpty(Name) ? Texts.Get("callout.slot." + Id) : Texts.Cell(Name);
    }

    private static List<Slot> slots;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        slots = null;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() => slots = null;

    /// <summary>Every slice of every page, in a fixed order (by id).</summary>
    public static IReadOnlyList<Slot> All
    {
        get
        {
            Load();
            return slots;
        }
    }

    /// <summary>
    /// The page's slices as they stand now: for each place the fitting row of the highest order (of equals, the first by
    /// id), null where none fits. into must hold Places entries.
    /// </summary>
    public static void Resolve(string page, Func<string, bool> fits, Slot[] into)
    {
        Load();
        for (int i = 0; i < into.Length; i++) into[i] = null;
        foreach (var slot in slots)
        {
            if (slot.Place < 0 || slot.Place >= into.Length || !string.Equals(slot.Page, page, StringComparison.OrdinalIgnoreCase)) continue;
            var held = into[slot.Place];
            if ((held != null && held.Order >= slot.Order) || !fits(slot.When)) continue;
            into[slot.Place] = slot;
        }
    }

    private static void Load()
    {
        if (slots != null) return;
        slots = new List<Slot>();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
            slots.Add(new Slot
            {
                Id = id, Page = row.Text("page", FirstPage), When = row.Text("when", "always"), Name = row.Text("name"),
                Opens = row.Text("opens"), Place = row.Whole("place", -1), Order = row.Whole("order", 0)
            });
        }
        slots.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
    }
}
