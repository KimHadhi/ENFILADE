using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Visible randomness that every PC draws the same (the user, 2026-11-21: "what I see is what others see";
/// Docs/Records/SyncAudit.md, rule 2). An event every PC shows (a shot, a hit, a kill, a swing) carries one seed, made
/// once by the PC that decided it and sent with it. Every PC runs that event's look and sound inside Scope(seed), and
/// every draw made there comes from that seed, so the same clip, picture, spray, spin and limb speed come out
/// everywhere, for four bytes on the wire. Outside a scope the draws fall back to UnityEngine.Random, as before.
/// Each place in the code that draws has a stream of its own within the event (its file and line pick it), so what one
/// PC alone draws (its own camera's kick on the shooter's PC) never shifts what the next effect draws: the n-th draw at a
/// place is the same on every PC that makes it. Scopes nest. Only for how a decided thing looks and sounds; what happens
/// (damage, who, where) is decided once and sent.
/// </summary>
public static class SharedRandom
{
    // The scopes open now, innermost on top: each event's seed and its places' streams.
    private struct Frame
    {
        public uint Seed;
        public Dictionary<int, uint> Places;
    }

    private static readonly Stack<Frame> frames = new Stack<Frame>();
    private static readonly Stack<Dictionary<int, uint>> spare = new Stack<Dictionary<int, uint>>();

    /// <summary>Inside an event's scope: draws are the event's, the same on every PC.</summary>
    public static bool InScope => frames.Count > 0;

    /// <summary>Closes the scope it was opened with.</summary>
    public readonly struct Handle : IDisposable
    {
        private readonly bool open;
        internal Handle(bool open) { this.open = open; }
        public void Dispose() { if (open) Exit(); }
    }

    /// <summary>Draws come from <paramref name="seed"/> until the handle is disposed. Seed 0: no scope.</summary>
    public static Handle Scope(uint seed)
    {
        if (seed == 0u) return new Handle(false);
        frames.Push(new Frame { Seed = seed, Places = spare.Count > 0 ? spare.Pop() : new Dictionary<int, uint>() });
        return new Handle(true);
    }

    private static void Exit()
    {
        if (frames.Count == 0) return;
        var frame = frames.Pop();
        frame.Places.Clear();
        spare.Push(frame.Places);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        while (frames.Count > 0) Exit();
    }

    // ---- Seeds: from facts every PC holds the same ----

    /// <summary>This PC's client id in a room, 0 alone: with an event's own local number it names the event everywhere.</summary>
    public static ulong LocalClient
    {
        get
        {
            var network = GameSession.Network;
            return network != null && network.IsListening ? network.LocalClientId : 0UL;
        }
    }

    /// <summary>A seed from numbers every PC knows the same (never 0).</summary>
    public static uint Seed(ulong a, ulong b = 0UL, ulong c = 0UL)
    {
        ulong h = 0x9E3779B97F4A7C15UL;
        h = Step(h ^ a);
        h = Step(h ^ b);
        h = Step(h ^ c);
        uint seed = (uint)(h ^ (h >> 32));
        return seed == 0u ? 1u : seed;
    }

    /// <summary>A seed from a place every PC was sent the same, to the millimetre, and a salt.</summary>
    public static uint Seed(Vector3 at, ulong salt = 0UL)
        => Seed((ulong)(uint)Mathf.RoundToInt(at.x * 1000f) | (ulong)(uint)Mathf.RoundToInt(at.y * 1000f) << 32,
            (ulong)(uint)Mathf.RoundToInt(at.z * 1000f), salt);

    private static ulong Step(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    // The place that draws: its file's name (not its folder, which a build may not share) and its line.
    private static int Place(string file, int line)
    {
        uint hash = 2166136261u;
        int start = Mathf.Max(file.LastIndexOf('/'), file.LastIndexOf('\\')) + 1;
        for (int i = start; i < file.Length; i++) { hash ^= file[i]; hash *= 16777619u; }
        hash ^= (uint)line; hash *= 16777619u;
        return (int)hash;
    }

    // The next number of the place's stream in the innermost event (xorshift32, the same on every PC).
    private static uint Next(string file, int line)
    {
        var frame = frames.Peek();
        int place = Place(file, line);
        if (!frame.Places.TryGetValue(place, out uint state)) state = Seed(frame.Seed, (ulong)(uint)place) | 1u;
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        if (state == 0u) state = 1u;
        frame.Places[place] = state;
        return state;
    }

    private static float Unit(string file, int line) => (Next(file, line) >> 8) * (1f / 16777216f);

    // ---- Draws ----

    /// <summary>0 to 1 (1 left out inside a scope).</summary>
    public static float Value([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => frames.Count > 0 ? Unit(file, line) : UnityEngine.Random.value;

    /// <summary>From <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static float Range(float min, float max, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => frames.Count > 0 ? min + (max - min) * Unit(file, line) : UnityEngine.Random.Range(min, max);

    /// <summary>A whole number from <paramref name="min"/> up to but not <paramref name="max"/>.</summary>
    public static int Range(int min, int max, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (frames.Count == 0) return UnityEngine.Random.Range(min, max);
        if (max <= min) return min;
        return min + (int)(Next(file, line) % (uint)(max - min));
    }

    public static Vector3 InsideUnitSphere([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (frames.Count == 0) return UnityEngine.Random.insideUnitSphere;
        for (int tries = 0; tries < 16; tries++)
        {
            var v = new Vector3(Unit(file, line) * 2f - 1f, Unit(file, line) * 2f - 1f, Unit(file, line) * 2f - 1f);
            if (v.sqrMagnitude <= 1f) return v;
        }
        return Vector3.zero;
    }

    public static Vector3 OnUnitSphere([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (frames.Count == 0) return UnityEngine.Random.onUnitSphere;
        var v = InsideUnitSphere(file, line);
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.up;
    }

    public static Vector2 InsideUnitCircle([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (frames.Count == 0) return UnityEngine.Random.insideUnitCircle;
        for (int tries = 0; tries < 16; tries++)
        {
            var v = new Vector2(Unit(file, line) * 2f - 1f, Unit(file, line) * 2f - 1f);
            if (v.sqrMagnitude <= 1f) return v;
        }
        return Vector2.zero;
    }

    public static Quaternion Rotation([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => frames.Count > 0
            ? Quaternion.Euler(Unit(file, line) * 360f, Unit(file, line) * 360f, Unit(file, line) * 360f)
            : UnityEngine.Random.rotation;
}
