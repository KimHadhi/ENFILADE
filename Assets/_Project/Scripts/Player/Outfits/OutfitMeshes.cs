using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Meshes made for one dressed model, destroyed with it: underwear pressed in under the clothes worn over it
/// (UnderwearFit.Press) fits only that model's outfit, so it is not kept for other models the way bind-pose copies are.
/// The wardrobe makes a new model for every try, and each try's copies go with the model it was made for.
/// </summary>
public sealed class OutfitMeshes : MonoBehaviour
{
    private readonly List<Mesh> made = new List<Mesh>();

    /// <summary>Keeps <paramref name="mesh"/> until <paramref name="model"/> is destroyed.</summary>
    internal static void Keep(GameObject model, Mesh mesh)
    {
        if (model == null || mesh == null) return;
        if (!model.TryGetComponent(out OutfitMeshes kept)) kept = model.AddComponent<OutfitMeshes>();
        kept.made.Add(mesh);
    }

    private void OnDestroy()
    {
        foreach (var mesh in made)
        {
            if (mesh == null) continue;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
        made.Clear();
    }
}
