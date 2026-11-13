using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

// GunClipRetarget: The stock in the shoulder. One part of the class; the rest is in GunClipRetarget.cs and the files beside it.
internal static partial class GunClipRetarget
{
    // A gun's back end (the butt of a stock), the way it points, both in its own space in true metres, and the frame it
    // hangs in from KINEMATION's weapon bone, as FPSBody hangs it (FPSBody.GunFrameOf).
    private struct Stock
    {
        public Vector3 butt, forward;
        public Quaternion frame;
    }

    private static bool MeasureStock(GameObject prefab, out Stock stock)
    {
        stock = default;
        if (prefab == null) return false;
        var instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            var kinemation = instance.GetComponent<CasFPSWeapon>();
            stock.frame = FPSBody.GunFrameOf(kinemation);
            GunMuzzle.Back(instance.transform, kinemation != null ? kinemation.aimPoint : null, StockBackDepth, out stock.butt, out stock.forward);
            return stock.forward.sqrMagnitude > .5f;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // How far the gun has to move (in the body's model space) for its butt to rest in the shoulder pocket. False when the
    // gun has no stock: its back end reaches less than StockReach behind the trigger hand's wrist (reach, m in game).
    private static bool ToShoulder(Transform body, Transform weapon, in Stock stock, float fit, Vector3 facing, out Vector3 move,
        out float reach, out Vector3 pocket)
    {
        move = pocket = Vector3.zero;
        reach = 0f;
        var hand = PreviewHumanoid.Bone(body, HumanBodyBones.RightHand);
        if (hand == null) return false;
        // The bake is at 1/fit of the size the body plays at; the gun is its true size in game.
        Quaternion gun = weapon.rotation * stock.frame;
        Vector3 butt = weapon.position + gun * stock.butt / Mathf.Max(.01f, fit);
        Vector3 pointing = gun * stock.forward;
        reach = Vector3.Dot(hand.position - butt, pointing) * fit;
        if (reach < StockReach || !Pocket(body, facing, pointing, out pocket)) return false;
        move = body.InverseTransformPoint(pocket) - body.InverseTransformPoint(butt);
        return true;
    }

    // The right shoulder pocket as the body stands in the pose: measured from its shoulder joint and neck (across and
    // down as the body faces), then on the front of the body or its clothes where the gun, sliding back along itself,
    // would meet it.
    private static bool Pocket(Transform body, Vector3 facing, Vector3 pointing, out Vector3 pocket)
    {
        pocket = default;
        var shoulder = PreviewHumanoid.Bone(body, HumanBodyBones.RightUpperArm);
        var neck = PreviewHumanoid.Bone(body, HumanBodyBones.Neck);
        if (neck == null) neck = PreviewHumanoid.Bone(body, HumanBodyBones.UpperChest);
        if (neck == null) neck = PreviewHumanoid.Bone(body, HumanBodyBones.Chest);
        if (shoulder == null || neck == null) return false;
        Vector3 up = body.up, side = Vector3.Cross(up, facing).normalized, along = pointing.normalized;
        Vector3 joint = shoulder.position;
        float span = Vector3.Distance(joint, neck.position);
        Vector3 point = joint - side * (Vector3.Dot(joint - neck.position, side) * PocketInward) - up * (span * PocketDrop);
        pocket = point + along * (FrontOf(body, point, along, span * PocketRadius, out float front) ? front : span * PocketFallbackDepth);
        return true;
    }

    // How far ahead of point, along forward, the body's outermost surface (skin or clothes) is within radius of that line,
    // from its meshes as posed. The forearms, hands and head are left out: they pass in front of the chest.
    private static bool FrontOf(Transform body, Vector3 point, Vector3 forward, float radius, out float front)
    {
        front = float.NegativeInfinity;
        var skip = new List<Transform>();
        foreach (var bone in new[] { HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.Head })
            if (PreviewHumanoid.Bone(body, bone) is Transform found) skip.Add(found);
        var vertices = new List<Vector3>();
        bool any = false;
        foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = renderer.sharedMesh;
            if (!renderer.enabled || mesh == null || !mesh.isReadable) continue;
            var bones = renderer.bones;
            var weights = mesh.boneWeights;
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, true);
                baked.GetVertices(vertices);
                // Baked with the renderer's scale, in its position and rotation.
                var toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 offset = toWorld.MultiplyPoint3x4(vertices[i]) - point;
                    float ahead = Vector3.Dot(offset, forward);
                    if (ahead <= front || (offset - forward * ahead).sqrMagnitude > radius * radius) continue;
                    if (i < weights.Length && Under(bones, weights[i].boneIndex0, skip)) continue;
                    front = ahead;
                    any = true;
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }
        return any;
    }

    // The vertex's main bone (Unity keeps the heaviest first) is one of skip or under one.
    private static bool Under(Transform[] bones, int index, List<Transform> skip)
    {
        if (bones == null || index < 0 || index >= bones.Length || bones[index] == null) return false;
        foreach (var root in skip)
            if (bones[index].IsChildOf(root)) return true;
        return false;
    }

    // Each bake's hold, written to Logs/gun-bake-report.txt (no console): how far each hand is from the gun bone here and on
    // KINEMATION's own arms, and where the gun and hands sit from the eyes (x right, y up, z ahead, m at scale 1).
    private static void Report(GameObject skin, WeaponData weapon, Transform source, Transform sourceWeapon, Transform body,
        Transform bodyWeapon, in GunBakes.Entry entry, float fit, bool trueSize, Vector3 fromCamera, Vector3 facing, string shoulder)
    {
        try
        {
            var right = PreviewHumanoid.Bone(body, HumanBodyBones.RightHand);
            var left = PreviewHumanoid.Bone(body, HumanBodyBones.LeftHand);
            var sourceRight = FindLike(source, "hand_r");
            var sourceLeft = FindLike(source, "hand_l");
            string Gap(Transform hand, Transform gun) => hand != null && gun != null ? Vector3.Distance(hand.position, gun.position).ToString("0.000") : "-";
            Vector3 forward = facing, up = body.up, side = Vector3.Cross(up, forward);
            Eyes(body, facing, out Vector3 eyesLocal);
            Vector3 eyes = body.TransformPoint(eyesLocal);
            string FromEyes(Transform point)
            {
                if (point == null) return "-";
                Vector3 d = point.position - eyes;
                return $"({Vector3.Dot(d, side):0.000}, {Vector3.Dot(d, up):0.000}, {Vector3.Dot(d, forward):0.000})";
            }
            string line = $"{System.DateTime.Now:HH:mm:ss} {skin.name} {weapon.name}: right hand to gun {Gap(right, bodyWeapon)} (KINEMATION {Gap(sourceRight, sourceWeapon)}), "
                + $"left {Gap(left, bodyWeapon)} (KINEMATION {Gap(sourceLeft, sourceWeapon)}); from the eyes: gun {FromEyes(bodyWeapon)} right {FromEyes(right)} left {FromEyes(left)}; grip {entry.hasGrip}; "
                + $"fit scale {fit:0.000}, true size {trueSize} (in game every length here x fit scale); "
                + $"KINEMATION's gun from its camera {fromCamera.ToString("F3")} (wanted in game from the eyes, before the stock); {shoulder}\n";
            string path = Path.Combine(Application.dataPath, "..", "Logs", "gun-bake-report.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, line);
        }
        catch (Exception error)
        {
            Debug.LogWarning($"GunClipRetarget: the bake report could not be written: {error.Message}");
        }
    }

    // The eyes in the body's model space, as PlayerBody finds them: the eye bones when they sit like eyes, else a little
    // above and ahead of the head bone (ahead the way the body faces standing square).
    private static bool Eyes(Transform body, Vector3 facing, out Vector3 eyes)
    {
        eyes = default;
        var head = PreviewHumanoid.Bone(body, HumanBodyBones.Head);
        if (head == null) return false;
        var left = PreviewHumanoid.Bone(body, HumanBodyBones.LeftEye);
        var right = PreviewHumanoid.Bone(body, HumanBodyBones.RightEye);
        Vector3 forward = facing, up = body.up;
        if (left != null && right != null)
        {
            Vector3 middle = (left.position + right.position) * .5f, fromHead = middle - head.position;
            float apart = Vector3.Distance(left.position, right.position);
            float above = Vector3.Dot(fromHead, up), ahead = Vector3.Dot(fromHead, forward);
            if (apart > .015f && apart < .2f && above > -.05f && above < .25f && ahead > .01f && ahead < .25f)
            {
                eyes = body.InverseTransformPoint(middle);
                return true;
            }
        }
        eyes = body.InverseTransformPoint(head.position + up * .08f + forward * .1f);
        return true;
    }

    // The eyes in the body's model space and the frame they look out of: ahead the way the hips face (the shooting stance
    // turns the shoulders, not the hips), up the model's up. FPSBody.EyeFrame measures any body the same way at load.
    private static bool EyeFrame(Transform body, out Vector3 eyes, out Quaternion frame)
    {
        eyes = default;
        frame = Quaternion.identity;
        var left = PreviewHumanoid.Bone(body, HumanBodyBones.LeftUpperLeg);
        var right = PreviewHumanoid.Bone(body, HumanBodyBones.RightUpperLeg);
        if (left == null || right == null) return false;
        Vector3 ahead = Vector3.Cross(body.InverseTransformPoint(right.position) - body.InverseTransformPoint(left.position), Vector3.up);
        if (ahead.sqrMagnitude < 1e-8f) return false;
        frame = Quaternion.LookRotation(ahead.normalized, Vector3.up);
        return Eyes(body, body.TransformDirection(ahead.normalized), out eyes);
    }

    // Both skeletons must face the same way in their own model space for Retarget Pro's FPS feature to carry the gun
    // across as it is (MakeTarget turns the body for it); a mismatch left is reported, since the guns would sit turned.
    private static void CheckFacing(Transform source, Transform body, GameObject skin)
    {
        Vector3 bodyForward = body.InverseTransformDirection(Forward(body));
        Vector3 sourceForward = source.InverseTransformDirection(SourceForward(source));
        float angle = Vector3.Angle(Vector3.ProjectOnPlane(sourceForward, Vector3.up), Vector3.ProjectOnPlane(bodyForward, Vector3.up));
        if (angle > 10f)
            Debug.LogWarning($"GunClipRetarget: KINEMATION's arms and {skin.name} still face {angle:0}° apart in their own model space; the guns may sit turned.", skin);
    }

    // KINEMATION's arms skeleton faces (UE names), from its shoulders.
    private static Vector3 SourceForward(Transform source)
    {
        var left = FindLike(source, "upperarm_l");
        var right = FindLike(source, "upperarm_r");
        if (left == null || right == null) return source.forward;
        Vector3 forward = Vector3.Cross(right.position - left.position, source.up);
        return forward.sqrMagnitude > 1e-6f ? forward.normalized : source.forward;
    }

    // The way KINEMATION's arms face in their own model space, as Retarget Pro lays them out (root turned straight).
    private static float ArmsFacing(GameObject arms)
    {
        var copy = Object.Instantiate(arms);
        try
        {
            ResetRootRotation(copy.transform);
            return MovementFixtures.YawOf(copy.transform.InverseTransformDirection(SourceForward(copy.transform)));
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    // Retarget Pro's preview turns a model's root straight and keeps its children where they were.
    private static void ResetRootRotation(Transform root)
    {
        if (Quaternion.Angle(root.rotation, Quaternion.identity) <= .0001f) return;
        var children = new List<(Transform child, Vector3 position, Quaternion rotation)>();
        foreach (Transform child in root) children.Add((child, child.position, child.rotation));
        root.rotation = Quaternion.identity;
        foreach (var (child, position, rotation) in children) child.SetPositionAndRotation(position, rotation);
    }

    private static Vector3 Forward(Transform body)
    {
        var left = PreviewHumanoid.Bone(body, HumanBodyBones.LeftUpperArm);
        var right = PreviewHumanoid.Bone(body, HumanBodyBones.RightUpperArm);
        if (left == null || right == null) return body.forward;
        Vector3 forward = Vector3.Cross(right.position - left.position, body.up);
        return forward.sqrMagnitude > 1e-6f ? forward.normalized : body.forward;
    }

    // The way the hips face (from the thighs, as EyeFrame takes it); without both thighs, the shoulders' way.
    private static Vector3 HipsForward(Transform body)
    {
        var left = PreviewHumanoid.Bone(body, HumanBodyBones.LeftUpperLeg);
        var right = PreviewHumanoid.Bone(body, HumanBodyBones.RightUpperLeg);
        if (left == null || right == null) return Forward(body);
        Vector3 forward = Vector3.Cross(right.position - left.position, body.up);
        return forward.sqrMagnitude > 1e-6f ? forward.normalized : Forward(body);
    }

    private static Transform FindLike(Transform root, string name)
    {
        if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase)) return root;
        foreach (Transform child in root)
            if (FindLike(child, name) is Transform found) return found;
        return null;
    }

    private static string FileName(string name)
    {
        foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        return name.Replace(' ', '_');
    }
}
