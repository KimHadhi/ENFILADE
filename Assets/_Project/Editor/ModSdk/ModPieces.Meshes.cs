using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ModPieces: measuring the meshes. One part of the class; the rest is in ModPieces.cs and the files beside it.
public static partial class ModPieces
{
    /// <summary>
    /// Every mesh of the model, measured for sorting. Those <paramref name="named"/> names (a mesh's name to the slot a
    /// row of the game's tables already gives it) come along as hosts for trim. The rest are marked when they are
    /// switched off as the prefab comes (<see cref="Piece.Off"/>) or copy the character's own body
    /// (<see cref="Piece.Copy"/>): the caller leaves those out, or lets its user keep them.
    /// </summary>
    /// <param name="body">The character the model is (its own meshes) or is made for; null when there is none to measure against.</param>
    /// <param name="model">The maker's prefix words: the model's own name in words, skipped at the start of a mesh's name.</param>
    /// <param name="underwear">
    /// Meshes named like underwear come along too, marked <see cref="Piece.Under"/>, for the caller to give their slots
    /// (<see cref="UnderSlot"/>). Without it they are skipped: the game's import sorts them in a step of its own.
    /// </param>
    /// <param name="own">The model is the character itself: none of its meshes copies its body.</param>
    public static List<Piece> Measure(GameObject prefab, Body body, IReadOnlyDictionary<string, string> named, ICollection<string> model,
        bool underwear = false, bool own = false)
    {
        var pieces = new List<Piece>();
        var root = prefab.transform;
        var toCharacter = root.worldToLocalMatrix;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
            var mesh = MeshOf(renderer);
            if (mesh == null) continue;
            bool under = IsUnderwear(renderer.name);
            if (under && !underwear) continue;
            string slot = null;
            bool isNamed = named != null && named.TryGetValue(renderer.name, out slot);
            var piece = new Piece { Renderer = renderer, Name = renderer.name, Named = isNamed, Slot = isNamed ? slot : null, Under = under && !isNamed };
            if (!isNamed)
            {
                piece.Off = !Shown(renderer, root);
                piece.Copy = !piece.Off && !own && body != null && body.Copies(renderer, mesh);
            }
            piece.Words = PieceWords(renderer.name, model);
            var matrix = toCharacter * renderer.transform.localToWorldMatrix;
            var vertices = Vertices(mesh);
            if (vertices != null)
            {
                piece.Points = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) piece.Points[i] = matrix.MultiplyPoint3x4(vertices[i]);
                piece.Box = BoxOf(piece.Points);
            }
            else piece.Box = BoxOf(mesh.bounds, matrix);
            if (!isNamed && body != null)
            {
                piece.Regions = body.Regions(renderer, mesh);
                var middle = piece.Box.center - body.Head;
                piece.Front = middle.x * body.Forward.x + middle.z * body.Forward.z;
            }
            pieces.Add(piece);
        }
        return pieces;
    }

    /// <summary>
    /// The underwear slot a mesh goes in: its name says (Bikini top, Underwear_Under), else where it sits: above halfway
    /// from the hips to the chest it is a top, below a bottom. <paramref name="both"/> when it reaches from the hips to
    /// the chest (a swimsuit, a bodysuit): a top that takes the bottom too.
    /// </summary>
    public static string UnderSlot(Renderer renderer, Frame frame, out bool both)
    {
        both = false;
        bool top = Named(renderer.name, TopWords, TopPieces), bottom = Named(renderer.name, BottomWords, BottomPieces);
        if (top != bottom) return top ? UnderTop : UnderBottom;
        if (frame == null || !(MeshOf(renderer) is Mesh mesh)) return UnderTop;
        var box = BoxOf(mesh.bounds, frame.ToCharacter * renderer.transform.localToWorldMatrix);
        both = box.min.y < frame.Hips + .02f && box.max.y > frame.Chest - .02f;
        return both || box.center.y > (frame.Hips + frame.Chest) * .5f ? UnderTop : UnderBottom;
    }

    // Drawn as the prefab comes: enabled, and itself and every parent up to the prefab's root active.
    private static bool Shown(Renderer renderer, Transform root)
    {
        if (!renderer.enabled) return false;
        for (var at = renderer.transform; at != null; at = at.parent)
        {
            if (!at.gameObject.activeSelf) return false;
            if (at == root) break;
        }
        return true;
    }

    // The mesh a renderer draws, and a local box as the box round it in another space: the game's MeshTools has both,
    // which the package can't use.
    private static Mesh MeshOf(Renderer renderer) =>
        renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

    private static Bounds BoxOf(Bounds local, Matrix4x4 matrix)
    {
        var box = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            box.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
        return box;
    }

    private static Bounds BoxOf(Vector3[] points)
    {
        if (points.Length == 0) return new Bounds();
        var box = new Bounds(points[0], Vector3.zero);
        foreach (var point in points) box.Encapsulate(point);
        return box;
    }

    // The editor reads any mesh, whatever its file's Read/Write setting says; a mesh that still won't be read gives null.
    private static bool CanRead(Mesh mesh) => mesh != null && (mesh.isReadable || Application.isEditor);

    private static Vector3[] Vertices(Mesh mesh)
    {
        if (!CanRead(mesh)) return null;
        try
        {
            var vertices = mesh.vertices;
            return vertices != null && vertices.Length == mesh.vertexCount ? vertices : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The character an outfit is made for (or the character itself): which of its bones is which part of the body, where
    /// its head, eyes, chest and ankles are (in its own space, where the outfit's pieces stand as made), and its meshes,
    /// to know a copy of them.
    /// </summary>
    public sealed class Body
    {
        public Vector3 Head, Forward;
        public float Eyes, Chest, Ankle;
        private readonly Dictionary<string, string> human = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> own = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Null when the object has no humanoid avatar, or the avatar lacks a head, a foot or a chest.</summary>
        /// <param name="apart">
        /// An object under the character that is no part of it: an outfit dropped under its avatar in the scene, whose
        /// meshes and bones would else be read as the avatar's own (and every piece of it as a copy of the body).
        /// </param>
        public static Body Of(GameObject character, Transform apart = null)
        {
            if (character == null) return null;
            var animator = character.GetComponentInChildren<Animator>(true);
            var avatar = animator != null ? animator.avatar : null;
            if (avatar == null || !avatar.isHuman) return null;
            var body = new Body();
            var bonesOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var human in avatar.humanDescription.human)
            {
                body.human[human.boneName] = human.humanName;
                bonesOf[human.humanName] = human.boneName;
            }
            var toCharacter = character.transform.worldToLocalMatrix;
            var transforms = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var bone in character.GetComponentsInChildren<Transform>(true))
                if (!transforms.ContainsKey(bone.name) && !Under(bone, apart)) transforms[bone.name] = bone;
            Vector3? At(string humanName) => bonesOf.TryGetValue(humanName, out string bone) && transforms.TryGetValue(bone, out var at)
                ? toCharacter.MultiplyPoint3x4(at.position) : (Vector3?)null;
            var head = At("Head");
            var foot = At("LeftFoot") ?? At("RightFoot");
            var chest = At("UpperChest") ?? At("Chest");
            if (head == null || foot == null || chest == null) return null;
            body.Head = head.Value;
            body.Ankle = foot.Value.y;
            body.Chest = chest.Value.y;
            var left = At("LeftEye");
            var right = At("RightEye");
            var eyes = left != null && right != null ? (left.Value + right.Value) * .5f : (Vector3?)null;
            body.Eyes = eyes?.y ?? body.Head.y + .04f;
            var forward = eyes != null ? eyes.Value - body.Head : Vector3.forward;
            forward.y = 0f;
            body.Forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
                if (!Under(renderer.transform, apart) && MeshOf(renderer) is Mesh mesh && !body.own.ContainsKey(renderer.name))
                    body.own[renderer.name] = mesh.vertexCount;
            return body;
        }

        private static bool Under(Transform part, Transform top) => top != null && (part == top || part.IsChildOf(top));

        /// <summary>A mesh of the character's own carried in the outfit's file (the reference body): same name, same size.</summary>
        public bool Copies(Renderer renderer, Mesh mesh) => own.TryGetValue(renderer.name, out int count) && count == mesh.vertexCount;

        /// <summary>The share of the piece's skin weight on each part of the body (head, neck, chest, thigh …).</summary>
        public Dictionary<string, float> Regions(Renderer renderer, Mesh mesh)
        {
            var shares = new Dictionary<string, float>(StringComparer.Ordinal);
            if (renderer is SkinnedMeshRenderer skinned && CanRead(mesh) && skinned.bones.Length > 0)
            {
                var bones = skinned.bones;
                var regions = new string[bones.Length];
                for (int i = 0; i < bones.Length; i++) regions[i] = RegionOf(bones[i]) ?? "other";
                float total = 0f;
                try
                {
                    foreach (var weight in mesh.GetAllBoneWeights())
                    {
                        if (weight.boneIndex < 0 || weight.boneIndex >= regions.Length || weight.weight <= 0f) continue;
                        shares.TryGetValue(regions[weight.boneIndex], out float sum);
                        shares[regions[weight.boneIndex]] = sum + weight.weight;
                        total += weight.weight;
                    }
                }
                catch (Exception)
                {
                    // A mesh whose skin won't be read after all: by the bone it hangs from, below.
                    total = 0f;
                }
                if (total > 0f)
                {
                    foreach (string region in shares.Keys.ToList()) shares[region] /= total;
                    return shares;
                }
                shares.Clear();
            }
            // Unskinned or unreadable: the body part it hangs from.
            shares[RegionOf(renderer is SkinnedMeshRenderer root && root.rootBone != null ? root.rootBone : renderer.transform) ?? "other"] = 1f;
            return shares;
        }

        // The part of the body a bone is: its own humanoid name, or its nearest humanoid parent's (Breast_L is the chest).
        private string RegionOf(Transform bone)
        {
            for (var at = bone; at != null; at = at.parent)
                if (human.TryGetValue(at.name, out string humanName)) return Region(humanName);
            return null;
        }

        private static string Region(string humanName)
        {
            switch (humanName)
            {
                case "Head": case "Jaw": case "LeftEye": case "RightEye": return "head";
                case "Neck": return "neck";
                case "Chest": case "UpperChest": case "LeftShoulder": case "RightShoulder": return "chest";
                case "Spine": return "belly";
                case "Hips": return "hips";
                case "LeftUpperArm": case "RightUpperArm": return "upperarm";
                case "LeftLowerArm": case "RightLowerArm": return "forearm";
                case "LeftHand": case "RightHand": return "hand";
                case "LeftUpperLeg": case "RightUpperLeg": return "thigh";
                case "LeftLowerLeg": case "RightLowerLeg": return "shin";
                case "LeftFoot": case "RightFoot": case "LeftToes": case "RightToes": return "foot";
            }
            // Fingers ("Left Thumb Proximal") are the hand.
            return humanName.IndexOf("Thumb", StringComparison.Ordinal) >= 0 || humanName.IndexOf("Index", StringComparison.Ordinal) >= 0
                || humanName.IndexOf("Middle", StringComparison.Ordinal) >= 0 || humanName.IndexOf("Ring", StringComparison.Ordinal) >= 0
                || humanName.IndexOf("Little", StringComparison.Ordinal) >= 0 ? "hand" : "other";
        }
    }

    /// <summary>
    /// Heights of a character's hips and chest, and a prefab's space turned into the character's (both stand their roots
    /// at one origin, as dressing aligns them): where underwear sits (<see cref="UnderSlot"/>).
    /// </summary>
    public sealed class Frame
    {
        public float Hips, Chest;
        public Matrix4x4 ToCharacter;

        /// <summary>Null when the character has no humanoid avatar with hips and a chest above them.</summary>
        public static Frame Of(GameObject character, GameObject prefab)
        {
            if (character == null || prefab == null) return null;
            var animator = character.GetComponentInChildren<Animator>(true);
            var avatar = animator != null ? animator.avatar : null;
            if (avatar == null || !avatar.isHuman) return null;
            string hipsName = null, chestName = null;
            foreach (var human in avatar.humanDescription.human)
            {
                if (human.humanName == "Hips") hipsName = human.boneName;
                else if (human.humanName == "Chest") chestName = human.boneName;
                else if (human.humanName == "UpperChest" && chestName == null) chestName = human.boneName;
            }
            var bones = character.GetComponentsInChildren<Transform>(true);
            var hips = bones.FirstOrDefault(bone => bone.name == hipsName);
            var chest = bones.FirstOrDefault(bone => bone.name == chestName);
            if (hips == null || chest == null) return null;
            var toCharacter = character.transform.worldToLocalMatrix;
            float hipsY = toCharacter.MultiplyPoint3x4(hips.position).y, chestY = toCharacter.MultiplyPoint3x4(chest.position).y;
            if (chestY <= hipsY) return null;
            return new Frame { Hips = hipsY, Chest = chestY, ToCharacter = prefab.transform.worldToLocalMatrix };
        }
    }
}
