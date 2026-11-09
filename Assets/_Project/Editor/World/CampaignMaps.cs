using UnityEditor;
using UnityEngine;

/// <summary>
/// The campaign's chapters as grey blocks (multiplayer plan stage 5A), one item each under
/// Tools > World > 캠페인 회색 맵. The story runs on from one chapter to the next:
/// 1장 옥상 탈출: survivors in a downtown flat cross the rooftops to a building site whose roof the rescue helicopter
///   lands on. Alarm shutter in the alley (hold out), rest stop in a corner shop, finale on the helipad. The helicopter
///   takes them, but goes down over the ring road.
/// 2장 막힌 고가도로: from the wreck along the jammed overpass toward the army checkpoint at the toll gate. The road is
///   broken, so they jump down to the road below (no way back). Rest stop at a filling station, finale at the gate.
/// 3장 지하철: the checkpoint has fallen; the way out is the subway under the river. Fuel cans for the depot's generator,
///   rest stop in the depot, finale at the far bank's exit.
/// Each chapter runs along +X; the gimmicks (5C) and objectives (5B) hang on the named spots under 진행 자리.
/// </summary>
public static class CampaignMaps
{
    private const float Wall = .3f, ParapetHeight = .9f;
    private enum Side { West, East, South, North }

    [MenuItem("Tools/World/캠페인 회색 맵/1장 옥상 탈출")]
    private static void Rooftops()
    {
        var b = GreyboxBuilder.Begin("Ch1_Rooftops", "1장 옥상 탈출");
        if (b == null) return;

        // The ground and the edge of the map.
        b.Box("땅", -30f, 240f, -.5f, 0f, -50f, 50f, b.Road, "Asphalt");
        b.Box("맵 끝 서", -30f, -29f, 0f, 8f, -50f, 50f);
        b.Box("맵 끝 동", 239f, 240f, 0f, 8f, -50f, 50f);
        b.Box("맵 끝 남", -30f, 240f, 0f, 8f, -50f, -49f);
        b.Box("맵 끝 북", -30f, 240f, 0f, 8f, 49f, 50f);

        // 1. The flat's roof: the start room (safe room), a low wall and two air conditioners to vault.
        b.Box("B1 아파트", 0f, 24f, 0f, 18f, -10f, 10f);
        Parapets(b, "B1", 0f, 24f, -10f, 10f, 18f);
        b.Box("B1 난간 서", 0f, Wall, 18f, 18f + ParapetHeight, -10f, 10f);
        Room(b, "시작 안전가옥", 2f, 9f, -3f, 3f, 18f, 3f, Side.East, -1f, 1f);
        b.PlayerStart(new Vector3(5f, 18f, 0f), 90f);
        b.Spot("Start", new Vector3(5.5f, 18f, 0f));
        b.Box("B1 낮은 벽", 16.6f, 17f, 18f, 19f, -3f, 3f);
        b.Box("B1 실외기 1", 13f, 15f, 18f, 19.1f, 4f, 6f, b.Metal, "Metal");
        b.Box("B1 실외기 2", 13f, 15f, 18f, 19.1f, -6f, -4f, b.Metal, "Metal");

        // 2. Over a 3 m gap, 2 m down. The lift room is climbed (wall climb, or its ladder), and the cable from its top
        // crosses the street.
        b.Box("B2", 27f, 47f, 0f, 16f, -10f, 10f);
        Parapets(b, "B2", 27f, 47f, -10f, 10f, 16f);
        b.Box("B2 엘리베이터실", 37f, 43f, 16f, 19.4f, -3f, 3f);
        b.Ladder("B2 엘리베이터실 사다리", new Vector3(40f, 16f, -3.15f), 180f, 4.4f);
        b.Box("집라인 기둥 1", 42.6f, 42.8f, 19.4f, 22.2f, -.1f, .1f, b.Metal, solid: false);
        b.Zipline("길 건너는 집라인", new Vector3(42.5f, 21.7f, 0f), new Vector3(70f, 15.1f, 0f));
        b.Box("집라인 기둥 2", 70.2f, 70.4f, 13f, 15.8f, -.1f, .1f, b.Metal, solid: false);
        b.Spot("LiftRoom", new Vector3(40f, 19.4f, 0f));
        b.Spot("ZiplineEnd", new Vector3(72f, 13f, 0f));
        b.EnemySpawn("B2 옥상 1", new Vector3(32f, 16f, 6f));
        b.EnemySpawn("B2 옥상 2", new Vector3(45f, 16f, -7f));

        b.Box("길1 차 1", 50f, 54.5f, 0f, 1.5f, -6f, -4.2f, b.Metal, "Metal");
        b.Box("길1 차 2", 56f, 60.5f, 0f, 1.5f, 3f, 4.8f, b.Metal, "Metal");
        b.EnemySpawn("길1", new Vector3(55f, 0f, -1f));
        b.EnemySpawn("길1 남쪽", new Vector3(55f, 0f, -25f));

        // 3. Too far to jump to the next roof: the swing bar over the gap. A ladder brings a faller back up.
        b.Box("B3", 63f, 80f, 0f, 13f, -10f, 10f);
        Parapets(b, "B3", 63f, 80f, -10f, 10f, 13f);
        b.Box("B3 위성안테나", 66f, 68f, 13f, 14.2f, 6f, 8f, b.Metal, "Metal");
        b.Bar("틈 위 철봉", new Vector3(83.5f, 15.9f, 0f), 90f, 3f);
        b.Box("철봉 기둥 1", 83.4f, 83.6f, 0f, 16.1f, -1.9f, -1.7f, b.Metal, solid: false);
        b.Box("철봉 기둥 2", 83.4f, 83.6f, 0f, 16.1f, 1.7f, 1.9f, b.Metal, solid: false);
        b.Ladder("틈에 떨어지면 올라오는 사다리", new Vector3(80.15f, 0f, 6f), 90f, 14f);
        b.EnemySpawn("B3 옥상 1", new Vector3(70f, 13f, -6f));
        b.EnemySpawn("B3 옥상 2", new Vector3(75f, 13f, 7f));

        // 4. Down into the alley by steps that don't hurt: the roof (12), the annex (8), a lorry (3), the ground.
        b.Box("B4", 87f, 104f, 0f, 12f, -10f, 10f);
        Parapets(b, "B4", 87f, 104f, -10f, 10f, 12f);
        b.Box("B4 별관", 104f, 112f, 0f, 8f, 4f, 50f);
        b.Box("골목 트럭", 108f, 114f, 0f, 3f, 0f, 3f, b.Metal, "MetalBox");
        b.Ladder("B4 비상 사다리", new Vector3(104.15f, 0f, -2f), 90f, 13f);
        b.Spot("B4Roof", new Vector3(95f, 12f, 0f));
        b.EnemySpawn("B4 옥상 1", new Vector3(95f, 12f, -6f));
        b.EnemySpawn("B4 옥상 2", new Vector3(100f, 12f, 6f));

        // 5. The alley, closed in on every side but the shutter. Its button starts the hold (Ch1.06): the alarm wails and
        // the shutter rises with the hold, so the way on (and the street's horde) opens when it ends.
        b.Box("B5 골목 남쪽 건물", 104f, 137f, 0f, 10f, -50f, -4f);
        b.Box("B6 골목 북쪽 건물", 112f, 137f, 0f, 9f, 4f, 50f);
        b.Box("B7 창고 남", 137f, 141f, 0f, 17f, -50f, -2.5f);
        b.Box("B7 창고 북", 137f, 141f, 0f, 17f, 2.5f, 50f);
        b.Box("B7 셔터 위 벽", 137f, 141f, 4.5f, 17f, -2.5f, 2.5f);
        b.Gate("셔터 (버티는 동안 올라감, 경보)", 136.7f, 137f, 0f, 4.6f, -2.6f, 2.6f, "Ch1.06", new Vector3(0f, 4.4f, 0f), b.Metal, "Metal");
        b.Box("셔터 버튼", 134f, 134.4f, 1f, 1.6f, -4f, -3.85f, b.Marker, solid: false);
        b.Spot("Alley", new Vector3(110f, 0f, -1f));
        b.Spot("ShutterButton", new Vector3(134.2f, 0f, -3f));
        b.Spot("AlleyHold", new Vector3(124f, 0f, 0f));
        b.Box("골목 쓰레기통 1", 118f, 120f, 0f, 1.4f, -3.9f, -2.6f, b.Metal, "MetalBox");
        b.Box("골목 쓰레기통 2", 126f, 128f, 0f, 1.4f, 2.6f, 3.9f, b.Metal, "MetalBox");
        b.Box("골목 팔레트", 122f, 123.2f, 0f, .9f, -1f, .2f, b.Wood, "WoodPlank");
        b.EnemySpawn("골목 서쪽 끝", new Vector3(106f, 0f, -2f));
        b.EnemySpawn("골목 서쪽 구석", new Vector3(105.5f, 0f, 2.5f));
        b.EnemySpawn("골목 트럭 옆", new Vector3(113f, 0f, -3.2f));
        b.EnemySpawn("골목 가운데", new Vector3(120f, 0f, 2f));
        b.EnemySpawn("셔터 통로", new Vector3(139f, 0f, 0f));

        // 6. The corner shop over the street: the rest stop (doors 5D, shop stage 6).
        Room(b, "편의점", 145f, 157f, 10f, 20f, 0f, 4.5f, Side.South, 149f, 151.5f);
        b.Door("편의점 문", 149f, 151.5f, 0f, 2.4f, 10.05f, 10.25f, "RestStop", new Vector3(2.5f, 0f, 0f));
        b.Box("편의점 계산대", 147f, 152f, 0f, 1f, 15f, 16f, b.Wood, "Wood");
        b.Shop(new Vector3(149.5f, 0f, 14.3f), new Vector3(149.5f, 2.2f, 16f));
        b.Box("편의점 진열대", 153.5f, 154.3f, 0f, 1.8f, 12f, 18f, b.Metal, "Metal");
        b.Spot("RestStop", new Vector3(151f, 0f, 15f));
        b.Box("길2 차 1", 144f, 148.5f, 0f, 1.5f, -8f, -6.2f, b.Metal, "Metal");
        b.Box("길2 차 2", 158f, 162.5f, 0f, 1.5f, -3f, -1.2f, b.Metal, "Metal");
        b.EnemySpawn("길2", new Vector3(150f, 0f, -12f));
        b.EnemySpawn("길2 북쪽", new Vector3(162f, 0f, 8f));

        // 7. The building site, through the gate in its fence. Players climb two containers, a wall up to the scaffold
        // and its ladder to the roof (helipad, finale); enemies walk two-lane switchback ramps up the south side.
        b.Box("공사장 펜스 남", 170f, 170.2f, 0f, 2.2f, -50f, -3f, b.Metal, "MetalPipe");
        b.Box("공사장 펜스 북", 170f, 170.2f, 0f, 2.2f, 3f, 50f, b.Metal, "MetalPipe");
        b.Spot("SiteGate", new Vector3(172f, 0f, 0f));
        b.Box("컨테이너 1", 176f, 182f, 0f, 2.6f, 10f, 12.5f, b.Metal, "MetalBox");
        b.Box("컨테이너 2", 179f, 182f, 2.6f, 5.2f, 10f, 12.5f, b.Metal, "MetalBox");
        b.Box("공사 중 건물", 182f, 200f, 0f, 14f, -9f, 9f);
        b.Box("비계 벽", 182f, 182.3f, 5.2f, 8.7f, 9f, 12f, b.Wood, "WoodPanel");
        b.Box("비계 발판", 182.3f, 188f, 8.5f, 8.7f, 9f, 12f, b.Wood, "WoodPlank");
        b.Box("비계 기둥", 187.8f, 188f, 0f, 8.5f, 11.8f, 12f, b.Metal, solid: false);
        b.Ladder("비계 사다리", new Vector3(185f, 8.7f, 9.15f), 0f, 6.3f);
        b.Ladder("바닥에서 지붕 사다리", new Vector3(195f, 0f, 9.15f), 0f, 15f);
        const float LaneA = -12f, LaneB = -10f, Lane = 2f;
        b.Ramp("적 경사로 1", new Vector3(186f, 0f, LaneA), new Vector3(195f, 3.5f, LaneA), Lane);
        b.Box("경사로 층계참 1", 195f, 198f, 3.2f, 3.5f, -13f, -9f);
        b.Ramp("적 경사로 2", new Vector3(195f, 3.5f, LaneB), new Vector3(186f, 7f, LaneB), Lane);
        b.Box("경사로 층계참 2", 183f, 186f, 6.7f, 7f, -13f, -9f);
        b.Ramp("적 경사로 3", new Vector3(186f, 7f, LaneA), new Vector3(195f, 10.5f, LaneA), Lane);
        b.Box("경사로 층계참 3", 195f, 198f, 10.2f, 10.5f, -13f, -9f);
        b.Ramp("적 경사로 4", new Vector3(195f, 10.5f, LaneB), new Vector3(186f, 14f, LaneB), Lane);
        b.Box("경사로 층계참 4 (지붕)", 183f, 186f, 13.7f, 14f, -13f, -9f);
        b.Box("헬기 착륙장 표시", 186f, 196f, 14f, 14.03f, -4f, 4f, b.Marker, solid: false);
        b.Spot("Helipad", new Vector3(191f, 14f, 0f));
        b.Box("무전기", 187.6f, 188.4f, 14f, 15.1f, 4.6f, 5.2f, b.Marker);
        b.Spot("Radio", new Vector3(188f, 14f, 4f));
        b.Box("크레인 기둥", 203f, 204.6f, 0f, 30f, -.8f, .8f, b.Metal, "MetalCrane");
        b.Box("크레인 팔", 176f, 212f, 30f, 31f, -.5f, .5f, b.Metal, solid: false);
        b.Box("크레인 무게추", 208f, 212f, 28.5f, 30f, -1f, 1f, solid: false);
        b.EnemySpawn("공사장 남서", new Vector3(175f, 0f, -15f));
        b.EnemySpawn("공사장 남동", new Vector3(205f, 0f, -15f));
        b.EnemySpawn("공사장 북동", new Vector3(205f, 0f, 14f));
        b.EnemySpawn("공사장 북서", new Vector3(176f, 0f, 20f));
        b.EnemySpawn("경사로 아래", new Vector3(186f, 0f, -16f));

        b.Finish("1장 옥상 탈출을 지었어요 (Scenes/Campaign/Ch1_Rooftops). 메인 메뉴의 맵 목록에도 나와요.\n" +
                 "길: 안전가옥 → 옥상 틈 점프 → 엘리베이터실 벽 오르기 → 집라인 → 철봉 → 골목으로 내려가기 → 셔터 → 편의점 → 공사장 컨테이너·비계·사다리 → 헬기 착륙장.");
    }

    [MenuItem("Tools/World/캠페인 회색 맵/2장 막힌 고가도로")]
    private static void Overpass()
    {
        var b = GreyboxBuilder.Begin("Ch2_Overpass", "2장 막힌 고가도로");
        if (b == null) return;
        const float Deck = 8f;

        b.Box("땅", -20f, 240f, -.5f, 0f, -40f, 40f, b.Road, "Asphalt");
        b.Box("맵 끝 서", -20f, -19f, 0f, 6f, -40f, 40f);
        b.Box("맵 끝 동", 239f, 240f, 0f, 6f, -40f, 40f);
        b.Box("맵 끝 남", -20f, 240f, 0f, 6f, -40f, -39f);
        b.Box("맵 끝 북", -20f, 240f, 0f, 6f, 39f, 40f);

        // The overpass, 8 m up, broken off at x 120. Under its first stretch is a fenced pit: a fall from the deck lands
        // there, and a ladder up the deck's side brings the faller back.
        b.Box("고가 상판", -19f, 120f, Deck - 1f, Deck, -7f, 7f, b.Concrete, "Asphalt");
        for (float x = 0f; x <= 100f; x += 20f) b.Box("고가 기둥 " + x, x - 1f, x + 1f, 0f, Deck - 1f, -1f, 1f);
        b.Box("고가 난간 북", -19f, 120f, Deck, Deck + 1f, 6.7f, 7f);
        b.Box("고가 난간 남 1", -19f, 79f, Deck, Deck + 1f, -7f, -6.7f);
        b.Box("고가 난간 남 2", 81f, 120f, Deck, Deck + 1f, -7f, -6.7f);
        b.Box("구덩이 벽", 100f, 101f, 0f, 6f, -39f, 39f);
        b.Ladder("구덩이에서 고가로 올라가는 사다리", new Vector3(80f, 0f, -7.15f), 180f, Deck + 1f);
        b.EnemySpawn("구덩이 1", new Vector3(40f, 0f, 12f));
        b.EnemySpawn("구덩이 2", new Vector3(90f, 0f, -12f));

        // The wreck they crawl out of.
        b.Box("헬기 동체", 2f, 9f, Deck, Deck + 2.2f, 1f, 4f, b.Metal, "Metal");
        b.Box("헬기 꼬리", 9f, 16f, Deck + 1f, Deck + 1.6f, 2.2f, 2.8f, b.Metal, "Metal");
        b.Box("헬기 날개", 0f, 12f, Deck + 2.3f, Deck + 2.4f, 2.3f, 2.7f, b.Metal, solid: false);
        b.PlayerStart(new Vector3(4f, Deck, -3f), 90f);
        b.Spot("Start", new Vector3(4f, Deck, -3f));

        // The jam: rows of stopped cars and two buses to run over and between, an overturned lorry across every lane.
        const float LaneA = -5f, LaneB = -1.8f, LaneC = 1.8f, LaneD = 5f;
        Car(b, 18f, Deck, LaneA); Car(b, 18f, Deck, LaneC);
        Car(b, 24f, Deck, LaneB); Car(b, 24f, Deck, LaneD);
        Car(b, 30f, Deck, LaneA); Car(b, 30f, Deck, LaneB);
        Bus(b, "고가 버스 1", 34f, 46f, Deck, 1f, 3.6f);
        Car(b, 49f, Deck, LaneA); Car(b, 50f, Deck, LaneB);
        b.Box("뒤집힌 트럭", 56f, 63f, Deck, Deck + 2.6f, -6.6f, 6.6f, b.Metal, "MetalBox");
        b.Spot("Overturn", new Vector3(66f, Deck, 0f));
        Car(b, 68f, Deck, LaneC); Car(b, 70f, Deck, LaneA);
        Car(b, 75f, Deck, LaneD); Car(b, 76f, Deck, LaneB);
        Bus(b, "고가 버스 2", 84f, 96f, Deck, -6.3f, -3.7f);
        Car(b, 100f, Deck, LaneC); Car(b, 106f, Deck, LaneD); Car(b, 107f, Deck, LaneA);
        b.EnemySpawn("고가 1", new Vector3(21f, Deck, 0f));
        b.EnemySpawn("고가 2", new Vector3(45f, Deck, -4.5f));
        b.EnemySpawn("고가 3", new Vector3(73f, Deck, 3.4f));
        b.EnemySpawn("고가 4", new Vector3(104f, Deck, -2f));

        // The break: jagged ends, and a bus parked under it, 4.8 m down, which doesn't hurt. From the road there is no way
        // back up: no wall under the broken end to climb, and the deck is beyond a jump from the bus.
        b.Box("끊어진 끝 조각 1", 120f, 121.5f, Deck - 1f, Deck - .3f, -6f, -2.5f, solid: false);
        b.Box("끊어진 끝 조각 2", 120f, 122f, Deck - 1.4f, Deck - .9f, 1.5f, 4.5f, solid: false);
        b.Box("철근", 120f, 123f, Deck - .6f, Deck - .55f, -1f, 1f, b.Metal, solid: false);
        b.Spot("BrokenEdge", new Vector3(116f, Deck, 0f));
        Bus(b, "아래 버스", 119f, 131f, 0f, -3f, -.4f);
        b.Spot("UnderBus", new Vector3(126f, 0f, 4f));

        // The road below: sound barriers either side, a filling station (rest stop), abandoned cars.
        b.Box("방음벽 남", 101f, 239f, 0f, 5f, -25.3f, -25f);
        b.Box("방음벽 북", 101f, 239f, 0f, 5f, 25f, 25.3f);
        Car(b, 136f, 0f, -8f); Car(b, 152f, 0f, -14f); Car(b, 176f, 0f, 6f); Car(b, 181f, 0f, -10f);
        b.Box("주유소 지붕", 140f, 156f, 5f, 5.4f, 8f, 20f, b.Metal, "Metal");
        foreach (var (x, z) in new[] { (140f, 8f), (155.5f, 8f), (140f, 19.5f), (155.5f, 19.5f) })
            b.Box("주유소 기둥", x, x + .5f, 0f, 5f, z, z + .5f, b.Metal, "MetalPipe");
        b.Box("주유기 1", 145f, 146f, 0f, 1.6f, 13f, 14f, b.Metal, "Metal");
        b.Box("주유기 2", 150f, 151f, 0f, 1.6f, 13f, 14f, b.Metal, "Metal");
        Room(b, "주유소 가게", 158f, 168f, 12f, 22f, 0f, 4f, Side.South, 161f, 163.5f);
        b.Door("주유소 가게 문", 161f, 163.5f, 0f, 2.4f, 12.05f, 12.25f, "Station", new Vector3(2.5f, 0f, 0f));
        b.Box("가게 계산대", 160f, 164f, 0f, 1f, 18.5f, 19.5f, b.Wood, "Wood");
        b.Shop(new Vector3(162f, 0f, 17.8f), new Vector3(162f, 2.2f, 19.5f));
        b.Spot("Station", new Vector3(163f, 0f, 17f));
        b.EnemySpawn("아래 도로 1", new Vector3(110f, 0f, 12f));
        b.EnemySpawn("아래 도로 2", new Vector3(140f, 0f, -15f));
        b.EnemySpawn("아래 도로 3", new Vector3(175f, 0f, 16f));
        b.EnemySpawn("아래 도로 4", new Vector3(185f, 0f, -16f));

        // The toll gate: booths under a canopy, then the fallen army checkpoint: sandbags, containers and a guard post
        // with the radio (finale). The subway entrance past it is where the chapter ends.
        b.Box("톨게이트 지붕", 188f, 198f, 6f, 6.5f, -24f, 24f, b.Concrete);
        for (float z = -18f; z <= 18f; z += 9f) b.Box("요금소 " + z, 191.5f, 194f, 0f, 2.8f, z - 1.25f, z + 1.25f);
        b.Spot("TollGate", new Vector3(198f, 0f, 0f));
        b.Box("모래주머니 남", 204f, 205f, 0f, 1.2f, -20f, -3f, b.Wood, "Tarp");
        b.Box("모래주머니 북", 204f, 205f, 0f, 1.2f, 3f, 20f, b.Wood, "Tarp");
        b.Box("군 컨테이너 1", 210f, 216f, 0f, 2.6f, -14f, -11.5f, b.Metal, "MetalBox");
        b.Box("군 컨테이너 2", 214f, 220f, 0f, 2.6f, 14f, 16.5f, b.Metal, "MetalBox");
        Room(b, "검문소 초소", 207f, 213f, 4f, 10f, 0f, 3f, Side.West, 6f, 8f);
        b.Box("초소 무전기", 211.5f, 212.6f, 0f, 1.1f, 8.6f, 9.6f, b.Marker);
        b.Spot("CheckpointRadio", new Vector3(211f, 0f, 8f));
        b.Spot("TollHold", new Vector3(206f, 0f, 0f));
        Room(b, "지하철 입구", 225f, 233f, -4f, 4f, 0f, 3.5f, Side.West, -2f, 2f, 3f);
        b.Box("입구 셔터 (3장으로)", 232f, 232.7f, 0f, 3.2f, -3.7f, 3.7f, b.Metal, "Metal");
        b.Spot("SubwayEntrance", new Vector3(229f, 0f, 0f));
        b.EnemySpawn("톨게이트 남", new Vector3(200f, 0f, -20f));
        b.EnemySpawn("톨게이트 북", new Vector3(200f, 0f, 20f));
        b.EnemySpawn("검문소 남", new Vector3(222f, 0f, -14f));
        b.EnemySpawn("검문소 북", new Vector3(222f, 0f, 12f));
        b.EnemySpawn("주유소 뒤", new Vector3(165f, 0f, -18f));

        b.Finish("2장 막힌 고가도로를 지었어요 (Scenes/Campaign/Ch2_Overpass).\n" +
                 "길: 헬기 잔해 → 막힌 차들 → 뒤집힌 트럭 → 끊어진 끝에서 버스로 뛰어내리기(돌아갈 수 없음) → 주유소 → 톨게이트 → 초소 무전 → 버티기 → 지하철 입구.");
    }

    [MenuItem("Tools/World/캠페인 회색 맵/3장 지하철")]
    private static void Subway()
    {
        var b = GreyboxBuilder.Begin("Ch3_Subway", "3장 지하철");
        if (b == null) return;
        const float Bank = 8f;

        b.Box("바닥", -10f, 345f, -.5f, 0f, -25f, 25f, b.Concrete, "Concrete");
        b.Box("맵 끝 서", -10f, -9f, 0f, 12f, -25f, 25f);
        b.Box("맵 끝 동 (강)", 344f, 345f, 0f, 10f, -25f, 25f);
        b.Box("맵 끝 남", -10f, 345f, 0f, 12f, -25f, -24f);
        b.Box("맵 끝 북", -10f, 345f, 0f, 12f, 24f, 25f);

        // 1. The concourse, down the stairs from chapter 2's entrance: ticket barriers to vault.
        b.Box("대합실 천장", -9f, 30f, 5f, 5.5f, -12.3f, 12.3f);
        b.Box("대합실 벽 남", -9f, 30f, 0f, 5f, -12.3f, -12f);
        b.Box("대합실 벽 북", -9f, 30f, 0f, 5f, 12f, 12.3f);
        b.Box("들어온 셔터", -9f, -8.7f, 0f, 3.2f, -4f, 4f, b.Metal, "Metal");
        b.Box("개찰구 남", 15f, 15.4f, 0f, 1f, -11.7f, -1f, b.Metal, "Metal");
        b.Box("개찰구 북", 15f, 15.4f, 0f, 1f, 1f, 11.7f, b.Metal, "Metal");
        b.Box("매점", 5f, 8f, 0f, 2.4f, 7f, 11f);
        b.PlayerStart(new Vector3(-5f, 0f, 0f), 90f);
        b.Spot("Start", new Vector3(-5f, 0f, 0f));
        b.Lamp(new Vector3(2f, 4.5f, 0f));
        b.Lamp(new Vector3(22f, 4.5f, 0f));
        b.EnemySpawn("대합실", new Vector3(24f, 0f, -9f));

        // 2. The platform hall: a platform on the north side, the tracks and a stopped train on the south.
        b.Box("승강장 홀 천장", 30f, 90f, 6f, 6.5f, -12.3f, 12.3f);
        b.Box("홀 칸막이 위", 29.7f, 30f, 5f, 6.5f, -12.3f, 12.3f);
        b.Box("승강장 홀 벽 남", 30f, 90f, 0f, 6f, -12.3f, -12f);
        b.Box("승강장 홀 벽 북", 30f, 90f, 0f, 6f, 12f, 12.3f);
        b.Box("승강장", 30f, 90f, 0f, 1.1f, 0f, 12f, b.Concrete, "Tile");
        b.Ramp("승강장 오르는 길", new Vector3(26f, 0f, 6f), new Vector3(30f, 1.1f, 6f), 11.4f);
        b.Box("멈춘 열차 1", 40f, 60f, 0f, 3.4f, -9f, -2.6f, b.Metal, "Metal");
        b.Box("멈춘 열차 2", 61.5f, 82f, 0f, 3.4f, -9f, -2.6f, b.Metal, "Metal");
        foreach (float x in new[] { 40f, 55f, 70f, 85f }) b.Box("승강장 기둥", x - .4f, x + .4f, 1.1f, 6f, 5.6f, 6.4f);
        b.Box("벤치 1", 46f, 48f, 1.1f, 1.6f, 9.5f, 10.1f, b.Wood, "Wood");
        b.Box("벤치 2", 76f, 78f, 1.1f, 1.6f, 9.5f, 10.1f, b.Wood, "Wood");
        b.Spot("Platform", new Vector3(75f, 1.1f, 6f));
        b.Lamp(new Vector3(45f, 5.5f, 4f));
        b.Lamp(new Vector3(65f, 5.5f, -6f));
        b.Lamp(new Vector3(85f, 5.5f, 4f));
        b.EnemySpawn("선로 1", new Vector3(50f, 0f, -11f));
        b.EnemySpawn("승강장 끝", new Vector3(72f, 1.1f, 10f));
        b.EnemySpawn("선로 2", new Vector3(88f, 0f, -5f));
        b.Box("홀 동벽 남", 90f, 90.3f, 0f, 6.5f, -12.3f, -6.3f);
        b.Box("홀 동벽 북", 90f, 90.3f, 0f, 6.5f, 2.3f, 12.3f);
        b.Box("터널 입구 위", 90f, 90.3f, 5.5f, 6.5f, -6.3f, 2.3f);

        // 3. The dark tunnel: rubble, a pile to climb, a derailed car leaving a side gap. A fuel can lies near its end.
        b.Box("터널 벽 남", 90.3f, 170f, 0f, 5.5f, -6.3f, -6f);
        b.Box("터널 벽 북", 90.3f, 170f, 0f, 5.5f, 2f, 2.3f);
        b.Box("터널 천장", 90.3f, 170f, 5.5f, 6f, -6.3f, 2.3f);
        b.Box("잔해 1", 110f, 113f, 0f, 1.4f, -5.9f, -2f, b.Concrete, "ConcreteGrit");
        b.Box("잔해 더미", 128f, 130f, 0f, 2.2f, -2f, 1.9f, b.Concrete, "ConcreteGrit");
        b.Box("탈선한 객차", 140f, 152f, 0f, 3f, -5.7f, -2.2f, b.Metal, "Metal");
        b.Can(new Vector3(160f, 0f, 1f));
        b.Lamp(new Vector3(100f, 5f, -2f), 10f, 1.6f);
        b.Lamp(new Vector3(125f, 5f, -2f), 10f, 1.6f);
        b.Lamp(new Vector3(150f, 5f, -2f), 10f, 1.6f);
        b.EnemySpawn("터널 1", new Vector3(100f, 0f, 0f));
        b.EnemySpawn("터널 2", new Vector3(120f, 0f, -4f));
        b.EnemySpawn("터널 3", new Vector3(146f, 0f, 0f));
        b.EnemySpawn("터널 4", new Vector3(162f, 0f, -4f));
        b.Spot("DepotDoor", new Vector3(172f, 0f, -2f));

        // 4. The depot: the office (rest stop), the generator that wants four cans, five cans about (the floor, atop two
        // stacked containers, a catwalk up a ladder, the tunnel, behind the office), and the flood gate the generator opens.
        b.Box("정비창 서벽 남", 170f, 170.3f, 0f, 9.5f, -20.3f, -6.3f);
        b.Box("정비창 서벽 북", 170f, 170.3f, 0f, 9.5f, 2.3f, 20.3f);
        b.Box("정비창 서벽 위", 170f, 170.3f, 5.5f, 9.5f, -6.3f, 2.3f);
        b.Box("정비창 천장", 170f, 218f, 9f, 9.5f, -20.3f, 20.3f);
        b.Box("정비창 벽 남", 170.3f, 218f, 0f, 9f, -20.3f, -20f);
        b.Box("정비창 벽 북", 170.3f, 218f, 0f, 9f, 20f, 20.3f);
        b.Box("정비창 동벽 남", 217.7f, 218f, 0f, 9f, -20.3f, -3f);
        b.Box("정비창 동벽 북", 217.7f, 218f, 0f, 9f, 3f, 20.3f);
        b.Box("방수문 위 벽", 217.7f, 218f, 4.5f, 9f, -3f, 3f);
        b.Gate("방수문 (발전기가 돌면 열림)", 217.3f, 217.7f, 0f, 4.6f, -3.1f, 3.1f, "Ch3.04", new Vector3(0f, 4.4f, 0f), b.Metal, "Metal", alarm: false);
        Room(b, "정비창 사무실", 172f, 182f, 10f, 18f, 0f, 3f, Side.South, 175f, 177.5f);
        b.Door("사무실 문", 175f, 177.5f, 0f, 2.4f, 10.05f, 10.25f, "Office", new Vector3(2.5f, 0f, 0f));
        b.Spot("Office", new Vector3(177f, 0f, 14.5f));
        b.Box("사무실 보급함", 179f, 181.5f, 0f, 1.8f, 16.6f, 17.6f, b.Metal, "MetalBox");
        b.Shop(new Vector3(180.2f, 0f, 15.9f), new Vector3(180.2f, 2.2f, 17.6f));
        b.Lamp(new Vector3(177f, 2.6f, 14f), 6f, 1.4f);
        b.Box("발전기", 205f, 209f, 0f, 2.5f, -6f, -2f, b.Metal, "Metal");
        b.Box("발전기 배기관", 206.8f, 207.2f, 2.5f, 8f, -4.2f, -3.8f, b.Metal, solid: false);
        b.Spot("Generator", new Vector3(203.4f, 0f, -4f));
        b.Box("정비 차량", 178f, 196f, 0f, 3.2f, -8f, -4.4f, b.Metal, "Metal");
        b.Box("컨테이너 1", 184f, 190f, 0f, 2.6f, -18f, -15.5f, b.Metal, "MetalBox");
        b.Box("컨테이너 2", 186f, 190f, 2.6f, 5.2f, -18f, -15.5f, b.Metal, "MetalBox");
        b.Box("통로 발판", 195f, 215f, 4f, 4.2f, 16f, 19.7f, b.Metal, "Gantry");
        b.Box("통로 난간", 195f, 215f, 4.2f, 5.2f, 15.9f, 16f, b.Metal, solid: false);
        b.Ladder("통로 사다리", new Vector3(200f, 0f, 15.85f), 180f, 5.2f);
        b.Can(new Vector3(192f, 0f, 8f));
        b.Can(new Vector3(188f, 5.2f, -16.7f));
        b.Can(new Vector3(210f, 4.2f, 18f));
        b.Can(new Vector3(180f, 0f, 19f));
        b.Lamp(new Vector3(182f, 8.5f, 0f), 16f);
        b.Lamp(new Vector3(198f, 8.5f, 6f), 16f);
        b.Lamp(new Vector3(210f, 8.5f, -12f), 16f);
        b.Lamp(new Vector3(205f, 8.5f, 14f), 16f);
        b.EnemySpawn("정비창 남", new Vector3(195f, 0f, -18f));
        b.EnemySpawn("정비창 북동", new Vector3(212f, 0f, 12f));
        b.EnemySpawn("정비창 가운데", new Vector3(186f, 0f, 2f));
        b.EnemySpawn("정비창 남동", new Vector3(214f, 0f, -14f));

        // 5. Under the river: a long service tunnel with pipes to vault, then the emergency ramp up to the far bank.
        b.Box("강 밑 터널 벽 남", 218f, 300f, 0f, 4.5f, -3.3f, -3f);
        b.Box("강 밑 터널 벽 북", 218f, 300f, 0f, 4.5f, 3f, 3.3f);
        b.Box("강 밑 터널 천장", 218f, 300f, 4.5f, 5f, -3.3f, 3.3f);
        foreach (float x in new[] { 240f, 265f, 285f }) b.Box("낮은 배관", x, x + .4f, 0f, 1f, -3f, 3f, b.Metal, "MetalPipe");
        b.Lamp(new Vector3(230f, 4f, 0f), 9f, 1.4f);
        b.Lamp(new Vector3(255f, 4f, 0f), 9f, 1.4f);
        b.Lamp(new Vector3(280f, 4f, 0f), 9f, 1.4f);
        b.EnemySpawn("강 밑 1", new Vector3(250f, 0f, 0f));
        b.EnemySpawn("강 밑 2", new Vector3(275f, 0f, 0f));
        b.Spot("StairsBottom", new Vector3(302f, 0f, 0f));
        b.Box("계단실 벽 남", 300f, 322f, 0f, 12f, -3.3f, -3f);
        b.Box("계단실 벽 북", 300f, 322f, 0f, 12f, 3f, 3.3f);
        b.Box("계단실 천장", 300f, 322f, 12f, 12.5f, -3.3f, 3.3f);
        b.Box("계단실 입구 위", 299.7f, 300f, 4.5f, 12.5f, -3.3f, 3.3f);
        b.Ramp("비상 계단", new Vector3(300f, 0f, 0f), new Vector3(322f, Bank, 0f), 6f);
        b.Lamp(new Vector3(311f, 7f, 0f), 10f, 1.4f);
        b.EnemySpawn("계단 아래", new Vector3(306f, 0f, 0f));

        // 6. The far bank, out in the open: the flare that calls the army's boat (finale) and the pier it comes to.
        b.Box("강 건너 광장", 322f, 344f, 0f, Bank, -24f, 24f, b.Concrete, "Concrete");
        b.Box("광장 서쪽 난간 남", 322f, 322.3f, Bank, Bank + 1.2f, -24f, -3.3f);
        b.Box("광장 서쪽 난간 북", 322f, 322.3f, Bank, Bank + 1.2f, 3.3f, 24f);
        b.Spot("Exit", new Vector3(326f, Bank, 0f));
        b.Box("신호탄 상자", 331.6f, 332.4f, Bank, Bank + 1.1f, 2.6f, 3.4f, b.Marker);
        b.Spot("Flare", new Vector3(332f, Bank, 2f));
        b.Spot("Plaza", new Vector3(333f, Bank, 0f));
        Car(b, 330f, Bank, -10f);
        Car(b, 334f, Bank, 12f);
        b.Box("모래주머니", 326f, 327f, Bank, Bank + 1.2f, -16f, -8f, b.Wood, "Tarp");
        b.Box("부두", 338f, 344f, Bank, Bank + .3f, -5f, 5f, b.Wood, "WoodPlank");
        b.Box("군 보트", 339.5f, 343.5f, Bank + .3f, Bank + 1.8f, 6f, 9f, b.Metal, solid: false);
        b.Spot("Boat", new Vector3(341f, Bank + .3f, 0f));
        b.EnemySpawn("광장 남서", new Vector3(330f, Bank, -21f));
        b.EnemySpawn("광장 북서", new Vector3(330f, Bank, 21f));
        b.EnemySpawn("광장 남동", new Vector3(342f, Bank, -20f));
        b.EnemySpawn("광장 북동", new Vector3(342f, Bank, 20f));

        b.Finish("3장 지하철을 지었어요 (Scenes/Campaign/Ch3_Subway).\n" +
                 "길: 대합실 → 승강장 → 어두운 터널 → 정비창 사무실(휴게소) → 연료통 4개를 발전기에 → 방수문 → 강 밑 터널 → 비상 계단 → 신호탄 → 버티기 → 보트.");
    }

    // A stopped car, 4.5 m long across x, at <paramref name="y"/> (the road it stands on).
    private static void Car(GreyboxBuilder b, float x, float y, float z)
        => b.Box("차", x - 2.25f, x + 2.25f, y, y + 1.5f, z - .95f, z + .95f, b.Metal, "Metal");

    private static void Bus(GreyboxBuilder b, string name, float x0, float x1, float y, float z0, float z1)
        => b.Box(name, x0, x1, y, y + 3.2f, z0, z1, b.Metal, "Metal");

    // Knee-high walls along a roof's two long sides.
    private static void Parapets(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float roof)
    {
        b.Box(name + " 난간 남", x0, x1, roof, roof + ParapetHeight, z0, z0 + Wall);
        b.Box(name + " 난간 북", x0, x1, roof, roof + ParapetHeight, z1 - Wall, z1);
    }

    // A hollow room with one door, walls inside its extents, and a roof.
    private static void Room(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float floor, float height,
        Side door, float doorFrom, float doorTo, float doorHeight = 2.4f)
    {
        float top = floor + height;
        RoomWall(b, name + " 서벽", x0, x0 + Wall, z0, z1, floor, top, door == Side.West, false, doorFrom, doorTo, floor + doorHeight);
        RoomWall(b, name + " 동벽", x1 - Wall, x1, z0, z1, floor, top, door == Side.East, false, doorFrom, doorTo, floor + doorHeight);
        RoomWall(b, name + " 남벽", x0, x1, z0, z0 + Wall, floor, top, door == Side.South, true, doorFrom, doorTo, floor + doorHeight);
        RoomWall(b, name + " 북벽", x0, x1, z1 - Wall, z1, floor, top, door == Side.North, true, doorFrom, doorTo, floor + doorHeight);
        b.Box(name + " 지붕", x0, x1, top, top + Wall, z0, z1);
    }

    private static void RoomWall(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float floor, float top,
        bool hasDoor, bool alongX, float from, float to, float doorTop)
    {
        if (!hasDoor) { b.Box(name, x0, x1, floor, top, z0, z1); return; }
        if (alongX)
        {
            b.Box(name + " 1", x0, from, floor, top, z0, z1);
            b.Box(name + " 2", to, x1, floor, top, z0, z1);
            b.Box(name + " 문 위", from, to, doorTop, top, z0, z1);
            return;
        }
        b.Box(name + " 1", x0, x1, floor, top, z0, from);
        b.Box(name + " 2", x0, x1, floor, top, to, z1);
        b.Box(name + " 문 위", x0, x1, doorTop, top, from, to);
    }
}
