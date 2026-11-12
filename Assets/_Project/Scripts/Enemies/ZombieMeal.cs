using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The body a calm zombie eats (the 2026-11-11 stealth memo, item 47: zombies lying over a corpse, tearing at it, with
/// their eyes on their food). One of the pack's characters, posed from the last frame of ZombieLooks.mealPose (lying on its
/// back) and baked into still meshes, so it costs a plain mesh to draw: no bones, no animator, no colliders (sight,
/// bullets and feet pass it). Every PC lays its own copy alike (ZombieBody picks it from the eater's network id and lays
/// it by the eater's feet as it takes its pose). The newest MostMeals stay; older ones go. Shapes are baked once per
/// character and shared.
/// </summary>
public static class ZombieMeal
{
    private const int MostMeals = 24;
    private const uint CharacterSalt = 0xA5u;

    private sealed class Part
    {
        public Mesh Mesh;
        public Material[] Materials;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private static readonly Dictionary<GameObject, List<Part>> baked = new Dictionary<GameObject, List<Part>>();
    private static readonly Queue<GameObject> laid = new Queue<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear()
    {
        baked.Clear();
        laid.Clear();
    }

    /// <summary>
    /// Lays a body with its chest at <paramref name="chest"/> (its height is the floor's), turned to <paramref name="yaw"/>
    /// degrees about up. Seed picks which character, as ZombieLooks picks clips.
    /// </summary>
    public static void Lay(ZombieLooks looks, uint seed, Vector3 chest, float yaw)
    {
        if (looks == null || looks.mealPose == null || looks.characters == null || looks.characters.Length == 0) return;
        var character = looks.characters[(int)(Mix(seed) % (uint)looks.characters.Length)];
        if (character == null) return;
        if (!baked.TryGetValue(character, out var parts)) baked[character] = parts = Bake(character, looks.mealPose);
        if (parts == null || parts.Count == 0) return;
        var body = new GameObject("Zombie Meal (" + character.name + ")");
        RuntimeSceneHierarchy.Group(body, RuntimeSceneHierarchy.World, "Bodies", null);
        body.transform.SetPositionAndRotation(chest, Quaternion.Euler(0f, yaw, 0f));
        foreach (var part in parts)
        {
            var piece = new GameObject(part.Mesh.name);
            piece.transform.SetParent(body.transform, false);
            piece.transform.localPosition = part.Position;
            piece.transform.localRotation = part.Rotation;
            piece.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
            var renderer = piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = part.Materials;
        }
        laid.Enqueue(body);
        while (laid.Count > MostMeals)
        {
            var oldest = laid.Dequeue();
            if (oldest != null) Object.Destroy(oldest);
        }
    }

    // Poses a throwaway copy of the character on the clip's last frame, bakes its visible skins and measures them from
    // its chest on the floor, then throws the copy away.
    private static List<Part> Bake(GameObject character, AnimationClip pose)
    {
        var parts = new List<Part>();
        var copy = Object.Instantiate(character, new Vector3(0f, -500f, 0f), Quaternion.identity);
        try
        {
            // The pack's prefabs carry every character with all but one switched off, as ZombieBody finds them.
            foreach (var skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!skin.gameObject.activeSelf) Object.DestroyImmediate(skin.gameObject);
            var animator = copy.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return parts;
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var graph = PlayableGraph.Create("Zombie Meal Pose");
            try
            {
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                var clip = AnimationClipPlayable.Create(graph, pose);
                clip.SetApplyFootIK(false);
                clip.SetTime(pose.length);
                clip.SetSpeed(0d);
                output.SetSourcePlayable(clip);
                graph.Evaluate(0f);
            }
            finally { graph.Destroy(); }
            var chestBone = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine)
                ?? animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 floor = copy.transform.position;
            Vector3 chest = chestBone != null ? new Vector3(chestBone.position.x, floor.y, chestBone.position.z) : floor;
            foreach (var skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin.sharedMesh == null) continue;
                var mesh = new Mesh { name = skin.sharedMesh.name + " (Meal)" };
                skin.BakeMesh(mesh, true);
                mesh.RecalculateBounds();
                parts.Add(new Part
                {
                    Mesh = mesh,
                    Materials = skin.sharedMaterials,
                    Position = skin.transform.position - chest,
                    Rotation = skin.transform.rotation
                });
            }
        }
        finally { Object.Destroy(copy); }
        return parts;
    }

    // The seed's character, apart from the eater's own look (another salt than ZombieBody's pick).
    private static uint Mix(uint seed)
    {
        unchecked
        {
            uint hash = seed ^ (CharacterSalt * 0x9E3779B9u);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash;
        }
    }
}
