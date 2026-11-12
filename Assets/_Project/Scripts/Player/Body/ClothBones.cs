using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The free bones of one worn part, as the other PCs need them: the PC that simulates reads where they went, and a copy
/// puts its own there. Places are kept relative to the bone the part hangs from, so the shape rides whatever the body is
/// doing on that PC. Only positions travel. A copy turns each bone from its resting turn toward its neighbour on the
/// chain, which is how the simulation turns it too, roots included when the part's roots turn at all.
/// </summary>
internal sealed class ClothBones
{
    // A bone right on its root has nowhere to go, but dividing by its reach still has to work.
    private const float LeastReach = .001f;
    // The share of its room a bone's cloth may fill.
    private const float RoomShare = .9f;
    /// <summary>
    /// The thinnest cloth gets (metres): the presets' own, which pushed a little at rest where a skirt hugs the hips and
    /// still looked right there.
    /// </summary>
    internal const float LeastThickness = .02f;
    // The thinnest a particle measured from its own cloth gets (metres), so a flat ribbon still has some.
    private const float LeastParticle = .001f;
    // A settling chain looks for where its bone first touches in steps of this many degrees, then this many halvings.
    private const float SettleStep = .5f;
    private const int SettleSearch = 10;

    private struct Bone
    {
        public Transform Transform;
        /// <summary>Its neighbours on the chain, -1 where there is none. Parents come first in the list.</summary>
        public int Up, Down;
        public bool Moves;
        public Vector3 Rest, Now, Wanted, RestAim;
        public Quaternion RestTurn;
        /// <summary>How far it hangs from its root, which is how far it can go.</summary>
        public float Reach;
        /// <summary>How much of the turn toward its neighbour it takes: all of it, unless it is a root.</summary>
        public float Turn;
        /// <summary>Its depth as Magica Cloth measures it: path length up to the root over the longest one, 1 at the far end.</summary>
        public float Depth;
        /// <summary>How far it hung from what it rests against when the part was made (metres).</summary>
        public float Room;
        /// <summary>Its particle's radius as the simulation will have it (metres), once FitRadii has set it; 0 before.</summary>
        public float Radius;
    }

    private readonly Transform anchor;
    private readonly Bone[] bones;
    private readonly float rootTurn;

    /// <summary>How many bones a pose of this part carries.</summary>
    internal int Moving { get; }
    /// <summary>How far the part hangs at the longest, in metres.</summary>
    internal float Length { get; }
    /// <summary>
    /// The particles' radius down the part as FitRadii set it, as shares of FittedScale (metres); null when the part's
    /// radius is the table's thickness (Thickness) or the preset's.
    /// </summary>
    internal AnimationCurve FittedRadius { get; private set; }
    internal float FittedScale { get; private set; }

    internal ClothBones(List<Transform> roots, float rootTurn)
    {
        this.rootTurn = rootTurn;
        anchor = roots[0].parent;
        var list = new List<Bone>();
        foreach (var root in roots) Add(root, -1, list.Count, list);
        bones = list.ToArray();
        float longest = 0f, deepest = 0f;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].Moves) Moving++;
            bones[i].RestAim = Aim(i, true);
            bones[i].Room = float.PositiveInfinity;
            longest = Mathf.Max(longest, bones[i].Reach);
            for (int at = i; bones[at].Moves; at = bones[at].Up) bones[i].Depth += Vector3.Distance(bones[at].Rest, bones[bones[at].Up].Rest);
            deepest = Mathf.Max(deepest, bones[i].Depth);
        }
        for (int i = 0; i < bones.Length; i++) bones[i].Depth = deepest > 0f ? bones[i].Depth / deepest : 0f;
        Length = longest * Mathf.Abs(anchor.lossyScale.x);
    }

    /// <summary>Notes how far each free bone hangs, as the part is made, from what it rests against.</summary>
    internal void MeasureRoom(System.Func<Vector3, float> room)
    {
        for (int i = 0; i < bones.Length; i++)
            if (bones[i].Moves) bones[i].Room = room(bones[i].Transform.position);
    }

    /// <summary>
    /// How near the part comes to something as it is made, given how far outside it a point is (below zero inside). With
    /// <paramref name="roots"/> the bones it hangs from count too: colliding line by line (a ring), Magica meets the line
    /// from a root to its first free bone, and a root stuck inside would lever the chain off its shape. Point by point,
    /// only the free bones meet anything.
    /// </summary>
    internal float Nearest(System.Func<Vector3, float> gap, bool roots)
    {
        float least = float.PositiveInfinity;
        for (int i = 0; i < bones.Length; i++)
            if (roots || bones[i].Moves) least = Mathf.Min(least, gap(bones[i].Transform.position));
        return least;
    }

    /// <summary>
    /// How thick the cloth is down the part, for Magica's particle radius: what was wanted (metres), shaped down the part
    /// by <paramref name="along"/> (shares by depth; null, the same all the way), but at each depth no more than the room
    /// its bones had when the part was made, so the legs don't push it out of shape before anything has moved, and never
    /// thinner than the presets made it unless less was wanted. The curve runs 0..1 of what was wanted. Null when nothing
    /// was.
    /// </summary>
    internal AnimationCurve Thickness(float wanted, AnimationCurve along = null)
    {
        if (wanted <= 0f) return null;
        var least = new SortedDictionary<float, float>();
        for (int i = 0; i < bones.Length; i++)
        {
            if (!bones[i].Moves) continue;
            float depth = Mathf.Round(bones[i].Depth * 100f) / 100f;
            float want = along != null ? wanted * Mathf.Max(0f, along.Evaluate(bones[i].Depth)) : wanted;
            float thick = Mathf.Clamp(bones[i].Room * RoomShare, Mathf.Min(LeastThickness, want), want) / wanted;
            least[depth] = least.TryGetValue(depth, out float before) ? Mathf.Min(before, thick) : thick;
        }
        if (least.Count == 0) return null;
        var keys = new List<Keyframe>();
        foreach (var pair in least)
        {
            if (keys.Count == 0 && pair.Key > 0f) keys.Add(new Keyframe(0f, pair.Value));
            keys.Add(new Keyframe(pair.Key, pair.Value));
        }
        // Straight lines between the depths, as the bones between them are.
        return Straight(keys);
    }

    /// <summary>
    /// Each free bone's particle for a chain that meets the body as measured (DressedBody). Its maker's PhysBone radius
    /// (<paramref name="scale"/> along <paramref name="along"/>), but never thicker than the room its bone has as drawn
    /// (<paramref name="room"/>: how far a point is from what it rests against), so nothing starts pushed off where its
    /// maker drew it: the shapes are fitted to the chain's thinnest particle, and a thicker one used to start inside its
    /// shape and be shoved out at once (on Mayo her side locks 5 and 8 mm off her bust, her White Board jacket's front 3
    /// and its belt 6, Ichigo's side hair 3 to 6). Where what it rests against lies on the body's real surface
    /// (<paramref name="rounding"/>: the chest's soft lump nearest it, that lump's rounding; below zero elsewhere), it is
    /// as thick as its own cloth reaches toward the body (<paramref name="reach"/>, below zero where that couldn't be
    /// measured: the bone above's is taken), plus the dip of a straight segment between two particles lying on that curve
    /// (its length squared over eight times the curve's radius, times <paramref name="dip"/>), so the cloth between them
    /// meets the curve rather than cutting into it. A PhysBone's radius is sized for VRChat's colliders, which sit inside
    /// the body: on Mayo's bust, measured to its real surface, it held her side locks 17 mm out where their cloth reaches 2
    /// to 5 mm toward her. Elsewhere the shapes are rougher and the maker's thickness keeps holding the cloth off them.
    /// Magica gives one radius to each depth, the least the bones there need, which is each bone's Radius after.
    /// </summary>
    internal void FitRadii(float scale, AnimationCurve along, System.Func<Transform, float> reach, System.Func<Vector3, float> room,
        System.Func<Vector3, float> rounding, float dip)
    {
        var own = new float[bones.Length];
        var least = new SortedDictionary<float, float>();
        for (int i = 0; i < bones.Length; i++)
        {
            own[i] = -1f;
            if (!bones[i].Moves) continue;
            float cloth = reach(bones[i].Transform);
            if (cloth < 0f) cloth = own[bones[i].Up];
            own[i] = cloth;
            Vector3 at = bones[i].Transform.position;
            float maker = scale * (along != null ? Mathf.Max(0f, along.Evaluate(bones[i].Depth)) : 1f);
            float wanted = Mathf.Min(maker, room(at));
            float bend = cloth >= 0f ? rounding(at) : -1f;
            if (bend >= 0f)
            {
                float span = Vector3.Distance(at, bones[bones[i].Up].Transform.position);
                if (bones[i].Down >= 0) span = Mathf.Max(span, Vector3.Distance(at, bones[bones[i].Down].Transform.position));
                float curve = bend + cloth;
                float sag = curve > 0f ? dip * span * span / (8f * curve) : 0f;
                wanted = Mathf.Min(wanted, cloth + sag);
            }
            wanted = Mathf.Max(LeastParticle, wanted);
            float depth = Mathf.Round(bones[i].Depth * 100f) / 100f;
            float share = scale > 0f ? wanted / scale : 0f;
            least[depth] = least.TryGetValue(depth, out float before) ? Mathf.Min(before, share) : share;
        }
        if (least.Count == 0 || scale <= 0f) return;
        var keys = new List<Keyframe>();
        foreach (var pair in least)
        {
            if (keys.Count == 0 && pair.Key > 0f) keys.Add(new Keyframe(0f, pair.Value));
            keys.Add(new Keyframe(pair.Key, pair.Value));
        }
        FittedRadius = Straight(keys);
        FittedScale = scale;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i].Moves) bones[i].Radius = scale * Mathf.Max(0f, FittedRadius.Evaluate(bones[i].Depth));
    }

    /// <summary>
    /// Lays a chain that hangs onto what is under it, as gravity would: from the root down, a joint holding just one bone
    /// turns it toward straight down (<paramref name="down"/>) until the bone's particle touches what it may rest on
    /// (<paramref name="onto"/>: how far outside that a point is), within that depth's angle limit (<paramref name="limit"/>,
    /// degrees), never past straight down and moving the bone no farther than <paramref name="reach"/> (metres). The bone
    /// keeps its own turn, so what hangs below it keeps the shape its maker drew and only comes along. A bone that touches
    /// nothing within that, or touches something else it meets first (<paramref name="gap"/>: all it meets), stays as
    /// drawn: a strand drawn in the air, or standing out, keeps its shape, and none is laid on a shape that doesn't lie
    /// on the body's real surface. VRChat makers draw hair a little off the body and hold it there with a limit cone
    /// tilted outward; Magica's cones can't tilt, and the chain rests as drawn while the character stands (VrcSway), so
    /// Mayo's side locks hung 15 to 22 mm in front of her bust. The turns are the pose the cloth rests in from then on:
    /// Magica takes the bones' turns as it is built, so this runs just before, once the shapes are fitted. Radius must be
    /// set (FitRadii).
    /// </summary>
    internal void Settle(System.Func<Vector3, float> gap, System.Func<Vector3, float> onto, Vector3 down, System.Func<float, float> limit,
        float reach)
    {
        if (reach <= 0f || down.sqrMagnitude < 1e-12f) return;
        for (int i = 0; i < bones.Length; i++)
        {
            if (!bones[i].Moves || bones[i].Radius <= 0f) continue;
            Transform joint = bones[bones[i].Up].Transform, bone = bones[i].Transform;
            // A joint holding more than this bone would carry the others along.
            if (joint.childCount != 1) continue;
            float radius = bones[i].Radius;
            if (gap(bone.position) <= radius) continue;
            Vector3 segment = bone.position - joint.position;
            Vector3 axis = Vector3.Cross(segment, down);
            float most = Mathf.Min(Vector3.Angle(segment, down), limit(bones[i].Depth));
            // Turning by an angle moves the bone 2 sin(angle/2) of the segment: no farther than reach.
            float length = segment.magnitude;
            if (length > 1e-6f) most = Mathf.Min(most, 2f * Mathf.Asin(Mathf.Clamp01(reach / (2f * length))) * Mathf.Rad2Deg);
            if (most < SettleStep || axis.sqrMagnitude < 1e-12f) continue;
            axis.Normalize();
            float touch = -1f;
            for (float angle = SettleStep; ; angle = Mathf.Min(most, angle + SettleStep))
            {
                Vector3 there = joint.position + Quaternion.AngleAxis(angle, axis) * segment;
                if (onto(there) <= radius)
                {
                    touch = angle;
                    break;
                }
                if (gap(there) <= radius || angle >= most) break;
            }
            if (touch < 0f) continue;
            float clear = Mathf.Max(0f, touch - SettleStep), touching = touch;
            for (int k = 0; k < SettleSearch; k++)
            {
                float middle = (clear + touching) * .5f;
                if (onto(joint.position + Quaternion.AngleAxis(middle, axis) * segment) <= radius) touching = middle;
                else clear = middle;
            }
            Quaternion keep = bone.rotation;
            joint.rotation = Quaternion.AngleAxis(clear, axis) * joint.rotation;
            bone.rotation = keep;
        }
    }

    /// <summary>A curve through these keys (in order of depth) with straight lines between them.</summary>
    internal static AnimationCurve Straight(List<Keyframe> keys)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            key.inTangent = i > 0 && key.time > keys[i - 1].time ? (key.value - keys[i - 1].value) / (key.time - keys[i - 1].time) : 0f;
            key.outTangent = i + 1 < keys.Count && keys[i + 1].time > key.time ? (keys[i + 1].value - key.value) / (keys[i + 1].time - key.time) : 0f;
            keys[i] = key;
        }
        return new AnimationCurve(keys.ToArray());
    }

    /// <summary>
    /// A part's bones as its cloth takes them: the root and everything under it, but not what an outfit hung on a bone
    /// below the root (OutfitBone), which goes to <paramref name="foreign"/> with all under it. Those are another row's:
    /// the outfit's. Magica would take them for particles of this part as well (it takes every transform under a root),
    /// so ClothPart.Wear marks them out of the part, and they aren't sent or measured as this part's either. A root
    /// that is itself an outfit's bone is that outfit's row and keeps everything under it. Either list may be null.
    /// </summary>
    internal static void Collect(Transform root, ICollection<Transform> own, ICollection<Transform> foreign)
    {
        own?.Add(root);
        foreach (Transform child in root)
        {
            if (!child.TryGetComponent<OutfitBone>(out _)) Collect(child, own, foreign);
            else if (foreign != null)
                foreach (var hung in child.GetComponentsInChildren<Transform>(true)) foreign.Add(hung);
        }
    }

    private void Add(Transform bone, int up, int root, List<Bone> list)
    {
        int index = list.Count;
        Vector3 rest = anchor.InverseTransformPoint(bone.position);
        list.Add(new Bone
        {
            Transform = bone, Up = up, Down = -1, Moves = up >= 0, Rest = rest, Now = rest, Wanted = rest,
            RestTurn = Quaternion.Inverse(anchor.rotation) * bone.rotation, Turn = up >= 0 ? 1f : rootTurn,
            Reach = Mathf.Max(LeastReach, Vector3.Distance(rest, list.Count > root ? list[root].Rest : rest))
        });
        foreach (Transform child in bone)
        {
            // What an outfit hung here moves with the outfit's own rows (Collect).
            if (child.TryGetComponent<OutfitBone>(out _)) continue;
            if (list[index].Down < 0)
            {
                var self = list[index];
                self.Down = list.Count;
                list[index] = self;
            }
            Add(child, index, root, list);
        }
    }

    // Along the chain from this bone: to the next one, or from the last one for the bone at the end.
    private Vector3 Aim(int index, bool resting)
    {
        ref var bone = ref bones[index];
        if (bone.Down >= 0) return Place(bone.Down, resting) - Place(index, resting);
        return bone.Up >= 0 ? Place(index, resting) - Place(bone.Up, resting) : Vector3.down;
    }

    private Vector3 Place(int index, bool resting) => resting ? bones[index].Rest : bones[index].Now;

    /// <summary>
    /// Writes where the moving bones are this moment into a body's whole pose, three numbers per bone (ClothPose.Pack)
    /// from bone <paramref name="at"/> on, each a share of its reach. Returns where the next part starts.
    /// </summary>
    internal int Write(sbyte[] pose, int at)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            ref var bone = ref bones[i];
            if (!bone.Moves) continue;
            int k = at++ * 3;
            if (k + 2 >= pose.Length) continue;
            Vector3 share = (anchor.InverseTransformPoint(bone.Transform.position) - bone.Rest) / bone.Reach;
            pose[k] = ClothPose.Pack(share.x);
            pose[k + 1] = ClothPose.Pack(share.y);
            pose[k + 2] = ClothPose.Pack(share.z);
        }
        return at;
    }

    /// <summary>
    /// Takes this part's bones from a body's whole pose, from bone <paramref name="at"/> on; those past the first
    /// <paramref name="carried"/> of the pose keep heading where they were. Returns where the next part starts.
    /// </summary>
    internal int Read(sbyte[] pose, int at, int carried)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            ref var bone = ref bones[i];
            if (!bone.Moves) continue;
            int k = at * 3;
            if (at++ < carried && k + 2 < pose.Length)
                bone.Wanted = bone.Rest + new Vector3(ClothPose.Unpack(pose[k]), ClothPose.Unpack(pose[k + 1]), ClothPose.Unpack(pose[k + 2])) * bone.Reach;
        }
        return at;
    }

    /// <summary>Moves toward the pose last reported. A copy runs no simulation, so nothing else touches these bones.</summary>
    internal void Follow(float rate, float dt)
    {
        float blend = 1f - Mathf.Exp(-rate * dt);
        for (int i = 0; i < bones.Length; i++)
            if (bones[i].Moves) bones[i].Now = Vector3.Lerp(bones[i].Now, bones[i].Wanted, blend);
        Quaternion frame = anchor.rotation;
        for (int i = 0; i < bones.Length; i++)
        {
            ref var bone = ref bones[i];
            if (bone.Turn <= 0f) continue;
            Quaternion turn = Quaternion.Slerp(bone.RestTurn, Quaternion.FromToRotation(bone.RestAim, Aim(i, false)) * bone.RestTurn, bone.Turn);
            if (bone.Moves) bone.Transform.SetPositionAndRotation(anchor.TransformPoint(bone.Now), frame * turn);
            else bone.Transform.rotation = frame * turn;
        }
    }
}
