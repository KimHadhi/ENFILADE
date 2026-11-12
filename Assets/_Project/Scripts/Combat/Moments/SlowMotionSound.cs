using UnityEngine;

/// <summary>
/// How the game sounds while SkullSlowMotion slows it: every world sound drops in pitch and the whole mix goes muffled,
/// both following how slowed the game is; menu and HUD sounds (the UI channel) stay as they are. The muffling is a
/// low-pass filter on the audio listener, put there switched off as soon as the listener is there and switched on only
/// while the game is slowed. Numbers: Tables/SkullSlowMotion.
/// </summary>
[DisallowMultipleComponent]
public sealed class SlowMotionSound : MonoBehaviour
{
    private const float OpenCutoff = 22000f;

    [SerializeField, Range(.1f, 1f), Tooltip("Pitch of world sounds at the slowest (1 leaves them as they are).")]
    private float soundPitch = .6f;
    [SerializeField, Range(100f, OpenCutoff), Tooltip("Highest frequency still heard at the slowest (Hz); 22000 leaves the sound open.")]
    private float soundCutoff = 2200f;

    private AudioLowPassFilter muffle;
    private bool slowed;

    private void LateUpdate()
    {
        // Added as the game slows, the filter would change the audio chain that very moment, which can click; so it goes on
        // ahead, switched off. A respawn or a scene change brings a new listener.
        if (muffle == null) muffle = Attach(Camera.main);
        float depth = SkullSlowMotion.Depth;
        bool now = depth > .001f;
        if (!now && !slowed) return;
        slowed = now;
        GameAudio.FollowWorldPitch(Mathf.Lerp(1f, soundPitch, depth));
        Muffle(depth);
    }

    private void OnDisable()
    {
        if (!slowed) return;
        slowed = false;
        GameAudio.FollowWorldPitch(1f);
        Muffle(0f);
    }

    // The filter on the view's listener, made open and switched off.
    private static AudioLowPassFilter Attach(Camera view)
    {
        var listener = view != null && view.TryGetComponent(out AudioListener own) ? own : null;
        if (listener == null) return null;
        if (listener.TryGetComponent(out AudioLowPassFilter filter)) return filter;
        filter = listener.gameObject.AddComponent<AudioLowPassFilter>();
        filter.cutoffFrequency = OpenCutoff;
        filter.lowpassResonanceQ = 1f;
        filter.enabled = false;
        return filter;
    }

    private void Muffle(float depth)
    {
        if (depth <= .001f)
        {
            if (muffle != null) muffle.enabled = false;
            return;
        }
        // The listener belongs to the player's camera, which a respawn or a scene change replaces.
        if (muffle == null)
        {
            var view = Camera.main;
            var listener = view != null && view.TryGetComponent(out AudioListener own) ? own : FindAnyObjectByType<AudioListener>();
            if (listener == null) return;
            if (!listener.TryGetComponent(out muffle)) muffle = listener.gameObject.AddComponent<AudioLowPassFilter>();
            muffle.lowpassResonanceQ = 1f;
        }
        muffle.enabled = true;
        // Heard pitch is logarithmic, so the cutoff falls evenly in octaves rather than in hertz.
        muffle.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoff), Mathf.Log(soundCutoff), depth));
    }
}
