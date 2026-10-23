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
    [Header("Defense and enemy attacks")]
    public AudioClip blockSound, parrySound, enemyAttackStartSound, perfectEvadeSound;
    [Range(0f, 1f)] public float blockVolume = .3f, parryVolume = .3f, enemyAttackVolume = .3f, perfectEvadeVolume = .3f;
    [Header("Perfect defense counter")]
    [Range(.01f, 1f)] public float perfectTimeScale = .08f;
    [Min(.1f)] public float counterWindowSeconds = 1f;
    [Min(1f)] public float counterDamage = 80f;
    [Min(0f)] public float counterPostureDamage = 60f;
    [Min(.1f)] public float counterReach = 4f;
    [Range(0f, 1f)] public float perfectVignetteStrength = .8f;
    private static CombatFeedbackSettings cached;
    public static CombatFeedbackSettings Instance => cached != null ? cached : cached = Resources.Load<CombatFeedbackSettings>("Combat/Feedback");
}
