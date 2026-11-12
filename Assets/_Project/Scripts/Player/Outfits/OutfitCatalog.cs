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
        /// <summary>
        /// The character's own renderers it takes the place of, "name|name". Naming any, it is worn beside the rest of its
        /// slot's own pieces instead of taking the whole slot: a mirrored side lock swaps one of Mayo's two side locks and
        /// leaves the rest of her hair (OutfitDresser.Replace).
        /// </summary>
        public string replaces = "";
    }

    /// <summary>A row of CharacterParts.csv: a character's own piece in one slot.</summary>
    public sealed class Part
    {
        public string skin = "", slot = "";
        /// <summary>
        /// Its renderers, switched off when the slot is filled, emptied or covered. In an underwear slot (OutfitSlot.under)
        /// they are the character's own underwear, switched on while nothing else is picked there.
        /// </summary>
        public string parts = "";
        /// <summary>Shape keys set when the slot is filled or emptied: avatars that hide a piece with a key ("Earring_x").</summary>
        public string shapes = "";
        /// <summary>
        /// The body's shape keys the piece holds on while it shows, "name|name": the skin drawn in under stockings
        /// (Shrink_Knee_L), feet shaped for heels. Once no showing piece holds a key it goes back to 0, so legs bared of
        /// their stockings are whole again instead of keeping the hollows made to hide under them.
        /// </summary>
        public string needs = "";
        /// <summary>What the piece is called on the wardrobe's tile ("스타킹 · 다리 리본"); empty reads its renderers' names.</summary>
        public string name = "";
    }

    private static List<OutfitSlot> slots;
    private static Dictionary<string, OutfitSlot> slotsById;
    private static Dictionary<string, OutfitItem> items;
    // An id no longer in the table, to the items cut out of it (OutfitItem.was), in the table's order.
    private static Dictionary<string, List<OutfitItem>> splits;
    private static readonly List<OutfitItem> NoItems = new List<OutfitItem>();
    private static Dictionary<string, Fit> fits;
    private static List<Part> parts;
    private static OutfitLibrary library;
    private static bool libraryLooked, listening;
    // Per item and character: the colour sets that look different on it, and the listed one each set looks like.
    private static Dictionary<(string item, string skin), (List<SkinVariant> Listed, Dictionary<SkinVariant, SkinVariant> Shown)> looks;

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

    /// <summary>
    /// The items cut out of an item the table no longer has (OutfitItem.was), in the table's order: what a look saved
    /// wearing it wears now. Empty for an id the table still has, or one nothing came out of.
    /// </summary>
    public static IReadOnlyList<OutfitItem> SplitFrom(string retired)
    {
        Ensure();
        return retired != null && !items.ContainsKey(retired) && splits.TryGetValue(retired, out var pieces) ? pieces : NoItems;
    }

    /// <summary>The item's model made for this character, or null when it was not made for them.</summary>
    public static OutfitLibrary.Entry Model(string itemId, string skinId)
    {
        var item = Item(itemId);
        var entry = item != null && Library != null ? Library.Find(item.ModelId, skinId) : null;
        // A piece made for anyone fits only a character whose spot for its slot is measured (OutfitSpots.csv): earrings
        // need ears, and Sil has none. One fitted to the face (the welding mask) fits any face, measured as it is worn.
        return entry != null && entry.ForAnyone && Slot(item.slot)?.FitsFace != true && !OutfitSpot.Has(item.slot, skinId) ? null : entry;
    }

    /// <summary>What the item needs on this character; null when its row is missing (worn as it comes).</summary>
    public static Fit FitOf(string itemId, string skinId)
    {
        Ensure();
        return fits.TryGetValue((itemId ?? "") + OutfitLook.Separator + (skinId ?? ""), out var fit) ? fit : null;
    }

    /// <summary>
    /// The colour sets that look different on this item, the first of each look in the library's order. A BOOTH outfit's
    /// colour sets each recolour the whole outfit, so on one piece many look alike: Sera Yura's sixty sets give its
    /// gloves 29 looks and its toast 3, and the wardrobe showed sixty chips for each, fifteen rows (2026-11-12). Empty
    /// when the item has no model for this character.
    /// </summary>
    public static List<SkinVariant> ColoursOf(OutfitItem item, string skinId) => item != null ? Looks(item, skinId).Listed : new List<SkinVariant>();

    /// <summary>The listed colour set (ColoursOf) that looks like the one with this id on the item: the chip shown worn.</summary>
    public static SkinVariant ShownColour(OutfitItem item, string skinId, string colourId)
    {
        if (item == null) return null;
        var (listed, shown) = Looks(item, skinId);
        var worn = Model(item.id, skinId)?.VariantOf(colourId);
        return worn != null && shown.TryGetValue(worn, out var like) ? like : listed.Count > 0 ? listed[0] : null;
    }

    // Each colour set's look on the item: the materials it puts on the item's own renderers in its model, in order.
    private static (List<SkinVariant> Listed, Dictionary<SkinVariant, SkinVariant> Shown) Looks(OutfitItem item, string skinId)
    {
        Ensure();
        looks ??= new Dictionary<(string, string), (List<SkinVariant>, Dictionary<SkinVariant, SkinVariant>)>();
        var key = (item.id ?? "", skinId ?? "");
        if (looks.TryGetValue(key, out var found)) return found;
        var listed = new List<SkinVariant>();
        var shown = new Dictionary<SkinVariant, SkinVariant>();
        var entry = Model(item.id, skinId);
        if (entry != null)
        {
            var made = MaterialsOf(item, entry, skinId);
            var first = new Dictionary<string, SkinVariant>(StringComparer.Ordinal);
            var look = new System.Text.StringBuilder();
            foreach (var variant in entry.Variants)
            {
                if (variant == null) continue;
                look.Clear();
                foreach (var material in made) look.Append(variant.On(material) is Material on ? on.GetInstanceID() : 0).Append(',');
                if (!first.TryGetValue(look.ToString(), out var like))
                {
                    first[look.ToString()] = like = variant;
                    listed.Add(variant);
                }
                shown[variant] = like;
            }
        }
        return looks[key] = (listed, shown);
    }

    /// <summary>
    /// The renderers the other pieces of the item's model name: a piece that names none takes every renderer of its model
    /// but these (OutfitDresser), so underwear given its own item (a Banshaku bikini, BoothUnderwear's rows) stays out of
    /// the shirt that came with it.
    /// </summary>
    public static HashSet<string> NamedByOthers(OutfitItem item)
    {
        Ensure();
        var others = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (item == null) return others;
        foreach (var other in items.Values)
            if (other != item && string.Equals(other.ModelId, item.ModelId, StringComparison.OrdinalIgnoreCase)) others.UnionWith(other.PartNames);
        return others;
    }

    // The materials the item's renderers were made with in its model: those it names, or, naming none, every renderer
    // no other piece of the model names and its fit doesn't drop (as OutfitDresser hands them out).
    private static List<Material> MaterialsOf(OutfitItem item, OutfitLibrary.Entry entry, string skinId)
    {
        var made = new List<Material>();
        if (entry.Model == null) return made;
        var named = new HashSet<string>(item.PartNames, StringComparer.OrdinalIgnoreCase);
        var others = named.Count == 0 ? NamedByOthers(item) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var drop = new HashSet<string>(Split(FitOf(item.id, skinId)?.drop), StringComparer.OrdinalIgnoreCase);
        foreach (var renderer in entry.Model.GetComponentsInChildren<Renderer>(true))
        {
            bool mine = named.Count > 0 ? named.Contains(renderer.name) : !others.Contains(renderer.name) && !drop.Contains(renderer.name);
            if (mine) made.AddRange(renderer.sharedMaterials);
        }
        return made;
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

    /// <summary>
    /// What the character wears of its own in the slot, for the wardrobe's tile (the user, 2026-11-11: each slot should
    /// say what is on it): its rows' names (a Texts id, part.&lt;id&gt;.name, or plain words), or their renderers' names
    /// made readable (MAYO_Leg_Ribbon → Leg Ribbon). Empty when it has nothing of its own there.
    /// </summary>
    public static string OwnName(string skinId, string slotId)
    {
        var names = new List<string>();
        foreach (var part in PartsOf(skinId))
        {
            if (!string.Equals(part.slot, slotId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(part.name)) Add(names, Texts.Cell(part.name));
            else foreach (string piece in Split(part.parts)) Add(names, Readable(piece, skinId));
        }
        return string.Join(" · ", names);
    }

    private static void Add(List<string> names, string name)
    {
        if (name.Length > 0 && !names.Contains(name)) names.Add(name);
    }

    // A renderer's name without its maker's prefix (the character's name, a short all-capitals tag like COS), spaced.
    private static string Readable(string renderer, string skinId)
    {
        var words = new List<string>(renderer.Split('_'));
        while (words.Count > 1 && (string.Equals(words[0], skinId, StringComparison.OrdinalIgnoreCase)
            || (words[0].Length <= 4 && words[0] == words[0].ToUpperInvariant())))
            words.RemoveAt(0);
        return string.Join(" ", words).Trim();
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
        splits = new Dictionary<string, List<OutfitItem>>(StringComparer.OrdinalIgnoreCase);
        fits = new Dictionary<string, Fit>(StringComparer.OrdinalIgnoreCase);
        parts = new List<Part>();
        foreach (string id in GameTables.RowIds(OutfitSlot.TableName))
        {
            var slot = Fill(OutfitSlot.TableName, id, new OutfitSlot { id = id });
            slots.Add(slot);
            slotsById[id] = slot;
        }
        foreach (string id in GameTables.RowIds(OutfitItem.TableName))
        {
            var item = items[id] = Fill(OutfitItem.TableName, id, new OutfitItem { id = id });
            foreach (string old in item.WasIds)
            {
                if (!splits.TryGetValue(old, out var pieces)) splits[old] = pieces = new List<OutfitItem>();
                pieces.Add(item);
            }
        }
        foreach (string id in GameTables.RowIds(FitsTable)) fits[id] = Fill(FitsTable, id, new Fit());
        foreach (string id in GameTables.RowIds(PartsTable)) parts.Add(Fill(PartsTable, id, new Part()));
    }

    private static void Forget()
    {
        slots = null;
        slotsById = null;
        items = null;
        splits = null;
        fits = null;
        parts = null;
        looks = null;
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
