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
        float longest = 0f;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].Moves) Moving++;
            bones[i].RestAim = Aim(i, true);
            longest = Mathf.Max(longest, bones[i].Reach);
        }
        Length = longest * Mathf.Abs(anchor.lossyScale.x);
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
