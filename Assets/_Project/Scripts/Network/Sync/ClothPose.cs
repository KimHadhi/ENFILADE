using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Where some of the free bones of a player's clothes and hair are at one moment: one run of the body's whole pose, three
/// small numbers per bone, each a share of how far that bone can go. A body's pose goes out in as many runs as it takes
/// (PlayerNetworkCloth): Netcode throws on an unreliable message over 1296 bytes (NetworkMessageManager's
/// NonFragmentedMessageMaxSize), and one pose of Mayo's own sway is 208 bones, Ichigo's 307, and an outfit adds 38-61
/// more. When the whole pose had to fit one list of 510 numbers (170 bones), theirs overflowed on every send from 0.8.44
/// on, so no PC ever saw their cloth move. Each run says where it starts, how many bones the whole pose has (a copy not
/// wearing the same yet can tell) and its turn among that run's sends (a late one can't undo a newer one).
/// </summary>
public struct ClothPose : INetworkSerializable
{
    /// <summary>The most bones one run carries: 480 bytes, under half a packet with the message around it.</summary>
    public const int RunBones = 160;
    /// <summary>The most bones a body's pose can carry at all: where a run starts is two bytes.</summary>
    public const int MostBones = ushort.MaxValue;
    private const float Scale = sbyte.MaxValue;
    private const int PerBone = 3;

    public ushort Start, Total, Turn;
    private FixedList512Bytes<sbyte> shifts;

    public int Count => shifts.Length / PerBone;

    /// <summary>
    /// The run of <paramref name="count"/> bones from bone <paramref name="start"/> of a whole pose packed three numbers
    /// per bone, as the <paramref name="turn"/>th send of that run of a pose of <paramref name="total"/> bones.
    /// </summary>
    public ClothPose(sbyte[] pose, int start, int count, int total, ushort turn)
    {
        Start = (ushort)Mathf.Clamp(start, 0, MostBones);
        Total = (ushort)Mathf.Clamp(total, 0, MostBones);
        Turn = turn;
        shifts = default;
        int from = Start * PerBone;
        int end = Mathf.Min(pose.Length, from + Mathf.Clamp(count, 0, RunBones) * PerBone);
        for (int i = from; i < end; i++) shifts.Add(pose[i]);
    }

    /// <summary>Puts this run's numbers into a whole pose packed three per bone, as far as it reaches.</summary>
    public void CopyTo(sbyte[] pose)
    {
        int at = Start * PerBone;
        for (int i = 0; i < shifts.Length && at + i < pose.Length; i++) pose[at + i] = shifts[i];
    }

    /// <summary>How many runs a pose of this many bones takes.</summary>
    public static int Runs(int bones) => (Mathf.Max(0, bones) + RunBones - 1) / RunBones;

    /// <summary>Whether a run's turn comes after another's, counting round past 65535.</summary>
    public static bool After(ushort turn, ushort than) => (short)(turn - than) > 0;

    /// <summary>A share of a bone's reach (-1..1) as one number.</summary>
    public static sbyte Pack(float share) => (sbyte)Mathf.RoundToInt(Mathf.Clamp(share, -1f, 1f) * Scale);

    /// <summary>One number back as a share of a bone's reach.</summary>
    public static float Unpack(sbyte shift) => shift / Scale;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Start);
        serializer.SerializeValue(ref Total);
        serializer.SerializeValue(ref Turn);
        ushort length = (ushort)shifts.Length;
        serializer.SerializeValue(ref length);
        if (serializer.IsReader) shifts.Length = Mathf.Min(length, shifts.Capacity);
        // Every number written is read, even past what fits here, so the message is never read out of step.
        for (int i = 0; i < length; i++)
        {
            sbyte shift = i < shifts.Length ? shifts[i] : (sbyte)0;
            serializer.SerializeValue(ref shift);
            if (serializer.IsReader && i < shifts.Length) shifts[i] = shift;
        }
    }
}
