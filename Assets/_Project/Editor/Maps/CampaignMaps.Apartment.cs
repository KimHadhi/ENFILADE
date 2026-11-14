using System.Collections.Generic;
using UnityEngine;

// CampaignMaps: chapter 1's block of flats as the map gimmicks remade it. One part of the class; the rest is in
// CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    // B4, the block of flats (the user, 2026-11-14, of the map gimmick card "아파트를 암살 놀이터로": "아파트 좀 넓게 해도
    // 돼"): x 87-104, z -16..16 (32 m deep, was 20), six storeys of 3 m and a roof at 18, and a cellar under its south-west
    // flat. On each floor a corridor along x (z -1.2..1.2) and four flats off it, k01 north-west, k02 north-east (round the
    // stairs), k03 south-east (round the lift) and k04 south-west, each a front room and a back room with a balcony.
    private const float AptX0 = 87f, AptX1 = 104f, AptZ0 = -16f, AptZ1 = 16f, AptRoof = 18f, Storey = 3f, AptCeiling = 2.7f;
    private const float AptIn0 = AptX0 + Wall, AptIn1 = AptX1 - Wall, AptS0 = AptZ0 + Wall, AptN1 = AptZ1 - Wall;
    // The party wall between the west and the east flats, the stairs (north-east) and the lift (south-east).
    private const float PartyX = 95.3f, CoreX = 99.7f, CoreZ1 = 10f, ShaftX = 100.6f, ShaftZ = -4.3f;
    private const float FrontNorth = 8.5f, FrontSouth = -8.5f, DoorHigh = 2.3f;
    private const int AptStoreys = 6;

    // The way through (top down): the swing bar puts them into 501 through the west wall the war brought down (5th and
    // 6th floor); the caretaker's flat 503 has the log that says which flat the master key is in this run (2nd to 4th
    // floor) and the one can of disinfectant gas for a flat's vent; each floor down there is a way by the stairs (one
    // flight fallen in each run), a hole through a flat's floor (one of two each run), a fire ladder between balconies, or
    // the lift (loud: it chimes and wakes the floor it opens on; its power is on the roof); the ground floor's stair door is
    // chained (the master key, or smashed in, loud); from the ground floor's corridor the cellar's switchboard powers the
    // alley's shutter and starts its hold, and the back door at the corridor's east end lets out into the alley.
    private static void ApartmentBlock(GreyboxBuilder b)
    {
        Rect3 floorsBox(int f) => new Rect3(AptIn0, AptIn1, f * Storey - .2f, f * Storey + AptCeiling + .1f, AptS0, AptN1);

        AptShell(b);
        for (int f = 0; f < AptStoreys; f++) AptStorey(b, f);
        AptSlabs(b);
        AptStairs(b);
        AptBalconies(b);
        AptCellar(b);
        var lift = AptLift(b, floorsBox);
        AptRoofTop(b, lift);
        AptVariants(b);
        AptGimmicks(b, floorsBox);
        AptCrowds(b);
        AptDressing(b);
    }

    // A box's extents (min-max on each axis), and its Bounds.
    private readonly struct Rect3
    {
        public readonly float X0, X1, Y0, Y1, Z0, Z1;
        public Rect3(float x0, float x1, float y0, float y1, float z0, float z1) { X0 = x0; X1 = x1; Y0 = y0; Y1 = y1; Z0 = z0; Z1 = z1; }
        public Bounds Bounds => new Bounds(new Vector3((X0 + X1) * .5f, (Y0 + Y1) * .5f, (Z0 + Z1) * .5f), new Vector3(X1 - X0, Y1 - Y0, Z1 - Z0));
    }

    // ---- Walls and slabs with openings ----

    // A flat face (u0..u1 by v0..v1) with rectangular openings, as few boxes as a grid of its edges allows: each band of
    // v cut into runs along u that miss every opening, a run carried on into the next band while it is the same.
    private static void Pierced(float u0, float u1, float v0, float v1, IList<(float u0, float u1, float v0, float v1)> holes,
        System.Action<int, float, float, float, float> box)
    {
        var us = new List<float> { u0, u1 };
        var vs = new List<float> { v0, v1 };
        foreach (var h in holes)
        {
            us.Add(Mathf.Clamp(h.u0, u0, u1)); us.Add(Mathf.Clamp(h.u1, u0, u1));
            vs.Add(Mathf.Clamp(h.v0, v0, v1)); vs.Add(Mathf.Clamp(h.v1, v0, v1));
        }
        us.Sort(); vs.Sort();
        Dedupe(us); Dedupe(vs);
        var open = new Dictionary<(float, float), float>();
        int made = 0;
        for (int j = 0; j + 1 < vs.Count; j++)
        {
            float vm = (vs[j] + vs[j + 1]) * .5f;
            var runs = new List<(float, float)>();
            float start = float.NaN;
            for (int i = 0; i + 1 < us.Count; i++)
            {
                float um = (us[i] + us[i + 1]) * .5f;
                bool cut = false;
                foreach (var h in holes) if (um > h.u0 && um < h.u1 && vm > h.v0 && vm < h.v1) { cut = true; break; }
                if (!cut && float.IsNaN(start)) start = us[i];
                if (cut && !float.IsNaN(start)) { runs.Add((start, us[i])); start = float.NaN; }
            }
            if (!float.IsNaN(start)) runs.Add((start, us[us.Count - 1]));
            // Runs that don't go on into this band end now.
            var ended = new List<(float, float)>();
            foreach (var key in open.Keys) if (!runs.Contains(key)) ended.Add(key);
            foreach (var key in ended) { box(made++, key.Item1, key.Item2, open[key], vs[j]); open.Remove(key); }
            foreach (var run in runs) if (!open.ContainsKey(run)) open[run] = vs[j];
        }
        foreach (var pair in open) box(made++, pair.Key.Item1, pair.Key.Item2, pair.Value, vs[vs.Count - 1]);
    }

    private static void Dedupe(List<float> values)
    {
        for (int i = values.Count - 1; i > 0; i--) if (values[i] - values[i - 1] < .005f) values.RemoveAt(i);
    }

    // A wall running along x (thickness z0..z1) from y0 to y1, openings (x from, x to, y from, y to).
    private static void WallX(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float y0, float y1,
        params (float, float, float, float)[] openings)
        => Pierced(x0, x1, y0, y1, openings, (i, a0, a1, c0, c1) => b.Box($"{name} {i + 1}", a0, a1, c0, c1, z0, z1));

    // A wall running along z (thickness x0..x1), openings (z from, z to, y from, y to).
    private static void WallZ(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float y0, float y1,
        params (float, float, float, float)[] openings)
        => Pierced(z0, z1, y0, y1, openings, (i, a0, a1, c0, c1) => b.Box($"{name} {i + 1}", x0, x1, c0, c1, a0, a1));

    // A floor slab with its top at y, holes (x from, x to, z from, z to).
    private static void SlabWith(GreyboxBuilder b, string name, float y, float x0, float x1, float z0, float z1,
        Material material, string surface, params (float, float, float, float)[] holes)
        => Pierced(x0, x1, z0, z1, holes, (i, a0, a1, c0, c1) => b.Box($"{name} {i + 1}", a0, a1, y - Wall, y, c0, c1, material, surface));

    // A door's opening in a wall: from..to along the wall, from the floor up to a door's height.
    private static (float, float, float, float) DoorIn(float from, float to, float floor, float high = DoorHigh) => (from, to, floor, floor + high);

    // ---- The shell ----

    private static void AptShell(GreyboxBuilder b)
    {
        // West: the hole the war blew in the 5th and 6th floors, where the swing bar lands them (z 2..10, from the 5th floor
        // up). The front entrance at the corridor's west end is bricked up behind its boards (AptDressing).
        WallZ(b, "B4 서벽", AptX0, AptIn0, AptZ0, AptZ1, 0f, AptRoof, (2f, 10f, 12f, AptRoof));
        // East: the back door into the alley.
        WallZ(b, "B4 동벽", AptIn1, AptX1, AptZ0, AptZ1, 0f, AptRoof, DoorIn(-1.1f, 1.1f, 0f));
        // North and south: the balcony doors, floors 2-6.
        var north = new List<(float, float, float, float)>();
        var south = new List<(float, float, float, float)>();
        for (int f = 1; f < AptStoreys; f++)
        {
            float y = f * Storey;
            north.Add(DoorIn(91f, 92.2f, y, 2.2f)); north.Add(DoorIn(100f, 101.2f, y, 2.2f));
            south.Add(DoorIn(91f, 92.2f, y, 2.2f)); south.Add(DoorIn(98f, 99.2f, y, 2.2f));
        }
        WallX(b, "B4 북벽", AptIn0, AptIn1, AptN1, AptZ1, 0f, AptRoof, north.ToArray());
        WallX(b, "B4 남벽", AptIn0, AptIn1, AptZ0, AptS0, 0f, AptRoof, south.ToArray());
        // The roof's parapets, open over the fallen corner.
        b.Box("B4 난간 북", AptX0, AptX1, AptRoof, AptRoof + ParapetHeight, AptN1, AptZ1);
        b.Box("B4 난간 남", AptX0, AptX1, AptRoof, AptRoof + ParapetHeight, AptZ0, AptS0);
        b.Box("B4 난간 동", AptIn1, AptX1, AptRoof, AptRoof + ParapetHeight, AptS0, AptN1);
        b.Box("B4 난간 서 남", AptX0, AptIn0, AptRoof, AptRoof + ParapetHeight, AptS0, 2f);
        b.Box("B4 난간 서 북", AptX0, AptIn0, AptRoof, AptRoof + ParapetHeight, 10f, AptN1);
        // The broken edges of the fallen corner: torn slab ends and rebar (seen only).
        b.Box("B4 무너진 모서리 조각 1", AptX0 - .4f, AptX0 + .6f, 14.6f, 15f, 2.6f, 5f, solid: false);
        b.Box("B4 무너진 모서리 조각 2", AptX0 - .2f, AptX0 + .9f, 17.4f, 17.8f, 6f, 9.4f, solid: false);
        b.Box("B4 무너진 철근", AptX0 - .6f, AptIn0 + 2.4f, 14.75f, 14.8f, 4f, 4.05f, b.Metal, solid: false);
    }

    // One storey's walls: the corridor's two walls with the flats' doors, the stairs' and the lift's, the party walls between
    // the flats (with the holes the variants plug or not), each flat's wall between its front and back rooms, and the lift's.
    private static void AptStorey(GreyboxBuilder b, int f)
    {
        float y = f * Storey, top = y + AptCeiling;
        string floor = $"B4 {f + 1}층";
        WallX(b, floor + " 복도 북벽", AptIn0, CoreX - Wall, 1.2f, 1.5f, y, top, DoorIn(92f, 93.2f, y), DoorIn(97f, 98.2f, y));
        // The stairs' door, the corridor's north wall from the stairs on.
        WallX(b, floor + " 계단 문 벽", CoreX - Wall, AptIn1, 1.2f, 1.5f, y, top, DoorIn(101.9f, 103.5f, y));
        WallX(b, floor + " 복도 남벽", AptIn0, AptIn1, -1.5f, -1.2f, y, top, DoorIn(91f, 92.2f, y), DoorIn(97.4f, 98.6f, y),
            DoorIn(101.55f, 102.75f, y, 2.2f));
        // Party walls: a hole in the front room and one in the back, north and south (AptVariants plugs one of each pair).
        WallZ(b, floor + " 북쪽 집 사이벽", PartyX, PartyX + Wall, 1.5f, AptN1, y, top, DoorIn(3f, 4.2f, y, 2.2f), DoorIn(12f, 13.2f, y, 2.2f));
        WallZ(b, floor + " 남쪽 집 사이벽", PartyX, PartyX + Wall, AptS0, -1.5f, y, top, DoorIn(-4.2f, -3f, y, 2.2f), DoorIn(-13.2f, -12f, y, 2.2f));
        // Front room from back room.
        bool fallen = f >= 4;
        if (fallen) WallX(b, floor + " 1호 안벽", 90.4f, PartyX, FrontNorth, FrontNorth + Wall, y, top, DoorIn(91.5f, 92.7f, y));
        else WallX(b, floor + " 1호 안벽", AptIn0, PartyX, FrontNorth, FrontNorth + Wall, y, top, DoorIn(89.5f, 90.7f, y));
        WallX(b, floor + " 2호 안벽", PartyX + Wall, CoreX - Wall, FrontNorth, FrontNorth + Wall, y, top, DoorIn(96.4f, 97.6f, y));
        WallX(b, floor + " 3호 안벽", PartyX + Wall, AptIn1, FrontSouth - Wall, FrontSouth, y, top, DoorIn(96.4f, 97.6f, y));
        // On the ground floor the cellar's flight runs down through it: the opening is as wide as the flight.
        WallX(b, floor + " 4호 안벽", AptIn0, PartyX, FrontSouth - Wall, FrontSouth, y, top, f == 0 ? DoorIn(AptIn0, 89.8f, y) : DoorIn(89.5f, 90.7f, y));
        // The lift's cell on this floor: its west and south walls (the corridor's wall is its front).
        b.Box(floor + " 승강기 서벽", ShaftX - Wall, ShaftX, y, top, ShaftZ - Wall, -1.5f);
        b.Box(floor + " 승강기 남벽", ShaftX - Wall, AptIn1, y, top, ShaftZ - Wall, ShaftZ);
    }

    // The floors of storeys 2-6 and the roof: open over the stairs' flights, the fallen corner (6th floor) and the holes
    // through flats' floors that the variants plug or not (AptVariants).
    private static void AptSlabs(GreyboxBuilder b)
    {
        var flights = (CoreX, AptIn1, 1.5f + Landing, CoreZ1);
        for (int f = 1; f < AptStoreys; f++)
        {
            var holes = new List<(float, float, float, float)> { flights };
            if (f == 5) holes.Add((AptIn0, 90.3f, 2f, 10f));
            foreach (var hole in FloorHoles(f)) holes.Add(hole);
            SlabWith(b, $"B4 {f + 1}층 바닥", f * Storey, AptIn0, AptIn1, AptS0, AptN1, b.Concrete, "Tile", holes.ToArray());
        }
        SlabWith(b, "B4 지붕", AptRoof, AptIn0, AptIn1, AptS0, AptN1, null, null, flights, (AptIn0, 90.3f, 2f, 10f));
    }

    private const float Landing = 1.2f;

    // The holes through a storey's floor down to the one below, two places each (AptVariants keeps one open a run). Each
    // leaves a way round it wide enough for a zombie (0.5 m round): the 3rd floor's first hole was in flat 2's front room
    // between its sofa and the stairs' wall, 1.0 and 1.2 m either side, and cut the flat's back room off from its door
    // (the rebuild of 11-14: 'B4 3층 바닥 6', the floor beside it, with no walk mesh); now it is in the back room's
    // corner by the stairs.
    private static (float, float, float, float)[] FloorHoles(int f) => f switch
    {
        4 => new[] { (96.5f, 98.1f, 12f, 13.6f), (92f, 93.6f, -6f, -4.4f) },
        3 => new[] { (92f, 93.6f, 12f, 13.6f), (97f, 98.6f, -13f, -11.4f) },
        2 => new[] { (102.1f, AptIn1, CoreZ1 + Wall, CoreZ1 + Wall + 1.6f), (92f, 93.6f, -13f, -11.4f) },
        _ => new (float, float, float, float)[0]
    };

    // The stairs, north-east of the corridor, ground floor to the roof: two flights a storey round a half landing.
    private static void AptStairs(GreyboxBuilder b)
    {
        b.Box("B4 계단 서벽", CoreX - Wall, CoreX, 0f, AptRoof, 1.5f, CoreZ1 + Wall);
        b.Box("B4 계단 북벽", CoreX - Wall, AptIn1, 0f, AptRoof, CoreZ1, CoreZ1 + Wall);
        Flights(b, "B4 계단", CoreX, AptIn1, 1.5f + Landing, CoreZ1 - Landing, CoreZ1, AptRoof, 0f);
        // The roof's stair hut, its door on the west.
        b.Box("B4 계단실 서벽 1", CoreX - Wall, CoreX, AptRoof, AptRoof + 2.7f, 1.2f, 1.6f);
        b.Box("B4 계단실 서벽 2", CoreX - Wall, CoreX, AptRoof, AptRoof + 2.7f, 2.8f, CoreZ1 + Wall);
        b.Box("B4 계단실 문 위", CoreX - Wall, CoreX, AptRoof + 2.3f, AptRoof + 2.7f, 1.6f, 2.8f);
        b.Box("B4 계단실 남벽", CoreX - Wall, AptX1, AptRoof, AptRoof + 2.7f, 1.2f, 1.5f);
        b.Box("B4 계단실 북벽", CoreX - Wall, AptX1, AptRoof, AptRoof + 2.7f, CoreZ1, CoreZ1 + Wall);
        b.Box("B4 계단실 동벽", AptIn1, AptX1, AptRoof, AptRoof + 2.7f, 1.5f, CoreZ1);
        b.Box("B4 계단실 지붕", CoreX - Wall, AptX1, AptRoof + 2.7f, AptRoof + 3f, 1.2f, CoreZ1 + Wall);
    }

    // Balconies on floors 2-6, north and south, one to each flat's back room; low walls between neighbours (vaulted, not
    // walked: zombies keep to the corridors), and fire ladders up through hatches between some of them.
    private static void AptBalconies(GreyboxBuilder b)
    {
        for (int f = 1; f < AptStoreys; f++)
        {
            float y = f * Storey;
            foreach (bool northSide in new[] { true, false })
            {
                float z0 = northSide ? AptZ1 : AptZ0 - 1.5f, z1 = northSide ? AptZ1 + 1.5f : AptZ0;
                float edge0 = northSide ? z1 - .2f : z0, edge1 = northSide ? z1 : z0 + .2f;
                string side = northSide ? "북" : "남";
                var hatches = new List<(float, float, float, float)>();
                foreach (var ladder in BalconyLadders())
                    if (ladder.north == northSide && ladder.f + 1 == f) hatches.Add(BalconyHatch(ladder));
                SlabWith(b, $"B4 {f + 1}층 {side} 발코니 바닥", y, 87.6f, 103.4f, z0, z1, b.Concrete, "Concrete", hatches.ToArray());
                b.Box($"B4 {f + 1}층 {side} 발코니 난간", 87.6f, 103.4f, y, y + 1f, edge0, edge1);
                b.Box($"B4 {f + 1}층 {side} 발코니 난간 서", 87.6f, 87.8f, y, y + 1f, z0, z1);
                b.Box($"B4 {f + 1}층 {side} 발코니 난간 동", 103.2f, 103.4f, y, y + 1f, z0, z1);
                b.Box($"B4 {f + 1}층 {side} 발코니 칸막이", PartyX, PartyX + .3f, y, y + 1.3f, z0, z1);
            }
        }
        foreach (var ladder in BalconyLadders())
        {
            float foot = ladder.f * Storey;
            // Across the hatch, the climber in it facing along the balcony (see BalconyLadders).
            b.Ladder($"B4 비상 사다리 {ladder.f + 1}층→{ladder.f + 2}층",
                new Vector3(ladder.x, foot, ladder.north ? AptZ1 + HatchDeep * .5f : AptZ0 - HatchDeep * .5f), ladder.alongEast ? 270f : 90f);
        }
    }

    // The fire ladders: from floor f's balcony up through a hatch in the one above, at the balcony's end on the north or
    // the south side. The rungs stand across the hatch at x and the climber goes up in it, between them and the balcony's
    // end, facing along the balcony (east when alongEast), so the climb off the top steps onto the balcony above
    // (MovementFixtures.TryLeaveLadderTop, 0.6 m in from the rungs). They stood against the outer wall facing it, and the
    // climb off went into the wall: the rebuild of 11-14 found no floor to stand on at their tops (LadderFit, "B4 비상
    // 사다리 2층→3층: there is no room to stand on the first top over its foot (B4 3층 바닥 9)").
    private static (int f, float x, bool north, bool alongEast)[] BalconyLadders() => new[]
    {
        (3, 88.6f, true, true),
        (2, 102.4f, false, false),
        (1, 88.6f, true, true),
    };

    // A fire ladder's hatch: deep enough off the wall and wide enough behind the rungs for a climber (0.3 m round, 0.27 m
    // off the rungs) with room to spare, and only 0.1 m past the rungs, so the balcony above starts right where the climb
    // off steps (x0, x1, z0, z1).
    private const float HatchDeep = .8f, HatchBehind = .7f, HatchPast = .1f;

    private static (float, float, float, float) BalconyHatch((int f, float x, bool north, bool alongEast) ladder)
    {
        float x0 = ladder.alongEast ? ladder.x - HatchBehind : ladder.x - HatchPast;
        float x1 = ladder.alongEast ? ladder.x + HatchPast : ladder.x + HatchBehind;
        return ladder.north ? (x0, x1, AptZ1, AptZ1 + HatchDeep) : (x0, x1, AptZ0 - HatchDeep, AptZ0);
    }

    // The cellar under the south-west flat (k04): a switchboard that powers the block and the alley's shutter. Down a flight
    // from the ground-floor flat; its floor at -3.
    private static void AptCellar(GreyboxBuilder b)
    {
        const float Down = -3f, X1 = PartyX;
        b.Box("B4 지하 바닥", AptIn0, X1, Down - Wall, Down, AptS0, -1.5f, b.Concrete, "Concrete");
        b.Box("B4 지하 서벽", AptX0, AptIn0, Down - Wall, 0f, AptZ0, -1.2f);
        b.Box("B4 지하 남벽", AptX0, X1 + Wall, Down - Wall, 0f, AptZ0, AptS0);
        b.Box("B4 지하 동벽", X1, X1 + Wall, Down - Wall, 0f, AptS0, -1.2f);
        b.Box("B4 지하 북벽", AptIn0, X1 + Wall, Down - Wall, 0f, -1.5f, -1.2f);
        // The ground floor of that flat is a slab over it, open where the flight goes down.
        SlabWith(b, "B4 1층 4호 바닥", 0f, AptIn0, X1, AptS0, -1.5f, b.Concrete, "Tile", (AptIn0, 89.8f, -12.6f, -6.3f));
        b.Ramp("B4 지하 계단", new Vector3(88.55f, Down, -12.4f), new Vector3(88.55f, 0f, -6.4f), 2f, b.Concrete);
        b.Box("B4 지하 계단 난간", 89.8f, 90f, 0f, 1f, -12.6f, -6.6f);
    }
}
