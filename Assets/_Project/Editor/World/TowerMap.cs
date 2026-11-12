using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the ENFILADE tower's scene (Tools > World > 엔필레이드 타워 짓기) from TowerLayout, with GreyboxBuilder: an old
/// army training block on a fenced square. Inside, four lanes of killhouse rooms climb side by side, level after level,
/// their shared walls glazed with armoured glass so each lane watches the next one's fight; between every three floors a
/// hall across all four lanes. Each room is a long one (TowerLayout.RoomLength) with holding pens at its far end, where its
/// zombies come out (TowerRun), rows of cover down its length and its floor number on the far wall, lit by downlights that
/// shine nothing up through the slab onto the level above. Each hall has its lanes' doors with a palm scanner beside each and a sign
/// over it, a shop counter, supplies and somewhere to sit. Doors are steel panels that slide into the wall (TowerDoor),
/// with a lamp over each. The look is the HUD's: dark slate, white light panels, pink neon (Art/Materials/Tower).
/// Rebuilding wipes hand edits, so the layout lives here and in TowerLayout.
/// </summary>
public static class TowerMap
{
    private const string Title = "엔필레이드 타워", EnemyPrefab = "Assets/_Project/Resources/Enemies/Enemy_Test.prefab";
    private const float W = TowerLayout.Wall, H = TowerLayout.Height, S = TowerLayout.Slab;
    private static readonly Color Cool = new Color(.9f, .94f, 1f), Pink = new Color(1f, .31f, .47f);
    private static Material wall, floor, trim, steel, neon, panel, lamp, glass;
    private static Transform fixtures, lights;

    [MenuItem("Tools/World/엔필레이드 타워 짓기", priority = 30)]
    public static void Build()
    {
        var b = GreyboxBuilder.Begin(TowerRun.SceneName, Title);
        if (b == null) return;
        b.AddsCampaign = false;
        // Real concrete, terrazzo and steel from Tables/MapSurfaces (map dressing stage 4), the flat HUD colours where it
        // has no row; the neon, light panels, lamps and glass stay as they are.
        wall = b.Surface("Wall", GreyboxBuilder.MaterialAt("Tower/TowerWall.mat"));
        floor = b.Surface("Floor", GreyboxBuilder.MaterialAt("Tower/TowerFloor.mat"));
        trim = b.Surface("Trim", GreyboxBuilder.MaterialAt("Tower/TowerTrim.mat"));
        steel = b.Surface("Steel", GreyboxBuilder.MaterialAt("Tower/TowerSteel.mat"));
        neon = GreyboxBuilder.MaterialAt("Tower/TowerNeon.mat");
        panel = GreyboxBuilder.MaterialAt("Tower/TowerPanel.mat");
        lamp = GreyboxBuilder.MaterialAt("Tower/TowerLamp.mat");
        glass = GreyboxBuilder.MaterialAt("Tower/TowerGlass.mat");
        fixtures = b.Group("◤━━ 타워 장치 (스캐너·문 등·표지)");
        lights = b.Group("◤━━ 타워 조명");

        int doors = 0;
        Shell(b);
        Slabs(b);
        StairWalls(b);
        for (int level = 0; level < TowerLayout.Levels; level++)
        {
            doors += EndWalls(b, level);
            if (TowerLayout.IsHall(level)) Hall(b, level);
            else Room(b, level);
            if (level < TowerLayout.Levels - 1) Stairs(b, level);
        }
        Square(b);
        Run(b);
        b.PlayerStart(TowerLayout.StartSpot, 90f);
        // From the square's south-west, low, looking up at the tower's west face (its name, the way in) and along its
        // south face. The second point is where the camera looks, not its angles.
        b.Shot(new Vector3(-60f, 8f, -52f), new Vector3(12f, 32f, 0f));
        b.Finish("엔필레이드 타워를 지었어요(Scenes/Campaign/" + TowerRun.SceneName + ".unity).\n" +
                 "레인 4개 × 10층, 휴게층 3곳과 1층 로비, 문 " + doors + "개, 스캐너 " + TowerLayout.Segments * TowerLayout.Lanes + "개.\n" +
                 "허브의 지도 탁자나 메인 메뉴에서 '엔필레이드 타워'를 고르면 돼요. 오늘의 방 구성은 Tables/TowerFloors, 진행 수치는 Tables/Tower.");
    }

    // ---- The building ----

    // The outer walls all the way up, with the way in on the west face.
    private static void Shell(GreyboxBuilder b)
    {
        float top = TowerLayout.FloorY(TowerLayout.Levels), x0 = TowerLayout.WestEnd - W, x1 = TowerLayout.EastEnd + W;
        float inner = TowerLayout.InnerHalf, outer = TowerLayout.OuterHalf;
        const float GateHalf = 2.4f, GateTop = 4.2f;
        b.Box("외벽 남", x0, x1, -.5f, top, -outer, -inner, wall);
        b.Box("외벽 북", x0, x1, -.5f, top, inner, outer, wall);
        b.Box("외벽 동", TowerLayout.EastEnd, x1, -.5f, top, -inner, inner, wall);
        b.Box("외벽 서", x0, TowerLayout.WestEnd, -.5f, top, -inner, -GateHalf, wall);
        b.Box("외벽 서", x0, TowerLayout.WestEnd, -.5f, top, GateHalf, inner, wall);
        b.Box("입구 위", x0, TowerLayout.WestEnd, GateTop, top, -GateHalf, GateHalf, wall);
        // The doorway's own floor, inside the wall's thickness, between the square and the lobby.
        b.Box("입구 문턱", x0, TowerLayout.WestEnd, -.5f, 0f, -GateHalf, GateHalf, floor);
        b.Box("입구 틀 위", x0 - .1f, TowerLayout.WestEnd + .1f, GateTop - .12f, GateTop, -GateHalf - .1f, GateHalf + .1f, neon, solid: false);
    }

    // The rooms' floors (and the roof), the stairwells' floors, and over each stairwell's first flight and landing the
    // ledge by the next room's door.
    private static void Slabs(GreyboxBuilder b)
    {
        float inner = TowerLayout.InnerHalf;
        b.Box("로비 바닥", TowerLayout.WestEnd, TowerLayout.EastEnd, -.5f, 0f, -inner, inner, floor);
        for (int level = 1; level <= TowerLayout.Levels; level++)
        {
            float top = TowerLayout.FloorY(level);
            b.Box(level == TowerLayout.Levels ? "지붕" : Floor(level) + " 바닥", TowerLayout.WestFace, TowerLayout.EastFace, top - S, top, -inner, inner, floor);
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
        doors.Sort((a, c) => a.z.CompareTo(c.z));
        float from = -inner;
        foreach (var door in doors)
        {
            if (door.z - half > from + .01f) b.Box(name, x0, x1, y0, y1, from, door.z - half, wall);
            b.Box(name + " 문 위", x0, x1, y0 + TowerLayout.DoorHeight, y1, door.z - half, door.z + half, wall);
            Door(b, level, door.lane, door.kind, east, door.z);
            from = door.z + half;
        }
        if (inner > from + .01f) b.Box(name, x0, x1, y0, y1, from, inner, wall);
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

    // ---- Rooms ----

    // Lights down a room's or a hall's length per lane, and rows of cover down a room's.
    private const int RoomLights = 3, CoverRows = 3;

    // A fight level: for every lane its glass to the next lane, its lights, its holding pens, some cover and its number.
    private static void Room(GreyboxBuilder b, int level)
    {
        float ceiling = TowerLayout.CeilingY(level), length = TowerLayout.RoomLength;
        int number = TowerLayout.FloorOf(level);
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            float mid = TowerLayout.LaneMid(k);
            if (k < TowerLayout.Lanes - 1) GlassWall(b, level, k);
            foreach (float z in new[] { mid - 2.2f, mid + 2.2f })
                b.Box("천장 등", 3f, length - 3f, ceiling - .06f, ceiling, z - .15f, z + .15f, panel, solid: false);
            for (int i = 0; i < RoomLights; i++)
                Downlight("방 등 " + Floor(level) + " " + TowerHUD.Letter(k), new Vector3(length * (i + .5f) / RoomLights, ceiling - .5f, mid), 11f, 2.4f);
            Pens(b, level, k);
            Cover(b, level, k);
            FloorMark(level, k, number);
        }
    }

    // The long wall a room shares with the next lane's: solid at its ends, over and under armoured glass along its
    // middle, the glass in panes between pillars, with a dark frame round each pane and a pink strip along the top.
    private static void GlassWall(GreyboxBuilder b, int level, int k)
    {
        float z0 = TowerLayout.LaneMax(k), z1 = TowerLayout.LaneMin(k + 1), zc = (z0 + z1) * .5f;
        float y = TowerLayout.FloorY(level), ceiling = TowerLayout.CeilingY(level);
        float from = TowerLayout.WindowFrom, to = TowerLayout.WindowTo, sill = y + TowerLayout.SillHeight, top = y + TowerLayout.WindowTop;
        const float Pillar = TowerLayout.PillarWidth;
        string name = Floor(level) + " " + TowerHUD.Letter(k) + "|" + TowerHUD.Letter(k + 1);
        b.Box("레인 벽 " + name, 0f, from, y, ceiling, z0, z1, wall);
        b.Box("레인 벽 " + name, to, TowerLayout.RoomLength, y, ceiling, z0, z1, wall);
        b.Box("레인 벽 아래 " + name, from, to, y, sill, z0, z1, wall);
        b.Box("레인 벽 위 " + name, from, to, top, ceiling, z0, z1, wall);
        float pane = (to - from - (TowerLayout.Panes - 1) * Pillar) / TowerLayout.Panes;
        for (int i = 0; i < TowerLayout.Panes; i++)
        {
            float p0 = from + i * (pane + Pillar), p1 = p0 + pane;
            if (i > 0) b.Box("유리 기둥 " + name, p0 - Pillar, p0, sill, top, z0, z1, wall);
            b.SeeThrough(b.Box("방탄 유리 " + name, p0, p1, sill, top, zc - .04f, zc + .04f, glass, "ArmorGlass"));
            b.Box("유리 틀", p0 - .08f, p0, sill, top, z0 - .05f, z1 + .05f, trim, solid: false);
            b.Box("유리 틀", p1, p1 + .08f, sill, top, z0 - .05f, z1 + .05f, trim, solid: false);
        }
        b.Box("유리 틀", from - .08f, to + .08f, sill - .06f, sill, z0 - .05f, z1 + .05f, trim, solid: false);
        b.Box("유리 네온", from - .08f, to + .08f, top, top + .07f, z0 - .05f, z1 + .05f, neon, solid: false);
    }

    // Two holding pens at the room's far corners, barred on the side facing the way in: where its zombies come out.
    private static void Pens(GreyboxBuilder b, int level, int k)
    {
        bool fromEast = TowerLayout.EntersFromEast(level);
        float wallX = fromEast ? 0f : TowerLayout.RoomLength, inward = fromEast ? 1f : -1f, y = TowerLayout.FloorY(level);
        float front = wallX + inward * 2f, lo = Mathf.Min(wallX, front), hi = Mathf.Max(wallX, front);
        foreach (bool upper in new[] { false, true })
        {
            float z0 = upper ? TowerLayout.LaneMax(k) - 2.6f : TowerLayout.LaneMin(k), z1 = upper ? TowerLayout.LaneMax(k) : TowerLayout.LaneMin(k) + 2.6f;
            b.Box("우리 지붕", lo, hi, y + 2.65f, y + 2.8f, z0, z1, trim, solid: false);
            b.Box("우리 네온", front - .04f, front + .04f, y + 2.6f, y + 2.68f, z0, z1, neon, solid: false);
            for (float z = z0 + .25f; z < z1 - .1f; z += .48f)
                b.Box("우리 창살", front - .03f, front + .03f, y, y + 2.65f, z - .03f, z + .03f, steel, solid: false);
        }
    }

    // Cover down the room: a row at each quarter of its length, each row one of four layouts and leaning to one side of
    // the lane's middle, the side alternating row by row, so the long room is crossed from cover to cover. Out of the way
    // of the doors (in the end walls), the pens and the champion's spot (within 5 m of the far wall) and the glass (kept
    // 2 m off the long walls).
    private static void Cover(GreyboxBuilder b, int level, int k)
    {
        float y = TowerLayout.FloorY(level), mid = TowerLayout.LaneMid(k);
        for (int row = 0; row < CoverRows; row++)
        {
            float x = TowerLayout.RoomLength * (row + 1) / (CoverRows + 1), side = (level + k + row) % 2 == 0 ? 1f : -1f;
            switch ((level * 3 + k + row) % 4)
            {
                case 0:
                    b.Dress(Across(b, "엄폐 방벽", x - 3.4f, x - .2f, y, y + .85f, mid, side, 1.9f, 2.5f), "jersey");
                    b.Dress(Across(b, "엄폐 방벽", x + .2f, x + 3.4f, y, y + .85f, mid, -side, 1.9f, 2.5f), "jersey");
                    break;
                case 1:
                    b.PropSolid("woodcrate", new Vector3(x - 2f, y, mid + side * 1.8f), 10f);
                    b.PropSolid("woodcrate", new Vector3(x + 1.5f, y, mid - side * 2.1f), -15f);
                    b.PropSolid("woodcrate", new Vector3(x + 2.4f, y, mid - side * 1.4f), 30f);
                    break;
                case 2:
                    b.Dress(Across(b, "엄폐 블록", x - .8f, x + .8f, y, y + 1.1f, mid, side, -.4f, 1.2f), "concreteblock");
                    break;
                default:
                    b.Dress(Across(b, "엄폐 헤스코", x - 2.5f, x - 1.5f, y, y + 1.4f, mid, side, 1.5f, 2.5f), "hesco");
                    b.Dress(Across(b, "엄폐 헤스코", x + 1.5f, x + 2.5f, y, y + 1.4f, mid, -side, 1.5f, 2.5f), "hesco");
                    break;
            }
        }
    }

    // A cover box from x0 to x1 and y0 to y1, across from near to far metres off the lane's middle on one side of it
    // (side +1 toward higher z, -1 toward lower; a negative near crosses the middle).
    private static GameObject Across(GreyboxBuilder b, string name, float x0, float x1, float y0, float y1, float mid, float side, float near, float far)
    {
        float a = mid + side * near, c = mid + side * far;
        return b.Box(name, x0, x1, y0, y1, Mathf.Min(a, c), Mathf.Max(a, c), wall);
    }

    // The floor's number large on the far wall, read from the way in, big enough to read from the far end of the room.
    private static void FloorMark(int level, int k, int number)
    {
        bool fromEast = TowerLayout.EntersFromEast(level);
        float wallX = fromEast ? 0f : TowerLayout.RoomLength, inward = fromEast ? 1f : -1f;
        var place = new Vector3(wallX + inward * .06f, TowerLayout.FloorY(level) + 3.9f, TowerLayout.LaneMid(k));
        Words3D("층 표시 " + number + "F " + TowerHUD.Letter(k), place, fromEast ? Vector3.left : Vector3.right,
            "<b>" + number + "</b><size=45%>F</size>", 20f, Pink, new Vector2(6f, 3f));
    }

    // ---- Halls ----

    // A hall across the four lanes: light, the lanes' scanners and signs, a shop counter, supplies, somewhere to sit.
    private static void Hall(GreyboxBuilder b, int level)
    {
        int segment = level / 4;
        float y = TowerLayout.FloorY(level), ceiling = TowerLayout.CeilingY(level), inner = TowerLayout.InnerHalf, length = TowerLayout.RoomLength;
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            float z = TowerLayout.LaneMid(k);
            b.Box("홀 천장 등", 2f, length - 2f, ceiling - .06f, ceiling, z - .2f, z + .2f, panel, solid: false);
            for (int i = 0; i < RoomLights; i++)
                Downlight("홀 등 " + Floor(level), new Vector3(length * (i + .5f) / RoomLights, ceiling - .5f, z), 12f, 2.2f);
        }
        // The way on: a pink band along the east wall over the doors, and a scanner, a sign and a spot to stand by each.
        b.Box("홀 네온", TowerLayout.RoomLength - .04f, TowerLayout.RoomLength, y + 4.9f, y + 5f, -inner, inner, neon, solid: false);
        for (int k = 0; k < TowerLayout.Lanes; k++)
        {
            Scanner(segment, k);
            Sign(segment, k);
            Vector3 at = TowerLayout.ScannerAt(segment, k);
            Vector3 stand = at + Vector3.left * .75f;
            b.Box("스캐너 자리", stand.x - .55f, stand.x + .55f, y, y + .01f, stand.z - .55f, stand.z - .5f, neon, solid: false);
            b.Box("스캐너 자리", stand.x - .55f, stand.x + .55f, y, y + .01f, stand.z + .5f, stand.z + .55f, neon, solid: false);
            b.Box("스캐너 자리", stand.x - .55f, stand.x - .5f, y, y + .01f, stand.z - .5f, stand.z + .5f, neon, solid: false);
        }
        // The shop: a counter with a stand in front of it (TowerLayout.ShopAt).
        Vector3 shop = TowerLayout.ShopAt(segment);
        b.Dress(b.Box("상점 카운터", shop.x + .7f, shop.x + 1.7f, y, y + 1.05f, shop.z - 1.6f, shop.z + 1.6f, trim), "shopcounter");
        b.Box("상점 네온", shop.x + 1.1f, shop.x + 1.3f, y + 2.7f, y + 3f, shop.z - 1.8f, shop.z + 1.8f, neon, solid: false);
        b.Prop("vending", new Vector3(shop.x + 1.4f, y, shop.z + 3f), 270f);
        // Supplies by the walls where people arrive, and somewhere to sit out of everyone's way: a corner by the arrivals
        // and another past the shop.
        b.Pickup(new Vector3(2.4f, y, 13.5f));
        if (segment > 0) b.Pickup(new Vector3(2.4f, y, -13.5f));
        b.Dress(b.Box("소파", 5f, 7.4f, y, y + .85f, inner - 1f, inner, wall), "couch");
        b.Dress(b.Box("소파", 9f, 11.4f, y, y + .85f, -inner, -inner + 1f, wall), "couch");
        b.Dress(b.Box("소파", length * .62f, length * .62f + 2.4f, y, y + .85f, inner - 1f, inner, wall), "couch");
        b.Dress(b.Box("소파", length * .68f, length * .68f + 2.4f, y, y + .85f, -inner, -inner + 1f, wall), "couch");
        b.Prop("plant", new Vector3(1f, y, inner - 1f));
        b.Prop("plant", new Vector3(length - 1f, y, -inner + 1f));
        b.Prop("plant", new Vector3(length * .5f, y, inner - .8f));
        b.Prop("plant", new Vector3(length * .5f, y, -inner + .8f));
        b.Prop("locker", new Vector3(length - 3.5f, y, inner - .4f), 180f);
        b.Prop("locker", new Vector3(length - 2.8f, y, inner - .4f), 180f);
        b.Ambience("Tower.Hall", 0f, TowerLayout.RoomLength, y, ceiling, -inner, inner, 2);
        if (segment == 0) Lobby(b);
        // The hall's name over its arrivals, read from its middle (18 m off: larger than at 16 m).
        string label = segment == 0 ? "LOBBY" : "REST · " + TowerLayout.FloorOf(level - 1) + "F";
        Words3D("홀 이름 " + label, new Vector3(.2f, y + 4.2f, 0f), Vector3.left, "<b>" + label + "</b>", 9f, Color.white, new Vector2(16f, 1.8f));
    }

    // The lobby's entrance hall: a desk, benches and the tower's name over the way in.
    private static void Lobby(GreyboxBuilder b)
    {
        b.Dress(b.Box("안내 데스크", -7f, -4.6f, 0f, 1.1f, 8.5f, 11f, trim), "infodesk");
        b.Dress(b.Box("의자 줄", -9.6f, -5f, 0f, .9f, -17.8f, -17f, wall), "seats");
        b.Dress(b.Box("의자 줄", -9.6f, -5f, 0f, .9f, 17f, 17.8f, wall), "seats");
        b.Box("로비 천장 등", -9f, -1.5f, H - S - .06f, H - S, -.3f, .3f, panel, solid: false);
        Downlight("로비 등", new Vector3(-5.5f, H - S - .5f, 0f), 12f, 2.2f);
        Words3D("로비 이름", new Vector3(-.3f, 4.1f, 0f), Vector3.right, "<b>ENFILADE</b>  TOWER", 7f, Pink, new Vector2(14f, 1.4f));
    }

    // A palm scanner by a lane's door, sunk in the floor until the hall chooses (TowerScanner).
    private static void Scanner(int segment, int lane)
    {
        var root = new GameObject("스캐너 " + segment + " " + TowerHUD.Letter(lane), typeof(TowerScanner));
        root.transform.SetParent(fixtures, false);
        root.transform.SetPositionAndRotation(TowerLayout.ScannerAt(segment, lane), Quaternion.LookRotation(Vector3.left, Vector3.up));
        // It rises out of the floor: its surfaces mapped in its own space, so they rise with it.
        Child(root.transform, "기둥", new Vector3(0f, .5f, 0f), new Vector3(.34f, 1f, .34f), GreyboxBuilder.MovingSurface(steel), true);
        var head = new GameObject("머리").transform;
        head.SetParent(root.transform, false);
        head.localPosition = new Vector3(0f, 1.04f, .03f);
        head.localRotation = Quaternion.Euler(35f, 0f, 0f);
        Child(head, "받침", new Vector3(0f, -.03f, 0f), new Vector3(.44f, .04f, .36f), GreyboxBuilder.MovingSurface(trim), false);
        var plate = Child(head, "손판", Vector3.zero, new Vector3(.4f, .035f, .32f), lamp, false);
        var line = Child(head, "인식선", new Vector3(0f, .022f, 0f), new Vector3(.36f, .012f, .025f), neon, false);
        var strip = Child(root.transform, "띠", new Vector3(0f, .5f, .175f), new Vector3(.07f, .78f, .02f), lamp, false);
        GreyboxBuilder.Configure(root.GetComponent<TowerScanner>(), so =>
        {
            so.FindProperty("segment").intValue = segment;
            so.FindProperty("lane").intValue = lane;
            so.FindProperty("plate").objectReferenceValue = plate.GetComponent<Renderer>();
            so.FindProperty("strip").objectReferenceValue = strip.GetComponent<Renderer>();
            so.FindProperty("scanLine").objectReferenceValue = line.transform;
        });
    }

    // The sign over a lane's door; the run writes the day's rooms on it (TowerSign).
    private static void Sign(int segment, int lane)
    {
        float y = TowerLayout.FloorY(TowerLayout.HallLevel(segment));
        var sign = new GameObject("표지 " + segment + " " + TowerHUD.Letter(lane), typeof(RectTransform));
        sign.transform.SetParent(fixtures, false);
        sign.transform.SetPositionAndRotation(new Vector3(TowerLayout.RoomLength - .25f, y + TowerLayout.DoorHeight + 1.25f, TowerLayout.StartHalf(lane) + .6f),
            Quaternion.LookRotation(Vector3.right, Vector3.up));
        GreyboxBuilder.Configure(sign.AddComponent<TowerSign>(), so =>
        {
            so.FindProperty("segment").intValue = segment;
            so.FindProperty("lane").intValue = lane;
        });
    }

    // ---- Outside ----

    // The square in front of the west face: the army's checkpoint at the tower's door, lamps, the edge of the map, and
    // the tower's own neon up its corners and round each hall.
    private static void Square(GreyboxBuilder b)
    {
        float x0 = TowerLayout.WestEnd - W, x1 = TowerLayout.EastEnd + W, outer = TowerLayout.OuterHalf, top = TowerLayout.FloorY(TowerLayout.Levels);
        // The map's east edge keeps a strip of ground behind the tower however long it is.
        float east = x1 + 14f;
        b.Box("땅 서", -80f, x0, -.5f, 0f, -60f, 60f, b.Road, "Asphalt");
        b.Box("땅 동", x1, east, -.5f, 0f, -60f, 60f, b.Road, "Asphalt");
        b.Box("땅 남", x0, x1, -.5f, 0f, -60f, -outer, b.Road, "Asphalt");
        b.Box("땅 북", x0, x1, -.5f, 0f, outer, 60f, b.Road, "Asphalt");
        b.Box("맵 끝 서", -81f, -80f, 0f, 5f, -60f, 60f);
        b.Box("맵 끝 동", east, east + 1f, 0f, 5f, -60f, 60f);
        b.Box("맵 끝 남", -80f, east, 0f, 5f, -61f, -60f);
        b.Box("맵 끝 북", -80f, east, 0f, 5f, 60f, 61f);
        // Neon up the four corners and round the halls' levels.
        foreach (float x in new[] { x0 - .06f, x1 + .06f })
            foreach (float z in new[] { -outer - .06f, outer + .06f })
                b.Box("탑 네온", x - .1f, x + .1f, 0f, top, z - .1f, z + .1f, neon, solid: false);
        for (int segment = 1; segment < TowerLayout.Segments; segment++)
        {
            float y = TowerLayout.FloorY(TowerLayout.HallLevel(segment)) - .2f;
            b.Box("탑 띠", x0 - .08f, x0, y, y + .25f, -outer, outer, neon, solid: false);
            b.Box("탑 띠", x1, x1 + .08f, y, y + .25f, -outer, outer, neon, solid: false);
            b.Box("탑 띠", x0, x1, y, y + .25f, -outer - .08f, -outer, neon, solid: false);
            b.Box("탑 띠", x0, x1, y, y + .25f, outer, outer + .08f, neon, solid: false);
        }
        Words3D("탑 이름", new Vector3(x0 - .15f, 8.2f, 0f), Vector3.right, "<b>ENFILADE</b>", 40f, Color.white, new Vector2(34f, 5f));
        Words3D("탑 이름 아래", new Vector3(x0 - .15f, 5.6f, 0f), Vector3.right, "TOWER  ·  TRAINING BLOCK 7", 10f, Pink, new Vector2(30f, 1.6f));
        // The army's checkpoint: a funnel of barriers to the door, sandbags, a guard tower, a lorry, lamps.
        float door = x0 - 1f;
        b.Dress(b.Box("검문 방벽", door - 16f, door - 2f, 0f, .9f, -6.5f, -5.9f), "jersey");
        b.Dress(b.Box("검문 방벽", door - 16f, door - 2f, 0f, .9f, 5.9f, 6.5f), "jersey");
        b.Dress(b.Box("모래주머니", door - 9f, door - 7.8f, 0f, 1.2f, -14f, -8f), "sandbags");
        b.Dress(b.Box("모래주머니", door - 9f, door - 7.8f, 0f, 1.2f, 8f, 14f), "sandbags");
        b.PropSolid("guardtower", new Vector3(door - 6f, 0f, -18f), 90f);
        b.PropSolid("militarytruck", new Vector3(door - 24f, 0f, 16f), 0f);
        b.Prop("warninglight", new Vector3(door - 2.5f, 0f, -5.3f), 90f);
        b.Prop("warninglight", new Vector3(door - 2.5f, 0f, 5.3f), 90f);
        foreach (float z in new[] { -22f, 22f }) b.Prop("streetlamp", new Vector3(door - 12f, 0f, z), z < 0f ? 0f : 180f);
        foreach (var (x, z) in new[] { (door - 20f, -9f), (door - 21f, -8.2f), (door - 19.5f, 9f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 17f % 360f);
        Light("입구 등", new Vector3(x0 - 2f, 4.6f, 0f), 12f, 1.8f);
    }

    // The run of the tower (TowerRun) and the inside's sound.
    private static void Run(GreyboxBuilder b)
    {
        var run = b.AddSystem("타워 진행").AddComponent<TowerRun>();
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
        GreyboxBuilder.Configure(run, so => so.FindProperty("enemyPrefab").objectReferenceValue = enemy != null ? enemy.GetComponent<EnemyHealth>() : null);
        b.Ambience("Tower.Inside", TowerLayout.WestEnd, TowerLayout.EastEnd, 0f, TowerLayout.FloorY(TowerLayout.Levels), -TowerLayout.InnerHalf,
            TowerLayout.InnerHalf, 1);
    }

    // ---- Helpers ----

    private static string Floor(int level) => TowerLayout.IsHall(level) ? (level == 0 ? "로비" : "휴게층" + level / 4) : TowerLayout.FloorOf(level) + "층";

    // A point light with no shadows, in the tower's cool white.
    private static Light Light(string name, Vector3 at, float range, float intensity)
    {
        var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
        light.transform.SetParent(lights, false);
        light.transform.position = at;
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = Cool;
        light.shadows = LightShadows.None;
        return light;
    }

    // A wide spot under a ceiling shining down, with no shadows. Not a point light: without shadows a point light shines
    // through the slab over it onto the walls of the level above, and the long floors need three times the lights; a cone
    // pointing down lights nothing above its own height.
    private static void Downlight(string name, Vector3 at, float range, float intensity)
    {
        var light = Light(name, at, range, intensity);
        light.type = LightType.Spot;
        light.spotAngle = 150f;
        light.innerSpotAngle = 100f;
        light.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
    }

    // A cube outside the terrain (no walk mesh, not static): a fixture's part.
    private static GameObject Cube(string name, Transform parent, Vector3 centre, Vector3 size, Material material, bool solid = false)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.position = centre;
        cube.transform.localScale = size;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) Object.DestroyImmediate(cube.GetComponent<Collider>());
        return cube;
    }

    // A cube placed in its parent's space.
    private static GameObject Child(Transform parent, string name, Vector3 local, Vector3 size, Material material, bool solid)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = local;
        cube.transform.localScale = size;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) Object.DestroyImmediate(cube.GetComponent<Collider>());
        return cube;
    }

    // Words standing in the world, read by someone looking along <paramref name="looking"/>, in the HUD's label face.
    private static void Words3D(string name, Vector3 at, Vector3 looking, string words, float size, Color colour, Vector2 box)
    {
        var holder = new GameObject(name, typeof(RectTransform));
        holder.transform.SetParent(fixtures, false);
        holder.transform.SetPositionAndRotation(at, Quaternion.LookRotation(looking, Vector3.up));
        var text = holder.AddComponent<TextMeshPro>();
        FontRoles.Apply(text, FontRoles.Label);
        text.rectTransform.sizeDelta = box;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.fontSize = size;
        text.color = colour;
        text.text = words;
    }
}
