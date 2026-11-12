using System.Collections.Generic;
using System.Reflection;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

/// <summary>
/// Lays out a humanoid body the way KINEMATION FPS Animation Framework's demo humanoid (FPSHumanoidPlayer) is set up, so
/// the demo's profiles, which name bones by their place in its rig (Rig_Humanoid, copied from the demo's), work on any
/// humanoid as they are: each of the rig's 90 elements is the body's own bone for that part (found from the avatar), or
/// one of the objects FPSAF's wizard adds (the IK weapon bone with its hand and elbow targets under the head, the camera
/// socket, the weapon bones in the hands), or an empty stand-in where this body has no such bone (no upper chest, no
/// jaw). The KRigComponent then lists them in that order.
/// The added objects keep world scale 1 whatever the body's fit scale is, so a gun under the weapon bone is its true size
/// and FPSAF's aim point maths stays in metres.
/// </summary>
internal sealed class FPSBodyRig
{
    private enum Kind { Root, Bone, End, Added }

    private readonly struct Element
    {
        public readonly string Name;
        public readonly Kind Kind;
        public readonly HumanBodyBones Bone;
        // Added: the element it hangs under. End: the element it ends. Bone: the element a missing bone stands in under.
        public readonly int Parent;
        // Added: the bone an IK target copies each frame (KVirtualElement), if any.
        public readonly HumanBodyBones Copies;
        public readonly bool HasCopy;

        public Element(string name, Kind kind, HumanBodyBones bone = HumanBodyBones.Hips, int parent = -1,
            HumanBodyBones copies = HumanBodyBones.Hips, bool hasCopy = false)
        {
            Name = name; Kind = kind; Bone = bone; Parent = parent; Copies = copies; HasCopy = hasCopy;
        }
    }

    // Element indices the code needs (the demo rig's order).
    public const int SkeletonIndex = 0, ChestIndex = 13, UpperChestIndex = 14, NeckIndex = 40, HeadIndex = 41, CameraIndex = 45,
        CameraSocketIndex = 46, IkWeaponBoneIndex = 48,
        WeaponBoneLeftIndex = 39, WeaponBoneRightIndex = 78, WeaponBoneIndex = 84;

    private static readonly Element[] Layout = BuildLayout();

    public KRigComponent Component { get; private set; }
    public Transform Skeleton { get; private set; }
    public Transform Head { get; private set; }
    public Transform IkWeaponBone { get; private set; }
    /// <summary>The camera element under the head; its child, the camera socket, is where FPSAF's Ads lines the sight up.</summary>
    public Transform Camera { get; private set; }
    public Transform WeaponBoneRight { get; private set; }
    public Transform CameraSocket { get; private set; }
    public Transform RightHand { get; private set; }

    private readonly List<GameObject> added = new();

    private static Element[] BuildLayout()
    {
        var list = new List<Element>
        {
            new("Skeleton", Kind.Root),
            new("Hips", Kind.Bone, HumanBodyBones.Hips),
        };
        void Leg(string side, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot, HumanBodyBones toes)
        {
            int start = list.Count;
            list.Add(new Element(side + "_UpperLeg", Kind.Bone, upper, 1));
            list.Add(new Element(side + "_LowerLeg", Kind.Bone, lower, start));
            list.Add(new Element(side + "_Foot", Kind.Bone, foot, start + 1));
            list.Add(new Element(side + "_Toes", Kind.Bone, toes, start + 2));
            list.Add(new Element(side + "_ToesEnd", Kind.End, parent: start + 3));
        }
        Leg("Left", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
        Leg("Right", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
        list.Add(new Element("Spine", Kind.Bone, HumanBodyBones.Spine, 1));
        list.Add(new Element("Chest", Kind.Bone, HumanBodyBones.Chest, 12));
        list.Add(new Element("UpperChest", Kind.Bone, HumanBodyBones.UpperChest, 13));

        void Arm(bool left)
        {
            string side = left ? "Left" : "Right";
            int shoulder = list.Count;
            list.Add(new Element(side + "_Shoulder", Kind.Bone, left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder, 14));
            list.Add(new Element(side + "_UpperArm", Kind.Bone, left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, shoulder));
            list.Add(new Element(side + "_LowerArm", Kind.Bone, left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, shoulder + 1));
            int hand = list.Count;
            list.Add(new Element(side + "_Hand", Kind.Bone, left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, shoulder + 2));
            // The demo rig's finger order: index, middle, pinky, ring, thumb.
            var fingers = left
                ? new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftLittleProximal,
                    HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftThumbProximal }
                : new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightLittleProximal,
                    HumanBodyBones.RightRingProximal, HumanBodyBones.RightThumbProximal };
            string[] names = { "Index", "Middle", "Pinky", "Ring", "Thumb" };
            for (int f = 0; f < 5; f++)
            {
                int first = list.Count;
                list.Add(new Element($"{side}_{names[f]}Proximal", Kind.Bone, fingers[f], hand));
                list.Add(new Element($"{side}_{names[f]}Intermediate", Kind.Bone, fingers[f] + 1, first));
                list.Add(new Element($"{side}_{names[f]}Distal", Kind.Bone, fingers[f] + 2, first + 1));
                list.Add(new Element($"{side}_{names[f]}DistalEnd", Kind.End, parent: first + 2));
            }
            list.Add(new Element(left ? "IK WeaponBoneLeft" : "IK WeaponBoneRight", Kind.Added, parent: hand));
        }

        Arm(true);
        list.Add(new Element("Neck", Kind.Bone, HumanBodyBones.Neck, 14));
        list.Add(new Element("Head", Kind.Bone, HumanBodyBones.Head, 40));
        list.Add(new Element("Jaw", Kind.Bone, HumanBodyBones.Jaw, 41));
        list.Add(new Element("Left_Eye", Kind.Bone, HumanBodyBones.LeftEye, 41));
        list.Add(new Element("Right_Eye", Kind.Bone, HumanBodyBones.RightEye, 41));
        list.Add(new Element("Camera", Kind.Added, parent: 41));
        list.Add(new Element("CameraSocket", Kind.Added, parent: 45));
        list.Add(new Element("Socket", Kind.Added, parent: 41));
        list.Add(new Element("IK WeaponBone", Kind.Added, parent: 41));
        list.Add(new Element("IK RightHand", Kind.Added, parent: 48, copies: HumanBodyBones.RightHand, hasCopy: true));
        list.Add(new Element("IK RightElbow", Kind.Added, parent: 48, copies: HumanBodyBones.RightLowerArm, hasCopy: true));
        list.Add(new Element("IK LeftHand", Kind.Added, parent: 48, copies: HumanBodyBones.LeftHand, hasCopy: true));
        list.Add(new Element("IK LeftElbow", Kind.Added, parent: 48, copies: HumanBodyBones.LeftLowerArm, hasCopy: true));
        list.Add(new Element("Neck_Twist_A", Kind.Added, parent: 40));
        Arm(false);
        foreach (string name in new[] { "IK_Elbow_Left", "IK_Elbow_Right", "ik_hand_gun", "IK_Hand_Left", "IK_Hand_Right", "WeaponBone", "WeaponBoneAdditive" })
            list.Add(new Element(name, Kind.Added, parent: 0));
        list.Add(new Element("IK RightFoot", Kind.Added, parent: 0, copies: HumanBodyBones.RightFoot, hasCopy: true));
        list.Add(new Element("IK RightKnee", Kind.Added, parent: 0, copies: HumanBodyBones.RightLowerLeg, hasCopy: true));
        list.Add(new Element("IK LeftFoot", Kind.Added, parent: 0, copies: HumanBodyBones.LeftFoot, hasCopy: true));
        list.Add(new Element("IK LeftKnee", Kind.Added, parent: 0, copies: HumanBodyBones.LeftLowerLeg, hasCopy: true));
        return list.ToArray();
    }

    /// <summary>Lays the rig out on <paramref name="animator"/>'s humanoid body, or returns null if it isn't one.</summary>
    public static FPSBodyRig Build(Animator animator, KRig rig)
    {
        if (animator == null || !animator.isHuman) return null;
        if (HierarchyField == null)
        {
            Debug.LogWarning("FPSBodyRig: KRigComponent no longer keeps its element list in 'hierarchy'; the body can't take FPSAF's rig.");
            return null;
        }
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (hips == null || head == null || hips.parent == null) return null;
        if (rig != null && rig.rigHierarchy != null && rig.rigHierarchy.Count != Layout.Length)
            Debug.LogWarning($"FPSBodyRig: the rig asset {rig.name} lists {rig.rigHierarchy.Count} elements, not the demo humanoid's {Layout.Length}; its profiles would name the wrong bones.", rig);

        var result = new FPSBodyRig();
        var transforms = new Transform[Layout.Length];
        var skeleton = hips.parent;
        for (int i = 0; i < Layout.Length; i++)
        {
            var element = Layout[i];
            switch (element.Kind)
            {
                case Kind.Root:
                    transforms[i] = skeleton;
                    break;
                case Kind.Bone:
                    transforms[i] = animator.GetBoneTransform(element.Bone);
                    if (transforms[i] == null) transforms[i] = result.Add(element.Name, transforms[element.Parent], false);
                    break;
                case Kind.End:
                    var end = transforms[element.Parent];
                    transforms[i] = end.childCount > 0 && Layout[element.Parent].Kind == Kind.Bone ? end.GetChild(0)
                        : result.Add(element.Name, end, false);
                    break;
                case Kind.Added:
                    transforms[i] = result.Add(element.Name, transforms[element.Parent], true);
                    if (element.HasCopy)
                    {
                        var target = animator.GetBoneTransform(element.Copies);
                        if (target != null) transforms[i].gameObject.AddComponent<KVirtualElement>().targetBone = target;
                    }
                    break;
            }
        }

        result.Skeleton = skeleton;
        result.Head = head;
        result.IkWeaponBone = transforms[IkWeaponBoneIndex];
        result.WeaponBoneRight = transforms[WeaponBoneRightIndex];
        result.CameraSocket = transforms[CameraSocketIndex];
        result.Camera = transforms[CameraIndex];
        result.RightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        // The IK weapon bone starts where the right hand holds it; FPSAF's Pose Sampler moves it there every frame anyway.
        if (result.RightHand != null) result.IkWeaponBone.position = result.RightHand.position;

        var component = skeleton.gameObject.AddComponent<KRigComponent>();
        HierarchyField.SetValue(component, new List<Transform>(transforms));
        result.Component = component;
        return result;
    }

    // KRigComponent keeps its element list private and fills it in the editor (Import Rig); here it is filled in the rig's
    // order at run time, which is the one difference from KINEMATION's own setup.
    private static readonly FieldInfo HierarchyField =
        typeof(KRigComponent).GetField("hierarchy", BindingFlags.Instance | BindingFlags.NonPublic);

    private Transform Add(string name, Transform parent, bool trueScale)
    {
        var made = new GameObject(name).transform;
        made.SetParent(parent, false);
        made.localPosition = Vector3.zero;
        made.localRotation = Quaternion.identity;
        if (trueScale)
        {
            Vector3 lossy = parent.lossyScale;
            made.localScale = new Vector3(Inverse(lossy.x), Inverse(lossy.y), Inverse(lossy.z));
        }
        added.Add(made.gameObject);
        return made;

        static float Inverse(float value) => Mathf.Abs(value) > 1e-6f ? 1f / value : 1f;
    }

    /// <summary>
    /// Whether <paramref name="bone"/> is one of the body's own bones, not an empty stand-in the layout added where the
    /// body has none (an avatar without an UpperChest): turning a stand-in turns nothing.
    /// </summary>
    public bool IsBodyBone(Transform bone) => bone != null && !added.Contains(bone.gameObject);

    /// <summary>Removes what the layout added to the body.</summary>
    public void Remove()
    {
        if (Component != null) Object.Destroy(Component);
        for (int i = added.Count - 1; i >= 0; i--)
            if (added[i] != null) Object.Destroy(added[i]);
        added.Clear();
    }
}
