using UnityEngine;

/// <summary>
/// Recorded sounds for the stealth round, at Resources/Stealth/StealthSounds: the thrown stone's clack (ThrownStone)
/// picks one of the stone impacts here, and the wall knock (PlayerKnock) one of the knocks; without the asset, or with
/// none set, each makes its own.
/// </summary>
[CreateAssetMenu(menuName = "ENFILADE/Stealth Sounds", fileName = "StealthSounds")]
public sealed class StealthSounds : ScriptableObject
{
    public const string ResourcePath = "Stealth/StealthSounds";

    [Tooltip("A stone landing on something hard. One is picked each time.")]
    public AudioClip[] stoneHits = new AudioClip[0];
    [Tooltip("Knuckles rapping on a wall (PlayerKnock). One is picked each time.")]
    public AudioClip[] knocks = new AudioClip[0];

    private int last = -1, lastKnock = -1;

    /// <summary>One of the stone impacts, never the same twice running when there are more; null with none.</summary>
    public AudioClip PickStone() => Pick(stoneHits, ref last);

    /// <summary>One of the knocks, never the same twice running when there are more; null with none.</summary>
    public AudioClip PickKnock() => Pick(knocks, ref lastKnock);

    private static AudioClip Pick(AudioClip[] clips, ref int previous)
    {
        if (clips == null || clips.Length == 0) return null;
        int pick = Random.Range(0, clips.Length);
        if (clips.Length > 1 && pick == previous) pick = (pick + 1) % clips.Length;
        previous = pick;
        return clips[pick];
    }
}
