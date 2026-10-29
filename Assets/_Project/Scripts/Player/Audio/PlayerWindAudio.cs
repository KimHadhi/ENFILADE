using UnityEngine;

/// <summary>
/// Wind in the ears at speed, after Beatrun's run wind: its rush and the flapping of clothes rise with how fast the
/// player moves, falling included, and die away quickly on slowing down. Numbers: Player.csv row Wind.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerWindAudio : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("No wind below this speed (m/s), full wind at the second. Falling counts.")]
    private float startSpeed = 6f, fullSpeed = 20f;
    [SerializeField, Range(0f, 1f), Tooltip("Loudness of the wind at full speed.")]
    private float volume = .5f;
    [SerializeField, Min(.01f), Tooltip("How long the wind takes to rise to full; to die away from full (s).")]
    private float riseTime = 2f, fallTime = .4f;

    private const string MovementBank = "Movement";
    private static readonly string[] Sounds = { "Wind/Run", "Wind/Cloth" };

    private PlayerMovement player;
    private CharacterSounds voices;
    private SoundLoop[] loops;
    private AudioClip[] clips;
    private float[] levels;
    private float wanted;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void OnDisable()
    {
        if (loops == null) return;
        foreach (var loop in loops) loop?.Stop();
        wanted = 0f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || player == null || !player.IsInitialized || !Build()) return;
        float level = volume * Mathf.InverseLerp(startSpeed, Mathf.Max(startSpeed + .1f, fullSpeed), player.ActualVelocity.magnitude);
        float time = level > wanted ? riseTime : fallTime;
        wanted = level;
        for (int i = 0; i < loops.Length; i++)
        {
            if (clips[i] == null) continue;
            if (level > 0f) loops[i].Play(clips[i], 1f);
            loops[i].FadeTo(level * levels[i], time);
            loops[i].Tick(dt);
        }
    }

    private bool Build()
    {
        if (loops != null) return true;
        if (voices == null) voices = GetComponent<CharacterSounds>();
        var bank = voices != null ? voices.Bank(MovementBank) : null;
        if (bank == null) return false;
        loops = new SoundLoop[Sounds.Length];
        clips = new AudioClip[Sounds.Length];
        levels = new float[Sounds.Length];
        for (int i = 0; i < Sounds.Length; i++)
        {
            loops[i] = voices.CreateLoop(Sounds[i] + " wind");
            clips[i] = bank.Pick(Sounds[i]);
            levels[i] = bank.SettingsOf(Sounds[i]).Volume;
        }
        return true;
    }
}
