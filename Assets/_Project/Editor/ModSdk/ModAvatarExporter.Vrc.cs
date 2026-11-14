// dev-text: file (the exporter's report, read by whoever makes a mod; nothing here is shown to players)
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// ModAvatarExporter: VRChat's and Modular Avatar's settings, read as they are. One part of the class; the rest is in
// ModAvatarExporter.cs and the files beside it.
public sealed partial class ModAvatarExporter
{
    // The PhysBones on the copy, by VRChat's own names for their settings. Found by the component's name and read
    // through Unity's own view of its fields, so the package builds without VRChat's SDK in the project.
    // <paramref name="wearer"/>: for an outfit, the avatar it is made for: a collider standing on one of that avatar's
    // bones (a skirt's against the legs) is written down by the bone's path from the avatar's root.
    private static List<ModAvatarFile.PhysBone> LiveSway(GameObject clone, GameObject wearer = null)
    {
        var found = new List<ModAvatarFile.PhysBone>();
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "VRCPhysBone" || !behaviour.enabled) continue;
            var read = new SerializedObject(behaviour);
            var on = Reference(read, "rootTransform") as Transform;
            if (on == null) on = behaviour.transform;
            if (!ModVrcYaml.Under(on, clone.transform)) continue;
            var bone = new ModAvatarFile.PhysBone
            {
                root = ModVrcYaml.PathOf(on, clone.transform),
                endpoint = read.FindProperty("endpointPosition") is SerializedProperty end && end.propertyType == SerializedPropertyType.Vector3 ? end.vector3Value : Vector3.zero,
                integration = Whole(read, "integrationType"),
                limitType = Whole(read, "limitType"),
                pull = Number(read, "pull"),
                spring = Number(read, "spring"),
                stiffness = Number(read, "stiffness"),
                gravity = Number(read, "gravity"),
                gravityFalloff = Number(read, "gravityFalloff"),
                immobile = Number(read, "immobile"),
                radius = Number(read, "radius"),
                maxAngleX = Number(read, "maxAngleX"),
                maxAngleZ = Number(read, "maxAngleZ"),
                pullCurve = Curve(read, "pullCurve"),
                springCurve = Curve(read, "springCurve"),
                stiffnessCurve = Curve(read, "stiffnessCurve"),
                gravityCurve = Curve(read, "gravityCurve"),
                immobileCurve = Curve(read, "immobileCurve"),
                radiusCurve = Curve(read, "radiusCurve"),
                maxAngleXCurve = Curve(read, "maxAngleXCurve"),
                maxAngleZCurve = Curve(read, "maxAngleZCurve"),
                collides = Whole(read, "allowCollision") != 0,
            };
            var ignore = new List<string>();
            foreach (var each in References(read, "ignoreTransforms"))
                if (each is Transform ignored && ModVrcYaml.Under(ignored, clone.transform)) ignore.Add(ModVrcYaml.PathOf(ignored, clone.transform));
            var colliders = new List<string>();
            var onBody = new List<string>();
            foreach (var each in References(read, "colliders"))
            {
                if (!(each is Component collider)) continue;
                var stands = Reference(new SerializedObject(collider), "rootTransform") as Transform;
                if (stands == null) stands = collider.transform;
                if (ModVrcYaml.Under(stands, clone.transform)) colliders.Add(ModVrcYaml.PathOf(stands, clone.transform));
                // The copy's colliders outside itself still point at the avatar the outfit was copied from under.
                else if (wearer != null && ModVrcYaml.Under(stands, wearer.transform)) onBody.Add(ModVrcYaml.PathOf(stands, wearer.transform));
            }
            bone.ignore = ignore.ToArray();
            bone.colliders = colliders.ToArray();
            bone.bodyColliders = onBody.ToArray();
            found.Add(bone);
        }
        return found;
    }

    // The avatar descriptor's mouth shapes and blink, by shape key name (VRChat keeps the blink as a number into the mesh's keys).
    private static ModAvatarFile.Face Face(GameObject clone, List<string> notes)
    {
        var face = new ModAvatarFile.Face();
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "VRCAvatarDescriptor") continue;
            var read = new SerializedObject(behaviour);
            var visemes = read.FindProperty("VisemeBlendShapes");
            if (visemes != null && visemes.isArray && Whole(read, "lipSync") == 3)
            {
                var names = new List<string>();
                for (int i = 0; i < visemes.arraySize; i++) names.Add(visemes.GetArrayElementAtIndex(i).stringValue ?? "");
                face.visemes = names.ToArray();
            }
            var lids = read.FindProperty("customEyeLookSettings.eyelidsBlendshapes");
            var mesh = read.FindProperty("customEyeLookSettings.eyelidsSkinnedMesh")?.objectReferenceValue as SkinnedMeshRenderer;
            if (Whole(read, "customEyeLookSettings.eyelidType") == 2 && lids != null && lids.isArray && lids.arraySize > 0 && mesh != null && mesh.sharedMesh != null)
            {
                int blink = lids.GetArrayElementAtIndex(0).intValue;
                if (blink >= 0 && blink < mesh.sharedMesh.blendShapeCount) face.blink = mesh.sharedMesh.GetBlendShapeName(blink);
            }
            notes.Add("아바타 디스크립터에서 " + (face.visemes.Length > 0 ? "입 모양 " + face.visemes.Length + "개" : "입 모양 없음") + ", "
                + (face.blink.Length > 0 ? "깜박임 '" + face.blink + "'" : "깜박임 없음") + "을 읽었습니다(없는 것은 게임이 흔한 이름으로 찾습니다).");
            break;
        }
        return face;
    }

    // Modular Avatar's Bone Proxies in an outfit: a part that follows one bone of the body (a hat on the head) instead of
    // the outfit's skeleton. Written down as set (the bone by Unity's name for it, the path on under it, how it is put
    // there by Modular Avatar's own word): the game puts the part under that bone when the outfit goes on. A proxy with
    // no humanoid bone names its target by a path from the avatar's root (bone "LastBone"). The outfit's own root may
    // be such a part (path ""): a hat that is all there is to the outfit. One placed under a bone of its avatar by
    // hand, with no proxy, follows that bone the same way, keeping where it was put.
    private static ModAvatarFile.Hold[] Holds(GameObject clone, GameObject source, GameObject wearer, List<string> notes)
    {
        var holds = new List<ModAvatarFile.Hold>();
        bool rooted = false;
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "ModularAvatarBoneProxy") continue;
            rooted |= behaviour.transform == clone.transform;
            var read = new SerializedObject(behaviour);
            var bone = read.FindProperty("boneReference");
            if (bone == null || (bone.propertyType != SerializedPropertyType.Enum && bone.propertyType != SerializedPropertyType.Integer)) continue;
            var mode = read.FindProperty("attachmentMode");
            string how = "";
            if (mode != null && mode.propertyType == SerializedPropertyType.Enum && mode.enumValueIndex >= 0 && mode.enumValueIndex < mode.enumNames.Length)
                how = mode.enumNames[mode.enumValueIndex];
            holds.Add(new ModAvatarFile.Hold
            {
                path = ModVrcYaml.PathOf(behaviour.transform, clone.transform),
                bone = ((HumanBodyBones)bone.intValue).ToString(),
                under = read.FindProperty("subPath") is SerializedProperty sub && sub.propertyType == SerializedPropertyType.String ? sub.stringValue ?? "" : "",
                mode = how,
            });
        }
        if (holds.Count > 0) notes.Add($"Modular Avatar Bone Proxy {holds.Count}개를 읽었습니다(입힐 때 게임이 그 뼈에 붙입니다).");
        if (!rooted && Parented(source, wearer) is ModAvatarFile.Hold byHand)
        {
            holds.Add(byHand);
            notes.Add($"아바타의 뼈 '{source.transform.parent.name}' 밑에 놓여 있어, 게임에서도 그 뼈({byHand.bone})를 따라가게 했습니다.");
        }
        return holds.ToArray();
    }

    // An outfit standing under one of its avatar's bones in the scene (dragged onto the head by hand): the nearest
    // humanoid bone above it, by Unity's name for it, and the path from that bone down to where it stands.
    private static ModAvatarFile.Hold Parented(GameObject source, GameObject wearer)
    {
        if (source == null || wearer == null || source == wearer || !source.transform.IsChildOf(wearer.transform)) return null;
        var above = source.transform.parent;
        if (above == null || above == wearer.transform) return null;
        var animator = wearer.GetComponentInChildren<Animator>(true);
        var avatar = animator != null ? animator.avatar : null;
        if (avatar == null || !avatar.isHuman) return null;
        var human = new Dictionary<string, string>();
        foreach (var bone in avatar.humanDescription.human) human[bone.boneName] = bone.humanName;
        for (var at = above; at != null && at != wearer.transform; at = at.parent)
            if (human.TryGetValue(at.name, out string name))
                // Unity's names for the fingers are written with blanks ("Left Thumb Proximal"); the game reads them without.
                return new ModAvatarFile.Hold { path = "", bone = name.Replace(" ", ""), under = at == above ? "" : ModVrcYaml.PathOf(above, at), mode = "AsChildKeepWorldPose" };
        return null;
    }

    // Modular Avatar's Merge Armature lets an outfit's bones carry a prefix or a suffix beside the avatar's names
    // ("Hips.outfit"), which it takes off when it joins them. The game joins bones by their names as they are, so the
    // copy's bones get the avatar's names back here.
    private static void PlainBones(GameObject clone, List<string> notes)
    {
        int renamed = 0;
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "ModularAvatarMergeArmature") continue;
            var read = new SerializedObject(behaviour);
            string prefix = Words(read, "prefix"), suffix = Words(read, "suffix");
            if (prefix.Length == 0 && suffix.Length == 0) continue;
            foreach (var bone in behaviour.GetComponentsInChildren<Transform>(true))
            {
                string name = bone.name;
                if (bone == behaviour.transform || name.Length <= prefix.Length + suffix.Length
                    || !name.StartsWith(prefix, System.StringComparison.Ordinal) || !name.EndsWith(suffix, System.StringComparison.Ordinal)) continue;
                bone.name = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
                renamed++;
            }
        }
        if (renamed > 0) notes.Add($"Modular Avatar가 뼈 이름에 붙여 둔 접두·접미사를 뼈 {renamed}개에서 뗐습니다(게임은 아바타와 같은 이름의 뼈끼리 붙입니다).");
    }

    private static string Words(SerializedObject read, string name)
    {
        var property = read.FindProperty(name);
        return property != null && property.propertyType == SerializedPropertyType.String ? property.stringValue ?? "" : "";
    }

    private static Object Reference(SerializedObject read, string name)
    {
        var property = read.FindProperty(name);
        return property != null && property.propertyType == SerializedPropertyType.ObjectReference ? property.objectReferenceValue : null;
    }

    private static IEnumerable<Object> References(SerializedObject read, string name)
    {
        var list = read.FindProperty(name);
        if (list == null || !list.isArray) yield break;
        for (int i = 0; i < list.arraySize; i++)
        {
            var each = list.GetArrayElementAtIndex(i);
            if (each.propertyType == SerializedPropertyType.ObjectReference && each.objectReferenceValue != null) yield return each.objectReferenceValue;
        }
    }

    private static float Number(SerializedObject read, string name)
    {
        var property = read.FindProperty(name);
        if (property == null) return 0f;
        float value = property.propertyType == SerializedPropertyType.Float ? property.floatValue
            : property.propertyType == SerializedPropertyType.Integer ? property.intValue : 0f;
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    // A whole number, an enum's or a yes/no (VRChat turned some yes/no settings into three-way ones along the way).
    private static int Whole(SerializedObject read, string name)
    {
        var property = read.FindProperty(name);
        if (property == null) return 0;
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.Enum: return property.intValue;
            case SerializedPropertyType.Boolean: return property.boolValue ? 1 : 0;
            case SerializedPropertyType.Float: return Mathf.RoundToInt(property.floatValue);
            default: return 0;
        }
    }

    private static ModAvatarFile.Curve Curve(SerializedObject read, string name)
    {
        var property = read.FindProperty(name);
        return property != null && property.propertyType == SerializedPropertyType.AnimationCurve
            ? new ModAvatarFile.Curve(property.animationCurveValue) : new ModAvatarFile.Curve();
    }
}
