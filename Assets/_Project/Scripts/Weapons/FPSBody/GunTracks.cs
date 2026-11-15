using System;
using UnityEngine;

/// <summary>
/// The list of every gun's actions (GunTrackSet), one asset to a gun: filled in the editor (GunTrackBake), loaded from
/// Resources/Combat/GunTracks. The design is in Docs/Features/GunHandling.md: an action's content belongs to its asset, and
/// the game reads it at the moment and weight the animation mixer gives.
/// </summary>
public sealed class GunTracks : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public WeaponData weapon;
        public GunTrackSet set;
        [Tooltip("What the tracks were read from; a change makes the editor read them again.")]
        public string source;
        [Tooltip("What reading was last tried from and failed at: not tried again until the gun's clips or arms change.")]
        public string failedSource;
    }

    public Entry[] entries = Array.Empty<Entry>();

    public const string ResourcePath = "Combat/GunTracks";
    private static GunTracks cached;

    public static GunTracks Instance => cached != null ? cached : cached = Resources.Load<GunTracks>(ResourcePath);

    /// <summary>That gun's actions; null for a gun whose clips were never read.</summary>
    public GunTrackSet For(WeaponData weapon)
    {
        if (weapon == null) return null;
        foreach (var entry in entries)
            if (entry.weapon == weapon) return entry.set;
        return null;
    }
}
