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
    }

    private readonly List<Part> parts = new List<Part>();
    private readonly bool[] prepared = new bool[2];

    public static HandSkinCorrection For(Animator animator)
    {
        var result = animator.GetComponent<HandSkinCorrection>();
        return result != null ? result : animator.gameObject.AddComponent<HandSkinCorrection>();
    }

    /// <summary>
    /// Copies, while the body is being made, each mesh this thumb's web skin lies on. Prepare used to copy them the first
    /// time a hand closed on a ledge, and copying a whole body mesh mid-mantle stalled that frame (Instantiate, about 40 ms
    /// in the hitch log): the first mantle's hitch.
    /// </summary>
    public void CopyAhead(Transform[] thumb)
    {
        foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh source = renderer.sharedMesh;
            if (source == null || parts.Exists(item => item.Renderer == renderer) || !Carries(renderer.bones, thumb)) continue;
            parts.Add(MakePart(renderer, source));
        }
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
        return part;
    }

    public void Prepare(Transform[] thumb, bool left, System.Func<Vector3, bool> clear)
    {
        int side = left ? 0 : 1;
        if (prepared[side]) return;
        foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh source = renderer.sharedMesh;
            if (source == null) continue;
            BoneWeight[] weights = source.boneWeights;
            Transform[] bones = renderer.bones;
            var affected = new bool[weights.Length];
            var web = new float[weights.Length];
            bool any = false;
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight w = weights[i];
                float amount = OnThumb(w.boneIndex0, w.weight0) + OnThumb(w.boneIndex1, w.weight1)
                    + OnThumb(w.boneIndex2, w.weight2) + OnThumb(w.boneIndex3, w.weight3);
                affected[i] = amount > .05f;
                web[i] = Mathf.Clamp01((OnRoot(w.boneIndex0, w.weight0) + OnRoot(w.boneIndex1, w.weight1)
                    + OnRoot(w.boneIndex2, w.weight2) + OnRoot(w.boneIndex3, w.weight3) - .05f) * 2f);
                any |= affected[i];
            }
            if (!any) continue;

            var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            renderer.BakeMesh(baked, true);
            Vector3[] before = baked.normals;
            Vector3[] original = baked.vertices;
            var delta = new Vector3[weights.Length];
            var displacements = new Vector3[weights.Length];
            Matrix4x4[] bind = source.bindposes;
            var matrices = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                matrices[i] = renderer.transform.worldToLocalMatrix * bones[i].localToWorldMatrix * bind[i];
            var inverses = new Matrix4x4[weights.Length];
            var native = new Vector3[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight w = weights[i];
                Matrix4x4 blended = Matrix4x4.zero;
                Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1);
                Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
                inverses[i] = blended.inverse;
                native[i] = inverses[i].MultiplyPoint3x4(original[i]);

                void Add(int index, float amount)
                {
                    if (amount <= 0f) return;
                    for (int cell = 0; cell < 16; cell++) blended[cell] += matrices[index][cell] * amount;
                }
            }
            Vector3[] relaxed = RelaxWeb(original, native, web, baked.triangles);
            float maxMove = .0025f / Mathf.Max(.001f, renderer.transform.lossyScale.magnitude / Mathf.Sqrt(3f));
            for (int i = 0; i < relaxed.Length; i++)
            {
                if (web[i] <= 0f) continue;
                relaxed[i] = original[i] + Vector3.ClampMagnitude(relaxed[i] - original[i], maxMove);
                if (clear(renderer.transform.TransformPoint(relaxed[i]))) continue;
                Vector3 target = relaxed[i];
                float safe = 0f, blocked = 1f;
                for (int attempt = 0; attempt < 7; attempt++)
                {
                    float at = (safe + blocked) * .5f;
                    if (clear(renderer.transform.TransformPoint(Vector3.Lerp(original[i], target, at)))) safe = at; else blocked = at;
                }
                relaxed[i] = Vector3.Lerp(original[i], target, safe);
            }
            baked.vertices = relaxed;
            baked.RecalculateNormals();
            Vector3[] after = baked.normals;
            for (int i = 0; i < weights.Length; i++)
            {
                if (!affected[i]) continue;
                delta[i] = inverses[i].MultiplyVector(after[i]).normalized - inverses[i].MultiplyVector(before[i]).normalized;
                displacements[i] = inverses[i].MultiplyVector(relaxed[i] - original[i]);
            }
            Dispose(baked);

            Part part = parts.Find(item => item.Renderer == renderer);
            if (part == null) parts.Add(part = MakePart(renderer, source));
            int count = renderer.sharedMesh.blendShapeCount;
            var previous = new float[count];
            for (int i = 0; i < count; i++) previous[i] = renderer.GetBlendShapeWeight(i);
            part.Shapes[side] = part.Copy.blendShapeCount;
            part.Copy.AddBlendShapeFrame(left ? "Ledge thumb web L" : "Ledge thumb web R", 100f, displacements, delta, null);
            renderer.sharedMesh = part.Copy;
            for (int i = 0; i < count; i++) renderer.SetBlendShapeWeight(i, previous[i]);

            float OnThumb(int index, float amount)
            {
                if (amount <= 0f) return 0f;
                return bones[index] == thumb[0] || bones[index] == thumb[1] || bones[index] == thumb[2] ? amount : 0f;
            }
            float OnRoot(int index, float amount) => amount > 0f && bones[index] == thumb[0] ? amount : 0f;
        }
        prepared[side] = true;
    }

    private static Vector3[] RelaxWeb(Vector3[] posed, Vector3[] native, float[] mask, int[] triangles)
    {
        // Weld UV/normal seams in the undeformed skin before relaxing, so the correction cannot open a seam.
        var welds = new Dictionary<Vector3Int, int>();
        var groups = new int[native.Length];
        var points = new List<Vector3>();
        var counts = new List<int>();
        var strength = new List<float>();
        for (int i = 0; i < native.Length; i++)
        {
            Vector3Int key = Vector3Int.RoundToInt(native[i] * 100000f);
            if (!welds.TryGetValue(key, out int group))
            {
                group = points.Count; welds.Add(key, group);
                points.Add(Vector3.zero); counts.Add(0); strength.Add(1f);
            }
            groups[i] = group; points[group] += posed[i]; counts[group]++;
            strength[group] = Mathf.Min(strength[group], mask[i]);
        }
        var neighbours = new List<int>[points.Count];
        for (int i = 0; i < points.Count; i++) points[i] /= counts[i];
        for (int i = 0; i < triangles.Length; i += 3)
            for (int edge = 0; edge < 3; edge++)
            {
                int a = groups[triangles[i + edge]], b = groups[triangles[i + (edge + 1) % 3]];
                Link(a, b); Link(b, a);
            }
        Vector3[] current = points.ToArray(), next = new Vector3[points.Count];
        // Alternating smoothing and inflation removes the folded spikes without shrinking the thumb web away.
        for (int pass = 0; pass < 8; pass++)
        {
            float amount = pass % 2 == 0 ? .45f : -.47f;
            for (int i = 0; i < current.Length; i++)
            {
                next[i] = current[i];
                if (neighbours[i] == null || neighbours[i].Count == 0) continue;
                Vector3 centre = Vector3.zero;
                foreach (int other in neighbours[i]) centre += current[other];
                next[i] += (centre / neighbours[i].Count - current[i]) * (amount * strength[i]);
            }
            Vector3[] swap = current; current = next; next = swap;
        }
        var result = (Vector3[])posed.Clone();
        for (int i = 0; i < result.Length; i++) if (mask[i] > 0f) result[i] += current[groups[i]] - points[groups[i]];
        return result;

        void Link(int a, int b)
        {
            if (a == b || strength[a] <= 0f) return;
            if (neighbours[a] == null) neighbours[a] = new List<int>();
            if (!neighbours[a].Contains(b)) neighbours[a].Add(b);
        }
    }

    public void SetWeight(bool left, float weight)
    {
        int side = left ? 0 : 1;
        foreach (var part in parts)
            if (part.Renderer != null && part.Shapes[side] >= 0) part.Renderer.SetBlendShapeWeight(part.Shapes[side], weight * 100f);
    }

    private void OnDestroy()
    {
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
