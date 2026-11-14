using UnityEngine;

/// <summary>
/// A searchlight that sweeps slowly to and fro (the map gimmick card "밤 차량기지", after PAYDAY 2's Murky Station): its
/// beam lights whoever it falls on (StealthLight counts it as any lamp: a zombie makes them out sooner and from farther),
/// so they time their crossing between its passes. Every PC turns it by the room's shared clock (the server's time),
/// so the beam the host's zombies see by is where everyone sees it. A smooth turn, never a flicker.
/// </summary>
[DisallowMultipleComponent]
public sealed class GimmickSweep : MonoBehaviour
{
    [SerializeField, Range(0f, 180f), Tooltip("How far either way it turns from where it was built facing (degrees).")]
    private float sweep = 60f;
    [SerializeField, Min(1f), Tooltip("One full sweep there and back (s).")]
    private float period = 16f;
    [SerializeField, Range(0f, 1f), Tooltip("Where in its sweep it starts, as a share of the period (two lights out of step).")]
    private float phase;

    private Quaternion built;

    private void Awake() => built = transform.localRotation;

    private void Update()
    {
        var network = GameSession.Network;
        double clock = network != null && network.IsListening ? network.ServerTime.Time : Time.timeAsDouble;
        float turn = Mathf.Sin((float)((clock / period + phase) % 1.0) * Mathf.PI * 2f) * sweep;
        transform.localRotation = Quaternion.Euler(0f, turn, 0f) * built;
    }
}
