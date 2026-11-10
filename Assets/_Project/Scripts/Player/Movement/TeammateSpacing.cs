using UnityEngine;

/// <summary>
/// Backlog 524, at the user's pick ("팀원 몸 얇게"): teammates bump into each other with a body about as thin as a body
/// is, not the capsule their movement uses (0.3 m round, so 0.6 m between two middles, front to back as much as side to
/// side). On this PC's player: side by side, the two capsules pass through each other, and this player keeps `radius`
/// of air round their middle against the other's, stepping out along the ground (Motor.AddDisplacement, so walls still
/// stop it; each PC moves only its own player, so the two meet halfway). One above the other they collide as before, so
/// standing on a teammate's head and riding along (MovementMotor.Ride) still works. Numbers: Tables/Player.csv, row
/// Teammate.
/// </summary>
[DisallowMultipleComponent]
public sealed class TeammateSpacing : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("Each body against a teammate's: half the gap kept between their middles (m).")]
    private float radius = .15f;
    [SerializeField, Min(0f), Tooltip("How fast it steps out of a teammate (m/s).")]
    private float pushSpeed = 3f;
    [SerializeField, Range(.1f, 1f), Tooltip("Feet this far up the other's height (share) count as above them: the capsules collide, so one can stand on the other.")]
    private float aboveShare = .7f;

    private PlayerMovement player;
    private CharacterController body;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        body = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (player == null || body == null || !player.IsInitialized || !player.IsLocal) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 feet = transform.position;
        float height = body.height * Mathf.Abs(transform.lossyScale.y);
        Vector3 push = Vector3.zero;
        foreach (var other in PlayerRoster.All)
        {
            if (other == null || other == player || !other.TryGetComponent(out CharacterController theirs)) continue;
            Vector3 at = other.transform.position;
            float theirHeight = theirs.height * Mathf.Abs(other.transform.lossyScale.y);
            bool stacked = feet.y >= at.y + theirHeight * aboveShare || at.y >= feet.y + height * aboveShare;
            // Side by side the capsules pass; one on the other they meet. Set only on a change (a capsule switched off
            // and on again forgets it, and is set again then).
            if (Usable(body) && Usable(theirs) && Physics.GetIgnoreCollision(body, theirs) == stacked)
                Physics.IgnoreCollision(body, theirs, !stacked);
            if (stacked || other.IsDead || !theirs.enabled) continue;
            Vector3 apart = Vector3.ProjectOnPlane(feet - at, Vector3.up);
            float gap = radius * 2f, distance = apart.magnitude;
            if (distance >= gap) continue;
            // Right on top of each other: step aside.
            Vector3 away = distance > 1e-3f ? apart / distance : transform.right;
            push += away * (gap - distance);
        }
        if (push == Vector3.zero || player.IsParkour || player.IsDead || player.IsHeld) return;
        // Its own half of the way out; the other's PC moves them the other half.
        player.Motor.AddDisplacement(Vector3.ClampMagnitude(push * .5f, pushSpeed * dt));
    }

    private static bool Usable(Collider collider) => collider.enabled && collider.gameObject.activeInHierarchy;
}
