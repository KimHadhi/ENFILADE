using UnityEngine;

/// <summary>
/// Recorded sounds for the stealth round's thrown stone (ThrownStone), at Resources/Stealth/StealthSounds. Its clack
/// picks one of the stone impacts here; without the asset, or with none set, the stone makes its own.
/// </summary>
[CreateAssetMenu(menuName = "ENFILADE/Stealth Sounds", fileName = "StealthSounds")]
public sealed class StealthSounds : ScriptableObject
{
    public const string ResourcePath = "Stealth/StealthSounds";

    [Tooltip("A stone landing on something hard. One is picked each time.")]
    public AudioClip[] stoneHits = new AudioClip[0];

    private int last = -1;

    /// <summary>One of the stone impacts, never the same twice running when there are more; null with none.</summary>
    public AudioClip PickStone()
    {
        if (stoneHits == null || stoneHits.Length == 0) return null;
        int pick = Random.Range(0, stoneHits.Length);
        if (stoneHits.Length > 1 && pick == last) pick = (pick + 1) % stoneHits.Length;
        last = pick;
        return stoneHits[pick];
    }
}
