using System;
using UnityEngine;

/// <summary>
/// Gun models by folder, for the dual pistols' other model (Akimbo.csv `model`, AkimboGuns.Look): each folder under
/// Art/Models/Weapons that holds a prefab or a model file is one entry, named after the folder (Art/Models/Weapons/
/// KraitP68 is "KraitP68"). The editor keeps it in step with the folders (AkimboModelsBuilder); the game loads it from
/// Resources/Combat/AkimboModels, which doesn't exist until such a folder does.
/// </summary>
public sealed class AkimboModels : ScriptableObject
{
    public const string ResourcePath = "Combat/AkimboModels";

    [Serializable]
    public struct Entry
    {
        [Tooltip("The folder's name under Art/Models/Weapons, as Akimbo.csv's model column names it.")]
        public string id;
        public GameObject model;
    }

    [SerializeField] private Entry[] models = Array.Empty<Entry>();

    private static AkimboModels cached;
    private static bool looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        cached = null;
        looked = false;
    }

    /// <summary>The model of the folder named <paramref name="id"/>, or null (none named, or no such folder yet).</summary>
    public static GameObject Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (!looked)
        {
            looked = true;
            cached = Resources.Load<AkimboModels>(ResourcePath);
        }
        if (cached == null || cached.models == null) return null;
        string wanted = id.Trim();
        foreach (var entry in cached.models)
            if (entry.model != null && string.Equals(entry.id, wanted, StringComparison.OrdinalIgnoreCase)) return entry.model;
        return null;
    }

#if UNITY_EDITOR
    /// <summary>Editor import step (Editor/Import/AkimboModelsBuilder): replaces the entries. False when nothing changed.</summary>
    public bool EditorSet(Entry[] entries)
    {
        entries ??= Array.Empty<Entry>();
        bool changed = models == null || models.Length != entries.Length;
        for (int i = 0; !changed && i < entries.Length; i++)
            changed = models[i].id != entries[i].id || models[i].model != entries[i].model;
        if (changed) models = entries;
        return changed;
    }
#endif
}
