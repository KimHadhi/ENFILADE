using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The melee weapons' models (MeleeWeapons), so a model dropped in later replaces the stand-in made in code: the editor
/// keeps this asset (Resources/Combat/MeleeModels) in step with Art/Models/Weapons/&lt;weapon&gt;/, taking the first
/// model file there (Editor/Import/MeleeModelBuilder). Until a file is there the asset doesn't exist and the katana is
/// the stand-in (KatanaModel). The model's own axes: its origin where the blade meets the guard, +Z along the blade to
/// the tip, +Y the blade's back; anything else is turned and moved in Player.csv (KatanaView.model*).
/// </summary>
public sealed class MeleeModels : ScriptableObject
{
    public const string ResourcePath = "Combat/MeleeModels";

    [Serializable]
    public sealed class Entry
    {
        public string weapon;
        public GameObject model;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    private static MeleeModels loaded;
    private static bool looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        loaded = null;
        looked = false;
    }

    /// <summary>The model linked for this melee weapon (its asset name), or null for the stand-in.</summary>
    public static GameObject For(string weapon)
    {
        if (!looked)
        {
            looked = true;
            loaded = Resources.Load<MeleeModels>(ResourcePath);
        }
        if (loaded == null || string.IsNullOrEmpty(weapon)) return null;
        foreach (var entry in loaded.entries)
            if (entry != null && entry.model != null && string.Equals(entry.weapon, weapon, StringComparison.OrdinalIgnoreCase))
                return entry.model;
        return null;
    }

#if UNITY_EDITOR
    public IReadOnlyList<Entry> Entries => entries;

    /// <summary>Editor import step (MeleeModelBuilder): the folders' models. False when nothing changed.</summary>
    public bool EditorRefresh(List<Entry> found)
    {
        bool changed = entries.Count != found.Count;
        for (int i = 0; !changed && i < found.Count; i++)
            changed = entries[i] == null || entries[i].weapon != found[i].weapon || entries[i].model != found[i].model;
        if (!changed) return false;
        entries = found;
        return true;
    }
#endif
}
