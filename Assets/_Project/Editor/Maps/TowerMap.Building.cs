using System.Collections.Generic;
using UnityEngine;

// TowerMap: The building. One part of the class; the rest is in TowerMap.cs and the files beside it.
public static partial class TowerMap
{
    // The outer walls all the way up, with the way in on the west face. Their outer 10 cm are a skin of one look with
    // painted windows (EnfiladeTower.Shell claims the boxes "외벽 겉", so the builder fits the windows to the whole
    // tower): the shader paints every sideways face of a look, and the skin keeps the windows off the inner faces. Inside,
    // the long walls along the rooms and halls go level by level in that level's wall; along the stairwells they stay
    // the base wall, but for the lobby's entrance hall (the west stairwell's ground level), which takes the halls' wall.
    private static void Shell(GreyboxBuilder b)
    {
        float top = TowerLayout.FloorY(TowerLayout.Levels), x0 = TowerLayout.WestEnd - W, x1 = TowerLayout.EastEnd + W;
        float inner = TowerLayout.InnerHalf, outer = TowerLayout.OuterHalf, lobby = TowerLayout.FloorY(1);
        float south = -outer + Skin, north = outer - Skin;
        const float GateHalf = 2.4f, GateTop = 4.2f;
        b.Box("외벽 겉 남", x0, x1, -.5f, top, -outer, south);
        b.Box("외벽 겉 북", x0, x1, -.5f, top, north, outer);
        b.Box("외벽 겉 동", x1 - Skin, x1, -.5f, top, south, north);
        b.Box("외벽 겉 서", x0, x0 + Skin, -.5f, top, south, -GateHalf);
        b.Box("외벽 겉 서", x0, x0 + Skin, -.5f, top, GateHalf, north);
        b.Box("외벽 겉 입구 위", x0, x0 + Skin, GateTop, top, -GateHalf, GateHalf);
        for (int level = 0; level < TowerLayout.Levels; level++)
        {
            float y0 = level == 0 ? -.5f : TowerLayout.FloorY(level), y1 = TowerLayout.FloorY(level + 1);
            float from = level == 0 ? x0 + Skin : TowerLayout.WestFace;
            b.Box("외벽 남 " + Floor(level), from, TowerLayout.EastFace, y0, y1, south, -inner, WallAt(level));
            b.Box("외벽 북 " + Floor(level), from, TowerLayout.EastFace, y0, y1, inner, north, WallAt(level));
        }
        b.Box("외벽 남 서쪽 계단", x0 + Skin, TowerLayout.WestFace, lobby, top, south, -inner, wall);
        b.Box("외벽 북 서쪽 계단", x0 + Skin, TowerLayout.WestFace, lobby, top, inner, north, wall);
        b.Box("외벽 남 동쪽 계단", TowerLayout.EastFace, x1 - Skin, -.5f, top, south, -inner, wall);
        b.Box("외벽 북 동쪽 계단", TowerLayout.EastFace, x1 - Skin, -.5f, top, inner, north, wall);
        b.Box("외벽 동", TowerLayout.EastEnd, x1 - Skin, -.5f, top, -inner, inner, wall);
        b.Box("외벽 서", x0 + Skin, TowerLayout.WestEnd, -.5f, lobby, -inner, -GateHalf, hallWall);
        b.Box("외벽 서", x0 + Skin, TowerLayout.WestEnd, -.5f, lobby, GateHalf, inner, hallWall);
        b.Box("입구 위", x0 + Skin, TowerLayout.WestEnd, GateTop, lobby, -GateHalf, GateHalf, hallWall);
        b.Box("외벽 서", x0 + Skin, TowerLayout.WestEnd, lobby, top, -inner, inner, wall);
        // The doorway's own floor, inside the wall's thickness, between the square and the lobby.
        b.Box("입구 문턱", x0, TowerLayout.WestEnd, -.5f, 0f, -GateHalf, GateHalf, hallFloor);
        b.Box("입구 틀 위", x0 - .1f, TowerLayout.WestEnd + .1f, GateTop - .12f, GateTop, -GateHalf - .1f, GateHalf + .1f, neon, solid: false);
    }

    // The rooms' floors (and the roof), the stairwells' floors, and over each stairwell's first flight and landing the
    // ledge by the next room's door.
    private static void Slabs(GreyboxBuilder b)
    {
        float inner = TowerLayout.InnerHalf;
        b.Box("로비 바닥", TowerLayout.WestEnd, TowerLayout.EastEnd, -.5f, 0f, -inner, inner, hallFloor);
        // Each slab in the floor of the level standing on it (its underside, the ceiling below, in that row's side set).
        for (int level = 1; level <= TowerLayout.Levels; level++)
        {
            float top = TowerLayout.FloorY(level);
            b.Box(level == TowerLayout.Levels ? "지붕" : Floor(level) + " 바닥", TowerLayout.WestFace, TowerLayout.EastFace, top - S, top, -inner, inner, FloorAt(level));
        }
        for (int level = 0; level < TowerLayout.Levels - 1; level++)
        {
            bool east = TowerLayout.ClimbsEast(level);
            float xw = east ? TowerLayout.EastFace : TowerLayout.WestFace, xo = east ? TowerLayout.EastEnd : TowerLayout.WestEnd;
            float turn = xw + (east ? 1f : -1f) * TowerLayout.FlightRun, ledge = TowerLayout.FloorY(level + 1);
            if (level > 0) b.Box(Floor(level) + " 계단 바닥", Mathf.Min(xw, xo), Mathf.Max(xw, xo), TowerLayout.FloorY(level) - S, TowerLayout.FloorY(level), -inner, inner, floor);
            for (int k = 0; k < TowerLayout.Lanes; k++)
            {
                float mid = TowerLayout.LaneMid(k);
                b.Box(Floor(level + 1) + " 계단 턱 " + TowerHUD.Letter(k), Mathf.Min(xw, xo), Mathf.Max(xw, xo), ledge - S, ledge,
                    TowerLayout.LaneMin(k), mid + .15f, floor);
                b.Box(Floor(level + 1) + " 계단 턱 " + TowerHUD.Letter(k), Mathf.Min(turn, xo), Mathf.Max(turn, xo), ledge - S, ledge,
                    mid + .15f, TowerLayout.LaneMax(k), floor);
            }
        }
        float roof = TowerLayout.FloorY(TowerLayout.Levels);
        b.Box("지붕 동", TowerLayout.EastFace, TowerLayout.EastEnd, roof - S, roof, -inner, inner, floor);
        b.Box("지붕 서", TowerLayout.WestEnd, TowerLayout.WestFace, roof - S, roof, -inner, inner, floor);
    }

    // The walls between the lanes' stairwells, all the way up (on the west from above the lobby's entrance hall).
    private static void StairWalls(GreyboxBuilder b)
    {
        float top = TowerLayout.FloorY(TowerLayout.Levels) - S;
        for (int k = 0; k < TowerLayout.Lanes - 1; k++)
        {
            float z0 = TowerLayout.LaneMax(k), z1 = TowerLayout.LaneMin(k + 1);
            b.Box("계단 칸막이 동", TowerLayout.EastFace, TowerLayout.EastEnd, 0f, top, z0, z1, wall);
            b.Box("계단 칸막이 서", TowerLayout.WestEnd, TowerLayout.WestFace, H - S, top, z0, z1, wall);
        }
    }

    // A level's two end walls with their doors: a room's way in on its end half and way out on its start half, a hall's
    // lanes' doors (out to the east, in from the west). The lobby has no west wall: its entrance hall is open to it.
    private static int EndWalls(GreyboxBuilder b, int level)
    {
        var east = new List<(float z, int lane, TowerDoorKind kind)>();
        var west = new List<(float z, int lane, TowerDoorKind kind)>();
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            if (TowerLayout.IsHall(level))
            {
                east.Add((TowerLayout.StartHalf(k), k, TowerDoorKind.Departure));
                if (level > 0) west.Add((TowerLayout.EndHalf(k), k, TowerDoorKind.Arrival));
                continue;
            }
            bool fromEast = TowerLayout.EntersFromEast(level);
            (fromEast ? east : west).Add((TowerLayout.EndHalf(k), k, TowerDoorKind.Entry));
            if (level < TowerLayout.Levels - 1) (fromEast ? west : east).Add((TowerLayout.StartHalf(k), k, TowerDoorKind.Exit));
        }
        int made = WallLine(b, level, true, east);
        if (level > 0) made += WallLine(b, level, false, west);
        return made;
    }

    private static int WallLine(GreyboxBuilder b, int level, bool east, List<(float z, int lane, TowerDoorKind kind)> doors)
    {
        float x0 = east ? TowerLayout.RoomLength : -W, x1 = x0 + W, y0 = TowerLayout.FloorY(level), y1 = TowerLayout.CeilingY(level);
        float half = TowerLayout.DoorWidth * .5f, inner = TowerLayout.InnerHalf;
        string name = Floor(level) + (east ? " 동쪽 벽" : " 서쪽 벽");
        var look = WallAt(level);
        doors.Sort((a, c) => a.z.CompareTo(c.z));
        float from = -inner;
        foreach (var door in doors)
        {
            if (door.z - half > from + .01f) b.Box(name, x0, x1, y0, y1, from, door.z - half, look);
            b.Box(name + " 문 위", x0, x1, y0 + TowerLayout.DoorHeight, y1, door.z - half, door.z + half, look);
            Door(b, level, door.lane, door.kind, east, door.z);
            from = door.z + half;
        }
        if (inner > from + .01f) b.Box(name, x0, x1, y0, y1, from, inner, look);
        return doors.Count;
    }

    // A steel door in an opening, sliding toward the lane's middle; a lamp through the wall over it, seen from both sides;
    // a dark frame round it.
    private static void Door(GreyboxBuilder b, int level, int lane, TowerDoorKind kind, bool east, float z)
    {
        float x = east ? TowerLayout.RoomLength + W * .5f : -W * .5f, y = TowerLayout.FloorY(level);
        float half = TowerLayout.DoorWidth * .5f, top = y + TowerLayout.DoorHeight;
        string name = Floor(level) + " " + TowerHUD.Letter(lane) + " " + Words(kind);
        var shape = b.SlidingPanel("문 " + name, x - .08f, x + .08f, y, top, z - half, z + half, steel, "Metal");
        bool startHalf = kind == TowerDoorKind.Departure || kind == TowerDoorKind.Exit;
        var light = Cube("문 등 " + name, fixtures, new Vector3(x, top + .3f, z), new Vector3(W + .12f, .14f, 1.2f), lamp);
        var door = shape.AddComponent<TowerDoor>();
        GreyboxBuilder.Configure(door, so =>
        {
            so.FindProperty("level").intValue = level;
            so.FindProperty("lane").intValue = lane;
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("openOffset").vector3Value = new Vector3(0f, 0f, startHalf ? 2.5f : -2.5f);
            so.FindProperty("lamp").objectReferenceValue = light.GetComponent<Renderer>();
        });
        const float Frame = .12f, Proud = .06f;
        b.Box("문틀", x - W * .5f - Proud, x + W * .5f + Proud, y, top + Frame, z - half - Frame, z - half, trim, solid: false);
        b.Box("문틀", x - W * .5f - Proud, x + W * .5f + Proud, y, top + Frame, z + half, z + half + Frame, trim, solid: false);
        b.Box("문틀", x - W * .5f - Proud, x + W * .5f + Proud, top, top + Frame, z - half, z + half, trim, solid: false);
    }

    private static string Words(TowerDoorKind kind) => kind switch
    {
        TowerDoorKind.Departure => "출발",
        TowerDoorKind.Arrival => "도착",
        TowerDoorKind.Entry => "입구",
        _ => "출구"
    };

    // A stairwell climbing from this level to the next for every lane: the first flight on the start half away from the
    // rooms, the landing, the second flight back on the end half; a wall between the flights and a lamp over the landing.
    private static void Stairs(GreyboxBuilder b, int level)
    {
        bool east = TowerLayout.ClimbsEast(level);
        float way = east ? 1f : -1f, xw = east ? TowerLayout.EastFace : TowerLayout.WestFace, xo = east ? TowerLayout.EastEnd : TowerLayout.WestEnd;
        float turn = xw + way * TowerLayout.FlightRun, y = TowerLayout.FloorY(level), half = y + H * .5f, up = TowerLayout.FloorY(level + 1);
        float width = TowerLayout.LaneWidth * .5f - .15f;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            string name = Floor(level) + "→" + Floor(level + 1) + " " + TowerHUD.Letter(k);
            float mid = TowerLayout.LaneMid(k), start = (TowerLayout.LaneMin(k) + mid - .15f) * .5f, end = (mid + .15f + TowerLayout.LaneMax(k)) * .5f;
            b.Ramp("계단 " + name + " 1", new Vector3(xw, y, start), new Vector3(turn, half, start), width, floor);
            b.Box("계단참 " + name, Mathf.Min(turn, xo), Mathf.Max(turn, xo), half - .3f, half, TowerLayout.LaneMin(k), TowerLayout.LaneMax(k), floor);
            b.Ramp("계단 " + name + " 2", new Vector3(turn, half, end), new Vector3(xw, up, end), width, floor);
            b.Box("계단 가운데 벽 " + name, Mathf.Min(xw, turn), Mathf.Max(xw, turn), y, up - S, mid - .15f, mid + .15f, wall);
            b.Box("계단 네온 " + name, Mathf.Min(xw, turn), Mathf.Max(xw, turn), up - S - .08f, up - S, mid - .17f, mid + .17f, neon, solid: false);
            Light("계단 등 " + name, new Vector3(turn + way * 1.75f, half + 2.3f, mid), 8f, 1.1f);
        }
    }
}
