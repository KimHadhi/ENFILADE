using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// The sights a gun can wear, as Rainbow Six Siege lets an operator pick one: the gun's own iron sights, red dots,
/// holographic sights and magnified scopes (GunScope). Every one is on the gun from the start (TacticalGunSetup embeds the
/// parts its sight socket offers); one shows at a time, with the gun's own iron sights up when it is an iron sight itself
/// or there is none. FPSGun aims through the shown one's aim point; the customize menu picks it (C, GunCustomizeMenu).
/// Where each sight sits is not the prefab's to say: the gun's sight socket (Tables/Weapons/GunMounts.csv, a rail on the
/// gun) and one rule (GunMounts.SeatSight) put it there as the gun wakes, so a table row moves a sight on every PC
/// without the gun being made again, and a mod's row does the same. Which sights the menu offers (fits), which one the gun
/// starts with (starts) and how the view is drawn through each (GunParts.csv) are the tables' too.
/// A gun's own iron sights modelled into its body mesh (a carry handle, a rear drum, a front post) are cut out of it as
/// the gun wakes, by the boxes of its row (folds), into a renderer of their own that folds away under an optic as iron
/// sight objects do; glass the pack draws as solid metal (hides: a built-in sight's lens) is cut away for good.
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
    [FormerlySerializedAs("initial"), SerializeField, Tooltip("The sight it starts with when its GunMounts row names none.")]
    private int start;

    public int Current { get; private set; } = -1;
    public int Count => sights != null ? sights.Length : 0;
    private bool Valid => Current >= 0 && Current < Count;
    /// <summary>The shown sight's aim point; null when there is none.</summary>
    public Transform AimPoint => Valid ? sights[Current].aimPoint : null;
    /// <summary>The shown sight's scope, if it is a magnified one.</summary>
    public GunScope Scope => Valid ? sights[Current].scope : null;

    /// <summary>The gun's id in the tables (GunMounts.IdOf: its object's name).</summary>
    public string GunId => GunMounts.IdOf(gameObject);

    /// <summary>
    /// The sight the gun starts with: the one its sight socket names (GunMounts.csv starts), else the prefab's own, else
    /// the first it offers. Lower case as the field it was: FPSGun, the weapons tab and the benches read it by that name.
    /// </summary>
    public int initial
    {
        get
        {
            if (Count == 0) return 0;
            var socket = GunMounts.Of(GunId, GunMounts.Sight);
            if (socket.Found && !string.IsNullOrEmpty(socket.Starts))
                for (int i = 0; i < Count; i++)
                    if (string.Equals(IdOf(i), socket.Starts, StringComparison.OrdinalIgnoreCase)) return i;
            int kept = Mathf.Clamp(start, 0, Count - 1);
            if (!socket.Found || socket.Offers(IdOf(kept))) return kept;
            for (int i = 0; i < Count; i++)
                if (socket.Offers(IdOf(i))) return i;
            return kept;
        }
    }

    /// <summary>The editor's setup says which sight a gun starts with when no table row does.</summary>
    public void StartWith(int index) => start = Mathf.Max(0, index);

    /// <summary>A sight's id in the tables: its part's name, or GunMounts.Own for the gun's own sights.</summary>
    public string IdOf(int index)
        => index >= 0 && index < Count && sights[index].part != null ? sights[index].part.name : GunMounts.Own;

    /// <summary>The gun's sight socket offers this sight (GunMounts.csv fits; a gun without a row offers all it carries).</summary>
    public bool Offered(int index)
    {
        if (index < 0 || index >= Count) return false;
        var socket = GunMounts.Of(GunId, GunMounts.Sight);
        return !socket.Found || socket.Offers(IdOf(index));
    }

    /// <summary>How many of its sights the gun offers.</summary>
    public int OfferedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Count; i++)
                if (Offered(i)) count++;
            return count;
        }
    }

    /// <summary>What the shown sight is (GunParts.csv): how far it magnifies and how its view is drawn.</summary>
    public GunMounts.Part Shown => Valid ? GunMounts.PartOf(IdOf(Current)) : default;

    /// <summary>What sight <paramref name="index"/> is (GunParts.csv).</summary>
    public GunMounts.Part PartOf(int index) => GunMounts.PartOf(IdOf(index));

    private bool cut;
    private int seatedFor = -1;

    private void Awake() => Seat();

    /// <summary>Shows sight <paramref name="index"/> (wrapped round) and hides the others.</summary>
    public void Select(int index)
    {
        if (Count == 0) return;
        // Also for whoever shows a gun without waking it (the weapons tab, the editor's benches).
        Seat();
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

    // ---- Each sight on the gun's rail (GunMounts) ----

    // The gun's own aim point: the sights entry with no part.
    private Transform OwnAim()
    {
        for (int i = 0; i < Count; i++)
            if (sights[i].part == null && sights[i].aimPoint != null) return sights[i].aimPoint;
        return null;
    }

    // What the sight parts hang on: KINEMATION's mount bone on its own guns, the setup's on the others.
    private Transform Mount()
    {
        for (int i = 0; i < Count; i++)
            if (sights[i].part != null && sights[i].part.transform.parent != null) return sights[i].part.transform.parent;
        return null;
    }

    /// <summary>
    /// Puts the gun's eye place, its sight socket and every sight on it where the tables have them (GunMounts): once as
    /// the gun wakes or is first shown, and again after the tables were read anew. A gun without rows stays as its prefab
    /// has it. <paramref name="foldOwnSights"/> also cuts the gun's own iron sights out of its mesh (its row's folds and
    /// hides); the editor's setup seats without it, for a mesh cut here is made in memory and a prefab can't keep it.
    /// </summary>
    public void Seat(bool foldOwnSights = true)
    {
        if (seatedFor == GunMounts.Version) return;
        seatedFor = GunMounts.Version;
        var gun = transform;
        string id = GunId;
        var eye = GunMounts.Of(id, GunMounts.Eye);
        var own = OwnAim();
        if (eye.Found && own != null) GunMounts.Place(gun, own, eye);
        var socket = GunMounts.Of(id, GunMounts.Sight);
        if (!socket.Found) return;
        var mount = Mount();
        if (mount != null)
        {
            // Where each sight's own eye point is from its part, before anything moves.
            var aims = new Vector3[Count];
            for (int i = 0; i < Count; i++)
                if (sights[i].part != null && sights[i].aimPoint != null)
                    aims[i] = sights[i].part.transform.InverseTransformPoint(sights[i].aimPoint.position);
            GunMounts.Place(gun, mount, socket);
            Vector3 eyeAt = eye.Found ? eye.Position : own != null ? gun.InverseTransformPoint(own.position) : socket.Position;
            for (int i = 0; i < Count; i++)
            {
                var part = sights[i].part;
                if (part == null || part.transform.parent != mount) continue;
                var data = GunMounts.PartOf(part.name);
                float along = GunMounts.SeatSight(socket, eyeAt, aims[i], data.FootBack, data.FootFront);
                part.transform.localPosition = new Vector3(0f, 0f, along);
                part.transform.localRotation = Quaternion.identity;
            }
        }
        if (foldOwnSights) FoldOwnSights(socket);
    }

    // ---- The gun's own iron sights, modelled into its body ----

    // Each body mesh is cut once a game for each set of boxes, whoever's gun it is: the mesh without what the boxes
    // hold, and the iron sights alone.
    private static readonly Dictionary<(Mesh mesh, string boxes), (Mesh body, Mesh irons)> cuts = new Dictionary<(Mesh, string), (Mesh, Mesh)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ForgetCuts() => cuts.Clear();

    // The pieces of the gun's own meshes that lie wholly inside a box of its row come out of them: the folds into a
    // renderer of their own that shows only with the gun's own sights, the hides for good. Once per gun.
    private void FoldOwnSights(in GunMounts.Socket socket)
    {
        if (cut) return;
        cut = true;
        if (socket.Folds.Length == 0 && socket.Hides.Length == 0) return;
        var added = new List<GameObject>();
        foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skin.sharedMesh == null || !OwnBody(skin.transform)) continue;
            var meshes = Cut(skin, socket);
            if (meshes.body == null) continue;
            skin.sharedMesh = meshes.body;
            if (meshes.irons == null) continue;
            var holder = skin.transform;
            var irons = new GameObject("Iron Sights") { layer = holder.gameObject.layer };
            irons.transform.SetParent(holder.parent, false);
            irons.transform.SetLocalPositionAndRotation(holder.localPosition, holder.localRotation);
            irons.transform.localScale = holder.localScale;
            var copy = irons.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = meshes.irons;
            copy.rootBone = skin.rootBone;
            copy.bones = skin.bones;
            copy.sharedMaterials = skin.sharedMaterials;
            copy.localBounds = skin.localBounds;
            copy.updateWhenOffscreen = skin.updateWhenOffscreen;
            copy.shadowCastingMode = skin.shadowCastingMode;
            copy.receiveShadows = skin.receiveShadows;
            copy.quality = skin.quality;
            copy.skinnedMotionVectors = skin.skinnedMotionVectors;
            copy.lightProbeUsage = skin.lightProbeUsage;
            copy.reflectionProbeUsage = skin.reflectionProbeUsage;
            added.Add(irons);
        }
        if (added.Count == 0) return;
        var all = new GameObject[(ironSights?.Length ?? 0) + added.Count];
        if (ironSights != null) Array.Copy(ironSights, all, ironSights.Length);
        added.CopyTo(all, all.Length - added.Count);
        ironSights = all;
        ShowIrons();
    }

    // Of the gun itself: not a sight on its mount, not a part GunAttachments switches, not an iron sight object.
    private bool OwnBody(Transform piece)
    {
        for (int i = 0; i < Count; i++)
            if (sights[i].part != null && piece.IsChildOf(sights[i].part.transform)) return false;
        if (ironSights != null)
            foreach (var iron in ironSights)
                if (iron != null && piece.IsChildOf(iron.transform)) return false;
        if (TryGetComponent(out GunAttachments parts) && parts.Carries(piece)) return false;
        return true;
    }

    // The renderer's mesh without the pieces its row's boxes hold, and those of the folds alone (null when there are none).
    // A piece is what hangs together: vertices at one place count as one, as a hard edge or a seam splits them.
    private (Mesh body, Mesh irons) Cut(SkinnedMeshRenderer skin, in GunMounts.Socket socket)
    {
        var source = skin.sharedMesh;
        var key = (source, (socket.FoldsText ?? "") + ";" + (socket.HidesText ?? ""));
        // A mesh with nothing in a box is remembered too (no meshes): it isn't read again for the next gun of its kind.
        if (cuts.TryGetValue(key, out var made)) return made;
        cuts[key] = default;
        if (!source.isReadable)
        {
            Debug.LogWarning($"[GunSights] {source.name} can't be read, so {name}'s iron sights stay on under an optic (switch Read/Write on in its import settings).");
            return default;
        }
        var raw = new List<Vector3>();
        source.GetVertices(raw);
        int count = raw.Count;
        // As the gun stands: a box is a place on the gun, and the mesh's own space is its bones' at rest.
        var bakedMesh = new Mesh();
        skin.BakeMesh(bakedMesh, true);
        var posed = new List<Vector3>();
        bakedMesh.GetVertices(posed);
        if (Application.isPlaying) Destroy(bakedMesh);
        else DestroyImmediate(bakedMesh);
        if (posed.Count != count || count == 0) return default;
        // BakeMesh(…, true) gives the vertices in the renderer's own space with its scale taken out of them, so its whole
        // matrix puts them in the world (position and rotation alone would only do for a renderer of scale 1).
        Matrix4x4 toGun = transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;

        var root = new int[count];
        for (int i = 0; i < count; i++) root[i] = i;
        int Find(int a)
        {
            while (root[a] != a) { root[a] = root[root[a]]; a = root[a]; }
            return a;
        }
        void Join(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b) root[a] = b;
        }
        // One piece: vertices at one place (to a hundredth of a millimetre), and the corners of each triangle.
        var place = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < count; i++)
        {
            var at = new Vector3Int(Mathf.RoundToInt(raw[i].x * 1e5f), Mathf.RoundToInt(raw[i].y * 1e5f), Mathf.RoundToInt(raw[i].z * 1e5f));
            if (place.TryGetValue(at, out int first)) Join(i, first);
            else place[at] = i;
        }
        var triangles = new List<int>();
        for (int s = 0; s < source.subMeshCount; s++)
        {
            source.GetTriangles(triangles, s);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Join(triangles[t], triangles[t + 1]);
                Join(triangles[t], triangles[t + 2]);
            }
        }

        // A piece is a box's when every vertex of it is inside that one box: which boxes still hold all of it, a bit each
        // (the first 32 of each kind; a gun has two or three).
        int foldCount = Mathf.Min(32, socket.Folds.Length), hideCount = Mathf.Min(32, socket.Hides.Length);
        uint allFolds = foldCount >= 32 ? uint.MaxValue : (1u << foldCount) - 1u, allHides = hideCount >= 32 ? uint.MaxValue : (1u << hideCount) - 1u;
        var folds = new Dictionary<int, uint>();
        var hides = new Dictionary<int, uint>();
        for (int i = 0; i < count; i++)
        {
            int piece = Find(i);
            Vector3 at = toGun.MultiplyPoint3x4(posed[i]);
            uint inFold = 0u, inHide = 0u;
            for (int b = 0; b < foldCount; b++)
                if (socket.Folds[b].Contains(at)) inFold |= 1u << b;
            for (int b = 0; b < hideCount; b++)
                if (socket.Hides[b].Contains(at)) inHide |= 1u << b;
            folds[piece] = (folds.TryGetValue(piece, out uint heldFold) ? heldFold : allFolds) & inFold;
            hides[piece] = (hides.TryGetValue(piece, out uint heldHide) ? heldHide : allHides) & inHide;
        }

        var body = Instantiate(source);
        var irons = Instantiate(source);
        var keep = new List<int>();
        var take = new List<int>();
        int taken = 0, hidden = 0;
        for (int s = 0; s < source.subMeshCount; s++)
        {
            source.GetTriangles(triangles, s);
            keep.Clear();
            take.Clear();
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int piece = Find(triangles[t]);
                // Glass never drawn goes first: a lens inside a built-in sight's box is no iron sight.
                if (hides[piece] != 0u) { hidden++; continue; }
                var into = folds[piece] != 0u ? take : keep;
                if (into == take) taken++;
                into.Add(triangles[t]);
                into.Add(triangles[t + 1]);
                into.Add(triangles[t + 2]);
            }
            body.SetTriangles(keep, s, false);
            irons.SetTriangles(take, s, false);
        }
        if (taken == 0 && hidden == 0)
        {
            // Nothing of this mesh is in a box (a magazine of its own renderer): it stays as it is.
            DestroyMesh(body);
            DestroyMesh(irons);
            return default;
        }
        body.name = source.name + " (own sights off)";
        body.bounds = source.bounds;
        body.hideFlags = HideFlags.DontSave;
        if (taken == 0)
        {
            DestroyMesh(irons);
            irons = null;
        }
        else
        {
            irons.name = source.name + " (own sights)";
            irons.bounds = source.bounds;
            irons.hideFlags = HideFlags.DontSave;
        }
        made = (body, irons);
        cuts[key] = made;
        return made;
    }

    private static void DestroyMesh(Mesh mesh)
    {
        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
    }
}
