using UnityEngine;

/// <summary>
/// Hiro's breathing, after Beatrun's. Running or climbing builds stress and resting on the ground lets it go. Breaths
/// alternate in and out: soft, or medium once stressed; short and quick at speed, long and slow otherwise. Fully
/// rested, the breathing goes quiet. In the air only an inhale is taken. A grunt (a vault's strain, a landing's
/// impact, from Tables/SoundCues) takes the mouth: the breath fades out, no breath starts while the grunt is due or
/// playing, and breathing resumes after it with an inhale (CharacterSounds.Breathe).
/// Breaths are Voice bank keys Breath/&lt;Soft|Medium&gt;&lt;Short|Long&gt;&lt;In|Out&gt;. Numbers: Player.csv row Breathing.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerBreathing : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("Faster than this on the ground, or climbing, the player exerts and stress builds (m/s).")]
    private float exertSpeed = 1.9f;
    [SerializeField, Min(0f), Tooltip("Faster than this, breaths are short and quick (m/s).")]
    private float quickSpeed = 3.8f;
    [SerializeField, Min(0f), Tooltip("Stress gained per second of exertion.")]
    private float stressGain = 13f;
    [SerializeField, Min(0f), Tooltip("Stress let go per second resting on the ground.")]
    private float stressRecovery = 16.5f;
    [SerializeField, Tooltip("Lowest and highest stress. At the lowest the player is rested and silent.")]
    private Vector2 stressRange = new Vector2(-50f, 150f);
    [SerializeField, Tooltip("Above this stress, breaths are medium instead of soft.")]
    private float heavyStress = 50f;
    [SerializeField, Min(.05f), Tooltip("Time between quick breaths (s); the more stressed, the closer to it.")]
    private float quickInterval = .5f;
    [SerializeField, Min(0f), Tooltip("Extra time between quick breaths when barely stressed (s).")]
    private float quickIntervalSlack = .25f;
    [SerializeField, Min(.05f), Tooltip("Time between slow breaths (s).")]
    private float slowInterval = 1.25f;
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
    }

    private void OnDisable()
    {
        if (sounds != null) sounds.Spoke -= OnSpoke;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || player == null || !player.IsInitialized || !player.DamageReceiver.IsAlive) return;
        float speed = new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;
        bool grounded = player.IsGrounded, climbing = player.IsParkour;
        if ((speed > exertSpeed || climbing) && (grounded || climbing)) stress += stressGain * dt;
        else if (grounded) stress -= stressRecovery * dt;
        else if (!inhale) return;
        stress = Mathf.Clamp(stress, stressRange.x, stressRange.y);
        if ((stress <= stressRange.x && inhale) || Time.time < nextBreath) return;

        bool quick = speed > quickSpeed;
        string key = BreathPrefix + (stress > heavyStress ? "Medium" : "Soft") + (quick ? "Short" : "Long") + (inhale ? "In" : "Out");
        // The mouth is busy with a grunt: try again once it is free.
        if (Sounds() == null || !sounds.Breathe(key, 1f, breathNoise)) return;
        float calm = stressRange.y > 0f ? Mathf.Abs(stress / stressRange.y - 1f) : 0f;
        nextBreath = Time.time + (quick ? quickInterval + calm * quickIntervalSlack : slowInterval);
        inhale = !inhale;
    }

    // A grunt took the mouth: breathe in once it is over.
    private void OnSpoke(string key, float length)
    {
        nextBreath = Mathf.Max(nextBreath, Time.time + length + afterVoicePause);
        inhale = true;
    }

    private CharacterSounds Sounds() => sounds != null ? sounds : sounds = GetComponent<CharacterSounds>();
}
