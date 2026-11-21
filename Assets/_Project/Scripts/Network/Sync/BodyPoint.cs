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
    /// <summary>The body's network id (a corpse keeps its zombie's), for a point on one.</summary>
    public ulong Target => target;

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

    // ---- Finding the body and its bone ------------------------------------------------------------------------------

    // Per rig: its human bones by transform, so a hitbox anywhere under a bone finds it by walking up.
    private static readonly Dictionary<Animator, Rig> rigs = new Dictionary<Animator, Rig>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStatics() => rigs.Clear();

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
    private static Animator HumanRig(Transform root)
    {
        foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            if (animator != null && animator.isHuman && animator.avatar != null) return animator;
        return null;
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
        return rigs[animator] = rig;
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
