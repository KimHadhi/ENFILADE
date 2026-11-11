using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The model of every outfit item for every character it was made for, at Resources/Characters/Outfits. The editor
/// fills it from the folders (Editor/Import/OutfitLibraryBuilder): Art/Models/Outfits/&lt;item&gt;/&lt;skin id&gt;.fbx
/// (or .prefab) is that item's model for that character, and each folder under &lt;item&gt;/Colors is a colour set whose
/// materials replace the model's materials of the same names. Slots, names and tuning live in the tables (OutfitCatalog).
/// </summary>
[CreateAssetMenu(fileName = "Outfits", menuName = "ENFILADE/Player/Outfit Library")]
public sealed class OutfitLibrary : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        [SerializeField] private string item;
        [SerializeField] private string skin;
        [SerializeField] private GameObject model;
        [SerializeField, Tooltip("Colour sets; the first is the default. Empty: the model as imported is the only look.")]
        private SkinVariant[] variants = Array.Empty<SkinVariant>();

        public Entry() { }

        public Entry(string item, string skin, GameObject model, SkinVariant[] variants)
        {
            this.item = item;
            this.skin = skin;
            this.model = model;
            this.variants = variants ?? Array.Empty<SkinVariant>();
        }

        public string Item => item;
        public string Skin => skin;
        public GameObject Model => model;
        public IReadOnlyList<SkinVariant> Variants => variants ?? Array.Empty<SkinVariant>();

        /// <summary>The colour set with this id, else the first; null when there are none.</summary>
        public SkinVariant VariantOf(string id)
        {
            if (variants == null || variants.Length == 0) return null;
            foreach (var variant in variants)
                if (variant != null && string.Equals(variant.Id, id, StringComparison.Ordinal)) return variant;
            return variants[0];
        }
    }

    public const string ResourcePath = "Characters/Outfits";

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

    public static OutfitLibrary Load() => Resources.Load<OutfitLibrary>(ResourcePath);

    /// <summary>The item's model for this character, or null when there is none.</summary>
    public Entry Find(string item, string skin)
    {
        if (entries == null) return null;
        foreach (var entry in entries)
            if (entry != null && string.Equals(entry.Item, item, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Skin, skin, StringComparison.OrdinalIgnoreCase)) return entry;
        return null;
    }

#if UNITY_EDITOR
    /// <summary>Editor import step (OutfitLibraryBuilder): the list as the folders have it now.</summary>
    public void EditorSet(Entry[] next) => entries = next ?? Array.Empty<Entry>();
#endif
}
