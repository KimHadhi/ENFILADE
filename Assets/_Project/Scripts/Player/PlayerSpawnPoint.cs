using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Where a scene's player starts. It spawns the player prefab before anything else wakes, so the HUD finds the player
/// the same way in every scene. Stand one of these in a scene instead of placing a player there.
/// </summary>
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class PlayerSpawnPoint : MonoBehaviour
{
    public const string PrefabPath = "Characters/Player";

    private void Awake()
    {
        var prefab = Resources.Load<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[PlayerSpawnPoint] Resources/{PrefabPath} is missing, so this scene has no player.", this);
            return;
        }
        var player = Instantiate(prefab, transform.position, transform.rotation, transform.parent);
        player.name = prefab.name;
        // A prefab can't hold links to a scene, so its input is joined to this scene's UI here.
        var input = player.GetComponent<PlayerInput>();
        if (input != null) input.uiInputModule = FindFirstObjectByType<InputSystemUIInputModule>();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * .3f, .3f);
        Gizmos.DrawRay(transform.position + Vector3.up * .3f, transform.forward);
    }
}
