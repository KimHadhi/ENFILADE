using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Map dressing, stage 5 (the user, 2026-11-12: "맵에 밀도가 없어"): what fills a map between its landmarks. Clutter
/// strews small things along a line on a floor (a parapet's inside, a wall's foot, a kerb), each picked and turned by
/// its place, so a build always lays the same. Skyline raises the city beyond the map's edge (Tables/MapSkyline): rings
/// of blocks round the playable area, seen only (no collider, so no walk mesh and nothing to stand on), their sizes
/// on their look's window grid so the painted windows (Tables/MapWindows) meet their corners and roofs, on a ground
/// that runs out past them under the fog.
/// </summary>
public sealed partial class GreyboxBuilder
{
    private const string SkylineTable = "MapSkyline";

    /// <summary>
    /// Small things along a line on a floor from <paramref name="from"/> to <paramref name="to"/>: about one every
    /// <paramref name="spacing"/> metres, a little off the line, each a kind of <paramref name="kinds"/> (Tables/MapArt)
    /// picked by its place and turned at random. A kind written "!kind" blocks (PropSolid); the rest are only seen.
    /// </summary>
    public void Clutter(Vector3 from, Vector3 to, float spacing, params string[] kinds)
    {
        float length = Vector3.Distance(from, to);
        if (length < .01f || kinds == null || kinds.Length == 0) return;
        int count = Mathf.Max(1, Mathf.RoundToInt(length / Mathf.Max(.5f, spacing)));
        Vector3 along = (to - from) / length, side = Vector3.Cross(Vector3.up, along);
        for (int i = 0; i < count; i++)
        {
            Vector3 at = Vector3.Lerp(from, to, (i + .5f) / count);
            uint hash = SpotHash(at);
            at += along * ((Unit(hash) - .5f) * spacing * .5f) + side * ((Unit(hash >> 7) - .5f) * .3f);
            string kind = kinds[(int)((hash >> 13) % (uint)kinds.Length)];
            float yaw = Unit(hash >> 3) * 360f;
            if (kind.StartsWith("!", System.StringComparison.Ordinal)) PropSolid(kind.Substring(1), at, yaw);
            else Prop(kind, at, yaw);
        }
    }

    // A number for a place, the same on every build.
    private static uint SpotHash(Vector3 at)
    {
        unchecked
        {
            uint h = (uint)Mathf.RoundToInt(at.x * 10f) * 73856093u ^ (uint)Mathf.RoundToInt(at.y * 10f) * 83492791u
                     ^ (uint)Mathf.RoundToInt(at.z * 10f) * 19349663u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return h;
        }
    }

    private static float Unit(uint hash) => (hash & 0xFFFFu) / 65535f;

    // The city beyond the edge, from this scene's row of Tables/MapSkyline (none without one). Ring by ring outward from
    // the playable area: along each side, blocks of random width, depth and floors with a street now and then, the
    // north and south sides running past the corners. Each block takes a look of the row's list and is sized on that
    // look's window grid and floor height. Laid before the layout is marked static, so it batches with the rest.
    private void Skyline()
    {
        if (!GameTables.TryGetRow(SkylineTable, sceneName, out var row)) return;
        float x0 = row.Number("x0", 0f), x1 = row.Number("x1", 0f), z0 = row.Number("z0", 0f), z1 = row.Number("z1", 0f);
        if (x1 <= x0 || z1 <= z0) return;
        var materials = new List<Material>();
        foreach (string part in Text(row, "looks").Split(';'))
        {
            string id = part.Trim();
            var look = id.Length > 0 ? Look(id) : null;
            if (look != null) materials.Add(look);
        }
        if (materials.Count == 0)
        {
            Debug.LogWarning("[Greybox] " + SkylineTable + " " + sceneName + ": none of its looks has a MapSurfaces row with its textures; no city beyond the edge.");
            return;
        }
        float gap = row.Number("gap", 8f), depth = Mathf.Max(8f, row.Number("depth", 34f)), street = row.Number("street", 10f);
        int rings = Mathf.Clamp(Mathf.RoundToInt(row.Number("rings", 2f)), 1, 4);
        float widthMin = Mathf.Max(6f, row.Number("widthMin", 16f)), widthMax = Mathf.Max(widthMin, row.Number("widthMax", 32f));
        int floorsMin = Mathf.Max(1, Mathf.RoundToInt(row.Number("floorsMin", 4f)));
        int floorsMax = Mathf.Max(floorsMin, Mathf.RoundToInt(row.Number("floorsMax", 14f)));
        uint state = (uint)Mathf.RoundToInt(row.Number("seed", 1f)) * 2654435761u + 12345u;
        float reach = gap + rings * (depth + street) + 30f;
        // Ground out to past the last ring (the map's own ground stops at its edge).
        Box("바깥 땅 서", x0 - reach, x0, -.5f, 0f, z0 - reach, z1 + reach, Road, solid: false);
        Box("바깥 땅 동", x1, x1 + reach, -.5f, 0f, z0 - reach, z1 + reach, Road, solid: false);
        Box("바깥 땅 남", x0, x1, -.5f, 0f, z0 - reach, z0, Road, solid: false);
        Box("바깥 땅 북", x0, x1, -.5f, 0f, z1, z1 + reach, Road, solid: false);
        int made = 0;
        for (int ring = 0; ring < rings; ring++)
        {
            float inner = gap + ring * (depth + street), outer = inner + depth;
            // North and south: the full width, past the corners; east and west between them.
            made += SkylineSide(ref state, materials, x0 - outer, x1 + outer, z1 + inner, depth, true, widthMin, widthMax, floorsMin, floorsMax, street);
            made += SkylineSide(ref state, materials, x0 - outer, x1 + outer, z0 - inner, -depth, true, widthMin, widthMax, floorsMin, floorsMax, street);
            made += SkylineSide(ref state, materials, z0 - inner, z1 + inner, x1 + inner, depth, false, widthMin, widthMax, floorsMin, floorsMax, street);
            made += SkylineSide(ref state, materials, z0 - inner, z1 + inner, x0 - inner, -depth, false, widthMin, widthMax, floorsMin, floorsMax, street);
        }
        surfaceNotes.Add("맵 밖 건물 " + made + "채");
    }

    // One side of a ring: blocks from start to end along x (alongX) or z, standing from the line `face` outward by
    // `depth` (its sign is the way out). Returns how many were laid.
    private int SkylineSide(ref uint state, List<Material> materials, float start, float end, float face, float depth, bool alongX,
        float widthMin, float widthMax, int floorsMin, int floorsMax, float street)
    {
        int made = 0;
        float at = start;
        while (at < end - 4f)
        {
            var look = materials[(int)(Next(ref state) % (uint)materials.Count)];
            float grid = Mathf.Max(1f, look.GetVector("_WindowGrid").x), floor = Mathf.Max(1f, look.GetVector("_WindowRows").x);
            // On the look's grid: a block starts at a grid line, spans whole panes, rises whole floors.
            at = Mathf.Ceil(at / grid) * grid;
            float width = Mathf.Max(2f, Mathf.Round(Mathf.Lerp(widthMin, widthMax, Unit(Next(ref state))) / grid)) * grid;
            width = Mathf.Min(width, Mathf.Max(grid * 2f, end - at));
            float deep = Mathf.Max(2f, Mathf.Round(Mathf.Lerp(Mathf.Abs(depth) * .55f, Mathf.Abs(depth), Unit(Next(ref state))) / grid)) * grid;
            float height = Mathf.Lerp(floorsMin, floorsMax + .99f, Unit(Next(ref state)));
            height = Mathf.Floor(height) * floor;
            float f0 = face, f1 = face + Mathf.Sign(depth) * deep;
            string name = "맵 밖 건물 " + (made + 1);
            if (alongX) Box(name, at, at + width, 0f, height, Mathf.Min(f0, f1), Mathf.Max(f0, f1), look, solid: false);
            else Box(name, Mathf.Min(f0, f1), Mathf.Max(f0, f1), 0f, height, at, at + width, look, solid: false);
            made++;
            // A street now and then, else a narrow gap between blocks.
            at += width + (Unit(Next(ref state)) < .35f ? street : grid);
        }
        return made;
    }

    private static uint Next(ref uint state)
    {
        unchecked
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
    }
}
