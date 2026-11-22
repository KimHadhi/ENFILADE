using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// A move that goes over the ground as its clip does: how far and at what pace are the clip's own, baked off its root by
/// ClipBench (Tables/Player/GaitMoves.csv kind travel, GaitMoveBake). The landing rolls had it first (0.9.288), each with its
/// clip squeezed into a time of the move's own before; the step dodge and the slide's dive roll go by it too, and the slide
/// (SlidePace) and the clips that end running (PlayerBody.RunOutPace) by its pace alone (PaceAt). One object is one move
/// under way. A clip that throws its body further in a frame than the most speed it is begun with owes the rest and
/// pays it in the frames after: BIPER's QuickShift covers 1.2 m in a thirtieth of a second, a jump cut seen from the eyes.
/// </summary>
public sealed class ClipTravel
{
    private const string Table = "GaitMoves";

    // A clip's pace is read over this long either side of its moment (s): its body swings to and fro over its feet, and the
    // player goes as it goes on the whole.
    private const float PaceOver = .08f;

    private sealed class Way
    {
        public float Length, Up, Metres;
        public float[] Gone;
    }

    private static readonly Dictionary<string, Way> ways = new Dictionary<string, Way>(StringComparer.OrdinalIgnoreCase);

    static ClipTravel() => GameTables.Reloaded += ways.Clear;

    /// <summary>How fast a clip's body goes over the ground at its moment (m/s); 0 for a clip with no baked travel.</summary>
    public static float PaceAt(string clip, float time)
    {
        var way = WayOf(clip);
        if (way == null) return 0f;
        float from = Mathf.Clamp(time - PaceOver, 0f, way.Length), to = Mathf.Clamp(time + PaceOver, 0f, way.Length);
        return to - from > 1e-3f ? way.Metres * (ShareAt(way.Gone, way.Length, to) - ShareAt(way.Gone, way.Length, from)) / (to - from) : 0f;
    }

    /// <summary>How long a clip is (s); 0 for a clip with no baked travel.</summary>
    public static float LengthOf(string clip) => WayOf(clip)?.Length ?? 0f;
    /// <summary>The clip's moment its body is back up (s); 0 for a clip with no baked travel.</summary>
    public static float UpOf(string clip) => WayOf(clip)?.Up ?? 0f;

    private static Way WayOf(string clip)
    {
        if (string.IsNullOrEmpty(clip)) return null;
        if (!ways.TryGetValue(clip, out var way)) ways[clip] = way = Read(clip);
        return way;
    }

    private static Way Read(string clip)
    {
        if (!GameTables.TryGetRow(Table, clip, out var row) || row.Text("kind") != "travel") return null;
        string[] cells = row.Text("profile").Split(';');
        var way = new Way { Length = row.Number("length", 0f), Metres = row.Number("metres", 0f), Gone = new float[cells.Length] };
        way.Up = row.Number("land", way.Length);
        for (int i = 0; i < cells.Length; i++)
            if (!float.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out way.Gone[i])) return null;
        return cells.Length >= 2 && way.Length > 0f && way.Metres > 0f ? way : null;
    }

    private float[] gone = Array.Empty<float>();
    private float length, most, owed;

    /// <summary>The clip's moment the body is at (s).</summary>
    public float Time { get; private set; }
    /// <summary>The clip's moment its body is back up on its feet (s).</summary>
    public float Up { get; private set; }
    /// <summary>How far the whole clip goes (m).</summary>
    public float Distance { get; private set; }
    /// <summary>The share of the way gone (0-1), not counting what is owed.</summary>
    public float Share => Distance > 0f ? Mathf.Clamp01(GoneAt(Time) - owed / Distance) : 1f;

    /// <summary>The way of <paramref name="clip"/> from its moment <paramref name="from"/> (s), as far as its own body goes and
    /// never faster than <paramref name="mostSpeed"/> (m/s). False for a clip with no baked travel.</summary>
    public bool Begin(string clip, float from, float mostSpeed)
    {
        Clear(from, mostSpeed);
        var way = WayOf(clip);
        if (way == null || way.Up <= 0f) return false;
        length = way.Length;
        Up = way.Up;
        Distance = way.Metres;
        gone = way.Gone;
        return true;
    }

    /// <summary>A way at one even pace, for a clip with no baked travel.</summary>
    public void BeginPlain(float distance, float seconds, float from, float mostSpeed)
    {
        Clear(from, mostSpeed);
        gone = new[] { 0f, 1f };
        length = Up = Mathf.Max(.05f, seconds);
        Distance = Mathf.Max(0f, distance);
    }

    private void Clear(float from, float mostSpeed)
    {
        gone = Array.Empty<float>();
        length = Up = Distance = owed = 0f;
        most = mostSpeed > 0f ? mostSpeed : float.PositiveInfinity;
        Time = Mathf.Max(0f, from);
    }

    /// <summary>How far the body goes in this step (m); the clip's moment goes on with it.</summary>
    public float Go(float dt)
    {
        if (dt <= 0f) return 0f;
        float before = GoneAt(Time);
        Time += dt;
        float want = Distance * (GoneAt(Time) - before) + owed;
        float go = Mathf.Min(want, most * dt);
        owed = want - go;
        return go;
    }

    /// <summary>The clip's moment goes on while the body stays (stopped by a wall): what it would have gone is let go.</summary>
    public void Pass(float dt)
    {
        Time += Mathf.Max(0f, dt);
        owed = 0f;
    }

    private float GoneAt(float time) => ShareAt(gone, length, time);

    // The share of the way gone at this moment of a clip.
    private static float ShareAt(float[] gone, float length, float time)
    {
        if (gone.Length < 2 || length <= 0f) return 1f;
        float at = Mathf.Clamp01(time / length) * (gone.Length - 1);
        int low = Mathf.Min(gone.Length - 2, Mathf.FloorToInt(at));
        return Mathf.Lerp(gone[low], gone[low + 1], at - low);
    }
}
