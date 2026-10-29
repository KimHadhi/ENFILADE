using UnityEngine;

/// <summary>
/// Beatrun's long-fall sounds. Once a fall is a big one (PlayerMovement.IsBigFall) a static rush loops and the heart
/// starts to pound, a little louder each beat and quicker the faster the fall. Touching down or catching hold cuts
/// the rush. Numbers: Player.csv row FallAudio; the beat itself is the FallHeartbeat cue of Tables/SoundCues.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerFallAudio : MonoBehaviour
{
    [SerializeField, Range(0f, 1f), Tooltip("Loudness of the static rush.")]
    private float staticVolume = .6f;
    [SerializeField, Min(.01f), Tooltip("How long the rush takes to come in; to cut out (s).")]
    private float staticFadeIn = .3f, staticFadeOut = .1f;
    [SerializeField, Min(0f), Tooltip("The first heartbeat comes this long after the fall becomes a big one (s).")]
    private float firstBeat = .5f;
    [SerializeField, Tooltip("Time between heartbeats (s): at the start of the fall; at heartbeatFastSpeed.")]
    private Vector2 heartbeatInterval = new Vector2(.5f, .3f);
    [SerializeField, Min(1f), Tooltip("Falling this fast, the heart beats at its quickest (m/s).")]
    private float heartbeatFastSpeed = 40f;
    [SerializeField, Range(0f, 1f), Tooltip("Loudness of the first heartbeat, and what each beat adds, up to full.")]
    private float heartbeatVolume = .3f, heartbeatVolumeStep = .05f;

    private const string MovementBank = "Movement", StaticSound = "Fall/Static", HeartbeatCue = "FallHeartbeat";

    private PlayerMovement player;
    private CharacterSounds voices;
    private Transform head;
    private SoundLoop rush;
    private float nextBeat, beatVolume;
    private bool falling;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void OnDisable()
    {
        rush?.Stop();
        falling = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || player == null || !player.IsInitialized) return;
        bool big = player.IsBigFall;
        if (big && !falling) Begin();
        else if (!big && falling) rush?.FadeTo(0f, staticFadeOut);
        falling = big;
        if (big && Time.time >= nextBeat) Beat();
        rush?.Tick(dt);
    }

    private void Begin()
    {
        nextBeat = Time.time + firstBeat;
        beatVolume = heartbeatVolume;
        if (voices == null) voices = GetComponent<CharacterSounds>();
        var bank = voices != null ? voices.Bank(MovementBank) : null;
        var clip = bank != null ? bank.Pick(StaticSound) : null;
        if (clip == null) return;
        rush ??= voices.CreateLoop("Fall static");
        rush.Play(clip, bank.SettingsOf(StaticSound).RandomPitch());
        rush.FadeTo(staticVolume * bank.SettingsOf(StaticSound).Volume, staticFadeIn);
    }

    // In the head: the beat plays where the camera is.
    private void Beat()
    {
        float fall = Mathf.Max(0f, -player.VerticalVelocity);
        float speed = Mathf.InverseLerp(player.AbilitySettings.bigFallMinSpeed, heartbeatFastSpeed, fall);
        nextBeat = Time.time + Mathf.Lerp(heartbeatInterval.x, heartbeatInterval.y, speed);
        if (head == null)
        {
            var view = GetComponentInChildren<Camera>();
            if (view != null) head = view.transform;
        }
        Vector3 point = head != null ? head.position : transform.position + Vector3.up * 1.6f;
        voices?.Play(HeartbeatCue, point, null, beatVolume);
        beatVolume = Mathf.Min(1f, beatVolume + heartbeatVolumeStep);
    }
}
