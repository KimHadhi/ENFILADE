using UnityEngine;

/// <summary>
/// One moment of a gun's own clip as KINEMATION's arms hold it, in the gun's frame (the gun model's, KINEMATION's weapon
/// bone turned by the gun's rotationOffset): how far the clip has moved and turned the gun from where its pose clip holds
/// it, and where each palm is on the gun, the way its fingers point and the way it faces (ArmIK's hand frame), and the way
/// each finger joint points (GunArms.Digits), when they were read; and the way each elbow points off its shoulder-to-hand
/// line, in the frame of the gun as the pose clip holds it (zero when the arm was too straight to tell).
/// </summary>
internal struct GunContact
{
    public Vector3 GunMove;
    public Quaternion GunTurn;
    public Vector3 RightPalm, RightFingers, RightFacing, LeftPalm, LeftFingers, LeftFacing;
    public Vector3 RightElbow, LeftElbow;
    // Each finger segment's way in its own hand's frame (GunArms.Digits): null when not read. The arrays belong to
    // whoever filled them (FPSBody's buffers for a clip's moment, the gun's own for its hold).
    public Vector3[] RightDigits, LeftDigits;
}

/// <summary>
/// A hidden copy of the KINEMATION arms skeleton a gun's own clips are made for (the FPS Animation Pack's SK_Arms_Mono, the
/// Tactical Shooter Pack's SKM_Operator). FPSBody samples the clip the body is playing (its hold, a reload, a draw, a look
/// over the gun) on it at the same moment and reads how the clip moves the gun, where each hand's palm is on it and how
/// each finger bends (GunContact), then holds its own gun and puts its own palms and fingers there, whatever the body's
/// build. The remade clips carry joint angles only: on a body with other arm lengths the hands were somewhere else at every
/// moment, and the gun stayed in the right hand (the user, 2026-11-12, asking whether a hand offset per character would fix
/// it; one offset fixes one moment).
/// Nothing of it is drawn, it never moves, and it has no Animator, so a clip's curves write its bones as they are.
/// </summary>
internal sealed class GunArms
{
    // KINEMATION's names on both skeletons.
    private const string GunBone = "ik_hand_gun";
    // HandGrip's palm centre: from the wrist toward the four knuckles, this share of the way.
    private const float PalmShare = .55f;
    /// <summary>Finger segments read per hand: index, middle, ring, little, thumb; base, middle, tip (HandGrip's order).</summary>
    internal const int DigitCount = 15;
    // KINEMATION's finger names in HandGrip's order (index, middle, ring, little, thumb).
    private static readonly string[] FingerNames = { "index", "middle", "ring", "pinky", "thumb" };

    private sealed class Hand
    {
        public Transform Wrist, Index, Middle, Ring, Pinky;
        // The arm's shoulder joint and elbow (upperarm_, lowerarm_), for the way the elbow points; null if missing.
        public Transform Shoulder, Elbow;
        // Each finger's three joints (FingerNames' order) and the way its tip joint points in its own axes: the middle
        // joint's way to the tip joint, as KINEMATION's skeletons lay every finger bone along the same axis.
        public readonly Transform[] Joints = new Transform[DigitCount];
        public readonly Vector3[] TipAxes = new Vector3[5];
        public bool Valid => Wrist != null && Index != null && Middle != null && Pinky != null;
        public bool HasDigits;
    }

    private readonly GameObject copy;
    private readonly Transform gun;
    private readonly Hand right, left;

    private GunArms(GameObject copy, Transform gun, Hand right, Hand left)
    {
        this.copy = copy;
        this.gun = gun;
        this.right = right;
        this.left = left;
    }

    /// <summary>A hidden copy of the arms model <paramref name="prefab"/>, or null if it has no weapon bone or hands.</summary>
    internal static GunArms Make(GameObject prefab)
    {
        if (prefab == null) return null;
        var copy = Object.Instantiate(prefab);
        copy.name = prefab.name + " (gun contacts)";
        copy.hideFlags = HideFlags.DontSave;
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        // Sampled straight onto the bones: an Animator reads a clip through its avatar, which kept KINEMATION's fingers off
        // the mannequin in the gun bake (GunClipRetarget, version 10).
        foreach (var animator in copy.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
        var root = copy.transform;
        var arms = new GunArms(copy, Find(root, GunBone), HandOf(root, "r"), HandOf(root, "l"));
        if (arms.gun != null && arms.right.Valid && arms.left.Valid) return arms;
        Debug.LogWarning($"GunArms: {prefab.name} has no {GunBone} or no hand_r/hand_l with index, middle and pinky knuckles; "
            + "its guns' clips stay on the remade clips alone.", prefab);
        arms.Release();
        return null;
    }

    /// <summary>Destroys the copy.</summary>
    internal void Release()
    {
        if (copy != null) Object.Destroy(copy);
    }

    /// <summary>Poses the copy as <paramref name="clip"/> has it <paramref name="time"/> seconds in.</summary>
    internal bool Sample(AnimationClip clip, float time)
    {
        if (copy == null || clip == null) return false;
        clip.SampleAnimation(copy, Mathf.Clamp(time, 0f, clip.length));
        return true;
    }

    /// <summary>The gun as the copy holds it now: KINEMATION's weapon bone, turned by the gun's <paramref name="frame"/>.</summary>
    internal void Gun(Quaternion frame, out Vector3 position, out Quaternion rotation)
    {
        position = gun.position;
        rotation = gun.rotation * frame;
    }

    /// <summary>
    /// Where a palm is now in the gun's frame: the gun at <paramref name="gunPosition"/>, <paramref name="toGun"/> turning
    /// the world into its frame. The palm's middle as HandGrip takes it, the fingers along the middle knuckle, the palm facing
    /// across from the pinky to the index knuckle as ArmIK.Create turns it.
    /// </summary>
    internal bool Palm(bool isLeft, Vector3 gunPosition, Quaternion toGun, out Vector3 palm, out Vector3 fingers, out Vector3 facing)
    {
        palm = fingers = facing = Vector3.zero;
        var hand = isLeft ? left : right;
        if (!hand.Valid) return false;
        Vector3 wrist = hand.Wrist.position;
        Vector3 knuckles = hand.Index.position + hand.Middle.position + hand.Pinky.position;
        int count = 3;
        if (hand.Ring != null) { knuckles += hand.Ring.position; count++; }
        Vector3 along = hand.Middle.position - wrist;
        Vector3 across = hand.Index.position - hand.Pinky.position;
        Vector3 faces = Vector3.Cross(along, across) * (isLeft ? -1f : 1f);
        if (along.sqrMagnitude < 1e-8f || faces.sqrMagnitude < 1e-10f) return false;
        palm = toGun * (wrist + (knuckles / count - wrist) * PalmShare - gunPosition);
        fingers = toGun * along.normalized;
        facing = toGun * faces.normalized;
        return true;
    }

    /// <summary>
    /// The way the elbow points now off the line from the shoulder joint to the wrist (unit), turned by
    /// <paramref name="toFrame"/> into the frame wanted. False when the arm lacks the joints or is too straight to tell.
    /// </summary>
    internal bool ElbowWay(bool isLeft, Quaternion toFrame, out Vector3 way)
    {
        way = Vector3.zero;
        var hand = isLeft ? left : right;
        if (!hand.Valid || hand.Shoulder == null || hand.Elbow == null) return false;
        Vector3 line = hand.Wrist.position - hand.Shoulder.position;
        if (line.sqrMagnitude < 1e-8f) return false;
        Vector3 off = Vector3.ProjectOnPlane(hand.Elbow.position - hand.Shoulder.position, line);
        // Under a centimetre off the line on a 25 cm upper arm the side it bends to is noise.
        if (off.sqrMagnitude < 1e-4f) return false;
        way = toFrame * off.normalized;
        return true;
    }

    /// <summary>
    /// The way each finger segment of a hand points now, in that hand's own frame: x along the way the fingers point (wrist
    /// to middle knuckle), y the way the palm faces (as Palm takes it), z across (x × y); index, middle, ring, little, thumb,
    /// each base, middle and tip (HandGrip's order). Read from where the joints are, so it holds whatever the bones' axes;
    /// any hand put in the same frame (HandGrip.WearDigits) takes the same finger shape. False when a joint is missing.
    /// </summary>
    internal bool Digits(bool isLeft, Vector3[] into)
    {
        var hand = isLeft ? left : right;
        if (!hand.Valid || !hand.HasDigits || into == null || into.Length < DigitCount) return false;
        Vector3 along = hand.Middle.position - hand.Wrist.position;
        Vector3 faces = Vector3.Cross(along, hand.Index.position - hand.Pinky.position) * (isLeft ? -1f : 1f);
        if (along.sqrMagnitude < 1e-8f || faces.sqrMagnitude < 1e-10f) return false;
        Vector3 x = along.normalized, y = Vector3.ProjectOnPlane(faces, x).normalized, z = Vector3.Cross(x, y);
        for (int f = 0; f < 5; f++)
            for (int j = 0; j < 3; j++)
            {
                int i = f * 3 + j;
                Vector3 way = j < 2 ? hand.Joints[i + 1].position - hand.Joints[i].position : hand.Joints[i].rotation * hand.TipAxes[f];
                if (way.sqrMagnitude < 1e-10f) return false;
                way.Normalize();
                into[i] = new Vector3(Vector3.Dot(way, x), Vector3.Dot(way, y), Vector3.Dot(way, z));
            }
        return true;
    }

    private static Hand HandOf(Transform root, string side)
    {
        var wrist = Find(root, "hand_" + side);
        if (wrist == null) return new Hand();
        var hand = new Hand
        {
            Wrist = wrist,
            Index = Find(wrist, "index_01_" + side),
            Middle = Find(wrist, "middle_01_" + side),
            Ring = Find(wrist, "ring_01_" + side),
            Pinky = Find(wrist, "pinky_01_" + side),
            Shoulder = Find(root, "upperarm_" + side),
            Elbow = Find(root, "lowerarm_" + side)
        };
        hand.HasDigits = true;
        for (int f = 0; f < 5 && hand.HasDigits; f++)
        {
            for (int j = 0; j < 3; j++)
            {
                var joint = Find(wrist, $"{FingerNames[f]}_0{j + 1}_{side}");
                if (joint == null) { hand.HasDigits = false; break; }
                hand.Joints[f * 3 + j] = joint;
            }
            if (!hand.HasDigits) break;
            // The tip joint has no joint past it: it points the way the middle joint points to it, in the middle joint's
            // own axes (KINEMATION's finger bones all lie along one axis).
            var middle = hand.Joints[f * 3 + 1];
            Vector3 toTip = Quaternion.Inverse(middle.rotation) * (hand.Joints[f * 3 + 2].position - middle.position);
            if (toTip.sqrMagnitude < 1e-10f) { hand.HasDigits = false; break; }
            hand.TipAxes[f] = toTip.normalized;
        }
        return hand;
    }

    private static Transform Find(Transform root, string name)
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
