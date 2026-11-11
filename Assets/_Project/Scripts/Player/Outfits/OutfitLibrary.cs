using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The outfit models for every character they were made for, at Resources/Characters/Outfits. One entry is one model
/// (Outfits.csv's model column, shared by the items cut from it) for one character, with its colour sets. Two editor
/// steps fill it: OutfitLibraryBuilder from the folders Art/Models/Outfits/&lt;model&gt;/&lt;skin id&gt;.fbx (or .prefab),
/// whose &lt;model&gt;/Colors/&lt;set&gt; folders hold materials replacing the model's of the same names; and BoothImport
/// from the BOOTH packages, whose colour sets are the package's own prefabs of each colour. Each keeps the other's.
/// </summary>
[CreateAssetMenu(fileName = "Outfits", menuName = "ENFILADE/Player/Outfit Library")]
public sealed class OutfitLibrary : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        [SerializeField, Tooltip("The model's id, as Outfits.csv's model column names it.")]
        private string id;
        [SerializeField] private string skin;
        [SerializeField] private GameObject model;
        [SerializeField, Tooltip("Colour sets; the first is the default. Empty: the model as imported is the only look.")]
        private SkinVariant[] variants = Array.Empty<SkinVariant>();

        public Entry() { }

        public Entry(string id, string skin, GameObject model, SkinVariant[] variants)
        {
            this.id = id;
            this.skin = skin;
            this.model = model;
            this.variants = variants ?? Array.Empty<SkinVariant>();
        }

        public string Id => id;
        public string Skin => skin;
        public GameObject Model => model;
        public IReadOnlyList<SkinVariant> Variants => variants ?? Array.Empty<SkinVariant>();

        /// <summary>The colour set with this id, else the first; null when there are none.</summary>
        public SkinVariant VariantOf(string variantId)
        {
            if (variants == null || variants.Length == 0) return null;
            foreach (var variant in variants)
                if (variant != null && string.Equals(variant.Id, variantId, StringComparison.Ordinal)) return variant;
            return variants[0];
        }
    }

    public const string ResourcePath = "Characters/Outfits";

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

    public static OutfitLibrary Load() => Resources.Load<OutfitLibrary>(ResourcePath);

    /// <summary>The model with this id made for this character, or null when there is none.</summary>
    public Entry Find(string modelId, string skin)
    {
        if (entries == null) return null;
        foreach (var entry in entries)
            if (entry != null && string.Equals(entry.Id, modelId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Skin, skin, StringComparison.OrdinalIgnoreCase)) return entry;
        return null;
    }

#if UNITY_EDITOR
    /// <summary>Editor import steps: replaces every entry <paramref name="owned"/> says is theirs with <paramref name="next"/>.</summary>
    public void EditorReplace(Predicate<Entry> owned, IEnumerable<Entry> next)
    {
        var kept = new List<Entry>();
        foreach (var entry in entries ?? Array.Empty<Entry>())
            if (entry != null && !owned(entry)) kept.Add(entry);
        kept.AddRange(next);
        entries = kept.ToArray();
    }
#endif
}
