using System.Collections.Generic;

/// <summary>
/// One thing to wear, a row of Outfits.csv: the slot it goes in, the slots it also fills (a one-piece is a top that takes
/// the bottom too), the slots it covers (a full mask hides glasses), its name, and which model and which of that model's
/// meshes it is. A BOOTH outfit is one model with a beret, a jacket, a skirt and shoes in it, and the user wanted each in
/// its own slot, so several items share a model and each shows only its own meshes. The model for each character it was
/// made for is in the <see cref="OutfitLibrary"/>; BOOTH outfits fit only their own avatars and clip through any other
/// body, so an item is offered only on characters its model was made for.
/// </summary>
public sealed class OutfitItem
{
    public const string TableName = "Outfits";

    public string id = "", slot = "", also = "", hides = "", name = "", model = "", parts = "", under = "";

    public string DisplayName => string.IsNullOrEmpty(name) ? id : name;
    /// <summary>The model it is a piece of (OutfitLibrary); an item with no model column is its own model.</summary>
    public string ModelId => string.IsNullOrEmpty(model) ? id : model;
    /// <summary>The renderers of the model it shows; none listed takes every renderer no other piece took.</summary>
    public IEnumerable<string> PartNames => OutfitCatalog.Split(parts);
    /// <summary>Slots it takes besides its own: nothing else can be worn there while it is on.</summary>
    public IEnumerable<string> AlsoSlots => OutfitCatalog.Split(also);
    /// <summary>Slots it covers: what is worn there, the character's own piece or an item, goes out of sight.</summary>
    public IEnumerable<string> HiddenSlots => OutfitCatalog.Split(hides);
    /// <summary>
    /// Slots whose underwear it is worn in place of (CharacterParts.csv under): a bodysuit or a bikini goes on the skin, so
    /// the character's own bra and panties stay off while it is on, instead of coming on once the dress is off and
    /// poking out of it (Mayo's bra stood 29 mm out of the Sera Yura bodysuit, 2026-11-12).
    /// </summary>
    public IEnumerable<string> UnderSlots => OutfitCatalog.Split(under);
}
