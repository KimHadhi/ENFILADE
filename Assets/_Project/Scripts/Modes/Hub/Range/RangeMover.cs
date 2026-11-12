using UnityEngine;

/// <summary>
/// Carries a range target back and forth along a rail (memo 46, the moving target): from where it stands to
/// <see cref="travel"/> away and back, easing at the ends, at Tables/Hub moverSpeed. It goes by the game's clock, so
/// every PC's target is about where the others' is.
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
        float along = Mathf.PingPong(Time.time * speed / length, 1f);
        transform.position = start + travel * Mathf.SmoothStep(0f, 1f, along);
    }
}
