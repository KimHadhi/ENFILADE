using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The outfit models for every character they were made for, at Resources/Characters/Outfits. One entry is one model
/// (Outfits.csv's model column, shared by the items cut from it) for one character, or for anyone (an accessory sold for
/// any avatar, skin <see cref="AnySkin"/>), with its colour sets. Two editor
/// steps fill it: OutfitLibraryBuilder from the folders Art/Models/Outfits/&lt;model&gt;/&lt;skin id&gt;.fbx (or .prefab),
/// whose &lt;model&gt;/Colors/&lt;set&gt; folders hold materials replacing the model's of the same names; and BoothImport
/// from the BOOTH packages, whose colour sets are the package's own prefabs of each colour. Each keeps the other's.
/// The models players bring as mods are not in this asset: ModOutfits makes an entry for each from the mod's own file
/// (its model loaded when first asked for), and OutfitCatalog.Model asks there after asking here.
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

        // A model a mod brings (ModOutfits): loaded from the mod's bundle when first asked for, with what the mod's file
        // says of how it goes on.
        [NonSerialized] private Func<GameObject> source;
        [NonSerialized] private Hold[] holds;
        [NonSerialized] private Standing? stood;

        public Entry() { }

        public Entry(string id, string skin, GameObject model, SkinVariant[] variants)
        {
            this.id = id;
            this.skin = skin;
            this.model = model;
            this.variants = variants ?? Array.Empty<SkinVariant>();
        }

        /// <summary>A model from a mod: none until one is asked for, then whatever <paramref name="source"/> loads.</summary>
        internal Entry(string id, string skin, Func<GameObject> source, Standing stood, Hold[] holds)
        {
            this.id = id;
            this.skin = skin;
            this.source = source;
            this.stood = stood;
            this.holds = holds;
        }

        public string Id => id;
        public string Skin => skin;
        public GameObject Model => model != null || source == null ? model : model = source();
        /// <summary>
        /// Whether there is a model to ask for. Asking a mod's for its <see cref="Model"/> loads it from its bundle, so
        /// whoever only lists what can be worn asks this.
        /// </summary>
        public bool HasModel => model != null || source != null;
        public IReadOnlyList<SkinVariant> Variants => variants ?? Array.Empty<SkinVariant>();
        /// <summary>Parts of the model that follow one bone of the body instead of its skeleton (a mod's: ModAvatarFile.Hold).</summary>
        public IReadOnlyList<Hold> Holds => holds ?? Array.Empty<Hold>();
        /// <summary>
        /// Where the model's root stands against the character's, when its file says so itself (a mod's). Null for the
        /// game's own models: their files and the characters' stood their roots at one origin (OutfitDresser.Align).
        /// </summary>
        public Standing? Stood => stood;
        /// <summary>
        /// Made for no one in particular (<see cref="AnySkin"/>): a BOOTH accessory sold for any avatar. Its bones are its
        /// own, so dressing never matches them to the character's by name, and it hangs on the slot's spots measured
        /// for each character (OutfitSpot), or is fitted to the face in a face slot (FaceFit). A hand-made one is the file
        /// "any" in its folder ('*' is no file name: the welding mask), which the editor files under AnySkin; "any" itself
        /// reads the same, should an older editor step have filed it so.
        /// </summary>
        public bool ForAnyone => string.Equals(skin, AnySkin, StringComparison.Ordinal) || string.Equals(skin, "any", StringComparison.OrdinalIgnoreCase);

        /// <summary>The colour set with this id, else the first; null when there are none.</summary>
        public SkinVariant VariantOf(string variantId)
        {
            if (variants == null || variants.Length == 0) return null;
            foreach (var variant in variants)
                if (variant != null && string.Equals(variant.Id, variantId, StringComparison.Ordinal)) return variant;
            return variants[0];
        }
    }

    /// <summary>
    /// A part of an outfit that follows one bone of the body, as Modular Avatar's Bone Proxy puts it: a hat or glasses
    /// sold inside an outfit, with no bones for the outfit's skeleton to carry them. <see cref="Path"/> from the model's
    /// root; <see cref="Under"/> a path on under the bone, or empty; with no humanoid bone (LastBone) that path runs
    /// from the character's root. It goes to the bone's own spot and turn unless it keeps where it stood
    /// (<see cref="KeepPosition"/>) or how it was turned (<see cref="KeepRotation"/>).
    /// </summary>
    public readonly struct Hold
    {
        public readonly string Path, Under;
        public readonly HumanBodyBones Bone;
        public readonly bool KeepPosition, KeepRotation;

        public Hold(string path, HumanBodyBones bone, string under, bool keepPosition, bool keepRotation)
        {
            Path = path ?? "";
            Bone = bone;
            Under = under ?? "";
            KeepPosition = keepPosition;
            KeepRotation = keepRotation;
        }
    }

    /// <summary>Where a model's root stands in the character's root (<see cref="Entry.Stood"/>).</summary>
    public readonly struct Standing
    {
        public readonly Vector3 Position, Scale;
        public readonly Quaternion Rotation;

        public Standing(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        /// <summary>At the character's own root: an outfit dropped under its avatar as it is.</summary>
        public static Standing Same => new Standing(Vector3.zero, Quaternion.identity, Vector3.one);
    }

    public const string ResourcePath = "Characters/Outfits";
    /// <summary>The skin of a model made for no one in particular (BoothSources.csv "model@*").</summary>
    public const string AnySkin = "*";

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    public static OutfitLibrary Load() => Resources.Load<OutfitLibrary>(ResourcePath);

    /// <summary>
    /// The model with this id made for this character, else the one made for anyone (<see cref="Entry.ForAnyone"/>);
    /// null when there is neither.
    /// </summary>
    public Entry Find(string modelId, string skin)
    {
        if (entries == null) return null;
        Entry anyone = null;
        foreach (var entry in entries)
        {
            if (entry == null || !string.Equals(entry.Id, modelId, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(entry.Skin, skin, StringComparison.OrdinalIgnoreCase)) return entry;
            if (entry.ForAnyone) anyone ??= entry;
        }
        return anyone;
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
