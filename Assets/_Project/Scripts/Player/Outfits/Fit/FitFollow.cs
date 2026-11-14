using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// A garment following the body's shape where its maker gave it no key for that. A body's shape keys (heels, a fuller
/// chest, slimmer legs) reshape the skin, and clothes made for the body carry keys of the same names to go with it; a
/// piece without one stays as it was made while the body under it moves away. White Board's socks have no heel key:
/// in heeled shoes Mayo's foot tipped down inside the shoe and the socks stayed flat, their toes standing 3 to 4 cm
/// out through the shoe's front (measured 2026-11-14).
/// Each of the garment's vertices takes the move of the skin it lies on: the few nearest body vertices, the nearer the
/// more, fading out toward the reach so cloth hanging clear of the body is left alone, and only skin that moves with
/// the bones it follows (a sock's foot never takes the other foot's). Measured against the makers' own keys where they
/// exist (Mayo's stockings and Ichigo's tights have heel keys), a key made this way lands within half a millimetre on
/// average and 2.5 mm at worst, over moves of 3 to 6 cm.
/// Plain arrays, no engine objects.
/// </summary>
internal static class FitFollow
{
    /// <summary>How many body vertices one garment vertex follows.</summary>
    public const int Most = 8;
    // Skin moved at least this share of a key's longest move under a garment tells which body the garment was made on.
    private const float TellingShare = .3f;

    /// <summary>Which skin each of a garment's vertices lies on.</summary>
    public sealed class Map
    {
        /// <summary>Most body vertices per garment vertex, -1 where fewer were found.</summary>
        public int[] Body;
        /// <summary>Their shares, already faded: a vertex's shares add up to its fade (1 close to the skin, 0 out of reach).</summary>
        public float[] Share;
        /// <summary>How far each garment vertex is from the nearest of them (m); infinite with none.</summary>
        public float[] Gap;
    }

    // Points filed in cells for looking up those near a place.
    private sealed class Cells
    {
        private readonly Dictionary<long, int> startOf = new Dictionary<long, int>();
        private readonly int[] order, startAt;
        private readonly Vector3[] points;
        private readonly float size;

        public Cells(Vector3[] points, float size)
        {
            this.points = points;
            this.size = size;
            var keys = new long[points.Length];
            order = new int[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                keys[i] = Key(Floor(points[i].x), Floor(points[i].y), Floor(points[i].z));
                order[i] = i;
            }
            Array.Sort(keys, order);
            var starts = new List<int>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0 && keys[i] == keys[i - 1]) continue;
                startOf[keys[i]] = starts.Count;
                starts.Add(i);
            }
            starts.Add(keys.Length);
            startAt = starts.ToArray();
        }

        private int Floor(float value) => Mathf.FloorToInt(value / size);

        private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

        /// <summary>The nearest points within reach, nearest first, into found and gaps (squared); returns how many.</summary>
        public int Nearest(Vector3 at, float reach, int[] tags, bool[] counted, Span<int> found, Span<float> gaps)
        {
            int held = 0, span = Mathf.CeilToInt(reach / size);
            float reach2 = reach * reach;
            int cx = Floor(at.x), cy = Floor(at.y), cz = Floor(at.z);
            for (int x = cx - span; x <= cx + span; x++)
                for (int y = cy - span; y <= cy + span; y++)
                    for (int z = cz - span; z <= cz + span; z++)
                    {
                        if (!startOf.TryGetValue(Key(x, y, z), out int cell)) continue;
                        for (int k = startAt[cell]; k < startAt[cell + 1]; k++)
                        {
                            int index = order[k];
                            if (counted != null)
                            {
                                int tag = tags[index];
                                if (tag < 0 || tag >= counted.Length || !counted[tag]) continue;
                            }
                            Vector3 to = points[index] - at;
                            float gap = to.x * to.x + to.y * to.y + to.z * to.z;
                            if (gap > reach2 || (held == found.Length && gap >= gaps[held - 1])) continue;
                            int slot = held < found.Length ? held++ : held - 1;
                            while (slot > 0 && gaps[slot - 1] > gap)
                            {
                                gaps[slot] = gaps[slot - 1];
                                found[slot] = found[slot - 1];
                                slot--;
                            }
                            gaps[slot] = gap;
                            found[slot] = index;
                        }
                    }
            return held;
        }
    }

    /// <summary>Finds the skin each garment vertex lies on, both as made (no keys on), in one space.</summary>
    /// <param name="garmentTags">For each garment vertex the bone it follows most, <paramref name="bodyTags"/> likewise; null with <paramref name="allow"/> null.</param>
    /// <param name="allow">By a garment vertex's tag, which body tags move with it. Null counts all; a null row counts none.</param>
    /// <param name="reach">How far from the skin a vertex still follows it (m); it follows fully up to half of that.</param>
    public static Map Build(Vector3[] garment, int[] garmentTags, Vector3[] body, int[] bodyTags, bool[][] allow, float reach)
    {
        var map = new Map { Body = new int[garment.Length * Most], Share = new float[garment.Length * Most], Gap = new float[garment.Length] };
        // Cloth lying on the skin finds all it needs in the cells next to it; only cloth standing off looks further.
        float close = Mathf.Max(reach / 3f, 1e-4f);
        var cells = new Cells(body, close);
        Parallel.For(0, garment.Length, v =>
        {
            Span<int> found = stackalloc int[Most];
            Span<float> gaps = stackalloc float[Most];
            bool[] counted = null;
            int held = 0;
            if (allow != null)
            {
                int tag = garmentTags[v];
                counted = tag >= 0 && tag < allow.Length ? allow[tag] : null;
            }
            if (allow == null || counted != null)
            {
                held = cells.Nearest(garment[v], close, bodyTags, counted, found, gaps);
                if (held < Most) held = cells.Nearest(garment[v], reach, bodyTags, counted, found, gaps);
            }
            int at = v * Most;
            for (int k = held; k < Most; k++) map.Body[at + k] = -1;
            map.Gap[v] = held > 0 ? Mathf.Sqrt(gaps[0]) : float.PositiveInfinity;
            if (held == 0) return;
            // Full up to half the reach, none at the reach: cloth standing clear of the skin keeps its own shape.
            float fade = Mathf.Clamp01(2f - 2f * map.Gap[v] / reach);
            float total = 0f;
            for (int k = 0; k < held; k++)
            {
                float share = 1f / (gaps[k] + 1e-10f);
                map.Body[at + k] = found[k];
                map.Share[at + k] = share;
                total += share;
            }
            for (int k = 0; k < held; k++) map.Share[at + k] *= fade / total;
        });
        return map;
    }

    /// <summary>
    /// The garment's own move for one of the body's keys: each vertex the shared move of the skin it lies on. Returns the
    /// longest move made (m); nothing over <paramref name="least"/> means the key doesn't concern this garment.
    /// </summary>
    /// <param name="bodyMoves">The key's move of each body vertex, in the space the map was built in.</param>
    /// <param name="into">One per garment vertex.</param>
    public static float Moves(Map map, Vector3[] bodyMoves, Vector3[] into)
    {
        float longest = 0f;
        for (int v = 0; v < into.Length; v++)
        {
            float x = 0f, y = 0f, z = 0f;
            int at = v * Most;
            for (int k = 0; k < Most; k++)
            {
                int index = map.Body[at + k];
                if (index < 0) break;
                float share = map.Share[at + k];
                Vector3 move = bodyMoves[index];
                x += move.x * share;
                y += move.y * share;
                z += move.z * share;
            }
            into[v] = new Vector3(x, y, z);
            float length = x * x + y * y + z * z;
            if (length > longest) longest = length;
        }
        return Mathf.Sqrt(longest);
    }

    /// <summary>
    /// Which body a garment was made on, for a key the character comes with on: the body with the key off (-1), with it
    /// on (1), or no telling (0). Over the garment's vertices that lie on skin the key moves, the middle distance to the
    /// skin with the key off is set against the middle distance to the skin with it on. A garment sits a millimetre or
    /// two off the body it was made on, and far off (or inside) the other: White Board's socks lie 1 to 2 mm off Mayo's
    /// flat foot and over a centimetre off the heeled one she comes with. Where the two are too alike to tell (loose
    /// socks standing 2 cm off either foot), the garment is best left as its maker made it.
    /// </summary>
    /// <param name="keyed">The body's vertices with the key on, in the map's space.</param>
    /// <param name="clearly">How much nearer one body must be: its middle distance under this share of the other's.</param>
    public static int MadeOn(Map map, Vector3[] garment, Vector3[] body, Vector3[] keyed, float reach, float clearly)
    {
        var cells = new Cells(keyed, Mathf.Max(reach / 3f, 1e-4f));
        var asMade = new List<float>();
        var withKey = new List<float>();
        Span<int> found = stackalloc int[1];
        Span<float> gaps = stackalloc float[1];
        // Only where the key moves the skin under the garment a good part of its longest move there: a heel key tips the
        // toes down by centimetres and shifts the shin by a hair, and the shin would say nothing either way.
        float longest = 0f;
        for (int v = 0; v < garment.Length; v++)
        {
            int nearest = map.Body[v * Most];
            if (nearest >= 0) longest = Mathf.Max(longest, (keyed[nearest] - body[nearest]).sqrMagnitude);
        }
        float least = Mathf.Max(1e-6f, longest * TellingShare * TellingShare);
        for (int v = 0; v < garment.Length; v++)
        {
            int nearest = map.Body[v * Most];
            if (nearest < 0 || (keyed[nearest] - body[nearest]).sqrMagnitude < least) continue;
            asMade.Add(map.Gap[v]);
            withKey.Add(cells.Nearest(garment[v], reach, null, null, found, gaps) > 0 ? Mathf.Sqrt(gaps[0]) : reach);
        }
        if (asMade.Count < 12) return 0;
        asMade.Sort();
        withKey.Sort();
        float off = asMade[asMade.Count / 2], on = withKey[withKey.Count / 2];
        return off < on * clearly ? -1 : on < off * clearly ? 1 : 0;
    }
}
