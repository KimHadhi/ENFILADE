using UnityEngine;

/// <summary>
/// The one object of its kind in the scene (the chat, the wave manager, the mission tracker), found once and remembered
/// until it is gone. Every script that needed one of these used to search the scene for itself and keep its own copy,
/// some searching again every frame while there was none. This searches at most once a frame, and a new scene's object
/// is found the first time someone asks after the old one went.
/// </summary>
public static class SceneOne<T> where T : Object
{
    private static T found;
    private static int lookedFrame = -1;

    /// <summary>The scene's object of this kind, or null when the scene has none (active objects only).</summary>
    public static T Get()
    {
        if (found != null) return found;
        if (lookedFrame == Time.frameCount) return null;
        lookedFrame = Time.frameCount;
        return found = Object.FindAnyObjectByType<T>();
    }
}
