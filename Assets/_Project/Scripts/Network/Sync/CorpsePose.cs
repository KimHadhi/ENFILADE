using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>A ragdoll at one moment: where its hips are and how each part is turned. The rest hangs off those.</summary>
public struct CorpsePose : INetworkSerializeByMemcpy
{
    private const float Scale = short.MaxValue;
    private const int PerRotation = 4;

    /// <summary>False while the player is alive, so a new corpse never follows the last one's final pose.</summary>
    public bool Laid;
    public Vector3 Hips;
    private FixedList128Bytes<short> rotations;

    public int Parts => rotations.Length / PerRotation;

    public void Add(Quaternion rotation)
    {
        rotations.Add((short)(rotation.x * Scale));
        rotations.Add((short)(rotation.y * Scale));
        rotations.Add((short)(rotation.z * Scale));
        rotations.Add((short)(rotation.w * Scale));
    }

    public Quaternion Rotation(int part)
    {
        int at = part * PerRotation;
        return new Quaternion(rotations[at] / Scale, rotations[at + 1] / Scale, rotations[at + 2] / Scale, rotations[at + 3] / Scale).normalized;
    }
}
