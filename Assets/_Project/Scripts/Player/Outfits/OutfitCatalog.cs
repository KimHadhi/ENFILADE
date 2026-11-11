using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The outfit tables, read once and kept until a table is saved again (GameTables.Reloaded): the slots
/// (OutfitSlots.csv), the items (Outfits.csv), what an item needs on one character (OutfitFits.csv), each character's
/// own pieces by slot (CharacterParts.csv) and the models (<see cref="OutfitLibrary"/>). The player body, the menu
/// preview and the character screen all read it.
/// </summary>
public static class OutfitCatalog
{
    public const string FitsTable = "OutfitFits", PartsTable = "CharacterParts";
    private const char ListSeparator = '|';

    /// <summary>A row of OutfitFits.csv, id "item@skin": what one item needs on one character.</summary>
    public sealed class Fit
    {
        /// <summary>Shape keys set while it is on, "name=weight|name=weight" (0 to 100; a name alone is 100).</summary>
        public string shapes = "";
        /// <summary>Renderers of its model it leaves off, "name|name", when it takes the whole model.</summary>
        public string drop = "";
    }

    /// <summary>A row of CharacterParts.csv: a character's own piece in one slot.</summary>
    public sealed class Part
    {
        public string skin = "", slot = "";
        /// <summary>Its renderers, switched off when the slot is filled, emptied or covered.</summary>
        public string parts = "";
        /// <summary>
        /// Renderers it covers that switch on once all of <see cref="parts"/> are off, whichever slot took them: the
        /// underwear a dress hid, so taking the dress off for a skirt leaves no bare chest.
        /// </summary>
        public string under = "";
        /// <summary>Shape keys set when the slot is filled or emptied: avatars that hide a piece with a key ("Earring_x").</summary>
        public string shapes = "";
        /// <summary>
        /// The body's shape keys the piece holds on while it shows, "name|name": the skin drawn in under stockings
        /// (Shrink_Knee_L), feet shaped for heels. Once no showing piece holds a key it goes back to 0, so legs bared of
        /// their stockings are whole again instead of keeping the hollows made to hide under them.
        /// </summary>
        public string needs = "";
    }

    private static List<OutfitSlot> slots;
    private static Dictionary<string, OutfitSlot> slotsById;
    private static Dictionary<string, OutfitItem> items;
    private static Dictionary<string, Fit> fits;
    private static List<Part> parts;
    private static OutfitLibrary library;
    private static bool libraryLooked, listening;

    // Without a domain reload between plays the cache would outlive an edit made while stopped.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        Forget();
        library = null;
        libraryLooked = false;
    }

    /// <summary>Every slot, in the table's order.</summary>
    public static IReadOnlyList<OutfitSlot> Slots
    {
        get
        {
            Ensure();
            return slots;
        }
    }

    public static OutfitLibrary Library
    {
        get
        {
            if (!libraryLooked)
            {
                library = OutfitLibrary.Load();
                libraryLooked = true;
            }
            return library;
        }
    }

    public static OutfitSlot Slot(string id)
    {
        Ensure();
        return id != null && slotsById.TryGetValue(id, out var slot) ? slot : null;
    }

    public static OutfitItem Item(string id)
    {
        Ensure();
        return id != null && items.TryGetValue(id, out var item) ? item : null;
    }

    /// <summary>The item's model made for this character, or null when it was not made for them.</summary>
    public static OutfitLibrary.Entry Model(string itemId, string skinId)
    {
        var item = Item(itemId);
        return item != null && Library != null ? Library.Find(item.ModelId, skinId) : null;
    }

    /// <summary>What the item needs on this character; null when its row is missing (worn as it comes).</summary>
    public static Fit FitOf(string itemId, string skinId)
    {
        Ensure();
        return fits.TryGetValue((itemId ?? "") + OutfitLook.Separator + (skinId ?? ""), out var fit) ? fit : null;
    }

    /// <summary>The items of a slot that have a model for this character, in the table's order.</summary>
    public static List<OutfitItem> ItemsFor(string skinId, string slotId)
    {
        Ensure();
        var found = new List<OutfitItem>();
        foreach (var item in items.Values)
            if (string.Equals(item.slot, slotId, StringComparison.OrdinalIgnoreCase) && Model(item.id, skinId)?.Model != null)
                found.Add(item);
        return found;
    }

    /// <summary>The character's own pieces, every slot.</summary>
    public static List<Part> PartsOf(string skinId)
    {
        Ensure();
        var found = new List<Part>();
        foreach (var part in parts)
            if (string.Equals(part.skin, skinId, StringComparison.OrdinalIgnoreCase)) found.Add(part);
        return found;
    }

    /// <summary>Whether the character wears something of its own in the slot that could be taken off.</summary>
    public static bool HasOwnPiece(string skinId, string slotId)
    {
        foreach (var part in PartsOf(skinId))
            if (string.Equals(part.slot, slotId, StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrEmpty(part.parts) || !string.IsNullOrEmpty(part.shapes))) return true;
        return false;
    }

    /// <summary>The entries of a "a|b|c" cell, trimmed, empty ones skipped.</summary>
    public static IEnumerable<string> Split(string list)
    {
        if (string.IsNullOrEmpty(list)) yield break;
        foreach (string piece in list.Split(ListSeparator))
        {
            string entry = piece.Trim();
            if (entry.Length > 0) yield return entry;
        }
    }

    private static void Ensure()
    {
        if (!listening)
        {
            GameTables.Reloaded += Forget;
            listening = true;
        }
        if (slots != null) return;
        slots = new List<OutfitSlot>();
        slotsById = new Dictionary<string, OutfitSlot>(StringComparer.OrdinalIgnoreCase);
        items = new Dictionary<string, OutfitItem>(StringComparer.OrdinalIgnoreCase);
        fits = new Dictionary<string, Fit>(StringComparer.OrdinalIgnoreCase);
        parts = new List<Part>();
        foreach (string id in GameTables.RowIds(OutfitSlot.TableName))
        {
            var slot = Fill(OutfitSlot.TableName, id, new OutfitSlot { id = id });
            slots.Add(slot);
            slotsById[id] = slot;
        }
        foreach (string id in GameTables.RowIds(OutfitItem.TableName)) items[id] = Fill(OutfitItem.TableName, id, new OutfitItem { id = id });
        foreach (string id in GameTables.RowIds(FitsTable)) fits[id] = Fill(FitsTable, id, new Fit());
        foreach (string id in GameTables.RowIds(PartsTable)) parts.Add(Fill(PartsTable, id, new Part()));
    }

    private static void Forget()
    {
        slots = null;
        slotsById = null;
        items = null;
        fits = null;
        parts = null;
        // The editor's BOOTH import fills the library while the game may be stopped; look again next time.
        libraryLooked = false;
    }

    // The row's cells into the object's fields of the same names, as GameTables does for bound objects.
    private static T Fill<T>(string table, string id, T row) where T : class
    {
        if (!GameTables.TryGetRow(table, id, out var cells)) return row;
        foreach (var cell in cells)
        {
            var result = TableFieldWriter.TryWrite(row, cell.Key, cell.Value, out string error);
            if (result == TableWrite.Invalid) Debug.LogWarning($"[Outfits] {table}.csv [{id}] '{cell.Key}' = '{cell.Value}': {error}.");
            else if (result == TableWrite.NotFound) Debug.LogWarning($"[Outfits] {table}.csv [{id}] '{cell.Key}' 열과 같은 이름의 값이 없습니다.");
        }
        return row;
    }
}
