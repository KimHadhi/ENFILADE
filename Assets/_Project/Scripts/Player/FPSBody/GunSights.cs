using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The sights a gun can wear, as Rainbow Six Siege lets an operator pick one: the gun's own iron sights, red dots,
/// holographic sights and magnified scopes (GunScope). Every one is on the gun from the start (TacticalGunSetup puts the
/// pack's choices on its sight mount); one shows at a time, with the gun's own iron sights up when it is an iron sight
/// itself or there is none. FPSGun aims through the shown one's aim point; the customize menu picks it (C, GunCustomizeMenu).
/// A gun whose iron sights are modelled into its body mesh (KINEMATION's MX16A4: carry handle, rear sight, front sight
/// tower) has them cut out of it when it wakes (ironsInMesh, ironTriangles), so they fold away under an optic too.
/// </summary>
[DisallowMultipleComponent]
public sealed class GunSights : MonoBehaviour
{
    [Serializable]
    public struct Sight
    {
        [Tooltip("The sight on the gun's mount; none for the gun's own iron sights.")]
        public GameObject part;
        [Tooltip("Where the eye lines up through it (FPSAF's aim point).")]
        public Transform aimPoint;
        [Tooltip("The gun's own iron sights stay up with it.")]
        public bool ironSight;
        [Tooltip("A magnified scope's lens, or none.")]
        public GunScope scope;
    }

    public Sight[] sights = Array.Empty<Sight>();
    [Tooltip("The gun's own iron sight parts, folded away under any other sight.")]
    public GameObject[] ironSights = Array.Empty<GameObject>();
    [Tooltip("The sight it starts with.")]
    public int initial;
    [Tooltip("A body mesh with the iron sights modelled into it: they are cut out into a renderer of their own as the gun "
             + "wakes, which then folds away as ironSights do.")]
    public SkinnedMeshRenderer ironsInMesh;
    [Tooltip("Which of that mesh's triangles are the iron sights, numbered through its submeshes in order (TacticalGunSetup).")]
    public int[] ironTriangles = Array.Empty<int>();
    [Tooltip("How many triangles the mesh had when they were numbered; another count means it changed and nothing is cut.")]
    public int ironTriangleTotal;

    public int Current { get; private set; } = -1;
    public int Count => sights != null ? sights.Length : 0;
    private bool Valid => Current >= 0 && Current < Count;
    /// <summary>The shown sight's aim point; null when there is none.</summary>
    public Transform AimPoint => Valid ? sights[Current].aimPoint : null;
    /// <summary>The shown sight's scope, if it is a magnified one.</summary>
    public GunScope Scope => Valid ? sights[Current].scope : null;

    private bool cut;

    private void Awake() => CutIronsOut();

    /// <summary>Shows sight <paramref name="index"/> (wrapped round) and hides the others.</summary>
    public void Select(int index)
    {
        if (Count == 0) return;
        Current = ((index % Count) + Count) % Count;
        for (int i = 0; i < Count; i++)
            if (sights[i].part != null) sights[i].part.SetActive(i == Current);
        ShowIrons();
    }

    private void ShowIrons()
    {
        if (!Valid) return;
        bool irons = sights[Current].part == null || sights[Current].ironSight;
        foreach (var iron in ironSights)
            if (iron != null) iron.SetActive(irons);
    }

    // ---- Iron sights modelled into the body ----

    // Each body mesh is cut once a game, whoever's gun it is: the mesh without its iron sights and the iron sights alone.
    private static readonly Dictionary<Mesh, (Mesh body, Mesh irons)> cuts = new Dictionary<Mesh, (Mesh, Mesh)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ForgetCuts() => cuts.Clear();

    private void CutIronsOut()
    {
        if (cut || ironsInMesh == null || ironTriangles == null || ironTriangles.Length == 0) return;
        cut = true;
        var source = ironsInMesh.sharedMesh;
        if (source == null) return;
        var meshes = Cut(source, ironTriangles, ironTriangleTotal);
        if (meshes.body == null) return;

        ironsInMesh.sharedMesh = meshes.body;
        var holder = ironsInMesh.transform;
        var irons = new GameObject("Iron Sights") { layer = holder.gameObject.layer };
        irons.transform.SetParent(holder.parent, false);
        irons.transform.SetLocalPositionAndRotation(holder.localPosition, holder.localRotation);
        irons.transform.localScale = holder.localScale;
        var skin = irons.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = meshes.irons;
        skin.rootBone = ironsInMesh.rootBone;
        skin.bones = ironsInMesh.bones;
        skin.sharedMaterials = ironsInMesh.sharedMaterials;
        skin.localBounds = ironsInMesh.localBounds;
        skin.updateWhenOffscreen = ironsInMesh.updateWhenOffscreen;
        skin.shadowCastingMode = ironsInMesh.shadowCastingMode;
        skin.receiveShadows = ironsInMesh.receiveShadows;
        skin.quality = ironsInMesh.quality;
        skin.skinnedMotionVectors = ironsInMesh.skinnedMotionVectors;
        skin.lightProbeUsage = ironsInMesh.lightProbeUsage;
        skin.reflectionProbeUsage = ironsInMesh.reflectionProbeUsage;

        var all = new GameObject[(ironSights?.Length ?? 0) + 1];
        if (ironSights != null) Array.Copy(ironSights, all, ironSights.Length);
        all[all.Length - 1] = irons;
        ironSights = all;
        ShowIrons();
    }

    private static (Mesh body, Mesh irons) Cut(Mesh source, int[] ironTriangles, int total)
    {
        if (cuts.TryGetValue(source, out var made) && made.body != null && made.irons != null) return made;
        if (!source.isReadable)
        {
            Debug.LogWarning($"[GunSights] {source.name} can't be read, so its iron sights stay on (switch Read/Write on in its import settings).");
            return default;
        }
        var iron = new HashSet<int>(ironTriangles);
        var triangles = new List<int>();
        var keep = new List<int>();
        var take = new List<int>();
        int number = 0;
        var body = Instantiate(source);
        var irons = Instantiate(source);
        for (int s = 0; s < source.subMeshCount; s++)
        {
            source.GetTriangles(triangles, s);
            keep.Clear();
            take.Clear();
            for (int t = 0; t + 2 < triangles.Count; t += 3, number++)
            {
                var into = iron.Contains(number) ? take : keep;
                into.Add(triangles[t]);
                into.Add(triangles[t + 1]);
                into.Add(triangles[t + 2]);
            }
            body.SetTriangles(keep, s, false);
            irons.SetTriangles(take, s, false);
        }
        if (number != total)
        {
            Debug.LogWarning($"[GunSights] {source.name} has {number} triangles, not the {total} its iron sights were picked from; "
                             + "they stay on. Make the gun again (Tools → ENFILADE → Combat → Make Tactical Shooter Pack Guns).");
            Destroy(body);
            Destroy(irons);
            return default;
        }
        body.name = source.name + " (iron sights off)";
        irons.name = source.name + " (iron sights)";
        body.bounds = source.bounds;
        irons.bounds = source.bounds;
        made = (body, irons);
        cuts[source] = made;
        return made;
    }
}
