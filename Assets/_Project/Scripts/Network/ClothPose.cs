using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Where the free bones of a player's clothes and hair are at one moment: three small numbers per bone, each a share of
/// how far that bone can go. A room sends one of these several times a second for each player, so it is kept small, and
/// only the bones in use go over the network.
/// </summary>
public struct ClothPose : INetworkSerializable
{
    private const float Scale = sbyte.MaxValue;
    private const int PerBone = 3;

    private FixedList512Bytes<sbyte> shifts;

    public int Count => shifts.Length / PerBone;

    public void Add(Vector3 share)
    {
        shifts.Add(Pack(share.x));
        shifts.Add(Pack(share.y));
        shifts.Add(Pack(share.z));
    }

    public Vector3 Shift(int bone)
    {
        int at = bone * PerBone;
        return new Vector3(shifts[at], shifts[at + 1], shifts[at + 2]) / Scale;
    }

    public bool Matches(ref ClothPose other)
    {
        if (shifts.Length != other.shifts.Length) return false;
        for (int i = 0; i < shifts.Length; i++)
            if (shifts[i] != other.shifts[i]) return false;
        return true;
    }

    private static sbyte Pack(float share) => (sbyte)Mathf.RoundToInt(Mathf.Clamp(share, -1f, 1f) * Scale);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        ushort length = (ushort)shifts.Length;
        serializer.SerializeValue(ref length);
        if (serializer.IsReader) shifts.Length = Mathf.Min(length, shifts.Capacity);
        for (int i = 0; i < shifts.Length; i++)
        {
            sbyte shift = shifts[i];
            serializer.SerializeValue(ref shift);
            if (serializer.IsReader) shifts[i] = shift;
        }
    }
}
