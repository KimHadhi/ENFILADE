using UnityEngine;
using Object = UnityEngine.Object;

// OutfitStudio: Clearing up. One part of the class; the rest is in OutfitStudio.cs and the files beside it.
public sealed partial class OutfitStudio
{
    /// <summary>Takes the standing piece away.</summary>
    public void Clear()
    {
        if (pose.IsValid()) pose.Destroy();
        if (sitter != null) Gone(sitter);
        sitter = null;
        shown.Clear();
        points.Clear();
        parts.Clear();
        framed.Clear();
        autoPart = 0;
        framedPart = int.MinValue;
        // The next piece is measured afresh.
        metered = false;
        gain = 1f;
    }

    public void Dispose()
    {
        Clear();
        if (film != null) { film.Release(); Gone(film); }
        film = null;
        if (meter != null) Gone(meter);
        meter = null;
        if (baked != null) Gone(baked);
        baked = null;
        if (root != null) Gone(root.gameObject);
    }

    private static void Gone(Object thing)
    {
        if (Application.isPlaying) Object.Destroy(thing);
        else Object.DestroyImmediate(thing);
    }
}
