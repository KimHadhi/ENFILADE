using TMPro;
using UnityEngine;

/// <summary>Shared imported presentation assets, loaded for existing scenes without prefab migration.</summary>
public sealed class CombatFeedbackSettings : ScriptableObject
{
    public Texture2D staggerImage;
    public AudioClip staggerSound;
    [Range(0f, 1f), Tooltip("Volume of the stagger cue. 0 mutes it; 1 is full volume.")]
    public float staggerSoundVolume = .25f;
    [Min(40f), Tooltip("Stagger image width at 3 metres, in reference screen pixels before the opening pop. Closer grows up to 2x; farther shrinks with distance.")]
    public float staggerImageWidth = 260f;
    public TMP_FontAsset promptFont;
    [Header("Shove Impacts")]
    public AudioClip[] shoveEnemySounds;
    public AudioClip shoveWorldSound;
    [Range(0f, 1f)] public float shoveEnemyVolume = .3f;
    [Range(0f, 1f)] public float shoveWorldVolume = .3f;
    private static CombatFeedbackSettings cached;
    public static CombatFeedbackSettings Instance => cached != null ? cached : cached = Resources.Load<CombatFeedbackSettings>("Combat/Feedback");
}
