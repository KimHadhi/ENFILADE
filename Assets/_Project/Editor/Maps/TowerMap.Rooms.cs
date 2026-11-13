using UnityEngine;

// TowerMap: Rooms. One part of the class; the rest is in TowerMap.cs and the files beside it.
public static partial class TowerMap
{
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
            Fittings(b, level, k);
            // The room's own reflections (Tables/MapReflections Tower.Room): its floor shines with its lights and neon.
            b.Reflections("Tower.Room", 0f, length, TowerLayout.FloorY(level), ceiling, TowerLayout.LaneMin(k), TowerLayout.LaneMax(k));
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
        var look = WallAt(level);
        b.Box("레인 벽 " + name, 0f, from, y, ceiling, z0, z1, look);
        b.Box("레인 벽 " + name, to, TowerLayout.RoomLength, y, ceiling, z0, z1, look);
        b.Box("레인 벽 아래 " + name, from, to, y, sill, z0, z1, look);
        b.Box("레인 벽 위 " + name, from, to, top, ceiling, z0, z1, look);
        float pane = (to - from - (TowerLayout.Panes - 1) * Pillar) / TowerLayout.Panes;
        for (int i = 0; i < TowerLayout.Panes; i++)
        {
            float p0 = from + i * (pane + Pillar), p1 = p0 + pane;
            if (i > 0) b.Box("유리 기둥 " + name, p0 - Pillar, p0, sill, top, z0, z1, look);
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
        var look = WallAt(level);
        for (int row = 0; row < CoverRows; row++)
        {
            float x = TowerLayout.RoomLength * (row + 1) / (CoverRows + 1), side = (level + k + row) % 2 == 0 ? 1f : -1f;
            switch ((level * 3 + k + row) % 4)
            {
                case 0:
                    b.Dress(Across(b, "엄폐 방벽", x - 3.4f, x - .2f, y, y + .85f, mid, side, 1.9f, 2.5f, look), "jersey");
                    b.Dress(Across(b, "엄폐 방벽", x + .2f, x + 3.4f, y, y + .85f, mid, -side, 1.9f, 2.5f, look), "jersey");
                    break;
                case 1:
                    b.PropSolid("woodcrate", new Vector3(x - 2f, y, mid + side * 1.8f), 10f);
                    b.PropSolid("woodcrate", new Vector3(x + 1.5f, y, mid - side * 2.1f), -15f);
                    b.PropSolid("woodcrate", new Vector3(x + 2.4f, y, mid - side * 1.4f), 30f);
                    break;
                case 2:
                    b.Dress(Across(b, "엄폐 블록", x - .8f, x + .8f, y, y + 1.1f, mid, side, -.4f, 1.2f, look), "concreteblock");
                    break;
                default:
                    b.Dress(Across(b, "엄폐 헤스코", x - 2.5f, x - 1.5f, y, y + 1.4f, mid, side, 1.5f, 2.5f, look), "hesco");
                    b.Dress(Across(b, "엄폐 헤스코", x + 1.5f, x + 2.5f, y, y + 1.4f, mid, -side, 1.5f, 2.5f, look), "hesco");
                    break;
            }
        }
    }

    // A cover box from x0 to x1 and y0 to y1, across from near to far metres off the lane's middle on one side of it
    // (side +1 toward higher z, -1 toward lower; a negative near crosses the middle), in the room's wall where its
    // model is missing.
    private static GameObject Across(GreyboxBuilder b, string name, float x0, float x1, float y0, float y1, float mid, float side, float near, float far,
        Material look)
    {
        float a = mid + side * near, c = mid + side * far;
        return b.Box(name, x0, x1, y0, y1, Mathf.Min(a, c), Mathf.Max(a, c), look);
    }

    // A fight room's fittings from the Military Training kit, on its walls and in its entry corner, clear of the cover
    // rows (9 m in and on), the pens and the champion's spot (the far 5 m), the doors and the floor's number: a start
    // line across the lane 2 m in from the way in; a camera high in the entry wall's corner on the lane's low side (the
    // door is on its high half) turned to the room; by the door an extinguisher, then the room's sign; two ammo crates
    // stacked or two boxes in that corner, blocking; spent paper targets pinned on the far wall between the exit and the
    // upper pen, under the number.
    private static void Fittings(GreyboxBuilder b, int level, int k)
    {
        bool fromEast = TowerLayout.EntersFromEast(level);
        float entryX = fromEast ? TowerLayout.RoomLength : 0f, farX = fromEast ? 0f : TowerLayout.RoomLength;
        float inward = fromEast ? -1f : 1f, face = fromEast ? -90f : 90f, y = TowerLayout.FloorY(level);
        float lo = TowerLayout.LaneMin(k), hi = TowerLayout.LaneMax(k), mid = TowerLayout.LaneMid(k), line = entryX + inward * 2f;
        int turn = level * 3 + k;
        b.Line("hubstripe", new Vector3(line, y + StripeLift, lo + .3f), new Vector3(line, y + StripeLift, hi - .3f), 90f);
        b.Mount("hubcamera", new Vector3(entryX, y + 4.9f, lo + .45f), face + (fromEast ? 35f : -35f));
        float door = TowerLayout.EndHalf(k) - TowerLayout.DoorWidth * .5f;
        b.Mount("hubextinguisher", new Vector3(entryX, y + 1.1f, door - .6f), face);
        var (sign, size) = RoomSigns[turn % RoomSigns.Length];
        b.Mount(sign, new Vector3(entryX, y + 1.75f, door - 1.6f), face, size);
        var corner = new Vector3(entryX + inward * .45f, y, lo + .72f);
        if (turn % 2 == 0)
        {
            b.PropSolid("hubammocrate", corner, 90f, 0);
            b.PropSolid("hubammocrate", corner + Vector3.up * b.ArtSize("hubammocrate").y, 84f, 1);
        }
        else
        {
            b.PropSolid("hubcardboard", corner, 90f + turn % 5 * 4f, 1);
            b.PropSolid("hubcardboard", corner + new Vector3(0f, 0f, .7f), 87f, 0);
        }
        b.Mount("hubtarget", new Vector3(farX, y + 1.55f, mid + .2f), face + 180f, 1f, turn % 3);
        b.Mount("hubtarget", new Vector3(farX, y + 1.55f, mid + 1.4f), face + 180f, 1f, (turn + 1) % 3);
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
}
