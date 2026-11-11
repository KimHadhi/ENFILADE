using System.Collections.Generic;

/// <summary>
/// One thing to wear, a row of Outfits.csv: the slot it goes in, the slots it also fills (a one-piece is a top that takes
/// the bottom too), the slots it covers (a full mask hides glasses) and its name. Its model for each character it was made
/// for sits in Art/Models/Outfits/&lt;id&gt;/&lt;skin id&gt; (<see cref="OutfitLibrary"/>). BOOTH outfits are made for
/// particular avatars and clip through any other body, so an item is offered only on characters it has a model for.
/// </summary>
public sealed class OutfitItem
{
    public const string TableName = "Outfits";

    public string id = "", slot = "", also = "", hides = "", name = "";

    public string DisplayName => string.IsNullOrEmpty(name) ? id : name;
    /// <summary>Slots it takes besides its own: nothing else can be worn there while it is on.</summary>
    public IEnumerable<string> AlsoSlots => OutfitCatalog.Split(also);
    /// <summary>Slots it covers: what is worn there, the character's own piece or an item, goes out of sight.</summary>
    public IEnumerable<string> HiddenSlots => OutfitCatalog.Split(hides);
}
