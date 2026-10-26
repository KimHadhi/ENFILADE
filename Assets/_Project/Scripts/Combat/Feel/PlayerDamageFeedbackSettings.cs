using UnityEngine;

/// <summary>Incoming player damage. All dimensions use the HUD's 1920x1080 reference resolution.</summary>
[CreateAssetMenu(menuName = "ENFILADE/Player Damage Feedback")]
public sealed class PlayerDamageFeedbackSettings : ScriptableObject
{
    [Header("Layers")]
    public bool directionArcs = true;
    public bool bloodVignette = true;
    public bool cameraShake = true;
    public bool criticalPulse = true;

    [Header("Independent hit arcs (maximum 24 simultaneous hits)")]
    public Color arcColor = new Color(1f, .12f, .08f, .95f);
    [Min(.1f)] public float hitSeconds = 1.65f;
    [Range(0f, 1f)] public float holdFraction = .22f;
    [Min(30f)] public float arcRadius = 88f;
    [Range(2f, 20f)] public float arcThickness = 5f;
    [Range(12f, 100f)] public float arcDegrees = 48f;

    [Header("Blood vignette (screen-locked, maximum 32 splashes)")]
    [Tooltip("Pooled blood toward the screen edge.")]
    public Color bloodColor = new Color(.36f, .01f, .02f, 1f);
    [Tooltip("Fresh blood at the inner part of the gradient.")]
    public Color freshColor = new Color(.62f, .02f, .035f, 1f);
    [Range(0f, 1f), Tooltip("Blood amount a full-strength hit adds on its side.")] public float bloodPerHit = .42f;
    [Min(0f)] public float bloodHoldSeconds = 1.6f;
    [Min(.1f)] public float bloodFadeSeconds = 3.4f;
    [Min(.05f), Tooltip("How long new blood keeps its brighter red.")] public float freshSeconds = 1.2f;
    [Range(0f, 1f), Tooltip("Strength of the smooth red gradient on the struck side.")] public float gradientOpacity = .6f;
    [Range(.45f, 1f), Tooltip("How far in the gradient reaches under heavy damage; 1 is the screen edge.")] public float gradientReach = .78f;
    [Range(0f, 1f), Tooltip("Fine blood flecks near the struck edge.")] public float speckDensity = .3f;
    [Range(.5f, 8f)] public float noiseScale = 2.6f;
    [Range(0, 4), Tooltip("Rivulets that leave the blood front and run down. Hits from below spawn none.")] public int dripsPerHit = 1;
    [Min(10f)] public float dripMaxLength = 170f;
    [Min(.1f), Tooltip("Time for a rivulet to reach its full length.")] public float dripSeconds = 2.4f;
    [Range(0f, 1f)] public float rearWashOpacity = .14f;

    [Header("Damage camera channel (Trauma squared, independent of gunfire)")]
    [Range(0f, 1f)] public float traumaPerHit = .38f;
    [Range(0f, 6f)] public float shakeMaxAngle = 2.2f;
    [Min(.1f)] public float shakeRecovery = 1.5f;
    [Min(1f)] public float shakeFrequency = 23f;
    [Range(0f, 3f)] public float impactKickDegrees = .75f;

    [Header("Low health: persistent stain and heartbeat")]
    [Range(.01f, 1f), Tooltip("A faint permanent stain starts below this HP ratio.")] public float injuredHealthFraction = .5f;
    [Range(0f, 1f), Tooltip("Stain amount as HP approaches zero.")] public float injuredBlood = .2f;
    [Range(.01f, 1f)] public float criticalHealthFraction = .25f;
    [Range(0f, 1f), Tooltip("Blood that stays on every edge while critical, between beats.")] public float criticalBlood = .15f;
    [Range(0f, 1f), Tooltip("Extra blood pushed inward at each beat.")] public float heartbeatSurge = .1f;
    [Range(30f, 180f)] public float heartbeatBpm = 58f;
    [Range(0f, 1f), Tooltip("Soft red vignette that stays on while critical and thumps inward on every beat.")] public float criticalVignette = .6f;

    private static PlayerDamageFeedbackSettings cached;
    public static PlayerDamageFeedbackSettings Instance
    {
        get
        {
            if (cached != null) return cached;
            cached = Resources.Load<PlayerDamageFeedbackSettings>("Combat/PlayerDamageFeedback");
            if (cached == null)
            {
                cached = CreateInstance<PlayerDamageFeedbackSettings>();
                cached.hideFlags = HideFlags.HideAndDontSave;
            }
            return cached;
        }
    }
}
