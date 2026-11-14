using UnityEngine;

/// <summary>
/// What an Assault's calls for fire share (Docs/Features/ClassCards.md, 어썰트): where the world under a strike is, whether
/// the sky is open over a spot, and how an A-10's gun run lays its rounds down a lane. Every PC asks the same questions of
/// the same world, so the rounds the Assault's PC lands (ClassCards) and the impacts every PC draws (GunRunFx) fall in
/// one place: the user's rule that what one screen shows is what every screen shows.
/// The rays here pass through players and zombies to the ground under them, and past triggers and the effects' layer (31).
/// Numbers: Tables/ClassAbilities, Assault.barrage* and Assault.gunRun*.
/// </summary>
internal static class AssaultStrikes
{
    internal const string Row = "Assault";
    // Room for a long aim through a crowd (each zombie is a dozen bone hitboxes): past this many the nearest could be lost.
    private static readonly RaycastHit[] hits = new RaycastHit[96];
    private static int enemyLayer = -2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => enemyLayer = -2;

    // Everything solid but zombies (their bone hitboxes stand on the ground, and a shell is aimed at what they stand on)
    // and the effects' own layer.
    private static int WorldMask
    {
        get
        {
            if (enemyLayer == -2) enemyLayer = LayerMask.NameToLayer("Enemy");
            int mask = Physics.DefaultRaycastLayers & ~(1 << 31);
            return enemyLayer >= 0 ? mask & ~(1 << enemyLayer) : mask;
        }
    }

    /// <summary>
    /// A number every PC reads the same way: the table's own, with no card of this PC's player added. The look of a strike
    /// another PC called is drawn from these, so they must not differ by who looks.
    /// </summary>
    internal static float Number(string name, float fallback) => GameTables.Number(PlayerClass.AbilityTable, Row, name, fallback);

    /// <summary>
    /// The first solid thing straight down from <paramref name="above"/> within <paramref name="depth"/> metres, players
    /// and zombies passed through: where a shell or a round meets the world. False when there is nothing.
    /// </summary>
    internal static bool Ground(Vector3 above, float depth, out Vector3 point)
    {
        point = above;
        if (!Nearest(above, Vector3.down, depth, WorldMask, out RaycastHit hit)) return false;
        point = hit.point;
        return true;
    }

    /// <summary>
    /// The first solid thing along a ray: zombies are met (a mark put on one lands at its feet), players are passed
    /// through. False when nothing is within <paramref name="reach"/>.
    /// </summary>
    internal static bool Ray(Vector3 from, Vector3 way, float reach, out RaycastHit hit)
        => Nearest(from, way, reach, Physics.DefaultRaycastLayers & ~(1 << 31), out hit);

    /// <summary>
    /// Nothing over <paramref name="at"/> for <paramref name="height"/> metres (players and zombies aside): a plane's
    /// rounds can come down on it. A roof, a ceiling, a bridge over it: no.
    /// </summary>
    internal static bool OpenSky(Vector3 at, float height) => !Nearest(at + Vector3.up * .3f, Vector3.up, height, WorldMask, out _);

    // The nearest hit of a ray that is not a player's (their capsule, their hitboxes, the body seen in first person).
    private static bool Nearest(Vector3 from, Vector3 way, float reach, int mask, out RaycastHit nearest)
    {
        nearest = default;
        if (reach <= 0f || way.sqrMagnitude < 1e-8f) return false;
        int count = Physics.RaycastNonAlloc(from, way.normalized, hits, reach, mask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.distance >= best) continue;
            if (hit.collider.GetComponentInParent<PlayerMovement>() != null) continue;
            best = hit.distance;
            nearest = hit;
            found = true;
        }
        return found;
    }

    // ---- The A-10's gun run ----

    /// <summary>How many 30 mm rounds a gun run puts down its lane (Assault.gunRunRounds).</summary>
    internal static int RoundCount => Mathf.Clamp(Mathf.RoundToInt(Number("gunRunRounds", 36f)), 1, 160);

    /// <summary>
    /// Where a gun run's rounds land, down the lane from <paramref name="from"/> to <paramref name="to"/>, and when as a
    /// share (0 to 1) of the run's time. They go down in order, each as far along as its share of the time (a little
    /// before or after), so the impacts walk the lane at the plane's pace, and a little to either side within the lane's
    /// width (Assault.gunRunWidth), each on whatever is under it seen from high above: a roof over the lane takes the
    /// rounds and shelters what is under it. <paramref name="landed"/> is false where nothing was there at all. The scatter
    /// comes from the lane's end, which every PC is sent as it is, so every PC lays the very same rounds.
    /// </summary>
    internal static void LayRounds(Vector3 from, Vector3 to, Vector3[] at, float[] share, bool[] landed)
    {
        int count = at.Length, seed = SeedOf(to);
        Vector3 flat = Vector3.ProjectOnPlane(to - from, Vector3.up);
        Vector3 side = flat.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
        float width = Mathf.Max(0f, Number("gunRunWidth", 3f));
        float top = Mathf.Max(from.y, to.y) + 30f, depth = 60f + Mathf.Abs(from.y - to.y);
        for (int i = 0; i < count; i++)
        {
            float along = Mathf.Clamp01((i + .5f + (Hash(seed, i, 1) - .5f) * .7f) / count);
            share[i] = (i + .5f) / count;
            Vector3 spot = Vector3.Lerp(from, to, along) + side * ((Hash(seed, i, 2) - .5f) * width);
            landed[i] = Ground(new Vector3(spot.x, top, spot.z), depth, out Vector3 ground);
            at[i] = landed[i] ? ground : spot;
        }
    }

    /// <summary>A number from a spot, the same on every PC for the same spot: what a strike's scatter grows from.</summary>
    internal static int SeedOf(Vector3 point) => Mathf.Abs(Mathf.RoundToInt(point.x * 73f + point.y * 151f + point.z * 37f)) % 9973;

    /// <summary>A steady 0 to 1 for one of a strike's parts: the same seed, index and salt give the same number anywhere.</summary>
    internal static float Hash(int seed, int index, int salt)
    {
        uint h = (uint)(seed * 73856093) ^ (uint)(index * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xffff) / 65535f;
    }

    // ---- Ground rings ----

    /// <summary>
    /// The points of a ring <paramref name="radius"/> round <paramref name="middle"/>, each set down on the ground under
    /// it (or left at the middle's height where there is none), a little over it so it is never buried: a reach drawn on
    /// stairs or a slope follows them.
    /// </summary>
    internal static void RingOnGround(Vector3 middle, float radius, Vector3[] points)
    {
        int count = points.Length;
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            Vector3 spot = middle + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            points[i] = (Ground(spot + Vector3.up * 2.5f, 6f, out Vector3 ground) ? ground : spot) + Vector3.up * .05f;
        }
    }

    /// <summary>
    /// A ring of <paramref name="points"/> (RingOnGround) drawn flat along the ground, <paramref name="width"/> across,
    /// from the first point round to <paramref name="share"/> of the way (1: all of it).
    /// </summary>
    internal static void DrawRing(FxShapes shapes, Vector3 middle, Vector3[] points, float width, Color color, float share = 1f)
    {
        int count = points.Length, segments = Mathf.Clamp(Mathf.CeilToInt(count * Mathf.Clamp01(share)), 0, count);
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = points[i], b = points[(i + 1) % count];
            Vector3 outward = Vector3.ProjectOnPlane((a + b) * .5f - middle, Vector3.up);
            if (outward.sqrMagnitude < 1e-6f) continue;
            shapes.Strip(a, b, outward.normalized, width, color);
        }
    }
}
