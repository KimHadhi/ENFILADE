using UnityEngine;

/// <summary>The player this enemy is after: the nearest one alive, reconsidered now and then so it doesn't keep flipping.</summary>
[DisallowMultipleComponent]
public sealed class EnemyTargeting : MonoBehaviour
{
    [SerializeField, Min(.05f)] private float retargetInterval = .5f;
    private PlayerMovement player;
    private ZombieMind mind;
    private float nextLook;

    public PlayerMovement Player
    {
        get
        {
            // A zombie that noticed someone goes after them, not whoever is nearest.
            if (mind == null) mind = GetComponent<ZombieMind>();
            var noticed = mind != null ? mind.Target : null;
            if (noticed != null) return player = noticed;
            bool lost = player == null || !player.IsInitialized || !player.DamageReceiver.IsAlive;
            if (lost || Time.time >= nextLook)
            {
                nextLook = Time.time + retargetInterval;
                player = PlayerRoster.Nearest(transform.position);
            }
            return player;
        }
    }
}
