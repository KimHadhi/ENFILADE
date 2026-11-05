using UnityEngine;

/// <summary>
/// One arm of KINEMATION's arms rig, read for the body's arm to copy: the shoulder, elbow and wrist joints, the hand's
/// frame (where the fingers point and the palm faces, measured as ArmIK measures the body's hand), the palm's centre
/// (as HandGrip measures it) and where each finger joint points. Bone names are KINEMATION's UE4-style skeleton's.
/// </summary>
internal sealed class GunRigArm
{
    // The body's finger order in HumanBodyBones (thumb, index, middle, ring, little) in KINEMATION's names.
    private static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "pinky" };
    // HandGrip puts the palm's centre this share of the way from the wrist to the knuckles.
    private const float PalmCentreShare = .55f;

    public Transform Shoulder { get; }
    public Transform Elbow { get; }
    public Transform Wrist { get; }
    /// <summary>Shoulder to elbow to wrist (m), from the rest pose.</summary>
    public float Length { get; }

    private readonly bool left;
    private readonly Transform[,] fingers = new Transform[5, 3];
    // Each finger joint's direction to the next joint, in the joint's own frame (the last joint continues the one before).
    private readonly Vector3[,] axes = new Vector3[5, 3];

    private GunRigArm(bool left, Transform shoulder, Transform elbow, Transform wrist)
    {
        this.left = left;
        Shoulder = shoulder; Elbow = elbow; Wrist = wrist;
        Length = Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position);
    }

    /// <summary>The arm in a rig whose bones are named upperarm_l, lowerarm_l, hand_l, index_01_l and so on.</summary>
    public static GunRigArm Create(Transform root, bool left)
    {
        string side = left ? "_l" : "_r";
        var shoulder = Find(root, "upperarm" + side);
        var elbow = Find(root, "lowerarm" + side);
        var wrist = Find(root, "hand" + side);
        if (shoulder == null || elbow == null || wrist == null) return null;
        var arm = new GunRigArm(left, shoulder, elbow, wrist);
        for (int f = 0; f < 5; f++)
        {
            for (int j = 0; j < 3; j++) arm.fingers[f, j] = Find(wrist, $"{FingerNames[f]}_0{j + 1}{side}");
            for (int j = 0; j < 3; j++)
            {
                var bone = arm.fingers[f, j];
                if (bone == null) continue;
                Transform next = j < 2 ? arm.fingers[f, j + 1] : bone.childCount > 0 ? bone.GetChild(0) : null;
                Vector3 local = next != null ? bone.InverseTransformDirection(next.position - bone.position) : Vector3.zero;
                arm.axes[f, j] = local.sqrMagnitude > 1e-10f ? local.normalized : j > 0 ? arm.axes[f, j - 1] : Vector3.zero;
            }
        }
        return arm;
    }

    /// <summary>Where the fingers point, where the palm faces, and the palm's centre, in the world.</summary>
    public void Frame(out Vector3 fingersPoint, out Vector3 palmFaces, out Vector3 palmCentre)
    {
        Vector3 wrist = Wrist.position;
        Transform index = fingers[1, 0], middle = fingers[2, 0], little = fingers[4, 0];
        fingersPoint = (middle != null ? middle.position : wrist + (wrist - Elbow.position)) - wrist;
        Vector3 thumbSide = index != null && little != null ? index.position - little.position : Wrist.forward;
        palmFaces = Vector3.Cross(fingersPoint, thumbSide) * (left ? -1f : 1f);
        Vector3 knuckles = Vector3.zero;
        int count = 0;
        for (int f = 1; f < 5; f++)
            if (fingers[f, 0] != null) { knuckles += fingers[f, 0].position; count++; }
        palmCentre = wrist + ((count > 0 ? knuckles / count : wrist + fingersPoint) - wrist) * PalmCentreShare;
    }

    /// <summary>Where joint <paramref name="joint"/> (0 knuckle, 2 tip) of finger <paramref name="finger"/> points.</summary>
    public bool Direction(int finger, int joint, out Vector3 direction)
    {
        var bone = fingers[finger, joint];
        direction = bone != null ? bone.TransformDirection(axes[finger, joint]) : Vector3.zero;
        return direction.sqrMagnitude > 1e-10f;
    }

    internal static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = Find(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
