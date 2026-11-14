using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// CampaignMaps: chapter 2's gimmicks: the jam's alarm cars and sleepers, the service area, the burning car pile, the toll
// road with its office, the army checkpoint and the span that falls in. One part of the class; the rest is in
// CampaignMaps.cs and the files beside it.
public static partial class CampaignMaps
{
    private const float LaneA = -5f, LaneB = -1.8f, LaneC = 1.8f, LaneD = 5f;

    // The jam (the card "경보 차가 섞인 정체 구간", after Left 4 Dead's car alarms): the stopped cars as before, five of them
    // with their alarms still set, of which a run arms some (a small red light pulses inside the ones that are). Shot,
    // struck, hit by a thrown stone or run into, an armed one wails and draws the calm ones to it, and the shot that sets it
    // off makes no noise of its own (ShotMuffle). A few lie asleep on the road between the cars, a run's pick of six places.
    private static void OverpassJam(GreyboxBuilder b)
    {
        Car(b, 18f, Deck, LaneA); Car(b, 18f, Deck, LaneC);
        Car(b, 30f, Deck, LaneA); Car(b, 30f, Deck, LaneB);
        Car(b, 49f, Deck, LaneA);
        Car(b, 68f, Deck, LaneC); Car(b, 70f, Deck, LaneA);
        Car(b, 76f, Deck, LaneB);
        Car(b, 106f, Deck, LaneD);
        var alarms = new (float x, float z)[] { (24f, LaneD), (24f, LaneB), (50f, LaneB), (75f, LaneD), (100f, LaneC), (107f, LaneA) };
        for (int i = 0; i < alarms.Length; i++)
        {
            var (x, z) = alarms[i];
            var car = b.Box($"경보 차 {i + 1}", x - 2.25f, x + 2.25f, Deck, Deck + 1.5f, z - .95f, z + .95f, b.Metal, "Metal");
            b.Dress(car, "car");
            var alarm = b.AddSwitch<GimmickAlarmCar>(car);
            // Its lamps: a hazard light at each end, and the armed tell inside the windscreen.
            var lamps = new List<Light>();
            foreach (float end in new[] { -2.3f, 2.3f })
            {
                var lamp = new GameObject("비상등", typeof(Light)).GetComponent<Light>();
                lamp.transform.SetParent(car.transform.parent, false);
                lamp.transform.position = new Vector3(x + end, Deck + .8f, z);
                lamp.type = LightType.Point;
                lamp.range = 5f;
                lamp.intensity = 2.2f;
                lamp.color = new Color(1f, .62f, .2f);
                lamp.shadows = LightShadows.None;
                lamp.lightmapBakeType = LightmapBakeType.Realtime;
                lamp.transform.SetParent(alarm.transform, true);
                lamps.Add(lamp);
            }
            var tell = new GameObject("경보 장치 불", typeof(Light)).GetComponent<Light>();
            tell.transform.SetParent(alarm.transform, true);
            tell.transform.position = new Vector3(x + .9f, Deck + 1.2f, z);
            tell.type = LightType.Point;
            tell.range = 1.2f;
            tell.intensity = .8f;
            tell.color = new Color(1f, .1f, .1f);
            tell.shadows = LightShadows.None;
            tell.lightmapBakeType = LightmapBakeType.Realtime;
            GreyboxBuilder.Configure(alarm, so =>
            {
                FillObjects(so.FindProperty("lamps"), lamps);
                so.FindProperty("tell").objectReferenceValue = tell;
                so.FindProperty("armedShare").floatValue = .6f;
                so.FindProperty("salt").intValue = 200 + i;
            });
        }
        var nests = b.Variant("정체 구간 잠든 놈 (여섯 중 셋)", 3, 220);
        foreach (var (x, z) in new[] { (21f, 0f), (27f, 3.4f), (52f, 3f), (66f, -3.5f), (80f, 0f), (103f, -3.4f) })
            b.Sleepers("Ch2.01", new Vector3(x, Deck, z), 2, 1.2f, b.Choice(nests, $"x {x}"));
        b.Walkers("Ch2.02", 1, null, new Vector3(88f, Deck, 1.5f), new Vector3(110f, Deck, 1.5f));
    }

    // The service area (the card "고속도로 휴게소 건물"): a forecourt of pumps south of the road (the card "주유소 기름 함정":
    // [F] spills a pump, a shot lights it) and a one-storey building north of it. The restaurant takes the front, its
    // store (the fuel cans for the car pile) the back; east of them the toilets, the staff corridor and the office, the
    // rest stop, behind its door. Ways in: the restaurant's glass front and side door, the toilets' door, the store's
    // back door. Asleep in the restaurant, the toilets or the store; a walker on the staff corridor; a radio to lure with.
    private static void ServiceArea(GreyboxBuilder b)
    {
        // The forecourt: canopy, two pump islands.
        b.Box("주유소 지붕", 138f, 154f, 5f, 5.4f, -20f, -8f, b.Metal, "Metal");
        foreach (var (x, z) in new[] { (138f, -20f), (153.5f, -20f), (138f, -8.5f), (153.5f, -8.5f) })
            b.Box("주유소 기둥", x, x + .5f, 0f, 5f, z, z + .5f, b.Metal, "MetalPipe");
        foreach (float px in new[] { 143f, 149f })
        {
            b.Box($"주유기 {px}", px - .5f, px + .5f, 0f, 1.5f, -14.5f, -13.5f, b.Metal, "Metal");
            var trap = b.Switch<GimmickFuelTrap>($"주유기 밸브 {px}", new Vector3(px, 1f, -13.2f));
            var pool = new Rect3(px - 3.5f, px + 3.5f, 0f, .6f, -19f, -9f);
            // The spill (a dark sheen) and the shot's target over it, both under the trap so they come with the valve.
            var spill = new GameObject("흘린 기름").transform;
            spill.SetParent(trap.transform, false);
            b.Box("기름 웅덩이", px - 3.3f - px, px + 3.3f - px, -.99f, -.97f, -18.8f + 13.2f, -9.2f + 13.2f, b.Road, solid: false, group: spill);
            var aim = new GameObject("기름 표적").transform;
            aim.SetParent(spill, false);
            aim.localPosition = new Vector3(0f, -.8f, -14f + 13.2f);
            aim.gameObject.AddComponent<BoxCollider>().size = new Vector3(7f, .4f, 10f);
            aim.gameObject.AddComponent<GimmickTarget>();
            spill.gameObject.SetActive(false);
            GreyboxBuilder.Configure(trap, so =>
            {
                so.FindProperty("pool").boundsValue = pool.Bounds;
                so.FindProperty("spill").objectReferenceValue = spill.gameObject;
                so.FindProperty("reach").floatValue = 1.6f;
            });
        }

        // The building.
        const float X0 = 156f, X1 = 178f, Z0 = 4f, Z1 = 22f, H = 4.5f, Mid = 168f;
        WallX(b, "휴게소 남벽", X0, X1, Z0, Z0 + Wall, 0f, H, DoorIn(159.5f, 162.5f, 0f, 2.6f), DoorIn(170f, 171.2f, 0f));
        WallX(b, "휴게소 북벽", X0, X1, Z1 - Wall, Z1, 0f, H, DoorIn(158f, 159.2f, 0f));
        WallZ(b, "휴게소 서벽", X0, X0 + Wall, Z0 + Wall, Z1 - Wall, 0f, H, DoorIn(9f, 10.2f, 0f));
        b.Box("휴게소 동벽", X1 - Wall, X1, 0f, H, Z0 + Wall, Z1 - Wall);
        b.Box("휴게소 지붕", X0, X1, H, H + Wall, Z0, Z1);
        WallX(b, "휴게소 식당 창고 벽", X0 + Wall, Mid, 15f, 15.3f, 0f, H, DoorIn(162f, 163.2f, 0f));
        WallZ(b, "휴게소 가운데 벽", Mid, Mid + Wall, Z0 + Wall, Z1 - Wall, 0f, H, DoorIn(9.6f, 10.8f, 0f));
        WallX(b, "휴게소 화장실 벽", Mid + Wall, X1 - Wall, 9f, 9.3f, 0f, H, DoorIn(175f, 176.2f, 0f));
        WallX(b, "휴게소 사무실 벽", Mid + Wall, X1 - Wall, 11.3f, 11.6f, 0f, H, DoorIn(172f, 173.2f, 0f));
        b.Door("휴게소 사무실 문", 172f, 173.2f, 0f, 2.3f, 11.35f, 11.55f, "Station", new Vector3(1.3f, 0f, 0f));
        b.Spot("Station", new Vector3(174f, 0f, 16.5f));

        // The restaurant: tables and chairs, the serving counter along its back, a radio on it.
        foreach (var (x, z) in new[] { (158.5f, 6.8f), (162.5f, 6.8f), (158.5f, 11f), (162.5f, 11f), (165.8f, 8f) })
        {
            b.PropSolid("table", new Vector3(x, 0f, z), x * 7f % 90f);
            b.Prop("chair", new Vector3(x - .9f, 0f, z), 90f + x * 11f % 30f);
            b.Prop("chair", new Vector3(x + .9f, 0f, z), 270f - x * 13f % 30f);
        }
        b.Dress(b.Box("휴게소 배식대", 157f, 166.5f, 0f, 1f, 13.6f, 14.6f, b.Wood, "Wood"), "shopcounter");
        var radio = b.Switch<GimmickLure>("휴게소 라디오", new Vector3(164f, 1.05f, 14.1f), 180f);
        b.Box("라디오", 163.75f, 164.25f, 1f, 1.28f, 13.95f, 14.25f, b.Marker, solid: false);
        GreyboxBuilder.Configure(radio, so =>
        {
            so.FindProperty("prompt").stringValue = "gimmick.lure.radio";
            so.FindProperty("radius").floatValue = 30f;
            so.FindProperty("reach").floatValue = 1.8f;
        });
        // The store: shelves, boxes, the fuel cans for the car pile.
        b.PropSolid("wireshelf", new Vector3(157f, 0f, 20.9f), 180f);
        b.PropSolid("wireshelf", new Vector3(166.8f, 0f, 20.9f), 180f);
        foreach (var (x, z) in new[] { (160.4f, 20.4f), (161.6f, 20.6f), (164.2f, 16.2f) }) b.Prop("box", new Vector3(x, 0f, z), x * 17f % 360f);
        foreach (var (x, z) in new[] { (158.4f, 17.2f), (159.4f, 17.6f), (160.6f, 17.3f), (163f, 18.4f), (164.1f, 18.8f), (165.3f, 18.3f) })
            b.Can(new Vector3(x, 0f, z));
        // The office: the shop counter, a desk, lockers.
        b.Dress(b.Box("휴게소 사무실 계산대", 169.5f, 174.5f, 0f, 1f, 19.8f, 20.8f, b.Wood, "Wood"), "shopcounter");
        b.Prop("cashregister", new Vector3(170.5f, 1f, 20.3f), 180f);
        b.Shop(new Vector3(172f, 0f, 19f), new Vector3(172f, 2.2f, 20.8f));
        b.PropSolid("locker", new Vector3(177.1f, 0f, 14f), 270f);
        b.PropSolid("fridge", new Vector3(177.1f, 0f, 18.5f), 270f);
        // The toilets and the corridor.
        foreach (float x in new[] { 169.4f, 171.8f, 174.2f }) b.Box("화장실 칸막이", x, x + .08f, 0f, 2f, 4.3f, 6.8f, b.Metal, "Metal");
        b.Lamp(new Vector3(172.5f, 3.8f, 10.3f), 6f, .6f);
        b.Lamp(new Vector3(162f, 3.9f, 9f), 9f, .7f);

        // Who is where.
        var crowd = b.Variant("휴게소 잠든 무리 (셋 중 둘)", 2, 230);
        b.Sleepers("Ch2.04", new Vector3(160.5f, 0f, 9f), 3, 2f, b.Choice(crowd, "식당"));
        b.Sleepers("Ch2.04", new Vector3(173f, 0f, 6.8f), 2, 1.2f, b.Choice(crowd, "화장실"));
        b.Sleepers("Ch2.04", new Vector3(162f, 0f, 18f), 2, 1.5f, b.Choice(crowd, "창고"));
        b.Walkers("Ch2.04", 1, null, new Vector3(169f, 0f, 10.3f), new Vector3(177f, 0f, 10.3f));
    }

    // The car pile across the road beyond the service area (the card "불로만 치우는 바리케이드", after Guts & Blackpowder's
    // Leipzig): it only burns away. The cans from the store poured on it (Ch2.09) light it as the hold begins (Ch2.10, a
    // burn hold: its clock runs only while the pile burns, each can worth the step's burn seconds), and a can poured on it
    // meanwhile keeps it going; the cans poured go back to the store. It sinks as the hold goes (a CampaignGate holding the
    // cars), and its fire burns what comes through it (BurnFire).
    private static void BurnPile(GreyboxBuilder b)
    {
        var block = b.Gate("차 무더기 (불로만 치움)", 186f, 192f, 0f, 3.2f, -25f, 25f, "Ch2.10", new Vector3(0f, -3.3f, 0f), b.Metal, "Metal", alarm: false);
        if (block.TryGetComponent(out Renderer look)) look.enabled = false;
        GreyboxBuilder.Configure(block.GetComponent<CampaignGate>(), so => so.FindProperty("openFrom").floatValue = 0f);
        // The cars, under one holder that sinks with it.
        var cars = b.Holder("차 무더기 차들");
        GreyboxBuilder.Configure(cars.gameObject.AddComponent<CampaignGate>(), so =>
        {
            so.FindProperty("step").stringValue = "Ch2.10";
            so.FindProperty("openOffset").vector3Value = new Vector3(0f, -3.3f, 0f);
            so.FindProperty("openFrom").floatValue = 0f;
            so.FindProperty("alarm").boolValue = false;
        });
        foreach (var (z, y, yaw) in new[] { (-21f, 0f, 80f), (-14f, 0f, 105f), (-7f, 0f, 70f), (0f, 0f, 95f), (7f, 0f, 120f), (14f, 0f, 85f),
                     (21f, 0f, 100f), (-10f, 1.5f, 30f), (3f, 1.5f, 150f), (16f, 1.5f, 60f) })
        {
            var car = b.Prop("car", new Vector3(189f, y, z), yaw, (int)(Mathf.Abs(z) + y * 3f));
            if (car != null) car.transform.SetParent(cars, true);
        }
        var fire = new GameObject("차 무더기 불", typeof(BurnFire));
        fire.transform.SetParent(b.Group("◤━━ 연출 (불)"), false);
        fire.transform.position = new Vector3(189f, 0f, 0f);
        GreyboxBuilder.Configure(fire.GetComponent<BurnFire>(), so =>
        {
            so.FindProperty("step").stringValue = "Ch2.10";
            so.FindProperty("fire").boundsValue = new Bounds(new Vector3(189f, 1f, 0f), new Vector3(10f, 2f, 30f));
        });
        b.Spot("BurnPile", new Vector3(183.5f, 0f, 0f));
    }

    // The toll road (the cards "톨게이트 관제실", "검문소 무기고", "무너지는 고가 달리기"): up the ramp beyond the pile to the
    // second deck; the toll plaza, its booths and the office on its north side (lockers and servers below, the cameras
    // and the barrier's switch upstairs); the army's barrier wall it raises; the checkpoint (sandbags, containers, the
    // armory a card opens, the guard post's radio); the last span, which falls in behind them as they run for it; the way
    // down to the subway entrance. The road under the deck is walled off: the only way on is up.
    private static void TollRoad(GreyboxBuilder b)
    {
        const float D0 = 214f, D1 = 298f, Span1 = 338f;
        b.Ramp("톨게이트 진입로", new Vector3(196f, 0f, 0f), new Vector3(D0, Deck, 0f), 10f, b.Road);
        b.Box("진입로 아래 벽 남", 196f, 196.5f, 0f, 5f, -25f, -5.2f);
        b.Box("진입로 아래 벽 북", 196f, 196.5f, 0f, 5f, 5.2f, 25f);
        b.Box("고가2 아래 벽", 296.5f, 297f, 0f, 5f, -25f, 25f);
        b.Ladder("고가2 아래에서 올라가는 사다리", new Vector3(262f, 0f, -9.15f), 180f);

        b.Box("고가2 상판", D0, D1, Deck - 1f, Deck, -9f, 9f, b.Road, "Asphalt");
        for (float x = 230f; x < D1; x += 20f) b.Box("고가 기둥 " + x, x - 1f, x + 1f, 0f, Deck - 1f, -1f, 1f);
        b.Box("고가 난간 남 고가2", D0, D1, Deck, Deck + 1f, -9f, -8.7f);
        b.Box("고가 난간 북 고가2 1", D0, 223.5f, Deck, Deck + 1f, 8.7f, 9f);
        b.Box("고가 난간 북 고가2 2", 225.7f, D1, Deck, Deck + 1f, 8.7f, 9f);

        // The toll plaza: booths under a canopy, and the army's barrier wall beyond them, raised from the office.
        b.Box("요금소 지붕", 222f, 234f, Deck + 6f, Deck + 6.3f, -9f, 9f, b.Concrete);
        foreach (float z in new[] { -5.75f, 0f, 5.75f }) b.Box("요금소 " + z, 227.5f, 229.5f, Deck, Deck + 2.8f, z - .75f, z + .75f);
        b.Gate("톨게이트 차단벽", 236.6f, 237f, Deck, Deck + 4f, -9f, 9f, "Ch2.11", new Vector3(0f, 4.3f, 0f), b.Metal, "Metal", alarm: false);
        b.Spot("TollGate", new Vector3(224f, Deck, 0f));
        TollOffice(b);
        Checkpoint(b);
        TheSpan(b, D1, Span1);

        // Down to the subway entrance, and the entrance itself (chapter 3 beyond its shutter).
        b.Ramp("지하철로 내려가는 길", new Vector3(350f, 0f, 0f), new Vector3(Span1, Deck, 0f), 6f, b.Road);
        Room(b, "지하철 입구", 350f, 358f, -4f, 4f, 0f, 3.5f, Side.West, -2f, 2f, 3f);
        b.Box("입구 셔터 (3장으로)", 357f, 357.7f, 0f, 3.2f, -3.7f, 3.7f, b.Metal, "Metal");
        b.Spot("SubwayEntrance", new Vector3(354f, 0f, 0f));
        b.EnemySpawn("고가2 1", new Vector3(216f, Deck, -6f));
        b.EnemySpawn("고가2 2", new Vector3(246f, Deck, 6f));
        b.EnemySpawn("고가2 3", new Vector3(266f, Deck, -6f));
        b.EnemySpawn("검문소 뒤", new Vector3(296f, Deck, 6f));
        b.EnemySpawn("경간 아래", new Vector3(320f, 0f, 14f));
        b.EnemySpawn("입구 앞", new Vector3(344f, 0f, -14f));
    }

    // The toll office, hanging off the deck's north side (x 222-234, z 9-17): lockers and the server room on the deck's
    // level, a flight up to the camera room and the control room (the barrier's switch, Ch2.11). Its cameras look over the
    // checkpoint (GimmickCamera). Asleep in the locker room, one in the camera room; a walker among the booths.
    private static void TollOffice(GreyboxBuilder b)
    {
        const float X0 = 222f, X1 = 234f, Z0 = 9f, Z1 = 17f, Up = Deck + 3f, Top = Deck + 6f, Mid = 228f;
        b.Box("관제실 바닥", X0, X1, Deck - 1f, Deck, Z0, Z1, b.Concrete, "Tile");
        foreach (float x in new[] { X0 + .5f, X1 - .5f }) b.Box("관제실 기둥", x - .4f, x + .4f, 0f, Deck - 1f, Z1 - 1.2f, Z1 - .4f);
        WallX(b, "관제실 남벽", X0, X1, Z0, Z0 + Wall, Deck, Top, DoorIn(224f, 225.2f, Deck));
        b.Box("관제실 북벽", X0, X1, Deck, Top, Z1 - Wall, Z1);
        b.Box("관제실 서벽", X0, X0 + Wall, Deck, Top, Z0 + Wall, Z1 - Wall);
        b.Box("관제실 동벽", X1 - Wall, X1, Deck, Top, Z0 + Wall, Z1 - Wall);
        b.Box("관제실 지붕", X0, X1, Top, Top + Wall, Z0, Z1);
        WallZ(b, "관제실 1층 가운데 벽", Mid, Mid + Wall, Z0 + Wall, Z1 - Wall, Deck, Up - Wall, DoorIn(12f, 13.2f, Deck));
        SlabWith(b, "관제실 2층 바닥", Up, X0 + Wall, X1 - Wall, Z0 + Wall, Z1 - Wall, b.Concrete, "Tile", (X0 + Wall, 227.8f, 14.8f, Z1 - Wall));
        b.Ramp("관제실 계단", new Vector3(222.9f, Deck, 15.6f), new Vector3(227.6f, Up, 15.6f), 1.4f, b.Concrete);
        WallZ(b, "관제실 2층 가운데 벽", Mid, Mid + Wall, Z0 + Wall, Z1 - Wall, Up, Top, DoorIn(10f, 11.2f, Up));
        // Below: lockers, the servers.
        foreach (float x in new[] { 223f, 224.2f, 225.4f }) b.PropSolid("locker", new Vector3(x, Deck, 9.75f), 0f);
        foreach (float z in new[] { 10.5f, 12.8f, 15.1f }) b.Box("서버 선반", 232.6f, 233.5f, Deck, Deck + 2f, z - .5f, z + .5f, b.Metal, "Metal");
        // Upstairs: the camera desk (its screens) and the control panel.
        b.PropSolid("hubdesk", new Vector3(224.5f, Up, 10.2f), 0f);
        foreach (int i in new[] { 0, 1, 2 }) b.Prop("hubscreen", new Vector3(223.7f + i * .8f, Up + .78f, 10.1f), 0f, i);
        var watching = b.Mark("CCTV가 보는 곳", new Vector3(283f, Deck, 0f));
        var cameras = b.Switch<GimmickCamera>("관제실 CCTV", new Vector3(224.5f, Up + 1.1f, 10.4f));
        GreyboxBuilder.Configure(cameras, so =>
        {
            so.FindProperty("watched").objectReferenceValue = watching;
            so.FindProperty("radius").floatValue = 22f;
            so.FindProperty("reach").floatValue = 1.8f;
        });
        b.Dress(b.Box("차단벽 제어반", 231.5f, 233f, Up, Up + 1.3f, 12.4f, 13.4f, b.Marker), "controlboxFit");
        b.Spot("TollControl", new Vector3(231f, Up, 12.9f));
        b.Lamp(new Vector3(230.5f, Up + 2.5f, 13f), 6f, .7f);
        b.Sleepers("Ch2.11", new Vector3(225f, Deck, 13f), 2, 1.2f);
        b.Sleepers("Ch2.11", new Vector3(226f, Up, 12.5f), 1, .6f);
        b.Walkers("Ch2.05", 1, null, new Vector3(226f, Deck, -3f), new Vector3(231f, Deck, -3f), new Vector3(231f, Deck, 3f));
    }

    // The army checkpoint (x 270-296): sandbags with a gap, containers, the armory (a container a card opens, the card on
    // the officer asleep among them: a run's pick of three places, GimmickItem with the sleeper under it), the guard post
    // with the radio that starts the finale (Ch2.06), the hold's place.
    private static void Checkpoint(GreyboxBuilder b)
    {
        b.Dress(b.Box("모래주머니 남", 272f, 273f, Deck, Deck + 1.2f, -9f, -3f, b.Wood, "Tarp"), "sandbags");
        b.Dress(b.Box("모래주머니 북", 272f, 273f, Deck, Deck + 1.2f, 3f, 9f, b.Wood, "Tarp"), "sandbags");
        for (float z = -8.4f; z <= -3.6f; z += 1.6f) b.Prop("barbedwire", new Vector3(272.5f, Deck + 1.2f, z), 90f);
        for (float z = 3.6f; z <= 8.4f; z += 1.6f) b.Prop("barbedwire", new Vector3(272.5f, Deck + 1.2f, z), 90f);
        b.Dress(b.Box("군 컨테이너", 277f, 283f, Deck, Deck + 2.6f, -8.6f, -6.1f, b.Metal, "MetalBox"), "container");
        // The armory: a container's walls with a door on its south face, locked (the officer's card), loud to break.
        const float A0 = 277f, A1 = 283f, AZ0 = 6.1f, AZ1 = 8.6f;
        b.Box("무기고 서벽", A0, A0 + .2f, Deck, Deck + 2.6f, AZ0, AZ1, b.Metal, "MetalBox");
        b.Box("무기고 동벽", A1 - .2f, A1, Deck, Deck + 2.6f, AZ0, AZ1, b.Metal, "MetalBox");
        b.Box("무기고 북벽", A0, A1, Deck, Deck + 2.6f, AZ1 - .2f, AZ1, b.Metal, "MetalBox");
        WallX(b, "무기고 남벽", A0, A1, AZ0, AZ0 + .2f, Deck, Deck + 2.6f, DoorIn(279f, 280.6f, Deck));
        b.Box("무기고 지붕", A0, A1, Deck + 2.6f, Deck + 2.8f, AZ0, AZ1, b.Metal, "MetalBox");
        var armory = b.Blocker("무기고 문 (장교 카드)", 279f, 280.6f, Deck, Deck + DoorHigh, AZ0 + .02f, AZ0 + .18f, null, b.Metal, "Metal");
        var lockDoor = b.AddSwitch<GimmickLock>(armory);
        GreyboxBuilder.Configure(lockDoor, so =>
        {
            so.FindProperty("item").stringValue = "armorycard";
            so.FindProperty("itemTitle").stringValue = "gimmick.item.armorycard";
            so.FindProperty("toughness").floatValue = 600f;
            so.FindProperty("wakeBox").boundsValue = new Bounds(new Vector3(283f, Deck + 1.5f, 0f), new Vector3(30f, 5f, 20f));
            so.FindProperty("openOffset").vector3Value = new Vector3(1.7f, 0f, 0f);
        });
        b.Pickup(new Vector3(278.2f, Deck, 7.6f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(281.8f, Deck, 7.6f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(280f, Deck, 7.9f), PickupSpot.Roll.Any);

        // The officer's card, where he lies asleep this run (three places).
        var card = b.Switch<GimmickItem>("장교 카드", new Vector3(275f, Deck, -4.5f));
        var spots = new List<Transform>
        {
            b.Mark("장교 자리 1", new Vector3(275f, Deck, -4.5f), 0f, card.transform.parent),
            b.Mark("장교 자리 2", new Vector3(286f, Deck, 4.8f), 0f, card.transform.parent),
            b.Mark("장교 자리 3", new Vector3(294.5f, Deck, 1.5f), 0f, card.transform.parent),
        };
        b.Box("카드", -.06f, .06f, 0f, .02f, -.04f, .04f, b.Marker, solid: false, group: card.transform);
        var shine = new GameObject("카드 빛", typeof(Light)).GetComponent<Light>();
        shine.transform.SetParent(card.transform, false);
        shine.transform.localPosition = Vector3.up * .3f;
        shine.type = LightType.Point;
        shine.range = 1.4f;
        shine.intensity = .7f;
        shine.color = new Color(.5f, .85f, 1f);
        shine.shadows = LightShadows.None;
        shine.lightmapBakeType = LightmapBakeType.Realtime;
        b.Sleepers("Ch2.06", new Vector3(275f, Deck, -4.5f) + new Vector3(.8f, 0f, 0f), 1, .1f, card.transform);
        GreyboxBuilder.Configure(card, so =>
        {
            so.FindProperty("id").stringValue = "armorycard";
            so.FindProperty("title").stringValue = "gimmick.item.armorycard";
            so.FindProperty("salt").intValue = 21;
            FillObjects(so.FindProperty("spots"), spots);
        });

        // The guard post with the radio.
        const float G0 = 286f, G1 = 294f, GZ0 = -9f, GZ1 = -4f;
        b.Box("검문소 초소 서벽", G0, G0 + Wall, Deck, Deck + 3f, GZ0, GZ1);
        b.Box("검문소 초소 동벽", G1 - Wall, G1, Deck, Deck + 3f, GZ0, GZ1);
        b.Box("검문소 초소 남벽", G0, G1, Deck, Deck + 3f, GZ0, GZ0 + Wall);
        WallX(b, "검문소 초소 북벽", G0, G1, GZ1 - Wall, GZ1, Deck, Deck + 3f, DoorIn(289f, 290.2f, Deck));
        b.Box("검문소 초소 지붕", G0, G1, Deck + 3f, Deck + 3.3f, GZ0, GZ1);
        b.Dress(b.Box("초소 무전기", 292f, 293.1f, Deck, Deck + 1.1f, -8.6f, -7.6f, b.Marker), "controlboxFit");
        b.Spot("CheckpointRadio", new Vector3(291.5f, Deck, -7f));
        b.Spot("TollHold", new Vector3(284f, Deck, 0f));
        b.PropSolid("militarytruck", new Vector3(262f, Deck, -5.5f), 0f);
        b.Dress(b.Box("HESCO 고가2 남", 268f, 269.9f, Deck, Deck + 2f, -9f, -6.5f, b.Wood, "Tarp"), "hesco");
        b.Dress(b.Box("HESCO 고가2 북", 268f, 269.9f, Deck, Deck + 2f, 6.5f, 9f, b.Wood, "Tarp"), "hesco");
        b.Walkers("Ch2.06", 1, null, new Vector3(275f, Deck, 1f), new Vector3(292f, Deck, 1f));
    }

    // The last span (the card "무너지는 고가 달리기", after Left 4 Dead 2's bridge run): four 10 m segments past the
    // checkpoint, each its slab and rails under a holder (in the walk-mesh bake while it stands), with a loose copy to
    // tumble down and a carve over its place; through the escape (Ch2.08) they go one after another from the west end.
    private static void TheSpan(GreyboxBuilder b, float x0, float x1)
    {
        var fall = b.Switch<GimmickCollapse>("무너지는 경간", Vector3.zero);
        var holders = new List<Transform>();
        var copies = new List<Transform>();
        var carves = new List<NavMeshObstacle>();
        int count = Mathf.RoundToInt((x1 - x0) / 10f);
        for (int i = 0; i < count; i++)
        {
            float a = x0 + i * 10f, c = a + 10f;
            var holder = b.Holder($"경간 {i + 1}");
            b.Box($"고가 상판 경간 {i + 1}", a, c, Deck - 1f, Deck, -9f, 9f, b.Road, "Asphalt", true, holder);
            b.Box($"고가 난간 남 경간 {i + 1}", a, c, Deck, Deck + 1f, -9f, -8.7f, null, null, true, holder);
            b.Box($"고가 난간 북 경간 {i + 1}", a, c, Deck, Deck + 1f, 8.7f, 9f, null, null, true, holder);
            holders.Add(holder);
            var copy = new GameObject($"무너지는 경간 {i + 1}").transform;
            copy.SetParent(fall.transform, false);
            copy.position = new Vector3((a + c) * .5f, Deck - .5f, 0f);
            b.Box("떨어지는 상판", -5f, 5f, -.5f, .5f, -9f, 9f, b.Road, solid: false, group: copy);
            copies.Add(copy);
            var carve = new GameObject($"경간 {i + 1} 길 끊기", typeof(NavMeshObstacle)).GetComponent<NavMeshObstacle>();
            carve.transform.SetParent(fall.transform, false);
            carve.transform.position = new Vector3((a + c) * .5f, Deck - .5f, 0f);
            carve.shape = NavMeshObstacleShape.Box;
            carve.size = new Vector3(10f, 3f, 18f);
            carve.carving = true;
            carve.carveOnlyStationary = false;
            carves.Add(carve);
        }
        b.Box("고가 기둥 경간 1", 307f, 309f, 0f, Deck - 1f, -1f, 1f);
        b.Box("고가 기둥 경간 2", 327f, 329f, 0f, Deck - 1f, -1f, 1f);
        GreyboxBuilder.Configure(fall, so =>
        {
            so.FindProperty("step").stringValue = "Ch2.08";
            so.FindProperty("first").floatValue = 2f;
            so.FindProperty("every").floatValue = 2.4f;
            so.FindProperty("drop").floatValue = Deck - .5f;
            FillObjects(so.FindProperty("segments"), holders);
            FillObjects(so.FindProperty("copies"), copies);
            FillObjects(so.FindProperty("carves"), carves);
        });
    }
}
