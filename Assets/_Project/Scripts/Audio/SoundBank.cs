using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Clips grouped by folder under one root: Art/Audio/Movement/Run/Concrete/*.wav becomes key "Run/Concrete" in the
/// Movement bank. The editor rebuilds the asset (Resources/Audio/&lt;id&gt;) whenever a file in the root changes, so adding
/// a sound is dropping it into the right folder. Volume and pitch per key live in Resources/Tables/Sounds.csv
/// (row "&lt;id&gt;/&lt;key&gt;").
/// </summary>
public sealed class SoundBank : ScriptableObject
{
    public const string ResourceFolder = "Audio";
    private const string SettingsTable = "Sounds";

    [Serializable]
    public sealed class Entry
    {
        public string key;
        public AudioClip[] clips;
    }

    public readonly struct Settings
    {
        public readonly float Volume, PitchMin, PitchMax;
        public Settings(float volume, float pitchMin, float pitchMax) { Volume = volume; PitchMin = pitchMin; PitchMax = pitchMax; }
        public float RandomPitch() => UnityEngine.Random.Range(PitchMin, PitchMax);
    }

    [SerializeField] private string id;
    [SerializeField] private List<Entry> entries = new List<Entry>();
    [NonSerialized] private Dictionary<string, AudioClip[]> lookup;
    [NonSerialized] private readonly Dictionary<string, Settings> settings = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
    [NonSerialized] private readonly Dictionary<string, int> lastPick = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public string Id => id;
    public IReadOnlyList<Entry> Entries => entries;

    public static SoundBank Load(string bankId) => Resources.Load<SoundBank>(ResourceFolder + "/" + bankId);

    public bool TryGet(string key, out AudioClip[] clips)
    {
        if (lookup == null)
        {
            lookup = new Dictionary<string, AudioClip[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
                if (entry != null && entry.clips != null && entry.clips.Length > 0) lookup[entry.key] = entry.clips;
        }
        return lookup.TryGetValue(key ?? "", out clips);
    }

    /// <summary>
    /// The clips of an event on a surface, following the surface's fallbacks (Duct → Metal → Concrete) and finally the
    /// event alone ("Vault"). Returns the key that matched, or null.
    /// </summary>
    public string Find(string soundEvent, string surface, out AudioClip[] clips)
    {
        foreach (string candidate in Surfaces.Fallbacks(surface))
        {
            string key = soundEvent + "/" + candidate;
            if (TryGet(key, out clips)) return key;
        }
        return TryGet(soundEvent, out clips) ? soundEvent : null;
    }

    /// <summary>A random clip of the key, never the same one twice in a row when there is a choice.</summary>
    public AudioClip Pick(string key)
    {
        if (!TryGet(key, out var clips)) return null;
        lastPick.TryGetValue(key, out int last);
        int index = UnityEngine.Random.Range(0, clips.Length);
        if (clips.Length > 1 && index == last) index = (index + 1 + UnityEngine.Random.Range(0, clips.Length - 1)) % clips.Length;
        lastPick[key] = index;
        return clips[index];
    }

    /// <summary>Volume and pitch range from Sounds.csv; 1, 1–1 when the key has no row.</summary>
    public Settings SettingsOf(string key)
    {
        if (settings.TryGetValue(key, out var cached)) return cached;
        float volume = 1f, pitchMin = 1f, pitchMax = 1f;
        if (GameTables.TryGetRow(SettingsTable, id + "/" + key, out var row))
        {
            Read(row, "volume", ref volume);
            Read(row, "pitchMin", ref pitchMin);
            Read(row, "pitchMax", ref pitchMax);
        }
        return settings[key] = new Settings(Mathf.Max(0f, volume), pitchMin, Mathf.Max(pitchMin, pitchMax));
    }

    private static void Read(IReadOnlyDictionary<string, string> row, string column, ref float value)
    {
        if (row.TryGetValue(column, out string raw)
            && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) value = parsed;
    }

    private void OnEnable()
    {
        lookup = null;
        settings.Clear();
        GameTables.Reloaded += settings.Clear;
    }

    private void OnDisable() => GameTables.Reloaded -= settings.Clear;

#if UNITY_EDITOR
    /// <summary>Editor import step (Editor/Import/SoundBankBuilder): replace the entries with the folders' clips.</summary>
    public bool EditorRefresh(string bankId, List<Entry> folders)
    {
        bool changed = id != bankId || entries.Count != folders.Count;
        for (int i = 0; !changed && i < folders.Count; i++)
            changed = entries[i].key != folders[i].key || !System.Linq.Enumerable.SequenceEqual(entries[i].clips, folders[i].clips);
        if (!changed) return false;
        id = bankId;
        entries = folders;
        lookup = null;
        return true;
    }
#endif
}
