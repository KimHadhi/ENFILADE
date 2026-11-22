using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// A move that goes over the ground as its clip does: how far and at what pace are the clip's own, baked off its root by
/// ClipBench (Tables/Player/GaitMoves.csv kind travel, GaitMoveBake). The landing rolls had it first (0.9.288), each with its
/// clip squeezed into a time of the move's own before; the step dodge and the slide's dive roll go by it too. One object is
/// one move under way. A clip that throws its body further in a frame than the most speed it is begun with owes the rest and
/// pays it in the frames after: BIPER's QuickShift covers 1.2 m in a thirtieth of a second, a jump cut seen from the eyes.
/// </summary>
public sealed class ClipTravel
{
    private const string Table = "GaitMoves";

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

    /// <summary>The way of <paramref name="clip"/> from its moment <paramref name="from"/> (s), never faster than
    /// <paramref name="mostSpeed"/> (m/s); the whole clip <paramref name="distance"/> (m) far, or as far as its own body goes
    /// (0). False for a clip with no baked travel.</summary>
    public bool Begin(string clip, float from, float mostSpeed, float distance = 0f)
    {
        Clear(from, mostSpeed);
        if (string.IsNullOrEmpty(clip) || !GameTables.TryGetRow(Table, clip, out var row) || row.Text("kind") != "travel") return false;
        string[] cells = row.Text("profile").Split(';');
        var read = new float[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            if (!float.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out read[i])) return false;
        length = row.Number("length", 0f);
        Up = row.Number("land", length);
        Distance = distance > 0f ? distance : row.Number("metres", 0f);
        if (read.Length < 2 || length <= 0f || Up <= 0f || Distance <= 0f) { Clear(from, mostSpeed); return false; }
        gone = read;
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

    // The share of the way gone at this moment of the clip.
    private float GoneAt(float time)
    {
        if (gone.Length < 2 || length <= 0f) return 1f;
        float at = Mathf.Clamp01(time / length) * (gone.Length - 1);
        int low = Mathf.Min(gone.Length - 2, Mathf.FloorToInt(at));
        return Mathf.Lerp(gone[low], gone[low + 1], at - low);
    }
}
