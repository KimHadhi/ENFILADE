using UnityEngine;

/// <summary>
/// Which blot a hit leaves on the wall or floor behind it (ImpactVfx.Splat): the generated splat or one of the Cry of Fear
/// blood decals (L4D2 workshop 3612824343) in Resources/Combat/BloodSplats, so the same blot doesn't stamp every wall.
/// Those textures are white detail with alpha (Tools/Blender/addon_blood_decals.py), so ImpactVfx's blood colour, size
/// and fade still decide how they look; a texture dropped into the folder joins them.
/// </summary>
public static class BloodSplatTextures
{
    private const string Folder = "Combat/BloodSplats";
    private static Texture2D[] textures;
    private static int last = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        textures = null;
        last = -1;
    }

    /// <summary>The material for the next splat, never the same blot twice in a row when there is a choice.</summary>
    public static Material Next()
    {
        textures ??= Resources.LoadAll<Texture2D>(Folder);
        int count = textures.Length + 1;
        int pick = Random.Range(0, count);
        if (count > 1 && pick == last) pick = (pick + 1 + Random.Range(0, count - 1)) % count;
        last = pick;
        // The last choice is the generated splat, so it stays in the mix.
        var texture = pick < textures.Length && textures[pick] != null ? textures[pick] : FeelAssets.BloodSplat;
        return FeelAssets.Particle(texture, false);
    }
}
