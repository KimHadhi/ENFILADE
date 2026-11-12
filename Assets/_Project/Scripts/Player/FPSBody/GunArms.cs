using UnityEngine;

/// <summary>
/// One moment of a gun's own clip as KINEMATION's arms hold it, in the gun's frame (the gun model's, KINEMATION's weapon
/// bone turned by the gun's rotationOffset): how far the clip has moved and turned the gun from where its pose clip holds
/// it, and where each palm is on the gun, the way its fingers point and the way it faces (ArmIK's hand frame).
/// </summary>
internal struct GunContact
{
    public Vector3 GunMove;
    public Quaternion GunTurn;
    public Vector3 RightPalm, RightFingers, RightFacing, LeftPalm, LeftFingers, LeftFacing;
}

/// <summary>
/// A hidden copy of the KINEMATION arms skeleton a gun's own clips are made for (the FPS Animation Pack's SK_Arms_Mono, the
/// Tactical Shooter Pack's SKM_Operator). FPSBody samples the clip the body is playing (a reload) on it at the same moment
/// and reads how the clip moves the gun and where each hand's palm is on it (GunContact), then holds its own gun and puts
/// its own palms there by IK, whatever the body's build. The remade clips carry joint angles only: on a body with other arm
/// lengths the hands were somewhere else at every moment, and the gun stayed in the right hand (the user, 2026-11-12, asking
/// whether a hand offset per character would fix it; one offset fixes one moment).
/// Nothing of it is drawn, it never moves, and it has no Animator, so a clip's curves write its bones as they are.
/// </summary>
internal sealed class GunArms
{
    // KINEMATION's names on both skeletons.
    private const string GunBone = "ik_hand_gun";
    // HandGrip's palm centre: from the wrist toward the four knuckles, this share of the way.
    private const float PalmShare = .55f;

    private sealed class Hand
    {
        public Transform Wrist, Index, Middle, Ring, Pinky;
        public bool Valid => Wrist != null && Index != null && Middle != null && Pinky != null;
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
            + "its guns' reloads stay on the remade clips alone.", prefab);
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

    private static Hand HandOf(Transform root, string side)
    {
        var wrist = Find(root, "hand_" + side);
        if (wrist == null) return new Hand();
        return new Hand
        {
            Wrist = wrist,
            Index = Find(wrist, "index_01_" + side),
            Middle = Find(wrist, "middle_01_" + side),
            Ring = Find(wrist, "ring_01_" + side),
            Pinky = Find(wrist, "pinky_01_" + side)
        };
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
