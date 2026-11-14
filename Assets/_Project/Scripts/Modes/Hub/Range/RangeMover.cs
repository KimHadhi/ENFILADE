using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Carries a range target back and forth along a rail (memo 46, the moving target): from where it stands to
/// <see cref="travel"/> away and back, easing at the ends, at Tables/Hub moverSpeed. In a room it goes by the room's
/// clock (Netcode's server time, the same on every PC within a tick), alone by the game's, so every PC's target stands
/// where the others' does and a friend's shot replayed here (WeaponController.ShowShot) meets it where it was on their
/// screen. Until 0.9.28 it went by each PC's own Time.time, counted from when that PC's game started: in a room each
/// target stood somewhere else on every PC, so a friend's shot that missed it on their screen rang it on this one, and
/// one that hit it there flew past it here (the user, 2026-11-14: "지 혼자 맞췄다는 띵 띵 소리").
/// </summary>
[DisallowMultipleComponent]
public sealed class RangeMover : MonoBehaviour
{
    [SerializeField, Tooltip("From where it starts to the rail's far end, in the world (m).")]
    private Vector3 travel = new Vector3(6f, 0f, 0f);

    private Vector3 start;

    private void Awake() => start = transform.position;

    private void Update()
    {
        float length = travel.magnitude;
        if (length < .01f) return;
        float speed = Mathf.Max(.1f, HubLobby.Number("moverSpeed", 2.5f));
        // Folded into one trip there and back while still a double, so a room open for hours keeps its precision.
        float along = Mathf.PingPong((float)(Clock() * speed / length % 2d), 1f);
        transform.position = start + travel * Mathf.SmoothStep(0f, 1f, along);
    }

    // The room's clock in a room, else this PC's.
    private static double Clock()
    {
        var network = NetworkManager.Singleton;
        return network != null && network.IsListening ? network.ServerTime.Time : Time.timeAsDouble;
    }
}
