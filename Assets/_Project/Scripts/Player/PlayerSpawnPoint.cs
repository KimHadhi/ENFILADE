using UnityEngine;

/// <summary>
/// Where a scene's player starts. Alone, it spawns the player prefab before anything else wakes, so the HUD finds the
/// player the same way in every scene. In a room GameSession spawns everyone here instead.
/// Stand one of these in a scene instead of placing a player there.
/// </summary>
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class PlayerSpawnPoint : MonoBehaviour
{
    private void Awake()
    {
        if (GameSession.IsOnline) return;
        if (PlayerFactory.Prefab == null)
        {
            Debug.LogWarning("[PlayerSpawnPoint] Resources/Characters/Player is missing, so this scene has no player.", this);
            return;
        }
        PlayerFactory.CreateLocal(transform.position, transform.rotation, transform.parent);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * .3f, .3f);
        Gizmos.DrawRay(transform.position + Vector3.up * .3f, transform.forward);
    }
}
