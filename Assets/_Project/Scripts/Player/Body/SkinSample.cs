using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every vertex of a body's skinned meshes where it is now, and the bones that carry it: read once as the body is made,
/// to measure what its cloth meets (BodySurface, SkirtPress). A mesh whose import doesn't keep Read/Write can't be read
/// in a build, so it is left out with a warning.
/// </summary>
internal sealed class SkinSample
{
    internal readonly struct Point
    {
        public readonly Vector3 Position;
        public readonly BoneWeight Weight;
        public readonly int Renderer;

        public Point(Vector3 position, BoneWeight weight, int renderer)
        {
            Position = position;
            Weight = weight;
            Renderer = renderer;
        }
    }

    private static readonly HashSet<string> unreadable = new HashSet<string>();
    private readonly List<Transform[]> bones = new List<Transform[]>();

    internal readonly List<Point> Points = new List<Point>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => unreadable.Clear();

    internal static SkinSample Read(Animator animator)
    {
        var sample = new SkinSample();
        var baked = new Mesh { name = "Cloth body measurement", hideFlags = HideFlags.HideAndDontSave };
        foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null) continue;
            if (!mesh.isReadable)
            {
                if (unreadable.Add(mesh.name))
                    Debug.LogWarning($"[Cloth] {mesh.name} 메시를 읽을 수 없어 팔에 눌린 치마가 실제 몸 앞에서 멈추지 못합니다. 모델 가져오기 설정에서 Read/Write를 켜 주세요.", renderer);
                continue;
            }
            // Scale compensated here; TransformPoint applies the body's fitted scale once below.
            renderer.BakeMesh(baked, true);
            var vertices = baked.vertices;
            var weights = mesh.boneWeights;
            if (weights.Length == vertices.Length && renderer.bones.Length > 0)
            {
                int index = sample.bones.Count;
                sample.bones.Add(renderer.bones);
                for (int i = 0; i < vertices.Length; i++)
                    sample.Points.Add(new Point(renderer.transform.TransformPoint(vertices[i]), weights[i], index));
            }
            baked.Clear();
        }
        Object.Destroy(baked);
        return sample;
    }

    /// <summary>The bone that carries most of a vertex.</summary>
    internal Transform Main(in Point point)
    {
        BoneWeight w = point.Weight;
        int most = w.boneIndex0;
        float weight = w.weight0;
        if (w.weight1 > weight) { most = w.boneIndex1; weight = w.weight1; }
        if (w.weight2 > weight) { most = w.boneIndex2; weight = w.weight2; }
        if (w.weight3 > weight) most = w.boneIndex3;
        return Bone(point.Renderer, most);
    }

    /// <summary>Whether any of these bones carries part of a vertex.</summary>
    internal bool CarriedBy(in Point point, HashSet<Transform> set)
    {
        BoneWeight w = point.Weight;
        return Carries(point.Renderer, w.boneIndex0, w.weight0, set) || Carries(point.Renderer, w.boneIndex1, w.weight1, set)
            || Carries(point.Renderer, w.boneIndex2, w.weight2, set) || Carries(point.Renderer, w.boneIndex3, w.weight3, set);
    }

    private bool Carries(int renderer, int bone, float weight, HashSet<Transform> set)
    {
        var transform = weight > 0f ? Bone(renderer, bone) : null;
        return transform != null && set.Contains(transform);
    }

    private Transform Bone(int renderer, int index)
    {
        var list = bones[renderer];
        return index >= 0 && index < list.Length ? list[index] : null;
    }
}
