using TMPro;
using UnityEngine;

/// <summary>Shared imported presentation assets, loaded for existing scenes without prefab migration.</summary>
public sealed class CombatFeedbackSettings : ScriptableObject
{
    public Texture2D staggerImage;
    public AudioClip staggerSound;
    public TMP_FontAsset promptFont;
    private static CombatFeedbackSettings cached;
    public static CombatFeedbackSettings Instance => cached != null ? cached : cached = Resources.Load<CombatFeedbackSettings>("Combat/Feedback");
}
