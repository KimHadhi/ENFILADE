using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The magazine in a gun is a real one (the user's playtest of 2026-11-11: "내 총에 달려 있는 탄창이 실제로 세상 내에
/// 존재하는 탄창이고 버리면 그거 바닥에 버리고 새로운 탄창으로 갈아 끼는 거지"). KINEMATION's reload clips pull the gun's
/// magazine part out, take it down to the pouch and bring it back in: one part plays the old magazine and the new one.
/// So, as the clip carries the old one away (dropShare of the way from its leaving to its farthest point,
/// ReloadMoments.Magazine), the hand lets it go: a copy of the magazine's own triangles, cut once from the gun's mesh,
/// takes its place in the world with the speed it had and falls to the floor (DroppedMagazine), while the gun's part is
/// shrunk to nothing until the clip has it at the pouch and brings it back as the new one. Tactical and empty reloads
/// alike. Every PC plays it for every player (a friend's copy reloads the same way: WeaponController.ShowReload), so
/// friends' magazines fall too, each PC's own physics. A gun with no magazine part in its clip (a shell reload), or whose
/// part waits parked off the gun (a revolver's speedloader), drops nothing. Numbers: Tables/Magazines.csv.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 5)]
public sealed class GunMagazine : MonoBehaviour, ITableBound
{
    public const string TableName = "Magazines", TableRow = "Default";

    [SerializeField, Tooltip("Magazines fall out in reloads at all.")]
    private bool drops = true;
    [SerializeField, Range(0f, 1f), Tooltip("The hand lets the old one go this share of the way (in time) from its leaving the gun to its farthest point.")]
    private float dropShare = .35f;
    [SerializeField, Min(0f), Tooltip("A part this far or further off the sight line before the reload (m) waits parked off the gun (a speedloader): not dropped.")]
    private float parkedFrom = .3f;
    [SerializeField, Min(0f), Tooltip("Thrown a little down and away from the gun as it is let go (m/s), on top of the hand's speed.")]
    private float push = .8f;
    [SerializeField, Min(0f), Tooltip("Most tumble as it goes (radians a second).")]
    private float spin = 7f;
    [SerializeField, Min(.01f), Tooltip("Its mass (kg).")]
    private float mass = .4f;
    [SerializeField, Min(0f), Tooltip("How long it lies on the floor before it sinks (s).")]
    private float lifeSeconds = 40f;
    [SerializeField, Min(.05f), Tooltip("How long it takes to sink out of sight (s).")]
    private float sinkSeconds = 1.5f;
    [SerializeField, Min(1), Tooltip("Most on the floor at once, everyone's together; past it the oldest sinks.")]
    private int mostOnFloor = 30;
    [SerializeField, Range(0f, 1f), Tooltip("Loudness of the clack as it lands.")]
    private float clackVolume = .5f;

    // The magazine's own triangles and materials, cut once per gun model and magazine part.
    private static readonly Dictionary<(Mesh, string), (Mesh mesh, Material[] materials)> cuts =
        new Dictionary<(Mesh, string), (Mesh, Material[])>();

    private FPSGun gun;
    private WeaponController controller;
    private Transform part;
    private string partPath;
    private Vector3 restScale, lastPosition;
    private ReloadMoments.Magazine moments;
    private float timer, span;
    private bool running, dropped, hidden, hasLast;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cuts.Clear();

    /// <summary>On a gun as FPSBody sets it up (FPSGun.Set).</summary>
    internal static GunMagazine For(FPSGun gun)
    {
        if (!gun.TryGetComponent(out GunMagazine magazine))
        {
            magazine = gun.gameObject.AddComponent<GunMagazine>();
            GameTables.Bind(TableName, TableRow, magazine);
        }
        magazine.gun = gun;
        return magazine;
    }

    public void OnTableApplied() { }

    /// <summary>
    /// Cuts the magazine out of the gun's mesh ahead (ReloadMoments, right after measuring the clips while the map loads),
    /// so the first reload in play doesn't pay for it. Once per gun model; later copies find it made.
    /// </summary>
    internal void Prepare()
    {
        if (!drops || gun == null || gun.Data == null) return;
        if (!ReloadMoments.TryGetMagazine(gun.Data, false, out var tactical) && !ReloadMoments.TryGetMagazine(gun.Data, true, out tactical)) return;
        if (tactical.Start > parkedFrom) return;
        if (part == null || partPath != tactical.Path)
        {
            part = Find(tactical.Path);
            partPath = tactical.Path;
            if (part != null) restScale = part.localScale;
        }
        if (part != null) Cut(out _, out _);
    }

    /// <summary>A reload began (FPSGun.Reload); length is how long its clip plays, for when the gun's own timer can't be read.</summary>
    internal void Begin(bool empty, float length)
    {
        Restore();
        running = false;
        if (!drops || gun == null || gun.Data == null) return;
        if (!ReloadMoments.TryGetMagazine(gun.Data, empty, out moments) || moments.Start > parkedFrom) return;
        if (part == null || partPath != moments.Path)
        {
            part = Find(moments.Path);
            partPath = moments.Path;
            if (part != null) restScale = part.localScale;
        }
        if (part == null) return;
        running = true;
        dropped = hasLast = false;
        timer = 0f;
        span = Mathf.Max(.05f, length);
    }

    /// <summary>The reload ended, done or cut short (FPSGun): the magazine is in the gun again.</summary>
    internal void End()
    {
        running = false;
        Restore();
    }

    // The part under the gun's animated model, by its path there (ReloadMoments measured it on the same model).
    private Transform Find(string path)
    {
        var animator = GetComponentInChildren<Animator>(true);
        var root = animator != null ? animator.transform : transform;
        return string.IsNullOrEmpty(path) ? null : root.Find(path);
    }

    // How far into the reload it is (0-1): the gun's own reload timer, as the reload sounds go by (WeaponFeedback), else
    // the clip's length.
    private float Progress()
    {
        if (controller == null) controller = GetComponentInParent<WeaponController>();
        var runtime = controller != null ? controller.Runtime : null;
        if (runtime != null && runtime.IsReloading && runtime.ReloadDuration > 0f && controller.EquippedData == gun.Data)
            return 1f - runtime.ReloadRemaining / runtime.ReloadDuration;
        return timer / span;
    }

    // After the clips and the gun framework have posed the gun this frame, before it is drawn.
    private void LateUpdate()
    {
        if (!running || part == null) return;
        float dt = Time.deltaTime;
        timer += dt;
        float progress = Progress();
        Vector3 at = part.position;
        Vector3 velocity = hasLast && dt > 0f ? (at - lastPosition) / dt : Vector3.zero;
        lastPosition = at;
        hasLast = true;
        if (!dropped && progress >= Mathf.Lerp(moments.Out, moments.Far, dropShare))
        {
            dropped = true;
            hidden = Drop(velocity);
        }
        if (hidden)
        {
            // At the pouch the hand has the new one: it shows from here on its way in.
            if (progress >= moments.Far) Restore();
            else part.localScale = Vector3.zero;
        }
        if (progress >= 1f) End();
    }

    // The old magazine leaves the hand: its copy goes into the world where the part is now, moving as it was. False when
    // there is nothing to drop (a mesh that can't be read), so the gun's part stays as it is.
    private bool Drop(Vector3 velocity)
    {
        if (!Cut(out var mesh, out var materials)) return false;
        Vector3 away = Vector3.ProjectOnPlane(part.position - transform.position, Vector3.up);
        away = away.sqrMagnitude > 1e-4f ? away.normalized : -transform.up;
        Vector3 throwing = velocity + (away * .5f + Vector3.down).normalized * push;
        Vector3 tumble = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * spin;
        DroppedMagazine.Make(mesh, materials, part.position, part.rotation, part.lossyScale, throwing, tumble, mass, lifeSeconds,
            sinkSeconds, clackVolume, mostOnFloor);
        return true;
    }

    private void Restore()
    {
        if (!hidden) return;
        hidden = false;
        if (part != null) part.localScale = restScale;
    }

    private void OnDisable()
    {
        running = false;
        Restore();
    }

    // ---- Cutting the magazine out of the gun ----

    // The magazine as a mesh in its part's own space: every triangle of the gun's skin whose three corners move with the
    // part (or a part under it), taken to that space through the part's bind pose, and any plain meshes under the part.
    private bool Cut(out Mesh mesh, out Material[] materials)
    {
        mesh = null;
        materials = null;
        var skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var key = (skins.Length > 0 ? skins[0].sharedMesh : null, partPath);
        if (cuts.TryGetValue(key, out var known))
        {
            mesh = known.mesh;
            materials = known.materials;
            return mesh != null;
        }
        var pieces = new List<CombineInstance>();
        var pieceMaterials = new List<Material>();
        foreach (var skin in skins) CutSkin(skin, pieces, pieceMaterials);
        foreach (var filter in part.GetComponentsInChildren<MeshFilter>(true))
        {
            var source = filter.sharedMesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (source == null || renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
            var shared = renderer.sharedMaterials;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                pieces.Add(new CombineInstance
                {
                    mesh = source, subMeshIndex = s, transform = part.worldToLocalMatrix * filter.transform.localToWorldMatrix
                });
                pieceMaterials.Add(s < shared.Length ? shared[s] : shared.Length > 0 ? shared[shared.Length - 1] : null);
            }
        }
        if (pieces.Count > 0)
        {
            mesh = new Mesh { name = partPath + " (dropped)", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(pieces.ToArray(), false, true);
            mesh.RecalculateBounds();
            materials = pieceMaterials.ToArray();
        }
        // The pieces cut from skins were made here only to be combined.
        foreach (var piece in pieces)
            if (piece.mesh != null && piece.mesh.name.EndsWith(" (cut)")) Destroy(piece.mesh);
        cuts[key] = (mesh, materials);
        return mesh != null;
    }

    private void CutSkin(SkinnedMeshRenderer skin, List<CombineInstance> pieces, List<Material> pieceMaterials)
    {
        var source = skin.sharedMesh;
        var bones = skin.bones;
        if (source == null || bones == null) return;
        int home = System.Array.IndexOf(bones, part);
        var moving = new bool[bones.Length];
        bool any = false;
        for (int b = 0; b < bones.Length; b++)
            if (bones[b] != null && bones[b].IsChildOf(part)) { moving[b] = true; any = true; }
        if (!any || home < 0) return;
        if (!source.isReadable)
        {
            Debug.LogWarning($"[GunMagazine] {source.name} can't be read (Read/Write off), so its magazine isn't dropped.", this);
            return;
        }
        var bind = source.bindposes[home];
        var weights = source.boneWeights;
        var vertices = source.vertices;
        var normals = source.normals;
        var uvs = source.uv;
        var shared = skin.sharedMaterials;
        for (int s = 0; s < source.subMeshCount; s++)
        {
            var triangles = source.GetTriangles(s);
            var keep = new List<int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
                if (Moves(triangles[t]) && Moves(triangles[t + 1]) && Moves(triangles[t + 2]))
                {
                    keep.Add(triangles[t]); keep.Add(triangles[t + 1]); keep.Add(triangles[t + 2]);
                }
            if (keep.Count == 0) continue;
            // Only the corners used, in the part's own space at the bind pose.
            var map = new Dictionary<int, int>();
            var ownVertices = new List<Vector3>();
            var ownNormals = new List<Vector3>();
            var ownUvs = new List<Vector2>();
            var ownTriangles = new int[keep.Count];
            for (int i = 0; i < keep.Count; i++)
            {
                int v = keep[i];
                if (!map.TryGetValue(v, out int mine))
                {
                    mine = ownVertices.Count;
                    map[v] = mine;
                    ownVertices.Add(bind.MultiplyPoint3x4(vertices[v]));
                    ownNormals.Add(v < normals.Length ? bind.MultiplyVector(normals[v]).normalized : Vector3.up);
                    ownUvs.Add(v < uvs.Length ? uvs[v] : Vector2.zero);
                }
                ownTriangles[i] = mine;
            }
            var piece = new Mesh { name = source.name + " (cut)", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            piece.SetVertices(ownVertices);
            piece.SetNormals(ownNormals);
            piece.SetUVs(0, ownUvs);
            piece.SetTriangles(ownTriangles, 0);
            pieces.Add(new CombineInstance { mesh = piece, subMeshIndex = 0, transform = Matrix4x4.identity });
            pieceMaterials.Add(s < shared.Length ? shared[s] : shared.Length > 0 ? shared[shared.Length - 1] : null);
        }

        // A corner moves with the magazine when the bone that moves it most is the part or one under it.
        bool Moves(int v)
        {
            if (v >= weights.Length) return false;
            var w = weights[v];
            int strongest = w.boneIndex0;
            float best = w.weight0;
            if (w.weight1 > best) { best = w.weight1; strongest = w.boneIndex1; }
            if (w.weight2 > best) { best = w.weight2; strongest = w.boneIndex2; }
            if (w.weight3 > best) strongest = w.boneIndex3;
            return strongest >= 0 && strongest < moving.Length && moving[strongest];
        }
    }
}
