using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// One paired execution (Tables/Executions.csv, a row per zombie state): a player clip and a zombie clip that share one
/// root, the zombie's feet. The game puts that root where the zombie is (ExecutionPairPlayer), the player's root at the
/// pair's offset from it, and plays both clips together on every PC; the kill lands at <see cref="Kill"/>. The Blender
/// execution workspace (Tools/Blender/Executions) writes the clips and the columns it measures; turn to fall are tuned by
/// hand. The pair's id is the zombie state it fits (ZombieBody.ExecutionState): back, front, sit, feed, crouch, prone,
/// supine.
/// </summary>
public sealed class ExecutionPair
{
    /// <summary>A hand's hold on one of the zombie's bones: from when to when, and where the wrist sits from that bone at
    /// the grab in the pair's own frame (x right, y up, z forward of the zombie's root).</summary>
    public readonly struct Contact
    {
        public readonly HumanBodyBones Bone;
        public readonly float From, To;
        public readonly Vector3 Offset;
        public readonly bool Valid;

        public Contact(HumanBodyBones bone, float from, float to, Vector3 offset)
        {
            Bone = bone; From = from; To = to; Offset = offset; Valid = to > from;
        }
    }

    public string Id { get; internal set; }
    /// <summary>Its row's place in the table, which every PC reads alike: what the network sends.</summary>
    public byte Index { get; internal set; }
    public bool Takedown { get; internal set; }
    public string PlayerClip { get; internal set; }
    public string ZombieClip { get; internal set; }
    public float Length { get; internal set; }
    public float Kill { get; internal set; }
    /// <summary>The player's root from the zombie's, in the zombie's frame (x right, z forward; metres).</summary>
    public Vector2 Offset { get; internal set; }
    /// <summary>Which way the player faces: degrees right of the way the zombie faces.</summary>
    public float Yaw { get; internal set; }
    public float Turn { get; internal set; }
    public float Align { get; internal set; }
    public float BlendIn { get; internal set; }
    public float BlendOut { get; internal set; }
    public HumanBodyBones HitBone { get; internal set; }
    /// <summary>Which way the dead body is sent, in the zombie's frame (x right, y up, z forward).</summary>
    public Vector3 Fall { get; internal set; }
    /// <summary>The zombie clip's first frame: its hips from its root (x right, z forward) and its head's way from them
    /// (degrees right of forward), to lay the clip over a body lying knocked down.</summary>
    public Vector2 StartHips { get; internal set; }
    public float StartHeadYaw { get; internal set; }
    public Contact LeftHand { get; internal set; }
    public Contact RightHand { get; internal set; }
}

/// <summary>The table of paired executions, read once and again whenever the tables reload (saving the CSV in Play).</summary>
public static class ExecutionPairs
{
    public const string TableName = "Executions";
    private static readonly List<ExecutionPair> pairs = new List<ExecutionPair>();
    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        pairs.Clear();
        loaded = false;
        GameTables.Reloaded -= Forget;
    }

    private static void Forget() => loaded = false;

    /// <summary>The pair for a zombie state (its row id), or null.</summary>
    public static ExecutionPair Find(string id)
    {
        Load();
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var pair in pairs)
            if (string.Equals(pair.Id, id, StringComparison.OrdinalIgnoreCase)) return pair;
        return null;
    }

    /// <summary>The pair another PC named by its index.</summary>
    public static ExecutionPair At(byte index)
    {
        Load();
        return index < pairs.Count ? pairs[index] : null;
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
        pairs.Clear();
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
            // A row the workspace never sent (no clips, no length) is no pair yet: that state keeps the old execution. The
            // Knife row only seats the Blender workspace's preview knife.
            float length = Number(row, "length", 0f);
            if (length <= 0f || pairs.Count >= byte.MaxValue) continue;
            pairs.Add(new ExecutionPair
            {
                Id = id,
                Index = (byte)pairs.Count,
                Takedown = string.Equals(Text(row, "kind"), "takedown", StringComparison.OrdinalIgnoreCase),
                PlayerClip = Text(row, "playerClip"),
                ZombieClip = Text(row, "zombieClip"),
                Length = length,
                Kill = Mathf.Clamp(Number(row, "kill", length * .4f), 0f, length),
                Offset = new Vector2(Number(row, "right", 0f), Number(row, "forward", -.5f)),
                Yaw = Number(row, "yaw", 0f),
                Turn = Mathf.Clamp01(Number(row, "turn", 0f)),
                Align = Mathf.Max(.01f, Number(row, "align", .2f)),
                BlendIn = Mathf.Max(.01f, Number(row, "blendIn", .12f)),
                BlendOut = Mathf.Max(.01f, Number(row, "blendOut", .25f)),
                HitBone = Bone(Text(row, "hitBone"), HumanBodyBones.Neck),
                Fall = new Vector3(Number(row, "fallRight", 0f), Number(row, "fallUp", 0f), Number(row, "fallForward", 1f)),
                StartHips = new Vector2(Number(row, "zHipsRight", 0f), Number(row, "zHipsForward", 0f)),
                StartHeadYaw = Number(row, "zHeadYaw", 0f),
                LeftHand = Hold(row, "l"),
                RightHand = Hold(row, "r"),
            });
        }
    }

    private static ExecutionPair.Contact Hold(IReadOnlyDictionary<string, string> row, string prefix)
    {
        string bone = Text(row, prefix + "Bone");
        if (string.IsNullOrEmpty(bone)) return default;
        return new ExecutionPair.Contact(Bone(bone, HumanBodyBones.Head), Number(row, prefix + "From", 0f), Number(row, prefix + "To", 0f),
            new Vector3(Number(row, prefix + "Right", 0f), Number(row, prefix + "Up", 0f), Number(row, prefix + "Forward", 0f)));
    }

    /// <summary>
    /// A bone by its Synty name (the Blender workspace's: Head, Neck, Spine_03 ...) or its Unity humanoid one (Chest ...),
    /// so the table means the same bone on any humanoid zombie body.
    /// </summary>
    public static HumanBodyBones Bone(string name, HumanBodyBones fallback)
    {
        switch ((name ?? "").Trim())
        {
            case "Head": return HumanBodyBones.Head;
            case "Neck": return HumanBodyBones.Neck;
            case "Spine_03": case "UpperChest": return HumanBodyBones.UpperChest;
            case "Spine_02": case "Chest": return HumanBodyBones.Chest;
            case "Spine_01": case "Spine": return HumanBodyBones.Spine;
            case "Hips": return HumanBodyBones.Hips;
        }
        return Enum.TryParse(name, true, out HumanBodyBones bone) ? bone : fallback;
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string column)
        => row != null && row.TryGetValue(column, out string value) ? value.Trim() : "";

    private static float Number(IReadOnlyDictionary<string, string> row, string column, float fallback)
        => row != null && row.TryGetValue(column, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            && !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback;
}
