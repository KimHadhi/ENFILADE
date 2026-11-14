using System.Collections.Generic;
using UnityEngine;

// CampaignMaps: chapter 3's gimmicks: the station office off the concourse, the sleeping tunnel and its derailed car,
// the train yard under its shed (stopped trains to crawl under and cross over, sweeping searchlights, the fuel cans a
// run puts somewhere new and the board that says where), and the store of caged zombies. One part of the class; the rest
// is in CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    // The train shed south of the depot (the card "밤 차량기지", after PAYDAY 2's Murky Station): x 170-218, z -62..-20.
    private const float ShedZ0 = -62f, ShedZ1 = -20f, ShedTop = 12f;
    // Its trains: rows along x, each two cars on bogies with a crawl space under them (0.75 m: prone fits, crouched and
    // zombies don't).
    private static readonly float[] TrainRows = { -26.5f, -33f, -39.5f, -46f, -52.5f };
    private const float CarLow = .75f, CarTop = 3.75f, CarHalf = 1.6f;

    // The station office (the card "역무실과 CCTV"): rooms behind the concourse's north wall (x 2-34, z 12.3-22), a staff
    // corridor from the concourse to the platform hall, the locker room, the camera room (its cameras look over the yard
    // and show its fuel cans: GimmickCamera with the board), and the station master's office with the platform's public
    // address (a lure on the tracks). Asleep in the lockers, one in the office; a walker on the corridor.
    private static void StationOffice(GreyboxBuilder b, GimmickCanSpots board)
    {
        const float X0 = 2f, X1 = 34f, Z0 = 12.3f, Z1 = 22f, H = 3.5f, Hall = 14.5f;
        b.Box("역무실 북벽", X0, X1, 0f, H, Z1 - Wall, Z1);
        b.Box("역무실 서벽", X0, X0 + Wall, 0f, H, Z0, Z1);
        b.Box("역무실 동벽", X1 - Wall, X1, 0f, H, Z0, Z1);
        b.Box("역무실 지붕", X0, X1, H, H + Wall, Z0, Z1);
        WallX(b, "역무실 복도 벽", X0 + Wall, X1 - Wall, Hall, Hall + Wall, 0f, H, DoorIn(7f, 8.2f, 0f), DoorIn(18f, 19.2f, 0f), DoorIn(28f, 29.2f, 0f));
        WallZ(b, "역무실 칸 벽 1", 13.7f, 14f, Hall + Wall, Z1 - Wall, 0f, H);
        WallZ(b, "역무실 칸 벽 2", 23.7f, 24f, Hall + Wall, Z1 - Wall, 0f, H);
        // The corridor's east end climbs to the platform's height, to the door into the platform hall.
        b.Ramp("역무실 복도 오르막", new Vector3(26.5f, 0f, 13.4f), new Vector3(30.5f, 1.1f, 13.4f), 2f, b.Concrete);
        b.Box("역무실 복도 끝 단", 30.5f, X1 - Wall, 0f, 1.1f, Z0, Hall, b.Concrete, "Tile");
        b.Spot("StationOffice", new Vector3(18.5f, 0f, 13.4f));
        // Lockers, the camera desk, the master's desk and the public address.
        foreach (float x in new[] { 3f, 4.2f, 5.4f, 9f, 10.2f }) b.PropSolid("locker", new Vector3(x, 0f, 21.35f), 180f);
        b.PropSolid("hubdesk", new Vector3(19f, 0f, 21f), 180f);
        foreach (int i in new[] { 0, 1, 2 }) b.Prop("hubscreen", new Vector3(18.2f + i * .8f, .78f, 21.1f), 180f, i);
        var looking = b.Mark("역무실 CCTV가 보는 곳", new Vector3(194f, 0f, -40f));
        var cameras = b.Switch<GimmickCamera>("역무실 CCTV", new Vector3(19f, 1.1f, 20.6f));
        GreyboxBuilder.Configure(cameras, so =>
        {
            so.FindProperty("watched").objectReferenceValue = looking;
            so.FindProperty("radius").floatValue = 32f;
            so.FindProperty("board").objectReferenceValue = board;
            so.FindProperty("reach").floatValue = 1.8f;
        });
        b.PropSolid("table", new Vector3(29f, 0f, 20.6f), 0f);
        b.Prop("officechair", new Vector3(29f, 0f, 19.6f), 180f);
        var speakers = new List<Transform>();
        foreach (float x in new[] { 45f, 65f, 85f }) speakers.Add(b.Mark("승강장 스피커", new Vector3(x, 5.2f, -8f)));
        var pa = b.Switch<GimmickLure>("역 안내 방송", new Vector3(29f, .9f, 20.4f));
        b.Box("방송 마이크", 28.85f, 29.15f, .78f, 1.05f, 20.25f, 20.55f, b.Marker, solid: false);
        GreyboxBuilder.Configure(pa, so =>
        {
            so.FindProperty("prompt").stringValue = "gimmick.lure.pa.station";
            so.FindProperty("cue").enumValueIndex = (int)GimmickSounds.Cue.Chime;
            so.FindProperty("radius").floatValue = 40f;
            so.FindProperty("seconds").floatValue = 28f;
            FillObjects(so.FindProperty("emitters"), speakers);
        });
        b.Lamp(new Vector3(8f, 3.2f, 13.4f), 7f, .55f);
        b.Lamp(new Vector3(19f, 3.2f, 18f), 6f, .6f);
        b.Sleepers("Ch3.01", new Vector3(7.5f, 0f, 18.5f), 2, 1.4f);
        b.Sleepers("Ch3.01", new Vector3(29f, 0f, 17.5f), 1, .5f);
        b.Walkers("Ch3.01", 1, null, new Vector3(3.5f, 0f, 13.4f), new Vector3(25f, 0f, 13.4f));
    }

    // The sleeping tunnel (the card "잠든 터널", after GTFO): crowds asleep along the track, a run's pick, and the derailed
    // car the way goes inside, over or beside: it is hollow now with doors at its ends, a crate at its west end to climb
    // its roof by, and the gap beside it. Darker than before: one dim lamp.
    private static void SleepingTunnel(GreyboxBuilder b)
    {
        const float C0 = 140f, C1 = 152f, CZ0 = -5.7f, CZ1 = -2.2f, CH = 3f;
        b.Box("탈선한 객차 바닥", C0, C1, 0f, .3f, CZ0, CZ1, b.Metal, "Metal");
        b.Box("탈선한 객차 지붕", C0, C1, CH - .2f, CH, CZ0, CZ1, b.Metal, "Metal");
        b.Box("탈선한 객차 남벽", C0, C1, .3f, CH - .2f, CZ0, CZ0 + .15f, b.Metal, "Metal");
        WallX(b, "탈선한 객차 북벽", C0, C1, CZ1 - .15f, CZ1, .3f, CH - .2f, (143f, 145.5f, 1.3f, 2.4f), (147f, 149.5f, 1.3f, 2.4f));
        WallZ(b, "탈선한 객차 서벽", C0, C0 + .15f, CZ0 + .15f, CZ1 - .15f, .3f, CH - .2f, (-4.5f, -3.3f, .3f, 2.6f));
        WallZ(b, "탈선한 객차 동벽", C1 - .15f, C1, CZ0 + .15f, CZ1 - .15f, .3f, CH - .2f, (-4.5f, -3.3f, .3f, 2.6f));
        b.Dress(b.Box("객차 오르는 상자", 137.6f, 139f, 0f, 1.4f, -5.4f, -4f, b.Wood, "WoodPlank"), "crateFit");
        b.Lamp(new Vector3(125f, 5f, -2f), 8f, .7f);
        var crowds = b.Variant("터널 잠든 무리 (여섯 중 넷)", 4, 240);
        foreach (var (x, z, n) in new[] { (98f, -3f, 3), (106f, 0f, 2), (120f, -3.5f, 3), (135f, 0f, 2), (146f, -4f, 2), (146f, .2f, 3) })
            b.Sleepers("Ch3.02", new Vector3(x, 0f, z), n, 1.6f, b.Choice(crowds, $"x {x} z {z}"));
    }

    // The train shed (the card "밤 차량기지"): the depot's south wall opens onto it (a wide way and the store's door), a
    // high roof, five rows of stopped trains (two cars a row, on bogies; ladders at the west ends to their roofs), two
    // searchlights sweeping from towers at its sides, crowds asleep between the trains, walkers along it. The cans' spots
    // (GimmickCanSpots, the board by the way in) are under trains, on roofs, in the corners, on the catwalk and in the
    // cage store's passage.
    private static GimmickCanSpots TrainShed(GreyboxBuilder b)
    {
        const float X0 = 170f, X1 = 218f;
        b.Box("차량기지 바닥", 165f, 225f, -.5f, .01f, -66f, -25f, null, "Concrete");
        b.Box("차량기지 서벽", X0, X0 + Wall, 0f, ShedTop, ShedZ0, ShedZ1);
        b.Box("차량기지 동벽", X1 - Wall, X1, 0f, ShedTop, ShedZ0, ShedZ1);
        b.Box("차량기지 남벽", X0, X1, 0f, ShedTop, ShedZ0, ShedZ0 + Wall);
        b.Box("차량기지 지붕", X0, X1, ShedTop, ShedTop + .5f, ShedZ0, ShedZ1);
        // The depot's south wall: the store's door at the passage, the wide way to the yard, the strip above the depot's
        // ceiling up to the shed's roof.
        WallX(b, "정비창 벽 남", X0 + Wall, X1 - Wall, ShedZ1 - Wall, ShedZ1, 0f, 9f, DoorIn(177.9f, 179.1f, 0f), (196f, 214f, 0f, 6f));
        b.Box("정비창 남벽 위", X0, X1, 9f, ShedTop, ShedZ1 - Wall, ShedZ1);
        b.Reflections("Ch3.Depot", X0 + Wall, X1 - Wall, 0f, ShedTop, ShedZ0, ShedZ1);
        foreach (float row in TrainRows)
            foreach (var (a, c) in new[] { (176f, 193f), (195f, 212f) })
            {
                b.Box($"열차 {row} {a}", a, c, CarLow, CarTop, row - CarHalf, row + CarHalf, b.Metal, "Metal");
                b.Box($"열차 대차 {row} {a} 서", a, a + 3f, 0f, CarLow, row - 1.2f, row + 1.2f, b.Metal, "Metal");
                b.Box($"열차 대차 {row} {a} 동", c - 3f, c, 0f, CarLow, row - 1.2f, row + 1.2f, b.Metal, "Metal");
            }
        foreach (float row in TrainRows) b.Ladder($"열차 사다리 {row}", new Vector3(175.85f, 0f, row), 270f);
        // Searchlights on towers at the shed's sides, sweeping slowly out of step.
        foreach (var (x, face, period, phase) in new[] { (172f, 90f, 16f, 0f), (216f, 270f, 19f, .35f) })
        {
            b.Box("탐조등 탑", x - .3f, x + .3f, 0f, 9f, -39.8f, -39.2f, b.Metal, "MetalPipe");
            var head = new GameObject("탐조등", typeof(Light), typeof(GimmickSweep));
            head.transform.SetParent(b.Group("◤━━ 탐조등"), false);
            head.transform.SetPositionAndRotation(new Vector3(x, 9.2f, -39.5f), Quaternion.Euler(32f, face, 0f));
            var light = head.GetComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 24f;
            light.innerSpotAngle = 14f;
            light.range = 46f;
            light.intensity = 26f;
            light.color = new Color(.92f, .96f, 1f);
            light.shadows = LightShadows.Hard;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            GreyboxBuilder.Configure(head.GetComponent<GimmickSweep>(), so =>
            {
                so.FindProperty("sweep").floatValue = 55f;
                so.FindProperty("period").floatValue = period;
                so.FindProperty("phase").floatValue = phase;
            });
        }
        b.Lamp(new Vector3(194f, 11f, -30f), 12f, .5f);
        // Who is where.
        var crowds = b.Variant("차량기지 잠든 무리 (여섯 중 넷)", 4, 250);
        foreach (var (x, z) in new[] { (186f, -29.8f), (203f, -36.3f), (183f, -42.8f), (205f, -49.3f), (192f, -55.5f), (210f, -29.8f) })
            b.Sleepers("Ch3.04", new Vector3(x, 0f, z), 3, 1.6f, b.Choice(crowds, $"{x} {z}"));
        b.Walkers("Ch3.04", 1, null, new Vector3(173f, 0f, -24f), new Vector3(173f, 0f, -58f), new Vector3(214f, 0f, -58f));
        b.Walkers("Ch3.04", 1, null, new Vector3(194f, 0f, -23f), new Vector3(194f, 0f, -59f));

        // The cans' spots and the board.
        var places = new (string id, Vector3 at)[]
        {
            ("gimmick.place.ch3.under2", new Vector3(184f, .05f, TrainRows[1])),
            ("gimmick.place.ch3.roof4", new Vector3(203f, CarTop, TrainRows[3])),
            ("gimmick.place.ch3.sw", new Vector3(172.5f, 0f, -59.5f)),
            ("gimmick.place.ch3.se", new Vector3(215.5f, 0f, -59.5f)),
            ("gimmick.place.ch3.between", new Vector3(194f, 0f, (TrainRows[2] + TrainRows[3]) * .5f)),
            ("gimmick.place.ch3.under5", new Vector3(203f, .05f, TrainRows[4])),
            ("gimmick.place.ch3.catwalk", new Vector3(210f, 4.2f, 18f)),
            ("gimmick.place.ch3.cage", new Vector3(178.5f, 0f, -15.5f)),
        };
        var board = b.Switch<GimmickCanSpots>("정비 게시판", new Vector3(194f, 1.6f, ShedZ1 + .5f));
        b.Box("정비 게시판 판", 192.8f, 195.2f, 1f, 2.4f, ShedZ1, ShedZ1 + .1f, b.Wood, "WoodPanel");
        var spots = new List<Transform>();
        var ids = new List<string>();
        foreach (var (id, at) in places)
        {
            spots.Add(b.Mark("연료통 자리 " + id.Substring(id.LastIndexOf('.') + 1), at, 0f, board.transform));
            ids.Add(id);
        }
        GreyboxBuilder.Configure(board, so =>
        {
            FillObjects(so.FindProperty("spots"), spots);
            Fill(so.FindProperty("places"), ids);
            so.FindProperty("cans").intValue = 4;
            so.FindProperty("reach").floatValue = 2f;
        });
        // The four cans themselves, where the first four spots are; the host moves them as the run's seed says.
        for (int i = 0; i < 4; i++) b.Can(places[i].at);
        return board;
    }

    // The store of caged zombies (the card "철창에 갇힌 좀비 창고", after Guts & Blackpowder's Kaub): the depot's south-west
    // corner, a passage between two cages from the depot to the shed; awake behind the bars, they reach through at whoever
    // goes by upright; the generator's hold bursts the cage doors (GimmickCage) unless the store's two doors were barred
    // (GimmickBarDoor).
    private static void CageStore(GreyboxBuilder b)
    {
        const float X0 = 170.3f, X1 = 186f, Z0 = -19.7f, Z1 = -11f, H = 4f, P0 = 177.4f, P1 = 179.6f;
        WallX(b, "철창 창고 북벽", X0, X1 + Wall, Z1, Z1 + Wall, 0f, H, DoorIn(177.9f, 179.1f, 0f));
        b.Box("철창 창고 동벽", X1, X1 + Wall, 0f, H, Z0, Z1);
        b.Box("철창 창고 지붕", X0, X1 + Wall, H, H + Wall, Z0, Z1 + Wall);
        // The bars: chain-link (Tables/MapArt "fence", seen and shot through: GimmickCage), solid to the ceiling so nobody
        // climbs over; along each cage's side a whole number of the fence's panels, the last one at the passage's north
        // end the door that bursts open, so bars and door are the same panel at the same height.
        var cage = b.Switch<GimmickCage>("철창", Vector3.zero);
        var doors = new List<GameObject>();
        var bars = new List<GameObject>();
        Vector3 panel = b.ArtSize("fence");
        float own = Mathf.Max(panel.x, panel.z), side = Z1 - Z0;
        int panels = own > .01f ? Mathf.Max(2, Mathf.RoundToInt(side / own)) : 3;
        float doorZ = Z1 - side / panels;
        foreach (var (a, c) in new[] { (P0 - .2f, P0), (P1, P1 + .2f) })
        {
            bars.Add(b.SeeThrough(b.Dress(b.Box("철창 살", a, c, 0f, H, Z0, doorZ, b.Metal, "MetalPipe"), "fence")));
            doors.Add(b.SeeThrough(b.DressPart(b.Blocker("철창 문", a, c, 0f, H, doorZ, Z1, cage.transform, b.Metal, "MetalPipe"), "fence")));
        }
        var cages = new List<Bounds>
        {
            new Rect3(X0, P0 - .2f, 0f, 3f, Z0, Z1).Bounds,
            new Rect3(P1 + .2f, X1, 0f, 3f, Z0, Z1).Bounds,
        };
        GreyboxBuilder.Configure(cage, so =>
        {
            so.FindProperty("step").stringValue = "Ch3.10";
            so.FindProperty("passage").boundsValue = new Rect3(P0, P1, 0f, 2.5f, Z0, Z1).Bounds;
            Fill(so.FindProperty("cages"), cages);
            FillObjects(so.FindProperty("doors"), doors);
            FillObjects(so.FindProperty("bars"), bars);
        });
        foreach (var (x, n) in new[] { (173.8f, 4), (182.8f, 4) }) b.CageCrowd("Ch3.03", new Vector3(x, 0f, -15.5f), n, 2.2f);
        // The two doors to bar.
        foreach (var (name, z0, z1) in new[] { ("철창 창고 북문", Z1 + .02f, Z1 + .28f), ("철창 창고 남문", ShedZ1 - .28f, ShedZ1 - .02f) })
        {
            var bar = b.Switch<GimmickBarDoor>(name + " 막기", Vector3.zero);
            var boards = b.Blocker(name + " 판자", 177.9f, 179.1f, 0f, 2.3f, z0, z1, bar.transform, b.Wood, "WoodPlank");
            boards.SetActive(false);
            GreyboxBuilder.Configure(bar, so =>
            {
                so.FindProperty("boards").objectReferenceValue = boards;
                so.FindProperty("reach").floatValue = 1.8f;
            });
        }
        b.Lamp(new Vector3(178.5f, 3.6f, -15.5f), 6f, .5f);
    }
}
