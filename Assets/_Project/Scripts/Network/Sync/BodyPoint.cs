using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A place the PC that decided something saw it on (Docs/Records/SyncAudit.md, rule 1: the deciding PC sends the result,
/// the others place it, nobody judges it again). On a body it is a point and a facing in the space of the body's human
/// bone the hit met (a zombie, alive or a corpse, a player), so blood lands on the same wound on every PC whatever pose
/// that body has there; anywhere else it is the world's point. Every PC turns it back into its own bone and its own world
/// point (Resolve). A body that has gone on this PC resolves to nothing: the caller falls back to the world point the
/// event carries anyway.
/// </summary>
public struct BodyPoint : INetworkSerializable
{
    private const byte World = 0, OnBody = 1;
    // Bone: HumanBodyBones + 1, or 0 for the body's own root (a body with no human rig: a target, a prop).
    private const byte RootBone = 0;

    private byte kind, bone;
    private ulong target;
    private Vector3 point, normal;

    /// <summary>Somewhere on a body (rather than in the world).</summary>
    public bool IsOnBody => kind == OnBody;

    /// <summary>A point in the world, on nothing that moves.</summary>
    public static BodyPoint InWorld(Vector3 point, Vector3 normal) => new BodyPoint { kind = World, point = point, normal = normal };

    /// <summary>
    /// The place a hit met on what this collider belongs to, as this PC sees it: on its body's bone if it has one every PC
    /// knows (a networked body, or a corpse of one), otherwise the world's point.
    /// </summary>
    public static BodyPoint Of(Collider collider, Vector3 point, Vector3 normal)
    {
        if (collider != null && TryBody(collider, out ulong id, out byte bone, out Transform space))
            return new BodyPoint
            {
                kind = OnBody, target = id, bone = bone,
                point = space.InverseTransformPoint(point), normal = space.InverseTransformDirection(normal),
            };
        return InWorld(point, normal);
    }

    /// <summary>
    /// The point and facing on this PC, and the transform it is on (null for the world's). False when the body is gone here:
    /// the caller uses the world point its event carries.
    /// </summary>
    public bool Resolve(out Vector3 worldPoint, out Vector3 worldNormal, out Transform on)
    {
        if (kind == World)
        {
            worldPoint = point; worldNormal = normal; on = null;
            return true;
        }
        on = SpaceOf(target, bone);
        if (on == null)
        {
            worldPoint = default; worldNormal = default;
            return false;
        }
        worldPoint = on.TransformPoint(point);
        worldNormal = on.TransformDirection(normal);
        return true;
    }

    // A body's point is in its bone's space, within a metre or two of the bone: half floats hold it to a millimetre or so,
    // 12 bytes for point and facing. A world point keeps whole floats.
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref kind);
        if (kind == World)
        {
            serializer.SerializeValue(ref point);
            SerializeHalf(serializer, ref normal);
            return;
        }
        serializer.SerializeValue(ref target);
        serializer.SerializeValue(ref bone);
        SerializeHalf(serializer, ref point);
        SerializeHalf(serializer, ref normal);
    }

    private static void SerializeHalf<T>(BufferSerializer<T> serializer, ref Vector3 value) where T : IReaderWriter
    {
        ushort x = 0, y = 0, z = 0;
        if (serializer.IsWriter) { x = Mathf.FloatToHalf(value.x); y = Mathf.FloatToHalf(value.y); z = Mathf.FloatToHalf(value.z); }
        serializer.SerializeValue(ref x);
        serializer.SerializeValue(ref y);
        serializer.SerializeValue(ref z);
        if (serializer.IsReader) value = new Vector3(Mathf.HalfToFloat(x), Mathf.HalfToFloat(y), Mathf.HalfToFloat(z));
    }

    // ---- A hit's place on the body it was sent with (EnemyHitMessage) -----------------------------------------------

    /// <summary>
    /// The human bone of a body nearest a point, and the point in that bone's space: for a hit sent on that body's own
    /// message, which names the body already. The body is a living one (its rig under <paramref name="body"/>) or the
    /// corpse it left (by its network id). 0: neither has a human rig here, and the point is the world's.
    /// </summary>
    public static byte NearestBone(ulong id, Transform body, Vector3 point, out Vector3 local)
    {
        local = point;
        var bones = BonesOf(id, body);
        if (bones == null) return RootBone;
        int best = -1;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            float distance = (bones[i].position - point).sqrMagnitude;
            if (distance < nearest) { nearest = distance; best = i; }
        }
        if (best < 0) return RootBone;
        local = bones[best].InverseTransformPoint(point);
        return (byte)(best + 1);
    }

    /// <summary>The point back on this PC's copy of the body or its corpse (NearestBone). False: neither is here.</summary>
    public static bool FromBone(ulong id, Transform body, byte bone, Vector3 local, out Vector3 point)
    {
        point = local;
        if (bone == RootBone) return false;
        var bones = BonesOf(id, body);
        var space = bones != null && bone - 1 < bones.Length ? bones[bone - 1] : null;
        if (space == null) return false;
        point = space.TransformPoint(local);
        return true;
    }

    // A body's human bones: its living rig's, else those of the corpse it left here.
    private static Transform[] BonesOf(ulong id, Transform body)
    {
        var animator = body != null ? HumanRig(body) : null;
        if (animator != null)
        {
            var rig = RigOf(animator);
            if (rig.Ids.Count > 0) return rig.Bones;
        }
        return ZombieCorpse.BonesOf(id);
    }

    // ---- Finding the body and its bone ------------------------------------------------------------------------------

    // Per rig: its human bones by transform, so a hitbox anywhere under a bone finds it by walking up.
    private static readonly Dictionary<Animator, Rig> rigs = new Dictionary<Animator, Rig>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStatics() { rigs.Clear(); bodies.Clear(); }

    private static bool TryBody(Collider collider, out ulong id, out byte bone, out Transform space)
    {
        id = 0; bone = RootBone; space = null;
        // A corpse's doll: the human bone its part turns, on the body it lies over (ZombieCorpse keeps the zombie's id).
        var doll = collider.GetComponentInParent<RagdollCorpse>();
        if (doll != null)
        {
            if (!doll.BoneOf(collider, out var human, out var real) || !ZombieCorpse.IdOf(doll, out id)) return false;
            bone = (byte)((int)human + 1); space = real;
            return true;
        }
        var network = collider.GetComponentInParent<NetworkObject>();
        if (network == null || !network.IsSpawned) return false;
        id = network.NetworkObjectId;
        var rig = HumanRig(network.transform);
        if (rig != null && TryHumanBone(rig, collider.transform, out bone, out space)) return true;
        bone = RootBone; space = network.transform;
        return true;
    }

    private static Transform SpaceOf(ulong id, byte bone)
    {
        var network = GameSession.Network;
        if (network != null && network.SpawnManager != null && network.SpawnManager.SpawnedObjects.TryGetValue(id, out var found) && found != null)
        {
            if (bone == RootBone) return found.transform;
            var rig = HumanRig(found.transform);
            var bones = rig != null ? RigOf(rig) : null;
            if (bones != null && bones.Bones[bone - 1] != null) return bones.Bones[bone - 1];
        }
        // Gone as a zombie here: perhaps lying as its corpse already.
        return ZombieCorpse.BoneOf(id, bone == RootBone ? HumanBodyBones.LastBone : (HumanBodyBones)(bone - 1), bone == RootBone);
    }

    // The body's own human rig: its first human Animator (a body's model; the same prefab gives every PC the same one).
    // Kept per body, as every hit asks: a rig that has left its body (laid as a corpse) is looked for again.
    private static readonly Dictionary<Transform, Animator> bodies = new Dictionary<Transform, Animator>();
    private static readonly List<Animator> found = new List<Animator>();

    private static Animator HumanRig(Transform root)
    {
        if (bodies.TryGetValue(root, out var kept) && kept != null && kept.transform.IsChildOf(root)) return kept;
        if (bodies.Count > 512) PruneBodies();
        Animator rig = null;
        root.GetComponentsInChildren(true, found);
        foreach (var animator in found)
            if (animator != null && animator.isHuman && animator.avatar != null) { rig = animator; break; }
        found.Clear();
        if (rig != null) bodies[root] = rig;
        else bodies.Remove(root);
        return rig;
    }

    private static readonly List<Transform> goneBodies = new List<Transform>();
    private static void PruneBodies()
    {
        goneBodies.Clear();
        foreach (var pair in bodies) if (pair.Key == null || pair.Value == null) goneBodies.Add(pair.Key);
        foreach (var body in goneBodies) bodies.Remove(body);
        if (bodies.Count > 512) bodies.Clear();
    }

    // A rig's human bones, both ways, read the first time it is met (while its Animator is on, as a body lives).
    private sealed class Rig
    {
        public readonly Dictionary<Transform, byte> Ids = new Dictionary<Transform, byte>();
        public readonly Transform[] Bones = new Transform[(int)HumanBodyBones.LastBone];
    }

    private static Rig RigOf(Animator animator)
    {
        if (rigs.TryGetValue(animator, out var rig)) return rig;
        if (rigs.Count > 256) Prune();
        rig = new Rig();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            var transform = animator.GetBoneTransform((HumanBodyBones)i);
            if (transform == null) continue;
            rig.Bones[i] = transform;
            rig.Ids[transform] = (byte)(i + 1);
        }
        // A rig met with its Animator off reads no bones: not kept, so it is read again once it is on.
        if (rig.Ids.Count > 0) rigs[animator] = rig;
        return rig;
    }

    private static bool TryHumanBone(Animator animator, Transform from, out byte bone, out Transform space)
    {
        var rig = RigOf(animator);
        for (var at = from; at != null && at != animator.transform.parent; at = at.parent)
            if (rig.Ids.TryGetValue(at, out bone)) { space = at; return true; }
        bone = RootBone; space = null;
        return false;
    }

    private static readonly List<Animator> gone = new List<Animator>();
    private static void Prune()
    {
        gone.Clear();
        foreach (var rig in rigs.Keys) if (rig == null) gone.Add(rig);
        foreach (var rig in gone) rigs.Remove(rig);
        if (rigs.Count > 256) rigs.Clear();
    }
}
