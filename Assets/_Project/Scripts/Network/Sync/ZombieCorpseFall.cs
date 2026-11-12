using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A falling zombie corpse at one moment, as the PC that runs its physics sends it (ZombieCorpse): which enemy it was
/// (its network id), where the hips are, and each doll part's turn packed into four bytes (the three smallest parts of
/// the quaternion, ten bits each, about 0.1 degree). With 12 parts that is about 70 bytes; the network sends the
/// messages of one tick together. Rested marks the last one: the body lies still from there.
/// </summary>
public struct ZombieCorpseFall : INetworkSerializable
{
    private const float Root2 = 1.41421356f;
    private const uint TenBits = 1023u;

    public ulong Id;
    public Vector3 Hips;
    public bool Rested;
    private FixedList64Bytes<uint> turns;

    public ZombieCorpseFall(ulong id, in CorpsePose pose, bool rested)
    {
        Id = id;
        Hips = pose.Hips;
        Rested = rested;
        turns = default;
        for (int i = 0; i < pose.Parts && turns.Length < turns.Capacity; i++) turns.Add(Pack(pose.Rotation(i)));
    }

    /// <summary>The pose to follow (RagdollCorpse.Follow).</summary>
    public CorpsePose Pose
    {
        get
        {
            var pose = new CorpsePose { Laid = true, Hips = Hips };
            for (int i = 0; i < turns.Length; i++) pose.Add(Unpack(turns[i]));
            return pose;
        }
    }

    // Leave out the largest component (it follows from the others) and keep it positive, so the other three lie within
    // ±1/√2 and fit ten bits each; two bits say which one was left out.
    private static uint Pack(Quaternion turn)
    {
        float x = turn.x, y = turn.y, z = turn.z, w = turn.w;
        float ax = Mathf.Abs(x), ay = Mathf.Abs(y), az = Mathf.Abs(z), aw = Mathf.Abs(w);
        uint largest = 0u;
        float most = ax;
        if (ay > most) { largest = 1u; most = ay; }
        if (az > most) { largest = 2u; most = az; }
        if (aw > most) largest = 3u;
        float sign = (largest == 0u ? x : largest == 1u ? y : largest == 2u ? z : w) < 0f ? -1f : 1f;
        float a, b, c;
        switch (largest)
        {
            case 0u: a = y; b = z; c = w; break;
            case 1u: a = x; b = z; c = w; break;
            case 2u: a = x; b = y; c = w; break;
            default: a = x; b = y; c = z; break;
        }
        return largest << 30 | Ten(a * sign) << 20 | Ten(b * sign) << 10 | Ten(c * sign);
    }

    private static Quaternion Unpack(uint packed)
    {
        uint largest = packed >> 30;
        float a = Real(packed >> 20), b = Real(packed >> 10), c = Real(packed);
        float d = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));
        switch (largest)
        {
            case 0u: return new Quaternion(d, a, b, c);
            case 1u: return new Quaternion(a, d, b, c);
            case 2u: return new Quaternion(a, b, d, c);
            default: return new Quaternion(a, b, c, d);
        }
    }

    private static uint Ten(float value) => (uint)Mathf.RoundToInt(Mathf.Clamp01(value * Root2 * .5f + .5f) * TenBits);
    private static float Real(uint bits) => ((bits & TenBits) / (float)TenBits - .5f) * 2f / Root2;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Id);
        serializer.SerializeValue(ref Hips);
        serializer.SerializeValue(ref Rested);
        byte count = (byte)turns.Length;
        serializer.SerializeValue(ref count);
        if (serializer.IsReader) turns.Length = Mathf.Min(count, turns.Capacity);
        for (int i = 0; i < turns.Length; i++)
        {
            uint turn = turns[i];
            serializer.SerializeValue(ref turn);
            if (serializer.IsReader) turns[i] = turn;
        }
    }
}
