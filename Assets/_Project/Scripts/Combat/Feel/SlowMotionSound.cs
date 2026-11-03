using UnityEngine;

/// <summary>
/// How the game sounds while SkullSlowMotion slows it: every world sound drops in pitch and the whole mix goes muffled,
/// both following how slowed the game is; menu and HUD sounds (the UI channel) stay as they are. The muffling is a
/// low-pass filter on the audio listener, switched on only while the game is slowed. Numbers: Tables/SkullSlowMotion.
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
