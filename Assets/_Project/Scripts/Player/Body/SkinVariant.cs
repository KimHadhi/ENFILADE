using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One colour set of a character skin: materials the model was imported with, swapped for others once the model is made.
/// Listed under each skin in the PlayerBody profile; the player picks one on the character screen
/// (<see cref="CharacterSkinView"/>), and other players see it through <see cref="PlayerNetworkPose"/>.
/// </summary>
[Serializable]
public sealed class SkinVariant
{
    [Serializable]
    public struct Swap
    {
        [Tooltip("A material the model was imported with.")]
        public Material from;
        [Tooltip("The material this colour set wears instead.")]
        public Material to;
    }

    [SerializeField, Tooltip("Saved in settings.json and sent to other players, so keep it once players have picked it.")]
    private string id;
    [SerializeField, Tooltip("Shown on the character screen. The part before the first space labels the colour card.")]
    private string displayName;
    [SerializeField, Tooltip("Materials to swap. Empty wears the model as imported.")]
    private Swap[] swaps = Array.Empty<Swap>();

    private static readonly List<Material> scratch = new List<Material>();

    public string Id => id;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
    /// <summary>The colour card's label: the display name up to its first space.</summary>
    public string ShortName
    {
        get
        {
            string name = DisplayName ?? "";
            int space = name.IndexOf(' ');
            return space > 0 ? name.Substring(0, space) : name;
        }
    }

    /// <summary>
    /// Dresses a model made from the skin in <paramref name="next"/>, taking <paramref name="worn"/> off first.
    /// Null stands for the model as imported. Materials no colour set names (a hit flash overlay) stay as they are.
    /// </summary>
    public static void Wear(GameObject model, SkinVariant worn, SkinVariant next)
    {
        if (model == null || worn == next) return;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            renderer.GetSharedMaterials(scratch);
            bool changed = false;
            for (int i = 0; i < scratch.Count; i++)
            {
                var material = Dressed(next, Original(worn, scratch[i]));
                if (material == scratch[i]) continue;
                scratch[i] = material;
                changed = true;
            }
            if (changed) renderer.SetSharedMaterials(scratch);
        }
        scratch.Clear();
    }

    private static Material Original(SkinVariant variant, Material material)
    {
        if (variant == null || material == null) return material;
        foreach (var swap in variant.swaps)
            if (swap.to == material && swap.from != null) return swap.from;
        return material;
    }

    private static Material Dressed(SkinVariant variant, Material material)
    {
        if (variant == null || material == null) return material;
        foreach (var swap in variant.swaps)
            if (swap.from == material && swap.to != null) return swap.to;
        return material;
    }
}
