using UnityEngine;

/// <summary>
/// Where a knife sits in a right fist, and so where its blade is: the one working of it. The arm seats the model by it
/// each frame on the hand as that finally is (ExecutionKnifeArm), and the same working puts a clip's blade in the eyes'
/// own frame from the clip's key alone (Blade, Reach), which is how a swing's reach is known before it is thrown
/// (PlayerMeleeCombat's step in) and how it is looked at without playing (KnifeSwingReport). The knife's own shape comes
/// from its model's markers (Measure): the handle leads from Pommel through the grip (the model's origin) to the blade,
/// Edge lies off that line on the cutting side, Tip is where the blade ends, and an optional BladeBase is where it
/// starts (without one the blade is taken from the grip, so the fist cuts with it). A model with no markers has its
/// blade along its -Z, 20 cm long, its edge along -Y.
/// The blade is what a swing hits with (the user, 2026-11-18: "공격 범위 및 히트 판정은 별도 박스 판정이 아닌 True FPS
/// 1인칭 화면에 보이는 실제 무기 모델 자체를 기준으로 처리"): BladeTrace cuts along the line from its start to its tip as
/// the arm shows it. Until then a sphere went out from the eyes 1.6 m and more, whatever the knife was doing.
/// </summary>
public static class KnifeSeat
{
    /// <summary>A hand's length (wrist to middle knuckle, m) to work a blade out with where no body is there to measure.</summary>
    public const float NominalHand = .06f;
    // A model with no Tip marker is taken to be this long from its grip (m at scale 1).
    private const float UnmarkedReach = .2f;
    // A swing's reach is looked for where the blade passes in front of the view: within this of the line of sight to
    // either side (m, about a body's half width), every this long of its clip (s), at this many places along the blade.
    private const float Front = .2f, Step = 1f / 120f;
    private const int Along = 4;

    /// <summary>A knife model as its markers say it.</summary>
    public struct Shape
    {
        /// <summary>The model's own ways: from the grip to the blade, and its cutting side.</summary>
        public Vector3 Blade, Edge;
        /// <summary>
        /// In the holder the arm seats (the grip at its origin, the blade along +z, the edge +y; m): where the blade starts
        /// and where it ends.
        /// </summary>
        public Vector3 Base, Tip;
        /// <summary>The model carries a Tip marker (else its length is a guess).</summary>
        public bool Marked;
        public float Length => (Tip - Base).magnitude;

        /// <summary>A knife nobody measured: the unmarked model's shape.</summary>
        public static Shape Plain => new Shape { Blade = Vector3.back, Edge = Vector3.down, Tip = Vector3.forward * UnmarkedReach };
    }

    /// <summary>
    /// The shape of a knife model (its root, a made copy's or the prefab's own) at a scale. The model then sits in its
    /// holder turned by Quaternion.Inverse(Quaternion.LookRotation(Blade, Edge)), scaled, at the holder's origin.
    /// </summary>
    public static Shape Measure(Transform root, float scale)
    {
        var shape = Shape.Plain;
        shape.Tip *= scale;
        if (root == null) return shape;
        Transform pommel = Find(root, "Pommel"), tip = Find(root, "Tip"), edge = Find(root, "Edge"), start = Find(root, "BladeBase");
        Vector3 blade = pommel != null ? -root.InverseTransformPoint(pommel.position)
            : tip != null ? root.InverseTransformPoint(tip.position) : Vector3.back;
        shape.Blade = blade.sqrMagnitude > 1e-8f ? blade.normalized : Vector3.back;
        Vector3 cutting = edge != null ? Vector3.ProjectOnPlane(root.InverseTransformPoint(edge.position), shape.Blade) : Vector3.zero;
        if (cutting.sqrMagnitude < 1e-8f) cutting = Vector3.ProjectOnPlane(Vector3.down, shape.Blade);
        if (cutting.sqrMagnitude < 1e-8f) cutting = Vector3.ProjectOnPlane(Vector3.right, shape.Blade);
        shape.Edge = cutting.normalized;
        Quaternion toHolder = Quaternion.Inverse(Quaternion.LookRotation(shape.Blade, shape.Edge));
        shape.Marked = tip != null;
        if (tip != null) shape.Tip = toHolder * root.InverseTransformPoint(tip.position) * scale;
        if (start != null) shape.Base = toHolder * root.InverseTransformPoint(start.position) * scale;
        return shape;
    }

    /// <summary>
    /// The holder of a knife in a right hand: its grip's place and its turn (blade along its +z, edge its +y), from the
    /// wrist, the hand's run (wrist to middle knuckle) and its palm's facing, the hand's length, and the row's seat in the
    /// hand's axes (ClassKnives: x away from the thumb, y out of the palm, z the fingers' way; held in hand lengths).
    /// False when the hand or the row gives no frame.
    /// </summary>
    public static bool Place(Vector3 wrist, Vector3 along, Vector3 palm, float handLength, Vector3 heldAxes, Vector3 bladeAxes,
        Vector3 edgeAxes, out Vector3 grip, out Quaternion turn)
    {
        grip = wrist;
        turn = Quaternion.identity;
        if (along.sqrMagnitude < 1e-10f || palm.sqrMagnitude < 1e-10f) return false;
        Quaternion frame = Quaternion.LookRotation(along, palm);
        Vector3 blade = frame * Mirror(bladeAxes);
        Vector3 edge = Vector3.ProjectOnPlane(frame * Mirror(edgeAxes), blade);
        if (edge.sqrMagnitude < 1e-8f) edge = Vector3.ProjectOnPlane(palm, blade);
        if (blade.sqrMagnitude < 1e-8f || edge.sqrMagnitude < 1e-8f) return false;
        grip = wrist + frame * Mirror(heldAxes * handLength);
        turn = Quaternion.LookRotation(blade, edge);
        return true;
    }

    /// <summary>
    /// The blade at a clip's key, in the frame the key is in (the eyes': x right, y up, z ahead; m), held in the knife's
    /// fighting grip: where it starts and its tip.
    /// </summary>
    public static bool Blade(in KnifeSwing.Key key, ClassKnives.Knife knife, float handLength, in Shape shape, out Vector3 start, out Vector3 tip)
    {
        start = tip = key.Wrist;
        if (knife == null || !Place(key.Wrist, key.Along, key.Palm, handLength, knife.MeleeHeld, knife.MeleeBlade, knife.MeleeEdge,
                out Vector3 grip, out Quaternion turn)) return false;
        start = grip + turn * shape.Base;
        tip = grip + turn * shape.Tip;
        return true;
    }

    /// <summary>
    /// How far ahead of the eyes a swing's blade gets while it is live (m): the furthest ahead any of it is as it passes
    /// in front of the view, or anywhere when it never passes there. A body no nearer than this is not cut however well
    /// the swing is aimed, so the body steps in by it (PlayerMeleeCombat). 0 for a clip that cuts nothing.
    /// </summary>
    public static float Reach(KnifeSwing clip, ClassKnives.Knife knife, float handLength, in Shape shape)
    {
        if (clip == null || !clip.Cuts) return 0f;
        float front = 0f, anywhere = 0f;
        int steps = Mathf.Max(1, Mathf.CeilToInt((clip.ActiveTo - clip.ActiveFrom) / Step));
        for (int i = 0; i <= steps; i++)
        {
            float time = Mathf.Lerp(clip.ActiveFrom, clip.ActiveTo, i / (float)steps);
            if (!clip.Sample(time, out var key) || !Blade(key, knife, handLength, shape, out Vector3 start, out Vector3 tip)) continue;
            for (int k = 0; k <= Along; k++)
            {
                Vector3 point = Vector3.Lerp(start, tip, k / (float)Along);
                anywhere = Mathf.Max(anywhere, point.z);
                if (Mathf.Abs(point.x) <= Front) front = Mathf.Max(front, point.z);
            }
        }
        return front > 0f ? front : anywhere;
    }

    /// <summary>A right hand's own axes from the row's (x away from the thumb, which on a right hand is its frame's -x).</summary>
    public static Vector3 Mirror(Vector3 handAxes) => new Vector3(-handAxes.x, handAxes.y, handAxes.z);

    private static Transform Find(Transform root, string name)
    {
        foreach (var part in root.GetComponentsInChildren<Transform>(true))
            if (part != root && string.Equals(part.name, name, System.StringComparison.OrdinalIgnoreCase)) return part;
        return null;
    }
}
