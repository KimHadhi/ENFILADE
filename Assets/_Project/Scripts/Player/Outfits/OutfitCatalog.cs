using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The outfit tables, read once and kept until a table is saved again (GameTables.Reloaded): the slots
/// (OutfitSlots.csv), the items (Outfits.csv), what an item needs on one character (OutfitFits.csv), each character's
/// own pieces by slot (CharacterParts.csv) and the items' models (<see cref="OutfitLibrary"/>). The player body, the
/// menu preview and the character screen all read it.
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
        /// <summary>Renderers of its file left off, "name|name".</summary>
        public string drop = "";
    }

    /// <summary>A row of CharacterParts.csv: the renderers of a character's own piece in one slot.</summary>
    public sealed class Part
    {
        public string skin = "", slot = "", parts = "";
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

    /// <summary>The item's model for this character, or null when it was not made for them.</summary>
    public static OutfitLibrary.Entry Model(string itemId, string skinId) => Library != null ? Library.Find(itemId, skinId) : null;

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

    /// <summary>The renderer names of the character's own piece in a slot; empty when it wears nothing there.</summary>
    public static List<string> OwnParts(string skinId, string slotId)
    {
        Ensure();
        var names = new List<string>();
        foreach (var part in parts)
            if (string.Equals(part.skin, skinId, StringComparison.OrdinalIgnoreCase) && string.Equals(part.slot, slotId, StringComparison.OrdinalIgnoreCase))
                names.AddRange(Split(part.parts));
        return names;
    }

    public static bool HasOwnPiece(string skinId, string slotId) => OwnParts(skinId, slotId).Count > 0;

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
