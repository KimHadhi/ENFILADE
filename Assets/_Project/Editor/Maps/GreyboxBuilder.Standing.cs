using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where a solid prop may stand (the user, 2026-11-18: "망가진 맵 오브젝트 배치 복구"; a 4 m shop chiller stood through
/// the flats' walls, a 4 m light tower through the cellar's ceiling and across the lift's machine room, street bins
/// half inside the buildings beside them).
/// A solid prop was stood at the point the layout gave, at the model's own size, with no idea of what was round it:
/// whether it fitted was the layout author's guess at a size only the build knows. Now the build, which measures the
/// model, owns where it stands:
/// - PropSolid stands it where it is told when that place is free, moves it the shortest way along the floor out of
///   whatever it would be inside of when it is not, and refuses it when that would take it farther than its own
///   length (how engines place a spawned actor: adjust if possible, don't spawn if still colliding).
/// - Furnish stands it in a room the layout names, its back on one of the room's walls, slid along that wall to the
///   nearest place where it is inside nothing; it is refused when it is bigger than the room or the wall has no such
///   place.
/// "Inside of" means the layout's solid boxes (walls, slabs, ceilings, other props) and the ways the layout keeps
/// clear (KeepClear: a doorway and the step either side of it, a hole through a floor). Nothing here is a size or an
/// offset for one prop: the rule is geometry on the sizes the build measured, so a new model or a mod's works unasked.
/// Every prop asked for is kept, and the build ends with what became of each one that was moved or refused and a last
/// look at every prop against the finished layout (a wall built after the prop it crosses): a line of the closing
/// window and Logs/MapAudit/&lt;scene&gt;.txt.
/// </summary>
public sealed partial class GreyboxBuilder
{
    /// <summary>A wall of a room, by the side of the room it stands on (west is -x, south is -z).</summary>
    public enum Side { West, East, South, North }

    // Touching is not crossing: two boxes cross when they overlap by more than this on every axis (m).
    private const float StandTouch = .03f;
    // A prop may sit this far into what it stands on (a kerb, a floor a little proud) without crossing it (m).
    private const float StandSunk = .1f;
    // The gap left between furniture and the wall behind it, and the step of the search along a wall (m).
    private const float WallGap = .02f, WallStep = .1f;
    // How many things in a row a prop is moved out of before it is given up (a corner takes two).
    private const int SettleTries = 6;

    // One solid prop the layout asked for, and what became of it.
    private sealed class Stood
    {
        public string Kind, Refused, MovedOutOf;
        public Vector3 Asked, At, Half;
        public float Yaw, Height;
        public Block Block;
    }

    private readonly List<Stood> stood = new List<Stood>();
    private readonly List<(string name, Bounds space)> ways = new List<(string, Bounds)>();

    /// <summary>The floor point where the solid prop asked for last was stood (where it was asked to stand, when it was
    /// refused), for a builder that hangs something on it: a generator's handle on the generator, wherever it settled.</summary>
    public Vector3 LastStoodAt { get; private set; }

    /// <summary>
    /// A way that stays open, by its extents (as Box takes them): a doorway with the step either side of its wall, a
    /// hole through a floor with the room over it. No solid prop is stood in it (PropSolid moves out of it, Furnish
    /// looks for a place beside it), and the build's audit names any that ends up there.
    /// </summary>
    public void KeepClear(string name, float x0, float x1, float y0, float y1, float z0, float z1)
    {
        var space = new Bounds(new Vector3((x0 + x1) * .5f, (y0 + y1) * .5f, (z0 + z1) * .5f),
            new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)));
        ways.Add((name, space));
    }

    /// <summary>
    /// A solid prop of <paramref name="kind"/> (Tables/MapArt) stood in <paramref name="room"/> (the free space between
    /// its walls, floor to ceiling) with its back on the room's <paramref name="wall"/> and its front (the kit's +Z)
    /// into the room, <paramref name="along"/> of the way along that wall (0 its west or south end, 1 its east or north
    /// end). The build measures the model: it stands at the nearest place along the wall to the one asked for where it is
    /// inside nothing, and is refused (null, named in the build's report) when it is wider, deeper or taller than the
    /// room or the wall has no free place for it.
    /// </summary>
    public GameObject Furnish(string kind, Bounds room, Side wall, float along = .5f, int pick = -1, float scale = 1f)
    {
        bool alongX = wall == Side.South || wall == Side.North;
        // A variant by its place, as PropSolid picks one: the room and where on its wall, the same on every build.
        Vector3 place = room.center + new Vector3((int)wall * 1.7f, 0f, along * 3.1f);
        if (!ArtRow(kind, pick, place, out var prefab, out _, out float turn, out float own)) return null;
        float size = own * scale;
        float angle = (wall == Side.West ? 90f : wall == Side.East ? 270f : wall == Side.South ? 0f : 180f) + turn;
        Vector3 half = BoundsOf(prefab).extents * size, turned = Turned(half * 2f, angle);
        float width = alongX ? turned.x : turned.z, depth = alongX ? turned.z : turned.x;
        float length = alongX ? room.size.x : room.size.z, deep = alongX ? room.size.z : room.size.x;
        float off = depth * .5f + WallGap;
        float held = wall == Side.West ? room.min.x + off : wall == Side.East ? room.max.x - off
            : wall == Side.South ? room.min.z + off : room.max.z - off;
        float low = (alongX ? room.min.x : room.min.z) + width * .5f, high = (alongX ? room.max.x : room.max.z) - width * .5f;
        float wanted = Mathf.Lerp(low, Mathf.Max(low, high), Mathf.Clamp01(along));
        Vector3 At(float on) => alongX ? new Vector3(on, room.min.y, held) : new Vector3(held, room.min.y, on);
        if (width > length || depth + WallGap > deep || turned.y > room.size.y)
            return Refuse(kind, At(wanted), half, turned.y, angle, "방보다 커요(모델 " + Metres(turned) + " m, 방 " + Metres(room.size) + " m)");
        // The nearest free place to the one asked for, looking both ways along the wall.
        string inTheWay = null;
        for (float away = 0f; away <= high - low + WallStep; away += WallStep)
            for (int sign = 1; sign >= (away > 0f ? -1 : 1); sign -= 2)
            {
                float on = wanted + away * sign;
                if (on < low - 1e-4f || on > high + 1e-4f) continue;
                if (Crosses(At(on), half, turned.y, angle, null, out string what, out _)) { inTheWay ??= what; continue; }
                return Stand(prefab, kind, At(on), angle, size, At(wanted), away > 0f ? inTheWay : null);
            }
        return Refuse(kind, At(wanted), half, turned.y, angle, "그 벽에 빈 자리가 없어요(" + inTheWay + ")");
    }

    // The place a solid prop takes when asked to stand at `floor`: there if it is free, else the shortest way along the
    // floor out of what it is in, one thing after another. False (and `why` names the first thing in its way) when that
    // takes it farther than its own longer side: then it does not belong there.
    private bool Settle(ref Vector3 floor, Vector3 half, float height, float yaw, out string why)
    {
        why = null;
        Vector3 asked = floor;
        float farthest = Mathf.Max(half.x, half.z) * 2f;
        for (int tries = 0; tries <= SettleTries; tries++)
        {
            if (!Crosses(floor, half, height, yaw, null, out string what, out Vector2 free)) return true;
            why ??= what;
            floor += new Vector3(free.x, 0f, free.y);
            Vector3 gone = floor - asked;
            if (gone.x * gone.x + gone.z * gone.z > farthest * farthest) break;
        }
        floor = asked;
        return false;
    }

    // The deepest thing a box standing at `floor` (its own half size turned by yaw, this high) would be inside of: its
    // name, and the shortest move along the floor that frees the box of it. False when it is inside nothing. For a
    // prop the ways kept clear count as things to be out of, and a lower prop as something it may stand on; for a body
    // that comes up at a spawn point (asProp off) neither: it may come up in a doorway, and on no sofa.
    private bool Crosses(Vector3 floor, Vector3 half, float height, float yaw, Block self, out string what, out Vector2 free, bool asProp = true)
    {
        what = null;
        free = Vector2.zero;
        float deepest = 0f, bottom = floor.y, top = floor.y + height;
        var centre = new Vector2(floor.x, floor.z);
        var own = new Vector2(half.x, half.z);
        foreach (var other in blocks)
        {
            if (other == self || other.Shape == null) continue;
            // What it stands on and what is over it are not in its way. Another prop under it is what it stands on even
            // when that prop's box was raised past its model (the band no zombie reaches): crates are stacked.
            float under = other.Prop && asProp ? Mathf.Max(bottom + StandSunk, (bottom + top) * .5f) : bottom + StandSunk;
            if (other.Box.max.y <= under || other.Box.min.y >= top - StandTouch) continue;
            if (!Overlap(centre, own, yaw, new Vector2(other.Box.center.x, other.Box.center.z), new Vector2(other.Size.x, other.Size.z) * .5f,
                    other.Yaw, out Vector2 way)) continue;
            if (way.sqrMagnitude <= deepest) continue;
            deepest = way.sqrMagnitude;
            free = way;
            what = other.Shape.name;
        }
        foreach (var (name, space) in ways)
        {
            if (!asProp) break;
            if (space.max.y <= bottom + StandTouch || space.min.y >= top - StandTouch) continue;
            if (!Overlap(centre, own, yaw, new Vector2(space.center.x, space.center.z), new Vector2(space.extents.x, space.extents.z), 0f,
                    out Vector2 way)) continue;
            if (way.sqrMagnitude <= deepest) continue;
            deepest = way.sqrMagnitude;
            free = way;
            what = name;
        }
        return what != null;
    }

    // ---- Spawn points ----

    // A spawn point is a place a body comes up at: a zombie's body standing there, half its width with a hand's room
    // and its height (m). A point the layout put where a solid thing stands (a truck the dressing parked on it, a
    // sofa at a corridor's end) had no body come up at it, and nothing said so: the game skips a point with no room
    // (CampaignHorde.Roomy). Measured on the scenes built 11-15 and 11-16: one such point in each of chapters 1, 2
    // and 4 (Tools/Maps/walk_joins.py). The layout's lines for the points come before the dressing's for the props,
    // so the point is what is moved: the shortest way out of the thing along its floor, at most as far as the game
    // looks for the walk mesh round a point (CampaignHorde.PointSearch).
    private const float PointHalf = .35f, PointHeight = 1.8f, PointFarthest = 2f;
    private readonly List<(string name, Vector3 asked, Vector3 at, string why)> pointsMoved = new List<(string, Vector3, Vector3, string)>(),
        pointsBlocked = new List<(string, Vector3, Vector3, string)>();

    // Finish, with every box and prop laid and before the bake: every spawn point stands free of them.
    private void FreeSpawnPoints()
    {
        var half = new Vector3(PointHalf, PointHeight * .5f, PointHalf);
        foreach (var point in enemySpawns)
        {
            if (point == null) continue;
            Vector3 asked = point.position - Vector3.up * Lift, floor = asked;
            string first = null;
            bool free = false;
            for (int tries = 0; tries <= SettleTries; tries++)
            {
                if (!Crosses(floor, half, PointHeight, 0f, null, out string what, out Vector2 way, false)) { free = true; break; }
                first ??= what;
                floor += new Vector3(way.x, 0f, way.y);
                Vector3 gone = floor - asked;
                if (gone.x * gone.x + gone.z * gone.z > PointFarthest * PointFarthest) break;
            }
            if (first == null) continue;
            // Out of the thing, but onto a floor: a place past a roof's edge is no place to come up at.
            if (!free || !FloorAt(floor))
            {
                pointsBlocked.Add((point.name, asked, asked, first));
                continue;
            }
            point.position = floor + Vector3.up * Lift;
            pointsMoved.Add((point.name, asked, floor, first));
        }
    }

    // Whether one of the layout's boxes has its top at this place's feet.
    private bool FloorAt(Vector3 floor)
    {
        foreach (var other in blocks)
        {
            if (other.Shape == null || other.Moves) continue;
            if (Mathf.Abs(other.Box.max.y - floor.y) > StandSunk + Lift) continue;
            if (floor.x < other.Box.min.x || floor.x > other.Box.max.x || floor.z < other.Box.min.z || floor.z > other.Box.max.z) continue;
            return true;
        }
        return false;
    }

    // For the build's audit: the points that were moved, and the ones that could not be.
    private string PointReport()
    {
        if (enemySpawns.Count == 0) return "";
        audit.Append("\n## 스폰 지점이 선 자리 (단단한 것 속에 놓인 지점은 가장 가까운 빈 바닥으로 옮긴다)\n");
        foreach (var (name, asked, at, why) in pointsMoved)
            audit.Append("옮김 ").Append(name).Append(" (").Append(Format(asked)).Append(" → ").Append(Format(at)).Append("): ").Append(why).Append(" 속이어서\n");
        foreach (var (name, asked, at, why) in pointsBlocked)
            audit.Append("막힘 ").Append(name).Append(" (").Append(Format(asked)).Append("): ").Append(why).Append(" 속이고, ")
                .Append(PointFarthest.ToString("0.#")).Append(" m 안에 빈 바닥이 없어요. 배치에서 지점을 옮겨야 해요\n");
        audit.Append("# 합계: 스폰 지점 ").Append(enemySpawns.Count).Append("개, 옮긴 것 ").Append(pointsMoved.Count).Append(", 막힌 것 ").Append(pointsBlocked.Count).Append('\n');
        if (pointsBlocked.Count == 0) return "";
        auditToRead = true;
        var names = new List<string>();
        foreach (var blocked in pointsBlocked) names.Add(blocked.name);
        return "\n단단한 것 속에 놓여 좀비가 나오지 못하는 스폰 지점 " + pointsBlocked.Count + "개: " + string.Join(", ", names) + " (배치에서 옮겨야 해요).";
    }

    // Two boxes on the floor (centre, half size, yaw about Y): whether they overlap by more than a touch, and then the
    // shortest move of the first that leaves them only touching (the separating-axis test on their four edge directions).
    private static bool Overlap(Vector2 a, Vector2 halfA, float yawA, Vector2 b, Vector2 halfB, float yawB, out Vector2 free)
    {
        free = Vector2.zero;
        // A yaw about Y takes the box's own x to (cos, -sin) and its own z to (sin, cos) on the floor (x, z).
        float ra = yawA * Mathf.Deg2Rad, rb = yawB * Mathf.Deg2Rad;
        Vector2 ax = new Vector2(Mathf.Cos(ra), -Mathf.Sin(ra)), az = new Vector2(Mathf.Sin(ra), Mathf.Cos(ra));
        Vector2 bx = new Vector2(Mathf.Cos(rb), -Mathf.Sin(rb)), bz = new Vector2(Mathf.Sin(rb), Mathf.Cos(rb));
        Vector2 between = a - b;
        float least = float.PositiveInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector2 axis = i == 0 ? ax : i == 1 ? az : i == 2 ? bx : bz;
            float reachA = halfA.x * Mathf.Abs(Vector2.Dot(axis, ax)) + halfA.y * Mathf.Abs(Vector2.Dot(axis, az));
            float reachB = halfB.x * Mathf.Abs(Vector2.Dot(axis, bx)) + halfB.y * Mathf.Abs(Vector2.Dot(axis, bz));
            float apart = Vector2.Dot(between, axis);
            float depth = reachA + reachB - Mathf.Abs(apart);
            if (depth <= StandTouch) return false;
            if (depth >= least) continue;
            least = depth;
            free = axis * (apart >= 0f ? depth : -depth);
        }
        return true;
    }

    // The model and its unseen box, where the build settled them.
    private GameObject Stand(GameObject prefab, string kind, Vector3 floor, float angle, float size, Vector3 asked, string movedOutOf)
    {
        var model = Place(prefab, floor, angle, Vector3.one * size, false);
        // The model's own box, measured unturned and turned with it: a car at 30° keeps a car-sized box, where its
        // axis-aligned bounds stood past its corners as unseen walls with a roof in the air beside it.
        Vector3 half = BoundsOf(prefab).extents * size;
        float height = half.y * 2f;
        string solid = ArtSolid(kind);
        bool cap = solid == "cap";
        if (!cap && TableCells.TryNumber(solid, out float upTo) && upTo > 0f)
            height = Mathf.Min(height, upTo);
        else if (!cap && height > SolidBandLow && height < SolidBandTop)
            height = height < SolidBandLow + SolidBandSink ? SolidBandLow : SolidBandTop;
        var box = Box(kind + " (충돌)", floor.x - half.x, floor.x + half.x, floor.y, floor.y + height, floor.z - half.z, floor.z + half.z);
        box.GetComponent<Renderer>().enabled = false;
        if (ShootOverCover.IsKind(kind)) box.AddComponent<ShootOverCover>();
        TurnBlock(box, angle);
        var block = BlockOf(box);
        if (block != null) block.Prop = true;
        if (cap) Cap(box, kind, new Vector3(floor.x, floor.y + height, floor.z), half, angle);
        if (ArtShots(kind)) ShapeShots(box, new[] { model });
        stood.Add(new Stood { Kind = kind, Asked = asked, At = floor, Half = half, Yaw = angle, Height = half.y * 2f, Block = block, MovedOutOf = movedOutOf });
        LastStoodAt = floor;
        return model;
    }

    private GameObject Refuse(string kind, Vector3 asked, Vector3 half, float height, float angle, string why)
    {
        stood.Add(new Stood { Kind = kind, Asked = asked, At = asked, Half = half, Yaw = angle, Height = height, Refused = why });
        LastStoodAt = asked;
        return null;
    }

    private static string Metres(Vector3 size) => size.x.ToString("0.00") + " × " + size.y.ToString("0.00") + " × " + size.z.ToString("0.00");

    // The end of a build: what became of the solid props, and a last look at each against the finished layout (a prop
    // stood before the wall it crosses was free when it was stood). Its lines go into the map's audit (GreyboxBuilder.
    // Audit, Logs/MapAudit/<scene>.txt); it returns the closing window's line: the props that need the layout changed
    // are named, the rest are counted.
    private string StandReport()
    {
        if (stood.Count == 0) return "";
        var text = audit;
        var refused = new List<string>();
        var crossing = new List<string>();
        int placed = 0, moved = 0;
        text.Append("\n## 단단한 소품이 선 자리 (GreyboxBuilder.Standing)\n");
        text.Append("# 거절 = 세우지 않음. 옮김 = 겹친 것에서 가장 짧게 비켜 세움. 겹침 = 다 짓고 나서도 벽·바닥·천장·다른 소품·비워 둘 길과 겹침(고쳐야 함).\n");
        foreach (var prop in stood)
        {
            if (prop.Refused != null)
            {
                refused.Add(prop.Kind);
                text.Append("거절 ").Append(prop.Kind).Append(" (").Append(Format(prop.Asked)).Append("): ").Append(prop.Refused).Append('\n');
                continue;
            }
            placed++;
            if (prop.MovedOutOf != null)
            {
                moved++;
                text.Append("옮김 ").Append(prop.Kind).Append(" (").Append(Format(prop.Asked)).Append(" → ").Append(Format(prop.At)).Append("): ")
                    .Append(prop.MovedOutOf).Append("에 겹쳐서\n");
            }
            if (Crosses(prop.At, prop.Half, prop.Height, prop.Yaw, prop.Block, out string what, out Vector2 free))
            {
                crossing.Add(prop.Kind);
                text.Append("겹침 ").Append(prop.Kind).Append(" (").Append(Format(prop.At)).Append("): ").Append(what).Append("에 ")
                    .Append(free.magnitude.ToString("0.00")).Append(" m\n");
            }
        }
        text.Append("# 합계: 세움 ").Append(placed).Append(" (그중 옮김 ").Append(moved).Append("), 거절 ").Append(refused.Count)
            .Append(", 겹침 ").Append(crossing.Count).Append('\n');
        if (refused.Count + crossing.Count + moved > 0) auditToRead = true;
        string line = "\n단단한 소품 " + placed + "개를 세웠어요(겹친 자리에서 비켜 세운 것 " + moved + "개).";
        if (refused.Count > 0) line += " 자리에 안 맞아 세우지 않은 것 " + refused.Count + "개: " + string.Join(", ", refused) + ".";
        if (crossing.Count > 0) line += " 벽·바닥이나 길과 아직 겹치는 것 " + crossing.Count + "개: " + string.Join(", ", crossing) + " (배치를 고쳐야 해요).";
        return line;
    }
}
