using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// One move of a gun's own made outside KINEMATION's clips (the shove's bash): how far the gun moves and turns off where
/// it is held, keyed over time, in the gun's own frame (x right, y up, z along the barrel; metres in game). The hands stay
/// where they hold the gun (FPSBody.Contacts), so the move fits any character and any gun of its kind.
/// </summary>
internal sealed class GunMove
{
    private readonly float[] times;
    private readonly Vector3[] moves;
    private readonly Quaternion[] turns;

    /// <summary>How long it plays (s), its last key.</summary>
    public float Length => times[times.Length - 1];
    /// <summary>When it strikes (s): the line "#impact=" of its file, else 0.</summary>
    public float Impact { get; }

    public GunMove(float[] times, Vector3[] moves, Quaternion[] turns, float impact)
    {
        this.times = times;
        this.moves = moves;
        this.turns = turns;
        Impact = impact;
    }

    /// <summary>The gun's move and turn <paramref name="time"/> seconds in, between its keys.</summary>
    public void Sample(float time, out Vector3 move, out Quaternion turn)
    {
        int last = times.Length - 1;
        if (time <= times[0] || last == 0) { move = moves[0]; turn = turns[0]; return; }
        if (time >= times[last]) { move = moves[last]; turn = turns[last]; return; }
        int lo = 0, hi = last;
        while (hi - lo > 1)
        {
            int middle = (lo + hi) / 2;
            if (times[middle] <= time) lo = middle; else hi = middle;
        }
        float t = Mathf.InverseLerp(times[lo], times[hi], time);
        move = Vector3.Lerp(moves[lo], moves[hi], t);
        turn = Quaternion.Slerp(turns[lo], turns[hi], t);
    }
}

/// <summary>
/// The gun moves the Blender shove workspace sends to the game (Tools/Blender/Shove, "게임에 보내기"):
/// Resources/Combat/GunMoves/&lt;name&gt;.csv, a header "time,x,y,z,qx,qy,qz,qw" and a row per sample, a line "#impact=0.067"
/// for the strike. Read on first use and again on GameTables.Reloaded, which the editor raises when Unity takes in a new
/// export (GunMovesWatcher) or on Tools > Data Tables > Reload Tables, so a new export plays at the next shove even in a
/// running game. A move that can't be read is reported once and plays nothing.
/// </summary>
internal static class GunMoves
{
    public const string Folder = "Combat/GunMoves/";
    private static readonly Dictionary<string, GunMove> cache = new();
    private static bool listening;

    /// <summary>The gun move named <paramref name="name"/>, or null when there is no such file or it holds no keys.</summary>
    public static GunMove Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!listening)
        {
            listening = true;
            GameTables.Reloaded += cache.Clear;
        }
        if (cache.TryGetValue(name, out var move)) return move;
        var file = Resources.Load<TextAsset>(Folder + name);
        move = file != null ? Parse(file.text, name) : null;
        if (move == null) Debug.LogWarning($"GunMoves: Resources/{Folder}{name}.csv is missing or holds no keys; that move plays nothing.");
        cache[name] = move;
        return move;
    }

    private static GunMove Parse(string text, string name)
    {
        var times = new List<float>();
        var moves = new List<Vector3>();
        var turns = new List<Quaternion>();
        float impact = 0f;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0) continue;
            if (line.StartsWith("#impact=", System.StringComparison.OrdinalIgnoreCase))
            {
                float.TryParse(line.Substring(8), NumberStyles.Float, CultureInfo.InvariantCulture, out impact);
                continue;
            }
            if (line[0] == '#' || char.IsLetter(line[0])) continue;
            string[] cells = line.Split(',');
            if (cells.Length < 8) continue;
            var values = new float[8];
            bool read = true;
            for (int i = 0; i < 8 && read; i++)
                read = float.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
            // Out of order or unreadable rows are left out rather than bending the curve.
            if (!read || (times.Count > 0 && values[0] <= times[times.Count - 1])) continue;
            var turn = new Quaternion(values[4], values[5], values[6], values[7]);
            float size = Mathf.Sqrt(turn.x * turn.x + turn.y * turn.y + turn.z * turn.z + turn.w * turn.w);
            if (size < 1e-4f) continue;
            times.Add(values[0]);
            moves.Add(new Vector3(values[1], values[2], values[3]));
            turns.Add(new Quaternion(turn.x / size, turn.y / size, turn.z / size, turn.w / size));
        }
        return times.Count > 0 ? new GunMove(times.ToArray(), moves.ToArray(), turns.ToArray(), impact) : null;
    }
}
