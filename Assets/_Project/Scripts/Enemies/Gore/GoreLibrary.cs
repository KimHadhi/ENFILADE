using UnityEngine;

/// <summary>
/// The art zombie gore uses (ZombieGore), at Resources/Combat/GoreLibrary: the RVFX Blood Effects Pack's gut chunks (only
/// their mesh and material are taken; the prefabs' own scripts never run) and its projected blood decal materials.
/// </summary>
[CreateAssetMenu(fileName = "GoreLibrary", menuName = "ENFILADE/Combat/Gore Library")]
public sealed class GoreLibrary : ScriptableObject
{
    [Tooltip("Gut chunks: thrown from a burst head, and laid on a stump as its torn face.")]
    public GameObject[] guts = new GameObject[0];
    [Tooltip("Projected blood decal materials (URP Decal): splats on walls and floors, and pools under a stump.")]
    public Material[] splats = new Material[0];

    private static GoreLibrary instance;
    private static bool looked;

    public static GoreLibrary Instance
    {
        get
        {
            if (looked) return instance;
            looked = true;
            return instance = Resources.Load<GoreLibrary>("Combat/GoreLibrary");
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() { instance = null; looked = false; }

    /// <summary>A gut chunk's mesh and material, picked by index (wrapped); false when the library has none.</summary>
    public bool Gut(int index, out Mesh mesh, out Material material)
    {
        mesh = null; material = null;
        if (guts == null || guts.Length == 0) return false;
        var prefab = guts[Mathf.Abs(index) % guts.Length];
        if (prefab == null) return false;
        var filter = prefab.GetComponentInChildren<MeshFilter>();
        var renderer = prefab.GetComponentInChildren<MeshRenderer>();
        mesh = filter != null ? filter.sharedMesh : null;
        material = renderer != null ? renderer.sharedMaterial : null;
        return mesh != null && material != null;
    }

    public Material Splat(int index)
    {
        if (splats == null || splats.Length == 0) return null;
        return splats[Mathf.Abs(index) % splats.Length];
    }
}
