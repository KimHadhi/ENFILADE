using UnityEngine;

/// <summary>
/// A rotating red beacon (a fire alarm's, GimmickFireAlarm; the user's note on the card "꺼야 멈추는 화재 경보": "경보등
/// 같은 건 번쩍여도 돼"): its beam turns round about up while it is on, by the room's shared clock, so it points the same
/// way on every PC. A turn takes turnSeconds, so a wall sees its sweep at most about once a second, and it fades in and
/// out over half a second; it never blinks the whole screen.
/// </summary>
[DisallowMultipleComponent]
public sealed class GimmickBeacon : MonoBehaviour
{
    [SerializeField, Tooltip("The beam, turned with it.")]
    private Light beam;
    [SerializeField, Min(.8f), Tooltip("One turn (s).")]
    private float turnSeconds = 1.2f;

    private Quaternion built;
    private float full, level;

    /// <summary>Whether it turns and shines.</summary>
    public bool On { get; set; }

    private void Awake()
    {
        built = transform.localRotation;
        if (beam == null) return;
        full = beam.intensity;
        beam.intensity = 0f;
        beam.enabled = false;
    }

    private void Update()
    {
        level = Mathf.MoveTowards(level, On ? 1f : 0f, Time.deltaTime * 2f);
        if (beam != null)
        {
            beam.enabled = level > .001f;
            beam.intensity = full * level;
        }
        if (level <= 0f) return;
        var network = GameSession.Network;
        double clock = network != null && network.IsListening ? network.ServerTime.Time : Time.timeAsDouble;
        transform.localRotation = Quaternion.Euler(0f, (float)(clock / turnSeconds % 1.0) * 360f, 0f) * built;
    }
}
