using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The meshes a dressed model wears that the fit made (OutfitFit: a layer with what is hidden under the clothes left
/// out, a piece given a shape key it lacked). OutfitFit keeps each such mesh for every model dressed the same, since the
/// wardrobe makes a new model for every try; this counts the model among a mesh's wearers until the model is destroyed,
/// and a mesh no model wears any more may go when its store turns over.
/// </summary>
public sealed class OutfitMeshes : MonoBehaviour
{
    private readonly List<OutfitFit.Kept> worn = new List<OutfitFit.Kept>();

    /// <summary>Counts <paramref name="model"/> as a wearer of a mesh the fit made, once.</summary>
    internal static void Hold(GameObject model, OutfitFit.Kept kept)
    {
        if (model == null || kept == null) return;
        if (!model.TryGetComponent(out OutfitMeshes held)) held = model.AddComponent<OutfitMeshes>();
        if (held.worn.Contains(kept)) return;
        held.worn.Add(kept);
        kept.Wearers++;
    }

    private void OnDestroy()
    {
        foreach (var kept in worn) kept.Wearers--;
        worn.Clear();
    }
}
