using UnityEngine;

/// <summary>
/// A black MainCamera with an audio listener for the moments no other camera exists. Scenes hold no camera: the player
/// and the title menu bring their own, so there is none while a room's map waits for players to spawn, or after a room
/// shutdown destroyed the player and before the menu loads. Without this Unity warns that no audio listener exists, and
/// the pack's blood effects that no MainCamera exists. It steps aside in the same frame another camera turns on, so
/// there is never a second listener. One per run, surviving scene loads.
/// </summary>
[DisallowMultipleComponent]
public sealed class StandbyCamera : MonoBehaviour
{
    private Camera view;
    private AudioListener listener;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Create()
    {
        var host = new GameObject("Standby Camera") { tag = "MainCamera" };
        DontDestroyOnLoad(host);
        host.AddComponent<StandbyCamera>();
    }

    private void Awake()
    {
        // Far below the world and looking away from it, so HUD code projecting through Camera.main finds nothing on screen.
        transform.SetPositionAndRotation(Vector3.down * 10000f, Quaternion.Euler(90f, 0f, 0f));
        view = gameObject.AddComponent<Camera>();
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = Color.black;
        view.cullingMask = 0;
        view.depth = -100f;
        view.enabled = false;
        listener = gameObject.AddComponent<AudioListener>();
        listener.enabled = false;
    }

    // LateUpdate comes after this frame's spawns and scene loads and before audio and rendering, so the switch is
    // never a frame late either way. The HUD camera only draws over another camera, so it never counts.
    private void LateUpdate()
    {
        int others = Camera.allCamerasCount - (HudCamera.IsDrawing ? 1 : 0);
        bool alone = others == (view.enabled ? 1 : 0);
        if (view.enabled != alone) view.enabled = listener.enabled = alone;
    }
}
