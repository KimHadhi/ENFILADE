using UnityEngine;

/// <summary>
/// Where everything of the ENFILADE tower stands (the user, 2026-11-12: "타워형 맵 … 1~4명 … 서로 방이 어떤 상황인지 볼 수
/// 있게 두 방이 겹쳐진 벽은 중간이 유리로"). One place for the numbers the builder lays it out by (Editor/Maps/TowerMap)
/// and the run reads it by (TowerRun), so the two can't drift apart.
/// The tower is four lanes side by side along z, each a column of rooms and stairs climbing level after level, 6 m
/// apart. The rooms of a level share their long walls, and the middle of each is armoured glass, so a lane sees the
/// fight next door. Every fourth level is a hall across all four lanes, the lobby at the bottom and a rest hall above
/// every third floor, where everyone meets again: four scanners at its east wall open the lanes of the next stretch.
/// Levels: 0 lobby; 1-3 floors 1-3; 4 hall; 5-7 floors 4-6; 8 hall; 9-11 floors 7-9; 12 hall; 13 floor 10, the last.
/// A room is x 0..36 between its end walls, four times as long as it is wide: at 16 m the user found the floors cramped
/// (2026-11-12, "너무 좁아. 좀 길쭉했으면 좋겠어 층마다"), and a long room is what the tower's name says, fire down its length.
/// The stairs climbing from a level stand beyond one end wall in a 10 m zone:
/// east from even levels, west from odd ones, so the rooms are walked alternately east-west and west-east. A stairwell
/// is two flights: the first leaves its level through the end wall on the lane's start half (the lower z half) and climbs
/// away from the room to a landing, the second comes back on the end half and arrives at the next level's end wall. So a
/// room's entry door is on its end half and its exit on its start half. Halls are always arrived at from the west and
/// left to the east.
/// </summary>
public static class TowerLayout
{
    public const int Lanes = 4, Levels = 14, Segments = 4, Floors = 10;
    /// <summary>A lane's inside width, wall thickness, level height and slab thickness (m).</summary>
    public const float LaneWidth = 9f, Wall = .4f, Height = 6f, Slab = .4f;
    /// <summary>A room's length between its end walls, and a stair zone's length beyond one (m).</summary>
    public const float RoomLength = 36f, StairLength = 10f;
    /// <summary>A door's opening (m).</summary>
    public const float DoorWidth = 2.4f, DoorHeight = 3f;
    /// <summary>How far a flight runs along x for its half level of rise (m); the landing takes the rest of the zone.</summary>
    public const float FlightRun = 6.5f;
    /// <summary>The glass in a shared long wall: along x, and its sill and top over the floor (m).</summary>
    public const float WindowFrom = 2.5f, WindowTo = RoomLength - 2.5f, SillHeight = .9f, WindowTop = 3.5f;
    /// <summary>The glass is this many panes with a pillar of this width between two (m): one pane down a 36 m room reads
    /// as a missing wall, panes read as an observation window.</summary>
    public const int Panes = 3;
    public const float PillarWidth = .8f;

    /// <summary>Half the inside width of all four lanes and the walls between them (18.6 m).</summary>
    public static float InnerHalf => (Lanes * LaneWidth + (Lanes - 1) * Wall) * .5f;
    /// <summary>Half the tower's width with its outer walls (19 m).</summary>
    public static float OuterHalf => InnerHalf + Wall;

    public static float LaneMin(int lane) => -InnerHalf + lane * (LaneWidth + Wall);
    public static float LaneMax(int lane) => LaneMin(lane) + LaneWidth;
    public static float LaneMid(int lane) => LaneMin(lane) + LaneWidth * .5f;
    /// <summary>The middle of the lane's start half (lower z): where a flight leaves, and a room's exit door.</summary>
    public static float StartHalf(int lane) => LaneMin(lane) + LaneWidth * .25f;
    /// <summary>The middle of the lane's end half (higher z): where a flight arrives, and a room's entry door.</summary>
    public static float EndHalf(int lane) => LaneMax(lane) - LaneWidth * .25f;

    /// <summary>The outer faces of the room's end walls: the east and west stair zones start there.</summary>
    public static float EastFace => RoomLength + Wall;
    public static float WestFace => -Wall;
    /// <summary>The far ends of the stair zones, inside the tower's outer walls.</summary>
    public static float EastEnd => EastFace + StairLength;
    public static float WestEnd => WestFace - StairLength;

    public static float FloorY(int level) => level * Height;
    /// <summary>The ceiling over a level: the underside of the next slab.</summary>
    public static float CeilingY(int level) => (level + 1) * Height - Slab;

    /// <summary>The stairs climbing from this level are east of the rooms (even levels) or west (odd).</summary>
    public static bool ClimbsEast(int level) => level % 2 == 0;
    /// <summary>A room's entry wall is east: the stairs that brought it there climbed from the level below on the east.</summary>
    public static bool EntersFromEast(int level) => ClimbsEast(level - 1);

    public static bool IsHall(int level) => level >= 0 && level < Levels && level % 4 == 0;
    public static int HallLevel(int segment) => segment * 4;
    public static int FirstLevel(int segment) => segment * 4 + 1;
    public static int LastLevel(int segment) => Mathf.Min(segment * 4 + 3, Levels - 1);
    /// <summary>How many floors a stretch climbs: three, and one for the last.</summary>
    public static int FloorsIn(int segment) => LastLevel(segment) - FirstLevel(segment) + 1;
    public static bool IsLastSegment(int segment) => segment >= Segments - 1;

    /// <summary>The floor number (1-10) a fight level is; 0 for a hall.</summary>
    public static int FloorOf(int level) => IsHall(level) ? 0 : level - level / 4;
    /// <summary>The floor number of the n-th floor (0-based) of a stretch.</summary>
    public static int FloorNumber(int segment, int index) => FloorOf(FirstLevel(segment) + index);

    /// <summary>The lane whose inside this z is in, or -1 (a wall, outside).</summary>
    public static int LaneOfZ(float z)
    {
        for (int lane = 0; lane < Lanes; lane++)
            if (z >= LaneMin(lane) - .15f && z <= LaneMax(lane) + .15f) return lane;
        return -1;
    }

    /// <summary>
    /// The lane a point is in during a stretch: its stairs and rooms from the hall's departure door up to below the next
    /// hall. -1 anywhere else (a hall, outside, another stretch).
    /// </summary>
    public static int LaneAt(Vector3 at, int segment)
    {
        int lane = LaneOfZ(at.z);
        if (lane < 0) return -1;
        float bottom = FloorY(HallLevel(segment)) - .5f;
        float top = IsLastSegment(segment) ? FloorY(Levels) + 2f : FloorY(HallLevel(segment + 1)) - .5f;
        if (at.y < bottom || at.y >= top) return -1;
        // On the hall's own level only the stairs past its east wall are the lane's.
        if (at.y < CeilingY(HallLevel(segment)) && at.x < EastFace - .2f) return -1;
        return lane;
    }

    /// <summary>Inside a stretch's hall (the lobby for the first, with its entrance hall to the west).</summary>
    public static bool InHall(Vector3 at, int segment)
    {
        float floor = FloorY(HallLevel(segment));
        if (at.y < floor - .5f || at.y > CeilingY(HallLevel(segment))) return false;
        float west = segment == 0 ? WestEnd : 0f;
        return at.x >= west - .2f && at.x <= RoomLength + .2f && Mathf.Abs(at.z) <= InnerHalf + .2f;
    }

    /// <summary>Inside a room, at least <paramref name="past"/> m in from its entry wall.</summary>
    public static bool InRoom(Vector3 at, int level, int lane, float past)
    {
        if (LaneOfZ(at.z) != lane) return false;
        float floor = FloorY(level);
        if (at.y < floor - .5f || at.y > CeilingY(level)) return false;
        return EntersFromEast(level) ? at.x >= -.2f && at.x <= RoomLength - past : at.x >= past && at.x <= RoomLength + .2f;
    }

    /// <summary>Where the dead come back and the left-behind are brought: a stretch's hall, its middle (the lobby's entrance hall).</summary>
    public static Vector3 HallSpot(int segment)
        => new Vector3(segment == 0 ? -5f : 4.5f, FloorY(HallLevel(segment)) + .05f, 0f);

    /// <summary>Where the players start, on the square in front of the tower's west face.</summary>
    public static Vector3 StartSpot => new Vector3(WestEnd - 14f, .05f, 0f);

    /// <summary>A hall's scanner for a lane: in front of the lane's departure door, to its side, so the doorway stays clear.</summary>
    public static Vector3 ScannerAt(int segment, int lane)
        => new Vector3(RoomLength - 1.5f, FloorY(HallLevel(segment)), StartHalf(lane) + 2.1f);

    /// <summary>A room's far end, where its zombies are let out: the end wall across from the entry.</summary>
    public static float FarX(int level) => EntersFromEast(level) ? 2.2f : RoomLength - 2.2f;

    /// <summary>Where a room's zombies come out: the holding pens at its far corners and its far middle.</summary>
    public static Vector3 PenAt(int level, int lane, int index)
    {
        float far = FarX(level), inward = EntersFromEast(level) ? 1f : -1f;
        float z = (index % 3) switch { 0 => LaneMin(lane) + 1.3f, 1 => LaneMax(lane) - 1.3f, _ => LaneMid(lane) + 1.6f };
        return new Vector3(far + inward * (index / 3) * 1.4f, FloorY(level), z);
    }

    /// <summary>Where a room's champion waits for whoever comes in.</summary>
    public static Vector3 ChampionAt(int level, int lane)
        => new Vector3(FarX(level) + (EntersFromEast(level) ? 2.6f : -2.6f), FloorY(level), LaneMid(lane));

    /// <summary>A rest hall's shop counter: the stand in front of it, in the hall's middle on the way from the arrivals to
    /// the scanners.</summary>
    public static Vector3 ShopAt(int segment) => new Vector3(RoomLength * .5f - .5f, FloorY(HallLevel(segment)), -1.6f);
}
