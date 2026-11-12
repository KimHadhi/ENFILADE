using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The zombie looks as ZombieBody wears them, pared once. Each Synty character prefab carries the whole pack: 101 objects,
/// 50 skinned meshes, 49 of them switched off. Every zombie that came used to copy all of it and then destroy the 49
/// (ZombieBody.Dress), and that is the Component.SetGameObject, Mesh.CalcBoneBounds and Object.Unregister in
/// Logs/hitches.txt whenever a horde, a brute or the range's practice foes came (604 ms at 63 s in the 22:13 run of
/// 2026-11-12). Here each look is copied once with only its worn character kept, under a switched-off shelf kept across
/// scenes, and ZombieBody copies that: about half the objects, one skinned mesh, nothing to destroy, and a corpse that
/// later sinks away has half as much to unregister. A look with nothing switched off is used as it is.
/// SceneWarmup makes them all behind the loading screen; one asked for later is made then.
/// </summary>
public static class ZombieLookTemplates
{
    private static readonly Dictionary<GameObject, GameObject> made = new Dictionary<GameObject, GameObject>();
    private static Transform shelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        made.Clear();
        shelf = null;
    }

    /// <summary>What to copy for <paramref name="look"/>: its pared copy, or the look itself when nothing in it is switched off.</summary>
    public static GameObject Of(GameObject look)
    {
        if (look == null || !Application.isPlaying) return look;
        FrameTags.Mark("zombie made");
        if (made.TryGetValue(look, out var template) && template != null) return template;
        template = Pare(look);
        made[look] = template;
        return template;
    }

    /// <summary>Pares every zombie look ahead (ZombieLooks), each the first time only.</summary>
    public static void Prepare()
    {
        var looks = ZombieLooks.Instance;
        if (looks == null || looks.characters == null) return;
        foreach (var look in looks.characters)
            if (look != null && !made.ContainsKey(look)) made[look] = Pare(look);
    }

    private static GameObject Pare(GameObject look)
    {
        bool switchedOff = false;
        foreach (var part in look.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (!part.gameObject.activeSelf) { switchedOff = true; break; }
        if (!switchedOff) return look;
        // Copied under the switched-off shelf, so the copy never wakes, draws or animates; its own root stays switched on,
        // so what ZombieBody copies from it comes out switched on, as a copy of the prefab did.
        var copy = Object.Instantiate(look, Shelf, false);
        copy.name = look.name;
        foreach (var part in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (part != null && !part.gameObject.activeSelf) Object.DestroyImmediate(part.gameObject);
        return copy;
    }

    private static Transform Shelf
    {
        get
        {
            if (shelf != null) return shelf;
            var root = new GameObject("Zombie Look Templates");
            root.SetActive(false);
            root.hideFlags = HideFlags.HideInHierarchy;
            Object.DontDestroyOnLoad(root);
            return shelf = root.transform;
        }
    }
}
