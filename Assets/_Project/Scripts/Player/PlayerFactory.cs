using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Makes player characters from the player prefab. A remote one learns it before it wakes, so it never adds screen
/// effects or claims the mouse.
/// </summary>
public static class PlayerFactory
{
    private const string PrefabPath = "Characters/Player";

    public static GameObject Prefab => Resources.Load<GameObject>(PrefabPath);

    public static GameObject CreateLocal(Vector3 position, Quaternion rotation, Transform parent = null)
    {
        var prefab = Prefab;
        var player = Object.Instantiate(prefab, position, rotation, parent);
        player.name = prefab.name;
        // A prefab can't hold links to a scene, so its input is joined to this scene's UI here.
        var input = player.GetComponent<PlayerInput>();
        if (input != null) input.uiInputModule = Object.FindFirstObjectByType<InputSystemUIInputModule>();
        return player;
    }

    public static GameObject CreateRemote(Vector3 position, Quaternion rotation, Transform parent, bool networkDriven)
    {
        var holder = new GameObject("Player Holder");
        holder.SetActive(false);
        var player = Object.Instantiate(Prefab, position, rotation, holder.transform);
        player.GetComponent<PlayerMovement>().MakeRemote(networkDriven);
        player.transform.SetParent(parent, true);
        Object.Destroy(holder);
        return player;
    }
}
