using UnityEngine;

/// <summary>
/// Hiro's breathing, after Beatrun's stress model, heard only when the player has really worked: sprinting (speed built
/// past walking), climbing and big landings build stress, and anything else on the ground lets it go, walking included.
/// A little out of breath stays silent; above that, breaths alternate in and out: soft, or medium once stressed; short
/// and quick while still exerting; once the effort stops they slow down as the breath settles, and end on an exhale.
/// In the air only an inhale is taken. A grunt (a vault's strain, a landing's impact, from Tables/SoundCues) takes the
/// mouth: the breath fades out, no breath starts while the grunt is due or playing, and breathing resumes after it
/// with an inhale (CharacterSounds.Breathe).
/// Breaths are Voice bank keys Breath/&lt;Soft|Medium&gt;&lt;Short|Long&gt;&lt;In|Out&gt;. Numbers: Player.csv row Breathing.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerBreathing : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("Faster than this on the ground (sprinting), or climbing, the player exerts and stress "
        + "builds (m/s). Walking stays under it and lets the breath settle.")]
    private float exertSpeed = 6f;
    [SerializeField, Min(0f), Tooltip("Stress gained per second of exertion.")]
    private float stressGain = 13f;
    [SerializeField, Min(0f), Tooltip("Stress let go per second on the ground without exerting.")]
    private float stressRecovery = 16.5f;
    [SerializeField, Tooltip("Lowest and highest stress. The player starts and rests at the lowest.")]
    private Vector2 stressRange = new Vector2(-50f, 150f);
    [SerializeField, Tooltip("Breaths are heard only above this stress: a little out of breath stays silent.")]
    private float audibleStress = 0f;
    [SerializeField, Tooltip("Above this stress, breaths are medium instead of soft.")]
    private float heavyStress = 50f;
    [SerializeField, Min(0f), Tooltip("Stress a hard landing adds at once.")]
    private float landingStress = 60f;
    [SerializeField, Min(0f), Tooltip("Stress a heavy landing (one that knocks the player down) adds at once.")]
    private float heavyLandingStress = 100f;
    [SerializeField, Min(.05f), Tooltip("Time between quick breaths while exerting (s); the more stressed, the closer to it.")]
    private float quickInterval = .5f;
    [SerializeField, Min(0f), Tooltip("Extra time between quick breaths when barely stressed (s).")]
    private float quickIntervalSlack = .25f;
    [SerializeField, Min(.05f), Tooltip("Time between breaths after exerting, while still very out of breath (s).")]
    private float slowInterval = 1.25f;
    [SerializeField, Min(.05f), Tooltip("Time between breaths just before they fall silent (s): they slow toward it as the breath settles.")]
    private float restedInterval = 2.2f;
    [SerializeField, Min(0f), Tooltip("Pause after a grunt before the next breath (s).")]
    private float afterVoicePause = .3f;
    [SerializeField, Min(0f), Tooltip("How far a breath carries for enemy hearing (m).")]
    private float breathNoise = 1.5f;

    private const string BreathPrefix = "Breath/";

    private PlayerMovement player;
    private CharacterSounds sounds;
    private float stress, nextBreath;
    private bool inhale = true;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void OnEnable()
    {
        stress = stressRange.x;
        inhale = true;
        if (Sounds() != null) sounds.Spoke += OnSpoke;
        if (player != null) player.Landed += OnLanded;
    }

    private void OnDisable()
    {
        if (sounds != null) sounds.Spoke -= OnSpoke;
        if (player != null) player.Landed -= OnLanded;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || player == null || !player.IsInitialized || !player.DamageReceiver.IsAlive) return;
        float speed = new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;
        bool grounded = player.IsGrounded, climbing = player.IsParkour;
        bool exerting = (speed > exertSpeed && grounded) || climbing;
        if (exerting) stress += stressGain * dt;
        else if (grounded) stress -= stressRecovery * dt;
        else if (!inhale) return;
        stress = Mathf.Clamp(stress, stressRange.x, stressRange.y);
        // Silent below the threshold, once the last breath was let out.
        if ((stress <= audibleStress && inhale) || Time.time < nextBreath) return;

        string key = BreathPrefix + (stress > heavyStress ? "Medium" : "Soft") + (exerting ? "Short" : "Long") + (inhale ? "In" : "Out");
        // The mouth is busy with a grunt: try again once it is free.
        if (Sounds() == null || !sounds.Breathe(key, 1f, breathNoise)) return;
        nextBreath = Time.time + Interval(exerting);
        inhale = !inhale;
    }

    // Quick while exerting; after it, slow and slower as the breath settles toward silence.
    private float Interval(bool exerting)
    {
        if (exerting)
        {
            float calm = stressRange.y > 0f ? Mathf.Abs(stress / stressRange.y - 1f) : 0f;
            return quickInterval + calm * quickIntervalSlack;
        }
        return Mathf.Lerp(restedInterval, slowInterval, Mathf.InverseLerp(audibleStress, heavyStress, stress));
    }

    // A hard or heavy landing knocks the wind out at once.
    private void OnLanded(float impact)
    {
        var tier = player.LastLandingTier;
        float added = tier == MovementLanding.Tier.Heavy ? heavyLandingStress : tier == MovementLanding.Tier.Hard ? landingStress : 0f;
        if (added > 0f) stress = Mathf.Clamp(stress + added, stressRange.x, stressRange.y);
    }

    // A grunt took the mouth: breathe in once it is over.
    private void OnSpoke(string key, float length)
    {
        nextBreath = Mathf.Max(nextBreath, Time.time + length + afterVoicePause);
        inhale = true;
    }

    private CharacterSounds Sounds() => sounds != null ? sounds : sounds = GetComponent<CharacterSounds>();
}
