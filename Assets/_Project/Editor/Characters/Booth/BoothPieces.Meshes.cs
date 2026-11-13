using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// BoothPieces: Meshes. One part of the class; the rest is in BoothPieces.cs and the files beside it.
internal static partial class BoothPieces
{
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

    private static Bounds BoxOf(Vector3[] points)
    {
        if (points.Length == 0) return new Bounds();
        var box = new Bounds(points[0], Vector3.zero);
        foreach (var point in points) box.Encapsulate(point);
        return box;
    }

    // The character an outfit is made for: which of its bones is which part of the body, where its head, eyes, chest and
    // ankles are (in its own space, where the outfit's pieces stand as made), and its meshes, to know a copy of them.
    private sealed class Body
    {
        public Vector3 Head, Forward;
        public float Eyes, Chest, Ankle;
        private readonly Dictionary<string, string> human = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> own = new Dictionary<string, int>(StringComparer.Ordinal);

        public static Body Of(GameObject character)
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
                if (!transforms.ContainsKey(bone.name)) transforms[bone.name] = bone;
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
                if (MeshTools.MeshOf(renderer) is Mesh mesh && !body.own.ContainsKey(renderer.name)) body.own[renderer.name] = mesh.vertexCount;
            return body;
        }

        /// <summary>A mesh of the character's own carried in the outfit's file (the reference body): same name, same size.</summary>
        public bool Copies(Renderer renderer, Mesh mesh) => own.TryGetValue(renderer.name, out int count) && count == mesh.vertexCount;

        /// <summary>The share of the piece's skin weight on each part of the body (head, neck, chest, thigh …).</summary>
        public Dictionary<string, float> Regions(Renderer renderer, Mesh mesh)
        {
            var shares = new Dictionary<string, float>(StringComparer.Ordinal);
            if (renderer is SkinnedMeshRenderer skinned && mesh.isReadable && skinned.bones.Length > 0)
            {
                var bones = skinned.bones;
                var regions = new string[bones.Length];
                for (int i = 0; i < bones.Length; i++) regions[i] = RegionOf(bones[i]) ?? "other";
                float total = 0f;
                foreach (var weight in mesh.GetAllBoneWeights())
                {
                    if (weight.boneIndex < 0 || weight.boneIndex >= regions.Length || weight.weight <= 0f) continue;
                    shares.TryGetValue(regions[weight.boneIndex], out float sum);
                    shares[regions[weight.boneIndex]] = sum + weight.weight;
                    total += weight.weight;
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
}
