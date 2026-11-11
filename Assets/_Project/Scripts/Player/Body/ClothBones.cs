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
    }

    private readonly Transform anchor;
    private readonly Bone[] bones;
    private readonly float rootTurn;

    /// <summary>How many bones a pose of this part carries.</summary>
    internal int Moving { get; }
    /// <summary>How far the part hangs at the longest, in metres.</summary>
    internal float Length { get; }

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
    /// How near the part comes to something as it is made, given how far outside it a point is (below zero inside). The
    /// bones it hangs from count too: Magica collides the line from a root to its first free bone, and a root stuck
    /// inside would lever the chain off its shape.
    /// </summary>
    internal float Nearest(System.Func<Vector3, float> gap)
    {
        float least = float.PositiveInfinity;
        for (int i = 0; i < bones.Length; i++) least = Mathf.Min(least, gap(bones[i].Transform.position));
        return least;
    }

    /// <summary>
    /// How thick the cloth is down the part, for Magica's particle radius: what was wanted (metres), but at each depth no
    /// more than the room its bones had when the part was made, so the legs don't push it out of shape before anything has
    /// moved, and never thinner than the presets made it. The curve runs 0..1 of what was wanted. Null when nothing was.
    /// </summary>
    internal AnimationCurve Thickness(float wanted)
    {
        if (wanted <= 0f) return null;
        var least = new SortedDictionary<float, float>();
        for (int i = 0; i < bones.Length; i++)
        {
            if (!bones[i].Moves) continue;
            float depth = Mathf.Round(bones[i].Depth * 100f) / 100f;
            float thick = Mathf.Clamp(bones[i].Room * RoomShare, Mathf.Min(LeastThickness, wanted), wanted) / wanted;
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
        for (int i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            key.inTangent = i > 0 ? (key.value - keys[i - 1].value) / (key.time - keys[i - 1].time) : 0f;
            key.outTangent = i + 1 < keys.Count ? (keys[i + 1].value - key.value) / (keys[i + 1].time - key.time) : 0f;
            keys[i] = key;
        }
        return new AnimationCurve(keys.ToArray());
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

    /// <summary>Adds where the moving bones are this moment, each as a share of its reach.</summary>
    internal void Write(ref ClothPose pose)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            ref var bone = ref bones[i];
            if (bone.Moves) pose.Add((anchor.InverseTransformPoint(bone.Transform.position) - bone.Rest) / bone.Reach);
        }
    }

    /// <summary>Takes this part's bones from a pose, starting at that bone of it. Returns where the next part starts.</summary>
    internal int Read(ref ClothPose pose, int at)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            ref var bone = ref bones[i];
            if (bone.Moves) bone.Wanted = bone.Rest + pose.Shift(at++) * bone.Reach;
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
