using UnityEngine;

/// <summary>
/// The eyes' view: where the camera sits in first person, and where it stays in the F5 view while the camera moves
/// behind the player. The gun, the fists and every reach that starts from the face (punches, shoves, executions,
/// counters) use it, so they stay with the body that is seen; the camera itself is for seeing, and the crosshair's ray.
/// Kept under LookPivot (PlayerCameraEffects moves the camera under a pivot of its own), made on first use with the
/// camera's pose; the camera's final placement moves it to the eyes each frame (PlayerCameraEffects, then PlayerBody).
/// </summary>
public static class PlayerView
{
    private const string Name = "Eye View";

    /// <summary>The eye view of the player this belongs to, or the camera, or the player itself.</summary>
    public static Transform EyesOf(Component part)
    {
        if (part == null) return null;
        var player = part.GetComponentInParent<PlayerMovement>();
        Transform root = player != null ? player.transform : part.transform;
        var camera = root.GetComponentInChildren<Camera>(true);
        Transform look = root.Find("CrouchPivot/BobPivot/LookPivot");
        if (look == null) look = root.Find("CrouchPivot/LookPivot");
        if (camera == null || look == null) return camera != null ? camera.transform : root;
        var eyes = look.Find(Name);
        if (eyes != null) return eyes;
        eyes = new GameObject(Name).transform;
        eyes.SetParent(look, false);
        eyes.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
        return eyes;
    }

    /// <summary>Puts the eyes where the camera is now.</summary>
    internal static void Follow(Transform eyes, Transform camera)
    {
        if (eyes != null && camera != null && eyes != camera) eyes.SetPositionAndRotation(camera.position, camera.rotation);
    }
}
