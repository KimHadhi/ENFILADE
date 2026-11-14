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

    public string id = "", slot = "", also = "", hides = "", name = "", model = "", parts = "", was = "", wear = "";
    /// <summary>
    /// See-through by its texture (lace, a net, sheer tights), so what lies under it shows and must stay: nothing is
    /// hidden under it or cut where it comes through it (OutfitFit). A material whose own colour is see-through is told
    /// from the material; a texture's see-through parts can't be read at run time, so such a piece says so here.
    /// </summary>
    public bool sheer;

    /// <summary>
    /// A piece fitted to the face (OutfitSlot.fit "face") worn on the back of the head instead (Outfits.csv wear "back"):
    /// the welding mask as it first went onto Kazusa's, Hiro's and Sil's heads by mistake, which the user liked as a look
    /// of its own (2026-11-13: "뒤통수에 위치하니까 멋있긴 해서 옵션으로 넣어 주면 좋을 듯"). FaceFit measures the face
    /// turned round.
    /// </summary>
    public bool WornBack => string.Equals(wear, "back", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Its name on the wardrobe: the name cell's words (a Texts id, outfit.&lt;id&gt;.name, or plain words), else the id.</summary>
    public string DisplayName => string.IsNullOrEmpty(name) ? id : Texts.Cell(name);
    /// <summary>The model it is a piece of (OutfitLibrary); an item with no model column is its own model.</summary>
    public string ModelId => string.IsNullOrEmpty(model) ? id : model;
    /// <summary>The renderers of the model it shows; none listed takes every renderer no other piece took.</summary>
    public IEnumerable<string> PartNames => OutfitCatalog.Split(parts);
    /// <summary>Slots it takes besides its own: nothing else can be worn there while it is on.</summary>
    public IEnumerable<string> AlsoSlots => OutfitCatalog.Split(also);
    /// <summary>Slots it covers: what is worn there, the character's own piece or an item, goes out of sight.</summary>
    public IEnumerable<string> HiddenSlots => OutfitCatalog.Split(hides);
    /// <summary>
    /// Items it was cut out of, ids gone from the table: a look saved wearing one of them wears this too, in its own slot
    /// (OutfitLook.Settled). The Banshaku shirt brought its stocking, thigh belt, choker and necklaces into the top slot
    /// until each became an item of its own (the user, 2026-11-12: "왜 셔츠인데 아래에 스타킹이나 가터 벨트까지 있어?"),
    /// and a look saved with the shirt keeps all of them on.
    /// </summary>
    public IEnumerable<string> WasIds => OutfitCatalog.Split(was);
}
