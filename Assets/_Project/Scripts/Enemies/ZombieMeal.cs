using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// The body a calm zombie eats (the 2026-11-11 stealth memo, item 47: zombies lying over a corpse, tearing at it, with
/// their eyes on their food). One of the pack's characters, posed from the last frame of ZombieLooks.mealPose (lying on its
/// back) and baked into still meshes, so it costs a plain mesh to draw: no bones and no animator. Its only collider is a
/// trigger round its head, which bullets alone meet (ZombieMealBody): sight, feet and everything else pass the body. Shot
/// in the head it bursts as a zombie's does (2026-11-15: "확인 사살"); for that the bake also keeps the body with its head
/// taken off the way gore takes one off a skin (GoreBody.Hide), and measures the head as the vertices that cut takes away.
/// Every PC lays its own copy alike (ZombieBody picks it from the eater's network id and lays it by the eater's feet as it
/// takes its pose), under the eater's id, by which a burst reaches every PC. The newest MostMeals stay; older ones go.
/// Shapes are baked once per character and shared.
/// </summary>
public static class ZombieMeal
{
    private const int MostMeals = 24;
    private const uint CharacterSalt = 0xA5u;
    // A vertex the head's cut moves by more than this (m) is the head's: below it is the difference between two bakes of
    // one pose, not the cut.
    private const float CutMoves = .001f;
    // Its head's trigger sits on the zombies' layer, as their own hitboxes do, so every cast treats it as one of them.
    private const string HitboxLayer = "Enemy";

    private sealed class Part
    {
        public Mesh Mesh, Headless;
        public Material[] Materials;
        public Vector3 Position, Scale;
        public Quaternion Rotation;
    }

    // One character's bake, in the meal's own space (its chest on the floor at the origin): its pieces, and its head as
    // the cut takes it away, the box round those vertices, the head bone and the bone it hangs from.
    private sealed class Baked
    {
        public readonly List<Part> Parts = new List<Part>();
        public bool HasHead;
        public Bounds Head;
        public Vector3 HeadBone, Neck;
    }

    private static readonly Dictionary<GameObject, Baked> baked = new Dictionary<GameObject, Baked>();
    private static readonly Queue<GameObject> laid = new Queue<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear()
    {
        baked.Clear();
        laid.Clear();
    }

    /// <summary>
    /// Lays a body with its chest at <paramref name="chest"/> (its height is the floor's), turned to <paramref name="yaw"/>
    /// degrees about up. Seed picks which character, as ZombieLooks picks clips; id is the eater's network id (0 alone),
    /// under which every PC knows this body.
    /// </summary>
    public static void Lay(ZombieLooks looks, uint seed, Vector3 chest, float yaw, ulong id = 0UL)
    {
        if (looks == null || looks.mealPose == null || looks.characters == null || looks.characters.Length == 0) return;
        var character = looks.characters[(int)(Mix(seed) % (uint)looks.characters.Length)];
        if (character == null) return;
        if (!baked.TryGetValue(character, out var bake)) baked[character] = bake = Bake(character, looks.mealPose);
        if (bake == null || bake.Parts.Count == 0) return;
        var body = new GameObject("Zombie Meal (" + character.name + ")");
        RuntimeSceneHierarchy.Group(body, RuntimeSceneHierarchy.World, "Bodies", null);
        body.transform.SetPositionAndRotation(chest, Quaternion.Euler(0f, yaw, 0f));
        var pieces = new MeshFilter[bake.Parts.Count];
        var headless = new Mesh[bake.Parts.Count];
        for (int i = 0; i < bake.Parts.Count; i++)
        {
            var part = bake.Parts[i];
            var piece = new GameObject(part.Mesh.name);
            piece.transform.SetParent(body.transform, false);
            piece.transform.localPosition = part.Position;
            piece.transform.localRotation = part.Rotation;
            // BakeMesh(mesh, true) leaves the renderer's own scale out of the vertices: it goes back on here.
            piece.transform.localScale = part.Scale;
            pieces[i] = piece.AddComponent<MeshFilter>();
            pieces[i].sharedMesh = part.Mesh;
            piece.AddComponent<MeshRenderer>().sharedMaterials = part.Materials;
            headless[i] = part.Headless;
        }
        if (bake.HasHead) Shootable(body, bake, pieces, headless, id);
        laid.Enqueue(body);
        while (laid.Count > MostMeals)
        {
            var oldest = laid.Dequeue();
            if (oldest != null) Object.Destroy(oldest);
        }
    }

    // The head's trigger round the vertices the cut takes away, and the neck's two ends the stump hangs on once it is gone.
    private static void Shootable(GameObject body, Baked bake, MeshFilter[] pieces, Mesh[] headless, ulong id)
    {
        var neck = new GameObject("Neck").transform;
        neck.SetParent(body.transform, false);
        neck.localPosition = bake.Neck;
        var stump = new GameObject("Head").transform;
        stump.SetParent(neck, false);
        stump.localPosition = bake.HeadBone - bake.Neck;
        var hitbox = new GameObject("Head Hitbox");
        int layer = LayerMask.NameToLayer(HitboxLayer);
        if (layer >= 0) hitbox.layer = layer;
        hitbox.transform.SetParent(body.transform, false);
        var box = hitbox.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = bake.Head.center;
        box.size = bake.Head.size;
        body.AddComponent<ZombieMealBody>().Set(pieces, headless, box, stump, bake.Head.center, id);
    }

    // Poses a throwaway copy of the character on the clip's last frame, bakes its visible skins and measures them from
    // its chest on the floor; then takes its head off as gore would and bakes them again; then throws the copy away.
    private static Baked Bake(GameObject character, AnimationClip pose)
    {
        var bake = new Baked();
        var copy = Object.Instantiate(character, new Vector3(0f, -500f, 0f), Quaternion.identity);
        try
        {
            // The pack's prefabs carry every character with all but one switched off, as ZombieBody finds them.
            foreach (var skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!skin.gameObject.activeSelf) Object.DestroyImmediate(skin.gameObject);
            var animator = copy.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) return bake;
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
            var chestBone = animator.GetBoneTransform(HumanBodyBones.Chest).OrNull() ?? animator.GetBoneTransform(HumanBodyBones.Spine).OrNull()
                ?? animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 floor = copy.transform.position;
            Vector3 chest = chestBone != null ? new Vector3(chestBone.position.x, floor.y, chestBone.position.z) : floor;
            var skins = copy.GetComponentsInChildren<SkinnedMeshRenderer>();
            var made = new List<SkinnedMeshRenderer>();
            foreach (var skin in skins)
            {
                if (skin.sharedMesh == null) continue;
                bake.Parts.Add(new Part
                {
                    Mesh = BakeSkin(skin, " (Meal)"),
                    Materials = skin.sharedMaterials,
                    Position = skin.transform.position - chest,
                    Rotation = skin.transform.rotation,
                    Scale = skin.transform.lossyScale
                });
                made.Add(skin);
            }
            BakeHeadless(copy, chest, made, bake);
        }
        finally { Object.Destroy(copy); }
        return bake;
    }

    // The same pose with the head taken off the skins exactly as a burst takes it off a zombie (GoreBody.Hide), so the
    // stump looks the same; the vertices that moved are the head, and the box round them is what a shot must meet.
    private static void BakeHeadless(GameObject copy, Vector3 chest, List<SkinnedMeshRenderer> skins, Baked bake)
    {
        var gore = GoreBody.Follow(null, copy);
        var head = gore != null ? gore.Bone(HumanBodyBones.Head) : null;
        if (head == null || head.parent == null) return;
        bake.HeadBone = head.position - chest;
        bake.Neck = head.parent.position - chest;
        if (gore.Hide(BodyPart.Head) == null) return;
        bool found = false;
        var whole = new List<Vector3>();
        var cut = new List<Vector3>();
        for (int i = 0; i < skins.Count; i++)
        {
            var part = bake.Parts[i];
            part.Headless = BakeSkin(skins[i], " (Meal, headless)");
            part.Mesh.GetVertices(whole);
            part.Headless.GetVertices(cut);
            for (int v = 0; v < whole.Count && v < cut.Count; v++)
            {
                Vector3 at = Place(part, whole[v]);
                if ((at - Place(part, cut[v])).sqrMagnitude <= CutMoves * CutMoves) continue;
                if (!found) { bake.Head = new Bounds(at, Vector3.zero); found = true; }
                else bake.Head.Encapsulate(at);
            }
        }
        // A skin the cut left alone keeps its whole mesh after a burst.
        foreach (var part in bake.Parts) if (part.Headless == null) part.Headless = part.Mesh;
        bake.HasHead = found;
    }

    // A baked vertex in the meal's own space: where its piece puts it (Lay).
    private static Vector3 Place(Part part, Vector3 vertex) => part.Position + part.Rotation * Vector3.Scale(part.Scale, vertex);

    private static Mesh BakeSkin(SkinnedMeshRenderer skin, string tag)
    {
        var mesh = new Mesh { name = skin.sharedMesh.name + tag };
        skin.BakeMesh(mesh, true);
        mesh.RecalculateBounds();
        return mesh;
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
