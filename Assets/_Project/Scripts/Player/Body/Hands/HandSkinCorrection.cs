using System.Collections.Generic;
using UnityEngine;

/// <summary>Relaxes pinched thumb-web skin in the held pose, without changing any finger joint or the original mesh.</summary>
[DisallowMultipleComponent]
internal sealed class HandSkinCorrection : MonoBehaviour
{
    private sealed class Part
    {
        public SkinnedMeshRenderer Renderer;
        public Mesh Original, Copy;
        public readonly int[] Shapes = { -1, -1 };
        // Each thumb's skin on this mesh, keyed by the thumb's root bone.
        public readonly Dictionary<Transform, Thumb> Thumbs = new Dictionary<Transform, Thumb>();
    }

    // One thumb's skin on one mesh, worked out from the bind pose while the body is made. The first grip then only
    // relaxes the few hundred vertices around the thumb web, where it used to scan, weld and invert the whole body mesh in
    // that frame: 66 ms of the first ledge hang's 97 ms frame in the 2026-11-06 hitch log.
    private sealed class Thumb
    {
        // The vertices the thumb bones move by more than 5%, which carry the correction, and their weights.
        public int[] Affected = System.Array.Empty<int>();
        public BoneWeight[] AffectedWeights = System.Array.Empty<BoneWeight>();
        // Web vertices whose welded group relaxes, and that group as an index into Members.
        public int[] Web = System.Array.Empty<int>();
        public int[] WebGroup = System.Array.Empty<int>();
        // Every welded group in play (the relaxing ones and their neighbours): its vertices, its strength (the least web
        // amount among them) and, for a relaxing group, the groups it is averaged with.
        public int[][] Members = System.Array.Empty<int[]>();
        public float[] Strength = System.Array.Empty<float>();
        public int[][] Neighbours = System.Array.Empty<int[]>();
    }

    // A correction under way. The first grip still cost 49 ms in one frame once the vertex work moved to load (2026-11-06
    // re-measure): baking, relaxing and adding the blend shape are each a few milliseconds plus the upload. So the grip
    // frame only bakes the held pose and the frames after relax it and then add the shape; the skin settles two frames
    // later, which nothing can see.
    private sealed class Work
    {
        public Part Part;
        public Thumb Data;
        public int Side;
        public Mesh Baked;
        public Matrix4x4[] Inverses;
        public Matrix4x4 ToWorld;
        public float MaxMove;
        public System.Func<Vector3, bool> Clear;
        public Vector3[] Delta, Displacements;
        // The frame of the last step, so the next one waits for another frame even if this LateUpdate runs after the grip's.
        public int Frame;
    }

    private readonly List<Part> parts = new List<Part>();
    private readonly List<Work> pending = new List<Work>();
    private readonly bool[] prepared = new bool[2];

    public static HandSkinCorrection For(Animator animator)
    {
        var result = animator.GetComponent<HandSkinCorrection>();
        return result != null ? result : animator.gameObject.AddComponent<HandSkinCorrection>();
    }

    /// <summary>
    /// Everything the first grip of this thumb needs that the pose doesn't change, done while the body is being made:
    /// the copy of each mesh the thumb's web skin lies on, already worn, and the thumb's vertices, welds and neighbours on
    /// it. Done at the first ledge grip instead, it stalled that frame: copying the mesh (about 40 ms), then scanning and
    /// inverting every vertex and uploading the swapped mesh (97 ms).
    /// </summary>
    public void CopyAhead(Transform[] thumb)
    {
        if (thumb == null || thumb.Length < 3) return;
        foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh source = renderer.sharedMesh;
            if (source == null) continue;
            Transform[] bones = renderer.bones;
            if (!Carries(bones, thumb)) continue;
            // The skin's own vertices and weights are read here, which needs Read/Write on the model. Without it this skin
            // goes without the thumb-web fix; reading it anyway threw and left the body half made (user, 2026-11-07).
            if (!source.isReadable)
            {
                ReportUnreadable(source, renderer);
                continue;
            }
            Part part = parts.Find(item => item.Renderer == renderer);
            if (part == null) parts.Add(part = MakePart(renderer, source));
            if (!part.Thumbs.ContainsKey(thumb[0])) part.Thumbs[thumb[0]] = Measure(part.Original, bones, thumb);
        }
    }

    private static readonly HashSet<Mesh> reportedUnreadable = new();

    private static void ReportUnreadable(Mesh mesh, Renderer renderer)
    {
        if (!reportedUnreadable.Add(mesh)) return;
        Debug.LogWarning($"HandSkinCorrection: the mesh '{mesh.name}' ({renderer.transform.root.name}) has Read/Write off, so its "
            + "thumb web isn't corrected when the hand grips. Turn on Read/Write in its model's import settings to have it.", renderer);
    }

    private static bool Carries(Transform[] bones, Transform[] thumb)
    {
        foreach (var bone in bones)
            if (bone != null && System.Array.IndexOf(thumb, bone) >= 0) return true;
        return false;
    }

    private static Part MakePart(SkinnedMeshRenderer renderer, Mesh source)
    {
        var part = new Part { Renderer = renderer, Original = source, Copy = Instantiate(source) };
        part.Copy.name = source.name + " (hand contact correction)";
        part.Copy.hideFlags = HideFlags.HideAndDontSave;
        // Worn from the start: swapping meshes at the first grip uploaded the whole mesh in that frame.
        Wear(renderer, part.Copy);
        return part;
    }

    // Swaps the mesh and keeps the blend shape weights it had (the face's).
    private static void Wear(SkinnedMeshRenderer renderer, Mesh mesh)
    {
        int count = renderer.sharedMesh != null ? renderer.sharedMesh.blendShapeCount : 0;
        var previous = new float[count];
        for (int i = 0; i < count; i++) previous[i] = renderer.GetBlendShapeWeight(i);
        renderer.sharedMesh = mesh;
        for (int i = 0; i < count; i++) renderer.SetBlendShapeWeight(i, previous[i]);
    }

    private static Thumb Measure(Mesh mesh, Transform[] bones, Transform[] thumb)
    {
        var onThumb = new bool[bones.Length];
        var onRoot = new bool[bones.Length];
        for (int b = 0; b < bones.Length; b++)
        {
            onThumb[b] = bones[b] == thumb[0] || bones[b] == thumb[1] || bones[b] == thumb[2];
            onRoot[b] = bones[b] == thumb[0];
        }
        BoneWeight[] weights = mesh.boneWeights;
        int count = weights.Length;
        var web = new float[count];
        var affected = new List<int>();
        var affectedWeights = new List<BoneWeight>();
        for (int i = 0; i < count; i++)
        {
            BoneWeight w = weights[i];
            if (Share(onThumb, w) > .05f) { affected.Add(i); affectedWeights.Add(w); }
            web[i] = Mathf.Clamp01((Share(onRoot, w) - .05f) * 2f);
        }
        var result = new Thumb { Affected = affected.ToArray(), AffectedWeights = affectedWeights.ToArray() };
        if (result.Affected.Length == 0) return result;

        // Weld UV/normal seams in the undeformed skin before relaxing, so the correction cannot open a seam.
        Vector3[] rest = mesh.vertices;
        var welds = new Dictionary<Vector3Int, int>();
        var groups = new int[count];
        var strength = new List<float>();
        for (int i = 0; i < count; i++)
        {
            Vector3Int key = Vector3Int.RoundToInt(rest[i] * 100000f);
            if (!welds.TryGetValue(key, out int group))
            {
                group = strength.Count; welds.Add(key, group);
                strength.Add(1f);
            }
            groups[i] = group;
            strength[group] = Mathf.Min(strength[group], web[i]);
        }
        // Only a group of web skin alone relaxes, averaged with every group it shares an edge with.
        var neighbours = new Dictionary<int, List<int>>();
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
            for (int edge = 0; edge < 3; edge++)
            {
                int a = groups[triangles[i + edge]], b = groups[triangles[i + (edge + 1) % 3]];
                Link(a, b); Link(b, a);
            }
        var local = new Dictionary<int, int>();
        foreach (var pair in neighbours)
        {
            Local(pair.Key);
            foreach (int other in pair.Value) Local(other);
        }
        var members = new List<int>[local.Count];
        for (int i = 0; i < local.Count; i++) members[i] = new List<int>();
        var webVertices = new List<int>();
        var webGroups = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (!local.TryGetValue(groups[i], out int at)) continue;
            members[at].Add(i);
            if (web[i] > 0f && neighbours.ContainsKey(groups[i])) { webVertices.Add(i); webGroups.Add(at); }
        }
        result.Members = new int[local.Count][];
        result.Strength = new float[local.Count];
        result.Neighbours = new int[local.Count][];
        foreach (var pair in local)
        {
            result.Members[pair.Value] = members[pair.Value].ToArray();
            result.Strength[pair.Value] = strength[pair.Key];
            if (!neighbours.TryGetValue(pair.Key, out var list)) continue;
            var mapped = new int[list.Count];
            for (int i = 0; i < list.Count; i++) mapped[i] = local[list[i]];
            result.Neighbours[pair.Value] = mapped;
        }
        result.Web = webVertices.ToArray();
        result.WebGroup = webGroups.ToArray();
        return result;

        void Link(int a, int b)
        {
            if (a == b || strength[a] <= 0f) return;
            if (!neighbours.TryGetValue(a, out var list)) neighbours[a] = list = new List<int>();
            if (!list.Contains(b)) list.Add(b);
        }

        void Local(int group)
        {
            if (!local.ContainsKey(group)) local.Add(group, local.Count);
        }
    }

    private static float Share(bool[] flagged, BoneWeight w)
        => Weight(flagged, w.boneIndex0, w.weight0) + Weight(flagged, w.boneIndex1, w.weight1)
           + Weight(flagged, w.boneIndex2, w.weight2) + Weight(flagged, w.boneIndex3, w.weight3);

    private static float Weight(bool[] flagged, int index, float amount) => amount > 0f && flagged[index] ? amount : 0f;

    public void Prepare(Transform[] thumb, bool left, System.Func<Vector3, bool> clear)
    {
        int side = left ? 0 : 1;
        if (prepared[side] || thumb == null || thumb.Length < 3) return;
        // A body built without CopyAhead (or a new skin's) pays for it here, once.
        if (!parts.Exists(item => item.Thumbs.ContainsKey(thumb[0]))) CopyAhead(thumb);
        foreach (var part in parts)
            if (part.Renderer != null && part.Thumbs.TryGetValue(thumb[0], out var data) && data.Affected.Length > 0)
                pending.Add(Hold(part, data, side, clear));
        prepared[side] = true;
    }

    // The grip frame's share: the held pose, baked, and the inverse skinning of the thumb's vertices in it.
    private static Work Hold(Part part, Thumb data, int side, System.Func<Vector3, bool> clear)
    {
        SkinnedMeshRenderer renderer = part.Renderer;
        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        renderer.BakeMesh(baked, true);
        Transform[] bones = renderer.bones;
        Matrix4x4[] bind = part.Original.bindposes;
        var matrices = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            matrices[i] = renderer.transform.worldToLocalMatrix * bones[i].localToWorldMatrix * bind[i];
        var inverses = new Matrix4x4[data.Affected.Length];
        for (int k = 0; k < data.Affected.Length; k++)
        {
            BoneWeight w = data.AffectedWeights[k];
            Matrix4x4 blended = Matrix4x4.zero;
            Add(ref blended, matrices, w.boneIndex0, w.weight0); Add(ref blended, matrices, w.boneIndex1, w.weight1);
            Add(ref blended, matrices, w.boneIndex2, w.weight2); Add(ref blended, matrices, w.boneIndex3, w.weight3);
            inverses[k] = blended.inverse;
        }
        return new Work
        {
            Part = part, Data = data, Side = side, Baked = baked, Inverses = inverses, Clear = clear, Frame = Time.frameCount,
            ToWorld = renderer.transform.localToWorldMatrix,
            MaxMove = .0025f / Mathf.Max(.001f, renderer.transform.lossyScale.magnitude / Mathf.Sqrt(3f))
        };
    }

    // One step of one correction per frame: relax, then add the shape.
    private void LateUpdate()
    {
        if (pending.Count == 0) return;
        Work work = pending[0];
        if (work.Part.Renderer == null) { if (work.Baked != null) Dispose(work.Baked); pending.RemoveAt(0); return; }
        if (work.Frame == Time.frameCount) return;
        work.Frame = Time.frameCount;
        if (work.Delta == null) { Relax(work); return; }
        Apply(work);
        pending.RemoveAt(0);
    }

    // The thumb web's skin, relaxed in the held pose and kept clear of what the hand holds.
    private static void Relax(Work work)
    {
        Thumb data = work.Data;
        Mesh baked = work.Baked;
        Vector3[] before = baked.normals;
        Vector3[] original = baked.vertices;

        // Alternating smoothing and inflation removes the folded spikes without shrinking the thumb web away.
        int groups = data.Members.Length;
        var points = new Vector3[groups];
        for (int g = 0; g < groups; g++)
        {
            int[] members = data.Members[g];
            foreach (int i in members) points[g] += original[i];
            points[g] /= members.Length;
        }
        Vector3[] current = (Vector3[])points.Clone(), next = new Vector3[groups];
        for (int pass = 0; pass < 8; pass++)
        {
            float amount = pass % 2 == 0 ? .45f : -.47f;
            for (int g = 0; g < groups; g++)
            {
                next[g] = current[g];
                int[] neighbours = data.Neighbours[g];
                if (neighbours == null || neighbours.Length == 0) continue;
                Vector3 centre = Vector3.zero;
                foreach (int other in neighbours) centre += current[other];
                next[g] += (centre / neighbours.Length - current[g]) * (amount * data.Strength[g]);
            }
            Vector3[] swap = current; current = next; next = swap;
        }

        var relaxed = (Vector3[])original.Clone();
        Matrix4x4 toWorld = work.ToWorld;
        for (int k = 0; k < data.Web.Length; k++)
        {
            int i = data.Web[k], g = data.WebGroup[k];
            Vector3 target = original[i] + Vector3.ClampMagnitude(current[g] - points[g], work.MaxMove);
            if (work.Clear(toWorld.MultiplyPoint3x4(target))) { relaxed[i] = target; continue; }
            float safe = 0f, blocked = 1f;
            for (int attempt = 0; attempt < 7; attempt++)
            {
                float at = (safe + blocked) * .5f;
                if (work.Clear(toWorld.MultiplyPoint3x4(Vector3.Lerp(original[i], target, at)))) safe = at; else blocked = at;
            }
            relaxed[i] = Vector3.Lerp(original[i], target, safe);
        }
        baked.vertices = relaxed;
        baked.RecalculateNormals();
        Vector3[] after = baked.normals;
        Dispose(baked);
        work.Baked = null;

        work.Delta = new Vector3[original.Length];
        work.Displacements = new Vector3[original.Length];
        for (int k = 0; k < data.Affected.Length; k++)
        {
            int i = data.Affected[k];
            Matrix4x4 inverse = work.Inverses[k];
            work.Delta[i] = inverse.MultiplyVector(after[i]).normalized - inverse.MultiplyVector(before[i]).normalized;
            work.Displacements[i] = inverse.MultiplyVector(relaxed[i] - original[i]);
        }
    }

    // The relaxed skin as a blend shape on the worn copy; HandGrip sets its weight while the hand holds a ledge.
    private static void Apply(Work work)
    {
        Part part = work.Part;
        SkinnedMeshRenderer renderer = part.Renderer;
        if (renderer.sharedMesh != part.Copy) Wear(renderer, part.Copy);
        int count = part.Copy.blendShapeCount;
        var previous = new float[count];
        for (int i = 0; i < count; i++) previous[i] = renderer.GetBlendShapeWeight(i);
        part.Copy.AddBlendShapeFrame(work.Side == 0 ? "Ledge thumb web L" : "Ledge thumb web R", 100f,
            work.Displacements, work.Delta, null);
        for (int i = 0; i < count; i++) renderer.SetBlendShapeWeight(i, previous[i]);
        part.Shapes[work.Side] = count;
    }

    private static void Add(ref Matrix4x4 blended, Matrix4x4[] matrices, int index, float amount)
    {
        if (amount <= 0f) return;
        Matrix4x4 bone = matrices[index];
        for (int cell = 0; cell < 16; cell++) blended[cell] += bone[cell] * amount;
    }

    public void SetWeight(bool left, float weight)
    {
        int side = left ? 0 : 1;
        foreach (var part in parts)
            if (part.Renderer != null && part.Shapes[side] >= 0) part.Renderer.SetBlendShapeWeight(part.Shapes[side], weight * 100f);
    }

    private void OnDestroy()
    {
        foreach (var work in pending) if (work.Baked != null) Dispose(work.Baked);
        pending.Clear();
        foreach (var part in parts)
        {
            if (part.Renderer != null && part.Renderer.sharedMesh == part.Copy) part.Renderer.sharedMesh = part.Original;
            Dispose(part.Copy);
        }
        parts.Clear();
    }

    private static void Dispose(Object value)
    {
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
