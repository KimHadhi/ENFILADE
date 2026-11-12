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
///   rest stop in the depot, finale at the far bank's exit. The army's boat takes them down the river to the airport.
/// 4장 공항 (the user's memo 15): from the cargo pier through the terminal (check-in hall, security, the duty-free shop as
///   the rest stop, a jet bridge with no plane) to the apron and the control tower's radio. An airliner left across the
///   runway keeps the last transport on the ground: its tug pushes it off while someone mans the tug (a manned hold,
///   CampaignPushback). Finale at the transport's ramp; it takes off as the song ends.
/// Each chapter runs along +X; the gimmicks (5C) and objectives (5B) hang on the named spots under 진행 자리.
/// </summary>
public static class CampaignMaps
{
    private const float Wall = .3f, ParapetHeight = .9f;
    private enum Side { West, East, South, North }

    /// <summary>Every chapter in a row after one question (memo 48: the small-prop fix needs each map rebuilt).</summary>
    [MenuItem("Tools/World/캠페인 회색 맵/모든 장 다시 짓기", priority = 0)]
    private static void RebuildAll() => GreyboxBuilder.RebuildAll("모든 장 다시 짓기",
        ("1장 옥상 탈출", Rooftops), ("2장 막힌 고가도로", Overpass), ("3장 지하철", Subway), ("4장 공항", Airport));

    [MenuItem("Tools/World/캠페인 회색 맵/1장 옥상 탈출", priority = 11)]
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
        b.Dress(b.Box("B1 실외기 1", 13f, 15f, 18f, 19.1f, 4f, 6f, b.Metal, "Metal"), "acunit");
        b.Dress(b.Box("B1 실외기 2", 13f, 15f, 18f, 19.1f, -6f, -4f, b.Metal, "Metal"), "acunit");

        // 2. Over a 3 m gap, 2 m down. The lift room is climbed (wall climb, or its ladder), and the cable from its top
        // crosses the street.
        b.Box("B2", 27f, 47f, 0f, 16f, -10f, 10f);
        Parapets(b, "B2", 27f, 47f, -10f, 10f, 16f);
        b.Box("B2 엘리베이터실", 37f, 43f, 16f, 19.4f, -3f, 3f);
        b.Ladder("B2 엘리베이터실 사다리", new Vector3(40f, 16f, -3.15f), 180f);
        b.Box("집라인 기둥 1", 42.6f, 42.8f, 19.4f, 22.2f, -.1f, .1f, b.Metal, solid: false);
        b.Zipline("길 건너는 집라인", new Vector3(42.5f, 21.7f, 0f), new Vector3(70f, 15.1f, 0f));
        b.Box("집라인 기둥 2", 70.2f, 70.4f, 13f, 15.8f, -.1f, .1f, b.Metal, solid: false);
        b.Spot("LiftRoom", new Vector3(40f, 19.4f, 0f));
        b.Spot("ZiplineEnd", new Vector3(72f, 13f, 0f));
        b.EnemySpawn("B2 옥상 1", new Vector3(32f, 16f, 6f));
        b.EnemySpawn("B2 옥상 2", new Vector3(45f, 16f, -7f));

        b.Dress(b.Box("길1 차 1", 50f, 54.5f, 0f, 1.5f, -6f, -4.2f, b.Metal, "Metal"), "car");
        b.Dress(b.Box("길1 차 2", 56f, 60.5f, 0f, 1.5f, 3f, 4.8f, b.Metal, "Metal"), "car");
        b.EnemySpawn("길1", new Vector3(55f, 0f, -1f));
        b.EnemySpawn("길1 남쪽", new Vector3(55f, 0f, -25f));

        // 3. Too far to jump to the next roof: the swing bar over the gap. A ladder brings a faller back up.
        b.Box("B3", 63f, 80f, 0f, 13f, -10f, 10f);
        Parapets(b, "B3", 63f, 80f, -10f, 10f, 13f);
        b.Box("B3 위성안테나", 66f, 68f, 13f, 14.2f, 6f, 8f, b.Metal, "Metal");
        b.Bar("틈 위 철봉", new Vector3(83.5f, 15.9f, 0f), 90f, 3f);
        b.Box("철봉 기둥 1", 83.4f, 83.6f, 0f, 16.1f, -1.9f, -1.7f, b.Metal, solid: false);
        b.Box("철봉 기둥 2", 83.4f, 83.6f, 0f, 16.1f, 1.7f, 1.9f, b.Metal, solid: false);
        b.Ladder("틈에 떨어지면 올라오는 사다리", new Vector3(80.15f, 0f, 6f), 90f);
        b.EnemySpawn("B3 옥상 1", new Vector3(70f, 13f, -6f));
        b.EnemySpawn("B3 옥상 2", new Vector3(75f, 13f, 7f));

        // 4. Down through the block of flats (user, 2026-11-12: inside the flats instead of down the ladder, a fight
        // indoors): the roof's stair hut, the top floor, a flat whose floor gave way, the east stairs to the ground floor
        // and its back door into the alley (Apartment). The annex now stands above the roof, so the old way down outside
        // (annex roof, lorry) is gone; jumping the 12 m into the alley hurts.
        Apartment(b);
        Parapets(b, "B4", 87f, 104f, -10f, 10f, 12f);
        b.Box("B4 난간 동", 103.7f, 104f, 12f, 12f + ParapetHeight, -10f, 4f);
        b.Box("B4 별관", 104f, 112f, 0f, 13.5f, 4f, 50f);
        b.Dress(b.Box("골목 트럭", 108f, 114f, 0f, 3f, 0f, 3f, b.Metal, "MetalBox"), "van");
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
        b.Dress(b.Box("셔터 버튼", 134f, 134.4f, 1f, 1.6f, -4f, -3.85f, b.Marker, solid: false), "controlboxFit");
        b.Spot("Alley", new Vector3(110f, 0f, -1f));
        b.Spot("ShutterButton", new Vector3(134.2f, 0f, -3f));
        b.Spot("AlleyHold", new Vector3(124f, 0f, 0f));
        b.Dress(b.Box("골목 쓰레기통 1", 118f, 120f, 0f, 1.4f, -3.9f, -2.6f, b.Metal, "MetalBox"), "dumpster");
        b.Dress(b.Box("골목 쓰레기통 2", 126f, 128f, 0f, 1.4f, 2.6f, 3.9f, b.Metal, "MetalBox"), "dumpster");
        b.Dress(b.Box("골목 팔레트", 122f, 123.2f, 0f, .9f, -1f, .2f, b.Wood, "WoodPlank"), "palletload");
        // Rubbish round the bins and boxes by the shutter (the Airport pack's bags, boxes and crates; seen only).
        foreach (var (x, z) in new[] { (117.4f, -3.4f), (120.6f, -3.5f), (121.3f, -2.9f), (125.4f, 3.4f), (128.6f, 3.2f), (129.4f, 3.5f) })
            b.Prop("garbage", new Vector3(x, 0f, z), x * 11f % 360f);
        foreach (var (x, z) in new[] { (131f, -3.5f), (132.1f, -3.4f), (131.6f, -2.6f) }) b.Prop("box", new Vector3(x, 0f, z), x * 5f % 360f);
        foreach (var (x, z) in new[] { (107f, 3.3f), (108.2f, 3.3f) }) b.Prop("crate", new Vector3(x, 0f, z), x * 7f % 360f);
        b.EnemySpawn("골목 서쪽 끝", new Vector3(106f, 0f, -2f));
        b.EnemySpawn("골목 서쪽 구석", new Vector3(105.5f, 0f, 2.5f));
        b.EnemySpawn("골목 트럭 옆", new Vector3(113f, 0f, -3.2f));
        b.EnemySpawn("골목 가운데", new Vector3(120f, 0f, 2f));
        b.EnemySpawn("셔터 통로", new Vector3(139f, 0f, 0f));

        // 6. The corner shop over the street: the rest stop (doors 5D, shop stage 6).
        Room(b, "편의점", 145f, 157f, 10f, 20f, 0f, 4.5f, Side.South, 149f, 151.5f);
        b.Door("편의점 문", 149f, 151.5f, 0f, 2.4f, 10.05f, 10.25f, "RestStop", new Vector3(2.5f, 0f, 0f));
        b.Dress(b.Box("편의점 계산대", 147f, 152f, 0f, 1f, 15f, 16f, b.Wood, "Wood"), "shopcounter");
        b.Prop("cashregister", new Vector3(148f, 1f, 15.5f), 180f);
        b.Shop(new Vector3(149.5f, 0f, 14.3f), new Vector3(149.5f, 2.2f, 16f));
        // Chest high (memo 48): a zombie's arm reaches a player standing on it, as it couldn't at 1.8 m.
        b.Dress(b.Box("편의점 진열대", 153.5f, 154.3f, 0f, 1.4f, 12f, 18f, b.Metal, "Metal"), "shopshelf");
        b.PropSolid("fridge", new Vector3(156.1f, 0f, 15f), 270f);
        b.PropSolid("vending", new Vector3(153.5f, 0f, 9.4f), 180f);
        b.PropSolid("bin", new Vector3(147.5f, 0f, 9.3f));
        b.Spot("RestStop", new Vector3(151f, 0f, 15f));
        b.Dress(b.Box("길2 차 1", 144f, 148.5f, 0f, 1.5f, -8f, -6.2f, b.Metal, "Metal"), "car");
        b.Dress(b.Box("길2 차 2", 158f, 162.5f, 0f, 1.5f, -3f, -1.2f, b.Metal, "Metal"), "car");
        b.EnemySpawn("길2", new Vector3(150f, 0f, -12f));
        b.EnemySpawn("길2 북쪽", new Vector3(162f, 0f, 8f));

        // 7. The building site, through the gate in its fence. Players climb two containers, a wall up to the scaffold
        // and its ladder to the roof (helipad, finale); enemies walk two-lane switchback ramps up the south side.
        b.Dress(b.Box("공사장 펜스 남", 170f, 170.2f, 0f, 2.2f, -50f, -3f, b.Metal, "MetalPipe"), "constructionfence");
        b.Dress(b.Box("공사장 펜스 북", 170f, 170.2f, 0f, 2.2f, 3f, 50f, b.Metal, "MetalPipe"), "constructionfence");
        b.Spot("SiteGate", new Vector3(172f, 0f, 0f));
        b.Dress(b.Box("컨테이너 1", 176f, 182f, 0f, 2.6f, 10f, 12.5f, b.Metal, "MetalBox"), "container");
        b.Box("컨테이너 2", 179f, 182f, 2.6f, 5.2f, 10f, 12.5f, b.Metal, "MetalBox");
        b.Box("공사 중 건물", 182f, 200f, 0f, 14f, -9f, 9f);
        b.Box("비계 벽", 182f, 182.3f, 5.2f, 8.7f, 9f, 12f, b.Wood, "WoodPanel");
        b.Box("비계 발판", 182.3f, 188f, 8.5f, 8.7f, 9f, 12f, b.Wood, "WoodPlank");
        b.Box("비계 기둥", 187.8f, 188f, 0f, 8.5f, 11.8f, 12f, b.Metal, solid: false);
        b.Ladder("비계 사다리", new Vector3(185f, 8.7f, 9.15f), 0f);
        b.Ladder("바닥에서 지붕 사다리", new Vector3(195f, 0f, 9.15f), 0f);
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
        b.Dress(b.Box("무전기", 187.6f, 188.4f, 14f, 15.1f, 4.6f, 5.2f, b.Marker), "controlboxFit");
        foreach (var (x, z) in new[] { (175f, 9f), (176.2f, 8.6f), (184f, -10.5f) }) b.Prop("crate", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Spot("Radio", new Vector3(188f, 14f, 4f));
        b.Box("크레인 기둥", 203f, 204.6f, 0f, 30f, -.8f, .8f, b.Metal, "MetalCrane");
        b.Box("크레인 팔", 176f, 212f, 30f, 31f, -.5f, .5f, b.Metal, solid: false);
        b.Box("크레인 무게추", 208f, 212f, 28.5f, 30f, -1f, 1f, solid: false);
        b.EnemySpawn("공사장 남서", new Vector3(175f, 0f, -15f));
        b.EnemySpawn("공사장 남동", new Vector3(205f, 0f, -15f));
        b.EnemySpawn("공사장 북동", new Vector3(205f, 0f, 14f));
        b.EnemySpawn("공사장 북서", new Vector3(176f, 0f, 20f));
        b.EnemySpawn("경사로 아래", new Vector3(186f, 0f, -16f));

        // Background sound by area (user, 2026-11-10; the rows are in Tables/Ambience, the roofs keep the map's own): the
        // street below the roofs, the start room, the alley with its bins and the shutter's passage, the corner shop.
        b.Ambience("Ch1.Street", -30f, 240f, -1f, 7f, -50f, 50f, 0);
        b.Ambience("Ch1.SafeRoom", 2f, 9f, 18f, 21f, -3f, 3f);
        b.Ambience("Ch1.Alley", 104f, 141f, -1f, 7f, -4f, 4f);
        b.Ambience("Ch1.Shop", 145f, 157f, 0f, 4.5f, 10f, 20f);

        // Field pickups (6D): a gun at the start and on the last roof, supplies along the way, more for the alley's hold
        // and the finale, a gun and supplies in the rest stop.
        b.Pickup(new Vector3(6.5f, 18f, 2f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(8f, 18f, -2f));
        b.Pickup(new Vector3(40f, 19.4f, 1.5f));
        b.Pickup(new Vector3(72f, 13f, 2f));
        b.Pickup(new Vector3(95f, 12f, 2f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(110f, 0f, 1.5f));
        b.Pickup(new Vector3(124f, 0f, 2f));
        b.Pickup(new Vector3(152.5f, 0f, 13.5f));
        b.Pickup(new Vector3(148f, 0f, 13f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(172f, 0f, 2f));
        b.Pickup(new Vector3(191f, 14f, -2f));
        DressRooftops(b);
        b.Finish("1장 옥상 탈출을 지었어요 (Scenes/Campaign/Ch1_Rooftops). 메인 메뉴의 맵 목록에도 나와요.\n" +
                 "길: 안전가옥 → 옥상 틈 점프 → 엘리베이터실 벽 오르기 → 집라인 → 철봉 → 아파트 옥상 계단실 → 4층 → 꺼진 바닥 → 동쪽 계단 → 1층 뒷문으로 골목 → 셔터 → 편의점 → 공사장 컨테이너·비계·사다리 → 헬기 착륙장.");
    }

    [MenuItem("Tools/World/캠페인 회색 맵/2장 막힌 고가도로", priority = 12)]
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
        // A road on top (this map's Road: concrete sides, a road's top), as the road below it.
        b.Box("고가 상판", -19f, 120f, Deck - 1f, Deck, -7f, 7f, b.Road, "Asphalt");
        for (float x = 0f; x <= 100f; x += 20f) b.Box("고가 기둥 " + x, x - 1f, x + 1f, 0f, Deck - 1f, -1f, 1f);
        b.Box("고가 난간 북", -19f, 120f, Deck, Deck + 1f, 6.7f, 7f);
        b.Box("고가 난간 남 1", -19f, 79f, Deck, Deck + 1f, -7f, -6.7f);
        b.Box("고가 난간 남 2", 81f, 120f, Deck, Deck + 1f, -7f, -6.7f);
        b.Box("구덩이 벽", 100f, 101f, 0f, 6f, -39f, 39f);
        b.Ladder("구덩이에서 고가로 올라가는 사다리", new Vector3(80f, 0f, -7.15f), 180f);
        b.EnemySpawn("구덩이 1", new Vector3(40f, 0f, 12f));
        b.EnemySpawn("구덩이 2", new Vector3(90f, 0f, -12f));

        // The wreck they crawl out of.
        b.Box("헬기 동체", 2f, 9f, Deck, Deck + 2.2f, 1f, 4f, b.Metal, "Metal");
        // The tail broke off and lies on the road (memo 48): hanging 1.6 m up it was a perch no zombie reached.
        b.Box("헬기 꼬리 (부러짐)", 9f, 16f, Deck, Deck + .8f, 2.2f, 2.8f, b.Metal, "Metal");
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
        // Within a zombie's arm from the ground (memo 48).
        b.Box("주유기 1", 145f, 146f, 0f, 1.5f, 13f, 14f, b.Metal, "Metal");
        b.Box("주유기 2", 150f, 151f, 0f, 1.5f, 13f, 14f, b.Metal, "Metal");
        Room(b, "주유소 가게", 158f, 168f, 12f, 22f, 0f, 4f, Side.South, 161f, 163.5f);
        b.Door("주유소 가게 문", 161f, 163.5f, 0f, 2.4f, 12.05f, 12.25f, "Station", new Vector3(2.5f, 0f, 0f));
        b.Dress(b.Box("가게 계산대", 160f, 164f, 0f, 1f, 18.5f, 19.5f, b.Wood, "Wood"), "shopcounter");
        b.Prop("cashregister", new Vector3(161f, 1f, 19f), 180f);
        b.Shop(new Vector3(162f, 0f, 17.8f), new Vector3(162f, 2.2f, 19.5f));
        // The filling station's shop: a shelf, a fridge, a vending machine outside, bins by the pumps.
        b.Dress(b.Box("가게 진열대", 165.6f, 166.4f, 0f, 1.4f, 13.5f, 18f, b.Metal, "Metal"), "shopshelf");
        b.PropSolid("fridge", new Vector3(163.5f, 0f, 21.3f), 180f);
        b.PropSolid("vending", new Vector3(157.2f, 0f, 15f), 270f);
        b.PropSolid("bin", new Vector3(147.6f, 0f, 9.2f));
        foreach (var (x, z) in new[] { (141f, 19f), (142.1f, 19.4f), (156f, 9f) }) b.Prop("garbage", new Vector3(x, 0f, z), x * 11f % 360f);
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
        b.Dress(b.Box("모래주머니 남", 204f, 205f, 0f, 1.2f, -20f, -3f, b.Wood, "Tarp"), "sandbags");
        b.Dress(b.Box("모래주머니 북", 204f, 205f, 0f, 1.2f, 3f, 20f, b.Wood, "Tarp"), "sandbags");
        b.Dress(b.Box("군 컨테이너 1", 210f, 216f, 0f, 2.6f, -14f, -11.5f, b.Metal, "MetalBox"), "container");
        b.Dress(b.Box("군 컨테이너 2", 214f, 220f, 0f, 2.6f, 14f, 16.5f, b.Metal, "MetalBox"), "container");
        Room(b, "검문소 초소", 207f, 213f, 4f, 10f, 0f, 3f, Side.West, 6f, 8f);
        b.Dress(b.Box("초소 무전기", 211.5f, 212.6f, 0f, 1.1f, 8.6f, 9.6f, b.Marker), "controlboxFit");
        // The army left in a hurry: crates by the sandbags.
        foreach (var (x, z) in new[] { (206f, -5.5f), (207.2f, -5.9f), (206.3f, 5.6f) }) b.Prop("crate", new Vector3(x, 0f, z), x * 7f % 360f);
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

        // Background sound by area (the deck keeps the map's own): the road and the pit below, the shade under the deck,
        // the filling station's canopy and shop, the guard post, the subway entrance.
        b.Ambience("Ch2.Below", -20f, 240f, -1f, 6.5f, -40f, 40f, 0);
        b.Ambience("Ch2.UnderDeck", -19f, 120f, -1f, 7f, -7f, 7f);
        b.Ambience("Ch2.Canopy", 140f, 156f, 0f, 5f, 8f, 20f);
        b.Ambience("Ch2.Shop", 158f, 168f, 0f, 4f, 12f, 22f);
        b.Ambience("Ch2.GuardPost", 207f, 213f, 0f, 3f, 4f, 10f);
        b.Ambience("Ch2.SubwayEntrance", 225f, 233f, 0f, 3.5f, -4f, 4f);

        // Field pickups (6D).
        b.Pickup(new Vector3(6f, Deck, -1f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(66f, Deck, 2f));
        b.Pickup(new Vector3(114f, Deck, 2f));
        b.Pickup(new Vector3(128f, 0f, 2f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(163f, 0f, 15.5f));
        b.Pickup(new Vector3(161f, 0f, 16f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(198f, 0f, 2f));
        b.Pickup(new Vector3(206f, 0f, 2f));
        b.Pickup(new Vector3(227f, 0f, 2f));
        DressOverpass(b);
        b.Finish("2장 막힌 고가도로를 지었어요 (Scenes/Campaign/Ch2_Overpass).\n" +
                 "길: 헬기 잔해 → 막힌 차들 → 뒤집힌 트럭 → 끊어진 끝에서 버스로 뛰어내리기(돌아갈 수 없음) → 주유소 → 톨게이트 → 초소 무전 → 버티기 → 지하철 입구.");
    }

    [MenuItem("Tools/World/캠페인 회색 맵/3장 지하철", priority = 13)]
    private static void Subway()
    {
        var b = GreyboxBuilder.Begin("Ch3_Subway", "3장 지하철");
        if (b == null) return;
        const float Bank = 8f;

        b.Box("바닥", -10f, 345f, -.5f, 0f, -25f, 25f, b.Concrete, "Concrete");
        // The floors that aren't the concourse's tiles (Tables/MapSurfaces by name), 1 cm over the map's floor: ballast
        // under the tracks and through the dark tunnel, the depot's and the service tunnel's concrete.
        b.Box("선로 바닥", 30f, 90.3f, -.5f, .01f, -12f, 0f, null, "Gravel");
        b.Box("터널 바닥", 90.3f, 170f, -.5f, .01f, -6f, 2f, null, "Gravel");
        b.Box("정비창 바닥", 170.3f, 217.7f, -.5f, .01f, -20f, 20f, null, "Concrete");
        b.Box("강 밑 터널 바닥", 218f, 300f, -.5f, .01f, -3f, 3f, null, "Concrete");
        b.Box("맵 끝 서", -10f, -9f, 0f, 12f, -25f, 25f);
        b.Box("맵 끝 동 (강)", 344f, 345f, 0f, 10f, -25f, 25f);
        b.Box("맵 끝 남", -10f, 345f, 0f, 12f, -25f, -24f);
        b.Box("맵 끝 북", -10f, 345f, 0f, 12f, 24f, 25f);

        // 1. The concourse, down the stairs from chapter 2's entrance: ticket barriers to vault.
        b.Box("대합실 천장", -9f, 30f, 5f, 5.5f, -12.3f, 12.3f);
        b.Box("대합실 벽 남", -9f, 30f, 0f, 5f, -12.3f, -12f);
        b.Box("대합실 벽 북", -9f, 30f, 0f, 5f, 12f, 12.3f);
        // Up to the ceiling (memo 48): 3.2 m high against the map's end it was a ledge no zombie could get onto.
        b.Box("들어온 셔터", -9f, -8.7f, 0f, 5f, -4f, 4f, b.Metal, "Metal");
        b.Box("개찰구 남", 15f, 15.4f, 0f, 1f, -11.7f, -1f, b.Metal, "Metal");
        b.Box("개찰구 북", 15f, 15.4f, 0f, 1f, 1f, 11.7f, b.Metal, "Metal");
        b.Box("매점", 5f, 8f, 0f, 2.4f, 7f, 11f);
        // Vending machines, bins and rubbish on the concourse, its exit sign (the Airport pack's props).
        b.PropSolid("vending", new Vector3(10f, 0f, -11.4f));
        b.PropSolid("vending", new Vector3(12f, 0f, -11.4f));
        foreach (var (x, z) in new[] { (3f, -10.8f), (20f, 10.8f) }) b.PropSolid("bin", new Vector3(x, 0f, z));
        foreach (var (x, z) in new[] { (-7f, 10.6f), (-6f, 11.2f), (26f, -11f) }) b.Prop("garbage", new Vector3(x, 0f, z), x * 11f % 360f);
        b.Hang("signExit", new Vector3(0f, 5f, 0f), 270f);
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
        b.Dress(b.Box("벤치 1", 46f, 48f, 1.1f, 1.6f, 9.5f, 10.1f, b.Wood, "Wood"), "seatsShort");
        b.Dress(b.Box("벤치 2", 76f, 78f, 1.1f, 1.6f, 9.5f, 10.1f, b.Wood, "Wood"), "seatsShort");
        foreach (var (x, z) in new[] { (52f, 11f), (64f, 10.9f) }) b.PropSolid("bin", new Vector3(x, 1.1f, z));
        foreach (var (x, z) in new[] { (58f, 4f), (71f, 8f) }) b.Prop("suitcase", new Vector3(x, 1.1f, z), x * 13f % 360f);
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
        b.Dress(b.Box("잔해 1", 110f, 113f, 0f, 1.4f, -5.9f, -2f, b.Concrete, "ConcreteGrit"), "rubblepile");
        b.Dress(b.Box("잔해 더미", 128f, 130f, 0f, 2.2f, -2f, 1.9f, b.Concrete, "ConcreteGrit"), "rubblepile");
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
        // Chest high (memo 48), within a zombie's arm.
        b.Box("사무실 보급함", 179f, 181.5f, 0f, 1.4f, 16.6f, 17.6f, b.Metal, "MetalBox");
        b.Shop(new Vector3(180.2f, 0f, 15.9f), new Vector3(180.2f, 2.2f, 17.6f));
        b.Lamp(new Vector3(177f, 2.6f, 14f), 6f, 1.4f);
        // The depot office's desk, and fuse boxes on the depot's walls.
        b.PropSolid("table", new Vector3(173.8f, 0f, 16.6f), 90f);
        b.Prop("screen", new Vector3(173.8f, .78f, 16.4f), 270f);
        b.Prop("desk", new Vector3(173.7f, .78f, 17.4f), 270f);
        b.Prop("officechair", new Vector3(174.9f, 0f, 16.6f), 270f);
        foreach (var (x, z) in new[] { (170.5f, 8f), (170.5f, -12f), (217.5f, 10f) }) b.Prop("fusebox", new Vector3(x, 1.3f, z), x < 200f ? 90f : 270f);
        foreach (var (x, z) in new[] { (183f, -13f), (184.2f, -12.6f), (199f, 13f), (215f, -18f) }) b.Prop("crate", new Vector3(x, 0f, z), x * 7f % 360f);
        foreach (var (x, z) in new[] { (192f, -18.8f), (193.1f, -19f) }) b.Prop("box", new Vector3(x, 0f, z), x * 5f % 360f);
        b.Box("발전기", 205f, 209f, 0f, 2.5f, -6f, -2f, b.Metal, "Metal");
        b.Box("발전기 배기관", 206.8f, 207.2f, 2.5f, 8f, -4.2f, -3.8f, b.Metal, solid: false);
        b.Spot("Generator", new Vector3(203.4f, 0f, -4f));
        b.Box("정비 차량", 178f, 196f, 0f, 3.2f, -8f, -4.4f, b.Metal, "Metal");
        b.Dress(b.Box("컨테이너 1", 184f, 190f, 0f, 2.6f, -18f, -15.5f, b.Metal, "MetalBox"), "container");
        b.Box("컨테이너 2", 186f, 190f, 2.6f, 5.2f, -18f, -15.5f, b.Metal, "MetalBox");
        b.Box("통로 발판", 195f, 215f, 4f, 4.2f, 16f, 19.7f, b.Metal, "Gantry");
        b.Box("통로 난간", 195f, 215f, 4.2f, 5.2f, 15.9f, 16f, b.Metal, solid: false);
        b.Ladder("통로 사다리", new Vector3(200f, 0f, 15.85f), 180f);
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
        // Paving on top, by name (Tables/MapSurfaces Ch3_Subway.Plaza), not the concourse's tiles.
        b.Box("강 건너 광장", 322f, 344f, 0f, Bank, -24f, 24f, null, "Concrete");
        b.Box("광장 서쪽 난간 남", 322f, 322.3f, Bank, Bank + 1.2f, -24f, -3.3f);
        b.Box("광장 서쪽 난간 북", 322f, 322.3f, Bank, Bank + 1.2f, 3.3f, 24f);
        b.Spot("Exit", new Vector3(326f, Bank, 0f));
        b.Dress(b.Box("신호탄 상자", 331.6f, 332.4f, Bank, Bank + 1.1f, 2.6f, 3.4f, b.Marker), "crateFit");
        b.Spot("Flare", new Vector3(332f, Bank, 2f));
        b.Spot("Plaza", new Vector3(333f, Bank, 0f));
        Car(b, 330f, Bank, -10f);
        Car(b, 334f, Bank, 12f);
        b.Dress(b.Box("모래주머니", 326f, 327f, Bank, Bank + 1.2f, -16f, -8f, b.Wood, "Tarp"), "sandbags");
        b.Box("부두", 338f, 344f, Bank, Bank + .3f, -5f, 5f, b.Wood, "WoodPlank");
        b.Box("군 보트", 339.5f, 343.5f, Bank + .3f, Bank + 1.8f, 6f, 9f, b.Metal, solid: false);
        b.Spot("Boat", new Vector3(341f, Bank + .3f, 0f));
        b.EnemySpawn("광장 남서", new Vector3(330f, Bank, -21f));
        b.EnemySpawn("광장 북서", new Vector3(330f, Bank, 21f));
        b.EnemySpawn("광장 남동", new Vector3(342f, Bank, -20f));
        b.EnemySpawn("광장 북동", new Vector3(342f, Bank, 20f));

        // Background sound by area (the platform hall, the tunnels and the depot keep the map's machinery hum): the
        // concourse, the depot office, the far bank out in the open.
        b.Ambience("Ch3.Concourse", -9f, 30f, -1f, 5f, -12f, 12f);
        b.Ambience("Ch3.Office", 172f, 182f, 0f, 3f, 10f, 18f);
        b.Ambience("Ch3.FarBank", 322f, 345f, 7f, 40f, -25f, 25f);

        // Field pickups (6D).
        b.Pickup(new Vector3(-3f, 0f, 1.5f));
        b.Pickup(new Vector3(75f, 1.1f, 4f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(156f, 0f, 1f));
        b.Pickup(new Vector3(172f, 0f, 0f));
        b.Pickup(new Vector3(177f, 0f, 13f));
        b.Pickup(new Vector3(176f, 0f, 15f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(200f, 0f, -2f));
        b.Pickup(new Vector3(300f, 0f, 1f));
        b.Pickup(new Vector3(333f, Bank, 2f));
        DressSubway(b);
        b.Finish("3장 지하철을 지었어요 (Scenes/Campaign/Ch3_Subway).\n" +
                 "길: 대합실 → 승강장 → 어두운 터널 → 정비창 사무실(휴게소) → 연료통 4개를 발전기에 → 방수문 → 강 밑 터널 → 비상 계단 → 신호탄 → 버티기 → 보트.");
    }

    [MenuItem("Tools/World/캠페인 회색 맵/4장 공항", priority = 14)]
    private static void Airport()
    {
        var b = GreyboxBuilder.Begin("Ch4_Airport", "4장 공항");
        if (b == null) return;
        // The terminal stands on a podium 4 m up, as departures do; the apron and the runway are at the ground.
        const float Floor = 4f, Roof = 16f, Upper = Floor + 5.4f;

        // The ground and the edge of the map: the river to the west, the airfield's wall on the other sides.
        b.Box("땅", -22f, 360f, -.5f, 0f, -60f, 60f, b.Road, "Asphalt");
        b.Box("맵 끝 서 (강)", -22f, -21f, 0f, 6f, -60f, 60f);
        b.Box("맵 끝 동", 359f, 360f, 0f, 8f, -60f, 60f);
        b.Box("맵 끝 남", -22f, 360f, 0f, 8f, -60f, -59f);
        b.Box("맵 끝 북", -22f, 360f, 0f, 8f, 59f, 60f);

        // 1. The cargo pier where chapter 3's boat left them, and the cargo yard: containers to climb, its gate in the fence.
        b.Box("화물 부두", -20f, -6f, 0f, .3f, -6f, 6f, b.Wood, "WoodPlank");
        b.Box("군 보트", -19.5f, -15.5f, 0f, 1.6f, 7f, 10f, b.Metal, solid: false);
        b.PlayerStart(new Vector3(-10f, .3f, 0f), 90f);
        b.Spot("Start", new Vector3(-10f, .3f, 0f));
        b.Dress(b.Box("화물 컨테이너 1", 0f, 12f, 0f, 2.6f, 12f, 14.5f, b.Metal, "MetalBox"), "containerRow");
        b.Dress(b.Box("화물 컨테이너 2", 3f, 9f, 2.6f, 5.2f, 12f, 14.5f, b.Metal, "MetalBox"), "container");
        b.Dress(b.Box("화물 컨테이너 3", 4f, 16f, 0f, 2.6f, -16f, -13.5f, b.Metal, "MetalBox"), "containerRow");
        b.Box("지게차", 10f, 12f, 0f, 2.2f, 4f, 7f, b.Metal, "Metal");
        b.Dress(b.Box("화물 팔레트 1", 2f, 3.2f, 0f, .9f, -4f, -2.8f, b.Wood, "WoodPlank"), "palletload");
        b.Dress(b.Box("화물 팔레트 2", 6f, 7.2f, 0f, .9f, 4.5f, 5.7f, b.Wood, "WoodPlank"), "palletload");
        b.Dress(b.Box("화물장 펜스 남", 20f, 20.2f, 0f, 2.4f, -59f, -4f, b.Metal, "MetalPipe"), "constructionfence");
        b.Dress(b.Box("화물장 펜스 북", 20f, 20.2f, 0f, 2.4f, 4f, 59f, b.Metal, "MetalPipe"), "constructionfence");
        b.Spot("CargoGate", new Vector3(23f, 0f, 0f));
        b.EnemySpawn("화물장 북", new Vector3(5f, 0f, 30f));
        b.EnemySpawn("화물장 남", new Vector3(8f, 0f, -35f));

        // 2. The landside road and the ramp up to the departures curb, where the terminal's two doors are. A wall from the
        // terminal's corners to the map's edges keeps everyone going through the terminal, not around it.
        Car(b, 27f, 0f, -14f); Car(b, 30f, 0f, 17f); Car(b, 41f, 0f, -24f); Car(b, 44f, 0f, 22f);
        b.Ramp("출발층 진입로", new Vector3(32f, 0f, 0f), new Vector3(48f, Floor, 0f), 12f, b.Road);
        b.Box("출발층 도로", 48f, 60f, 0f, Floor, -40.3f, 40.3f, b.Road, "Asphalt");
        Car(b, 53f, Floor, -22f); Car(b, 55f, Floor, 24f);
        b.Box("공항 셔틀버스", 50.5f, 53.1f, Floor, Floor + 3.2f, -37f, -25f, b.Metal, "Metal");
        b.Box("공항 담장 북", 60f, 60.3f, 0f, 6f, 40.3f, 59f);
        b.Box("공항 담장 남", 60f, 60.3f, 0f, 6f, -59f, -40.3f);
        b.Spot("Departures", new Vector3(57f, Floor, 0f));
        b.EnemySpawn("도로 북", new Vector3(38f, 0f, 45f));
        b.EnemySpawn("도로 남", new Vector3(38f, 0f, -45f));
        b.EnemySpawn("출발층 북", new Vector3(54f, Floor, 36f));

        // 3. The terminal: the departure hall with check-in islands, the information desk and a food court upstairs
        // reached by a stopped escalator. Its roof keeps the sun out, so lamps light it.
        b.Box("터미널 바닥", 60f, 180.3f, 0f, Floor, -40.3f, 40.3f, b.Concrete, "Tile");
        b.Box("터미널 지붕", 60f, 180.3f, Roof, Roof + .5f, -40.3f, 40.3f);
        b.Box("터미널 벽 북", 60f, 180.3f, Floor, Roof, 40f, 40.3f);
        b.Box("터미널 벽 남", 60f, 180.3f, Floor, Roof, -40.3f, -40f);
        b.Box("정면 벽 1", 60f, 60.3f, Floor, Roof, -40f, -10f);
        b.Box("정면 벽 2", 60f, 60.3f, Floor, Roof, -6f, 6f);
        b.Box("정면 벽 3", 60f, 60.3f, Floor, Roof, 10f, 40f);
        b.Box("정면 문 위 1", 60f, 60.3f, Floor + 3f, Roof, -10f, -6f);
        b.Box("정면 문 위 2", 60f, 60.3f, Floor + 3f, Roof, 6f, 10f);
        // Dressed with the Airport Terminal Scene pack (GreyboxBuilder.Dress, Tables/MapArt): the boxes stay the collision.
        b.Dress(b.Box("안내 데스크", 66f, 70f, Floor, Floor + 1.1f, -2f, 2f, b.Wood, "Wood"), "infodesk");
        foreach (float z in new[] { -26f, -12f, 2f })
        {
            b.Dress(b.Box("체크인 카운터", 76f, 100f, Floor, Floor + 1.1f, z, z + 2.4f, b.Wood, "Wood"), "checkin");
            for (float x = 80f; x <= 96f; x += 8f) b.Hang("signCheckin", new Vector3(x, Floor + 4.6f, z + 1.2f));
            // The queue's posts on the hall side of each island.
            for (float x = 78f; x <= 98f; x += 2.5f) b.Prop("linepost", new Vector3(x, Floor, z - 1.6f));
        }
        b.Box("포장기", 104f, 105.2f, Floor, Floor + 1.4f, -4f, -2.8f, b.Metal, "Metal");
        foreach (float z in new[] { -36f, -32f }) b.Dress(b.Box("홀 의자 줄", 104f, 116f, Floor, Floor + .5f, z, z + .6f, b.Metal, "Metal"), "seats");
        // Left behind when everyone ran: luggage, trolleys, bins and rubbish (seen only, walked through, but the trolleys,
        // bins and plants block), vending machines and an ATM by the walls, and the hall's ceiling lamps.
        foreach (var (x, z) in new[] { (73f, -20f), (74.5f, -19.2f), (83f, -15f), (92f, -15.5f), (78f, -1f), (95f, -2f), (101f, 6f), (86f, 8f),
                     (110f, -10f), (65f, 12f), (71f, -30f), (104f, -20f) })
            b.Prop("suitcase", new Vector3(x, Floor, z), x * 13f % 360f);
        foreach (var (x, z) in new[] { (72f, -8f), (102f, -14f), (105f, 9f), (90f, 20f) }) b.PropSolid("trolley", new Vector3(x, Floor, z), x * 7f % 360f);
        foreach (var (x, z) in new[] { (89.3f, -18f), (109.3f, -18f), (89.3f, 14f), (109.3f, 14f) }) b.PropSolid("bin", new Vector3(x, Floor, z));
        foreach (var (x, z) in new[] { (61f, -38f), (62.2f, -37.2f), (118f, -38.5f), (61f, 38f), (118f, 26f), (116.8f, 26.8f) })
            b.Prop("garbage", new Vector3(x, Floor, z), x * 11f % 360f);
        foreach (var (x, z) in new[] { (64f, -38.5f), (115f, -37.5f), (116.2f, -36.2f) }) b.Prop("box", new Vector3(x, Floor, z), x * 5f % 360f);
        b.PropSolid("vending", new Vector3(96f, Floor, -39.4f));
        b.PropSolid("vending", new Vector3(98f, Floor, -39.4f));
        b.PropSolid("atm", new Vector3(61f, Floor, -14f), 90f);
        foreach (var (x, z) in new[] { (61.2f, -11.5f), (61.2f, 11.5f), (61.2f, -4.5f), (61.2f, 4.5f) }) b.PropSolid("plant", new Vector3(x, Floor, z));
        foreach (float x in new[] { 88f, 108f })
            foreach (float z in new[] { -18f, 14f })
                b.Box("홀 기둥", x - .6f, x + .6f, Floor, Roof, z - .6f, z + .6f);
        b.Box("2층 식당가", 64f, 116f, Upper - .4f, Upper, 28f, 40f, b.Concrete, "Tile");
        b.Ramp("멈춘 에스컬레이터", new Vector3(70f, Floor, 14f), new Vector3(70f, Upper, 28f), 3f, b.Metal);
        b.Box("2층 난간 1", 64f, 68.5f, Upper, Upper + 1f, 28f, 28.2f, b.Metal, "MetalPipe");
        b.Box("2층 난간 2", 71.5f, 116f, Upper, Upper + 1f, 28f, 28.2f, b.Metal, "MetalPipe");
        foreach (float x in new[] { 80f, 96f, 112f }) b.Box("2층 기둥", x - .4f, x + .4f, Floor, Upper - .4f, 28.2f, 29f);
        b.Dress(b.Box("식당 계산대", 84f, 92f, Upper, Upper + 1.1f, 37f, 38.2f, b.Wood, "Wood"), "glasscounter");
        foreach (float x in new[] { 76f, 82f, 98f, 104f })
        {
            b.Dress(b.Box("식탁", x, x + 1f, Upper, Upper + .75f, 32f, 33f, b.Wood, "Wood"), "table");
            b.Prop("chair", new Vector3(x + .5f, Upper, 31.4f), 0f);
            b.Prop("chair", new Vector3(x + .5f, Upper, 33.6f), 180f);
        }
        b.EnemySpawn("홀 서남", new Vector3(66f, Floor, -34f));
        b.EnemySpawn("홀 동남", new Vector3(112f, Floor, -26f));
        b.EnemySpawn("홀 동북", new Vector3(114f, Floor, 22f));
        b.EnemySpawn("홀 2층", new Vector3(100f, Upper, 35f));

        // 4. The security line across the terminal: four lanes between the scanners, a detector frame in each.
        foreach (var (z0, z1) in new[] { (-40f, -14f), (-10f, -6f), (-2f, 2f), (6f, 10f), (14f, 40f) })
            b.Box("검색대 칸막이", 120f, 120.3f, Floor, Floor + 4f, z0, z1);
        foreach (float lane in new[] { -12f, -4f, 4f, 12f })
        {
            b.Dress(b.Box("엑스레이 검색기", 120.3f, 125.5f, Floor, Floor + 1.2f, lane - 2f, lane - 1f, b.Metal, "Metal"), "xray");
            // The walk-through detector, seen only: everyone walks through it.
            b.Prop("bodyscanner", new Vector3(126.65f, Floor, lane), 90f);
            b.Prop("linepost", new Vector3(117.5f, Floor, lane - 1.2f));
            b.Prop("linepost", new Vector3(117.5f, Floor, lane + 1.2f));
        }
        b.Spot("Security", new Vector3(129f, Floor, 0f));

        // 5. Airside: the duty-free shop is the rest stop (its door, its till), a café, the gates' seats, a stopped moving
        // walkway, and the gate desk by the jet bridge's door.
        Room(b, "면세점", 140f, 154f, 26f, 40f, Floor, 4f, Side.South, 145f, 147.5f);
        b.Door("면세점 문", 145f, 147.5f, Floor, Floor + 2.4f, 26.05f, 26.25f, "DutyFree", new Vector3(2.5f, 0f, 0f));
        b.Dress(b.Box("면세점 계산대", 148f, 152f, Floor, Floor + 1f, 35f, 36f, b.Wood, "Wood"), "shopcounter");
        b.Prop("cashregister", new Vector3(149f, Floor + 1f, 35.5f), 180f);
        b.Prop("perfume", new Vector3(151f, Floor + 1f, 35.4f));
        b.Prop("whiskey", new Vector3(151.6f, Floor + 1f, 35.6f));
        b.Shop(new Vector3(150f, Floor, 34.3f), new Vector3(150f, Floor + 2.2f, 36f));
        b.Dress(b.Box("면세점 진열대", 141f, 141.8f, Floor, Floor + 1.4f, 29f, 37f, b.Metal, "Metal"), "shopshelf");
        b.PropSolid("glassshelf", new Vector3(152.6f, Floor, 29.5f), 270f);
        b.PropSolid("glassshelf", new Vector3(152.6f, Floor, 31.5f), 270f);
        b.Hang("banner", new Vector3(146.25f, Floor + 3.6f, 25.8f));
        b.Spot("DutyFree", new Vector3(147f, Floor, 32f));
        b.Lamp(new Vector3(147f, Floor + 3.5f, 33f), 9f, 1.6f);
        b.Dress(b.Box("카페 계산대", 128f, 134f, Floor, Floor + 1.1f, -38f, -36.8f, b.Wood, "Wood"), "glasscounter");
        foreach (float x in new[] { 130f, 134f })
        {
            b.PropSolid("table", new Vector3(x, Floor, -33.5f));
            b.Prop("chair", new Vector3(x, Floor, -34.4f), 0f);
            b.Prop("chair", new Vector3(x, Floor, -32.6f), 180f);
        }
        b.Box("멈춘 무빙워크", 128f, 170f, Floor, Floor + .15f, -31f, -29.6f, b.Metal, "Metal");
        foreach (float z in new[] { -24f, -20f, -16f, 12f, 16f })
            b.Dress(b.Box("탑승구 의자 줄", 158f, 176f, Floor, Floor + .5f, z, z + .6f, b.Metal, "Metal"), "seats");
        b.Dress(b.Box("탑승구 데스크", 172f, 175f, Floor, Floor + 1.1f, 5f, 6.2f, b.Wood, "Wood"), "boardingdesk");
        b.Spot("Gate", new Vector3(177f, Floor, 0f));
        b.Hang("signGates", new Vector3(150f, Floor + 4.2f, 0f), 90f);
        b.Hang("signGates", new Vector3(175f, Floor + 3.4f, -3f), 90f);
        b.Hang("signExit", new Vector3(63f, Floor + 3.4f, 0f), 270f);
        foreach (var (x, z) in new[] { (160f, -22f), (163f, -21.6f), (167f, -18f), (171f, -14f), (161f, 14f), (169f, 18f), (174f, 10f), (139f, -6f) })
            b.Prop("suitcase", new Vector3(x, Floor, z), x * 17f % 360f);
        foreach (var (x, z) in new[] { (165f, -26f), (157f, 10f), (170f, 14.5f), (138f, 8f) }) b.Prop("bag", new Vector3(x, Floor, z), x * 9f % 360f);
        foreach (var (x, z) in new[] { (156f, -12f), (156f, 20f), (137f, -20f) }) b.PropSolid("bin", new Vector3(x, Floor, z));
        foreach (var (x, z) in new[] { (179f, -38f), (178f, -37f), (127f, 38f), (179f, 38f) }) b.Prop("garbage", new Vector3(x, Floor, z), x * 11f % 360f);
        b.PropSolid("vending", new Vector3(160f, Floor, 39.4f), 180f);
        b.PropSolid("vending", new Vector3(162f, Floor, 39.4f), 180f);
        foreach (var (x, z) in new[] { (154.5f, -10f), (154.5f, 8f) }) b.PropSolid("plant", new Vector3(x, Floor, z));
        foreach (var (x, z) in new[] { (72f, -18f), (72f, 20f), (96f, -30f), (96f, -4f), (110f, 20f), (132f, -14f), (150f, 8f), (166f, -24f), (166f, 22f) })
        {
            b.Lamp(new Vector3(x, Roof - 1f, z), 20f, 2.4f);
            b.Hang("lamp", new Vector3(x, Roof, z));
        }
        b.EnemySpawn("대합실 남", new Vector3(136f, Floor, -24f));
        b.EnemySpawn("대합실 동남", new Vector3(172f, Floor, -35f));
        b.EnemySpawn("대합실 북", new Vector3(166f, Floor, 34f));

        // 6. The gate's jet bridge: its plane is gone, so it ends in the air over the apron, and its service stairs go down.
        b.Box("동쪽 벽 1", 180f, 180.3f, Floor, Roof, -40f, -2f);
        b.Box("동쪽 벽 2", 180f, 180.3f, Floor, Roof, 2f, 40f);
        b.Box("탑승구 문 위", 180f, 180.3f, Floor + 2.6f, Roof, -2f, 2f);
        b.Box("탑승교 바닥", 180.3f, 196f, Floor - .4f, Floor, -1.6f, 1.6f, b.Metal, "Gantry");
        b.Box("탑승교 벽 남", 180.3f, 196f, Floor, Floor + 2.6f, -1.9f, -1.6f, b.Metal, "Metal");
        b.Box("탑승교 벽 북", 180.3f, 196f, Floor, Floor + 2.6f, 1.6f, 1.9f, b.Metal, "Metal");
        b.Box("탑승교 지붕", 180.3f, 196f, Floor + 2.6f, Floor + 2.9f, -1.9f, 1.9f, b.Metal);
        b.Box("탑승교 다리", 191.7f, 192.3f, 0f, Floor - .4f, -.3f, .3f, b.Metal, "MetalPipe");
        b.Box("탑승교 끝 바닥", 196f, 199f, Floor - .4f, Floor, -2.4f, 2.4f, b.Metal, "Gantry");
        b.Box("탑승교 끝 벽 북", 196f, 199f, Floor, Floor + 2.6f, 2.1f, 2.4f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 남 1", 196f, 196.6f, Floor, Floor + 2.6f, -2.4f, -2.1f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 남 2", 198.4f, 199f, Floor, Floor + 2.6f, -2.4f, -2.1f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 동 1", 198.7f, 199f, Floor, Floor + 2.6f, -2.1f, -1.2f, b.Metal, "Metal");
        b.Box("탑승교 끝 벽 동 2", 198.7f, 199f, Floor, Floor + 2.6f, 1.2f, 2.1f, b.Metal, "Metal");
        b.Box("탑승교 끝 지붕", 196f, 199f, Floor + 2.6f, Floor + 2.9f, -2.4f, 2.4f, b.Metal);
        b.Box("정비 계단 위", 196.4f, 198.6f, Floor - .4f, Floor, -3.6f, -2.4f, b.Metal, "Gantry");
        b.Ramp("정비 계단", new Vector3(197.5f, 0f, -11f), new Vector3(197.5f, Floor, -3.6f), 1.6f, b.Metal);
        b.Lamp(new Vector3(188f, Floor + 2.3f, 0f), 8f, 1.2f);
        b.Spot("Apron", new Vector3(197.5f, 0f, -12.5f));

        // 7. The apron: baggage carts, freight cans, a fuel truck, a catering truck, a stairs truck, and the control tower,
        // whose ground-floor radio reaches the transport's crew.
        foreach (float x in new[] { 196f, 199f, 202f, 205f }) b.Box("수하물 카트", x, x + 2.4f, 0f, 1f, 14f, 15.4f, b.Metal, "MetalBox");
        b.Box("견인 트랙터", 207.5f, 210.5f, 0f, 1.4f, 13.7f, 15.7f, b.Metal, "Metal");
        foreach (float x in new[] { 200f, 202.2f, 204.4f, 206.6f })
            b.Box("항공 화물 컨테이너", x, x + 1.6f, 0f, 1.5f, 30f, 31.6f, b.Metal, "MetalBox");
        b.Box("급유차", 226f, 235f, 0f, 3f, 10f, 12.6f, b.Metal, "Metal");
        b.Box("기내식 트럭", 205f, 212f, 0f, 3.8f, -32f, -29.5f, b.Metal, "MetalBox");
        b.Ramp("계단차 계단", new Vector3(238f, 0f, -21f), new Vector3(238f, 3.5f, -14.5f), 1.6f, b.Metal);
        b.Box("계단차 발판", 237.2f, 238.8f, 3.2f, 3.5f, -14.5f, -13f, b.Metal, "Gantry");
        Room(b, "관제탑 1층", 214f, 224f, -52f, -42f, 0f, 4f, Side.North, 217.5f, 220f);
        b.Box("관제탑 기둥", 217f, 221f, 4.3f, 26f, -49f, -45f);
        b.Box("관제탑 관제실", 214f, 224f, 26f, 30f, -52f, -42f, b.Metal, "Metal");
        b.Dress(b.Box("관제탑 무전기", 221.5f, 223f, 0f, 1.1f, -51.2f, -50.2f, b.Marker), "controlboxFit");
        b.PropSolid("table", new Vector3(217.5f, 0f, -50.6f));
        b.Prop("screen", new Vector3(217.5f, .78f, -50.8f), 180f);
        b.Prop("desk", new Vector3(218.3f, .78f, -50.6f), 180f);
        b.Prop("officechair", new Vector3(217.5f, 0f, -49.3f), 180f);
        foreach (var (x, z) in new[] { (215f, -43f), (223f, -43.2f) }) b.Prop("fusebox", new Vector3(x, 1.2f, z), 180f);
        b.Spot("TowerRadio", new Vector3(222.2f, 0f, -49.5f));
        // The apron's leftovers: crates and boxes by the catering truck and the carts.
        foreach (var (x, z) in new[] { (204f, -27f), (205.2f, -26.4f), (213f, -27.5f), (200f, 17f), (201.3f, 17.6f) }) b.Prop("crate", new Vector3(x, 0f, z), x * 7f % 360f);
        foreach (var (x, z) in new[] { (214f, -28f), (198f, 12f), (210f, 31f) }) b.Prop("box", new Vector3(x, 0f, z), x * 5f % 360f);
        foreach (var (x, z) in new[] { (196f, 20f), (233f, 9f) }) b.Prop("suitcase", new Vector3(x, 0f, z), x * 13f % 360f);
        b.Lamp(new Vector3(219f, 3.4f, -47f), 8f, 1.4f);
        b.EnemySpawn("계류장 북", new Vector3(215f, 0f, 45f));
        b.EnemySpawn("계류장 북동", new Vector3(240f, 0f, 30f));
        b.EnemySpawn("계류장 남", new Vector3(232f, 0f, -55f));
        b.EnemySpawn("터미널 밑", new Vector3(186f, 0f, 25f));

        // 8. The runway, and across it the airliner that was being pushed back when everything fell: the transport can't
        // take off past it. Its tug pushes it 40 m north, off the runway, while someone stands at the tug (Ch4.09, a
        // manned hold; CampaignPushback). Its body, wings and engines hang high enough to walk under, and its gear and
        // the tug cut the walk mesh as they go.
        b.Box("활주로", 238f, 359f, 0f, .02f, -12f, 12f, b.Concrete, solid: false);
        for (float x = 245f; x < 355f; x += 12f) b.Box("활주로 중앙선", x, x + 6f, .02f, .03f, -.2f, .2f, b.Metal, solid: false);
        var plane = b.Pushback("버려진 여객기 (견인차로 밀어냄)", "Ch4.09", new Vector3(0f, 0f, 40f));
        b.Box("여객기 동체", 278f, 282f, 2.4f, 6.6f, -20f, 16f, b.Metal, "Metal", group: plane);
        b.Box("여객기 기수", 278.6f, 281.4f, 3f, 6f, -22f, -20f, b.Metal, "Metal", group: plane);
        b.Box("여객기 날개", 263f, 297f, 5.4f, 5.8f, -6f, 1f, b.Metal, "Metal", group: plane);
        b.Box("여객기 엔진 왼쪽", 268f, 271f, 2.6f, 5.4f, -9f, -3f, b.Metal, "Metal", group: plane);
        b.Box("여객기 엔진 오른쪽", 289f, 292f, 2.6f, 5.4f, -9f, -3f, b.Metal, "Metal", group: plane);
        b.Box("여객기 수평 꼬리", 273f, 287f, 6.6f, 6.9f, 12f, 15.5f, b.Metal, solid: false, group: plane);
        b.Box("여객기 수직 꼬리", 279.85f, 280.15f, 6.6f, 12f, 10f, 16f, b.Metal, solid: false, group: plane);
        b.Carve(b.Box("여객기 앞바퀴", 279.7f, 280.3f, 0f, 2.4f, -17.3f, -16.7f, b.Metal, "MetalPipe", group: plane));
        b.Carve(b.Box("여객기 뒷바퀴 왼쪽", 276.6f, 277.4f, 0f, 5.4f, -3.4f, -2.6f, b.Metal, "MetalPipe", group: plane));
        b.Carve(b.Box("여객기 뒷바퀴 오른쪽", 282.6f, 283.4f, 0f, 5.4f, -3.4f, -2.6f, b.Metal, "MetalPipe", group: plane));
        var tug = b.Box("견인차", 278.8f, 281.2f, 0f, 1.2f, -26f, -22.5f, b.Metal, "Metal", group: plane);
        b.Carve(tug);
        b.PushbackTug(plane, tug);
        b.Box("견인 막대", 279.9f, 280.1f, .4f, .6f, -22.5f, -17.3f, b.Metal, solid: false, group: plane);
        b.Spot("Tug", new Vector3(280f, 0f, -24.25f), plane);
        b.EnemySpawn("활주로 서남", new Vector3(250f, 0f, -45f));
        b.EnemySpawn("활주로 서북", new Vector3(250f, 0f, 45f));
        b.EnemySpawn("활주로 남", new Vector3(300f, 0f, -48f));
        b.EnemySpawn("활주로 북동", new Vector3(318f, 0f, 48f));

        // 9. The last transport at the runway's east end, its rear ramp down: the finale's hold is around it, the escape
        // is into its hold, and as the song ends it goes with whoever is aboard.
        b.Box("수송기 화물칸 바닥", 322f, 346f, 0f, 1.2f, -3f, 3f, b.Metal, "Gantry");
        b.Box("수송기 벽 남", 322f, 346f, 1.2f, 5.5f, -3f, -2.7f, b.Metal, "Metal");
        b.Box("수송기 벽 북", 322f, 346f, 1.2f, 5.5f, 2.7f, 3f, b.Metal, "Metal");
        b.Box("수송기 앞벽", 322f, 322.3f, 1.2f, 5.5f, -2.7f, 2.7f, b.Metal, "Metal");
        b.Box("수송기 지붕", 322f, 346f, 5.5f, 5.8f, -3f, 3f, b.Metal, "Metal");
        b.Box("수송기 기수", 318f, 322f, 1f, 4.5f, -2.2f, 2.2f, b.Metal, "Metal");
        b.Ramp("수송기 램프", new Vector3(352f, 0f, 0f), new Vector3(346f, 1.2f, 0f), 5.4f, b.Metal);
        b.Box("수송기 날개", 330f, 336f, 5.8f, 6.2f, -22f, 22f, b.Metal, "Metal");
        foreach (float z in new[] { -15.4f, -8.2f, 5.8f, 13f }) b.Box("수송기 엔진", 328f, 332.5f, 3.6f, 5.8f, z, z + 2.4f, b.Metal, "Metal");
        b.Box("수송기 바퀴 덮개 남", 328f, 340f, 0f, 1.5f, -4.2f, -3f, b.Metal, "Metal");
        b.Box("수송기 바퀴 덮개 북", 328f, 340f, 0f, 1.5f, 3f, 4.2f, b.Metal, "Metal");
        b.Box("수송기 수직 꼬리", 340f, 346f, 5.8f, 12f, -.15f, .15f, b.Metal, solid: false);
        b.Box("수송기 수평 꼬리", 341f, 346f, 10.5f, 10.8f, -9f, 9f, b.Metal, solid: false);
        b.Dress(b.Box("화물칸 짐 1", 326f, 328f, 1.2f, 2.4f, -2.2f, -.6f, b.Wood, "WoodPlank"), "crateFit");
        b.Dress(b.Box("화물칸 짐 2", 330f, 332.4f, 1.2f, 2.2f, .8f, 2.2f, b.Wood, "WoodPlank"), "crateFit");
        b.Prop("moneybag", new Vector3(329f, 1.2f, -1.9f), 30f);
        b.Dress(b.Box("출발 신호등", 350.6f, 351.4f, 0f, 1.3f, -5f, -4.2f, b.Marker), "controlboxFit");
        b.Spot("Transport", new Vector3(353f, 0f, 0f));
        b.Spot("Signal", new Vector3(351f, 0f, -3.6f));
        b.Spot("Cargo", new Vector3(336f, 1.2f, 0f));
        b.EnemySpawn("수송기 남", new Vector3(340f, 0f, -32f));
        b.EnemySpawn("수송기 북", new Vector3(340f, 0f, 32f));
        b.EnemySpawn("활주로 동남", new Vector3(356f, 0f, -20f));
        b.EnemySpawn("활주로 동북", new Vector3(356f, 0f, 20f));

        // Background sound by area (rows in Tables/Ambience; the runway and the road keep the map's own): the pier, the
        // hall, the concourse, the duty-free shop, the jet bridge, the apron's machinery, the tower's radio room.
        b.Ambience("Ch4.Pier", -22f, 20f, -1f, 10f, -60f, 60f, 0);
        b.Ambience("Ch4.Hall", 60f, 120f, Floor - 1f, Roof, -40f, 40f);
        b.Ambience("Ch4.Concourse", 120f, 180f, Floor - 1f, Roof, -40f, 40f);
        b.Ambience("Ch4.DutyFree", 140f, 154f, Floor, Floor + 4f, 26f, 40f);
        b.Ambience("Ch4.Bridge", 180.3f, 199f, Floor - .5f, Floor + 3f, -2.4f, 2.4f);
        b.Ambience("Ch4.Apron", 180f, 250f, -1f, 12f, -60f, 60f, 0);
        b.Ambience("Ch4.Tower", 214f, 224f, 0f, 4f, -52f, -42f);

        // Field pickups (6D): a gun at the pier, upstairs in the hall and in the rest stop, supplies along the way and
        // in the transport's hold for the finale.
        b.Pickup(new Vector3(-8f, .3f, 2.5f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(6f, 0f, 9f));
        b.Pickup(new Vector3(52f, Floor, -4f));
        b.Pickup(new Vector3(68f, Floor, 3.5f));
        b.Pickup(new Vector3(110f, Upper, 34f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(131f, Floor, 3f));
        b.Pickup(new Vector3(151f, Floor, 30f));
        b.Pickup(new Vector3(143f, Floor, 33f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(197.5f, 0f, -14.5f));
        b.Pickup(new Vector3(219f, 0f, -46f));
        b.Pickup(new Vector3(262f, 0f, -20f));
        b.Pickup(new Vector3(336f, 1.2f, 1.6f));
        b.Pickup(new Vector3(350f, 0f, 8f));
        b.Shot(new Vector3(-10f, 50f, -75f), new Vector3(170f, 0f, 0f));
        DressAirport(b);
        b.Finish("4장 공항을 지었어요 (Scenes/Campaign/Ch4_Airport).\n" +
                 "길: 화물 부두 → 화물장 → 출발층 진입로 → 터미널 출발 홀(2층 식당가) → 보안 검색대 → 면세점(휴게소) → 탑승교와 정비 계단 → " +
                 "계류장 → 관제탑 무전 → 활주로를 막은 여객기를 견인차로 밀어내기(누가 견인차 옆에 있어야 움직임) → 수송기 → 출발 신호 → 버티기 → 탑승.");
    }

    // Chapter 1's block of flats B4, x 87-104, z -10..10: floors at 0, 3, 6 and 9 m under the roof (12), and on each a
    // corridor along x (z -1.2..1.2) with flats north and south of it. The roof's stair hut (west stairs) reaches the top
    // floor only. There the way on is the south-east flat, whose floor gave way: a 3 m drop (under
    // Ability.safeFallDistance, so it doesn't hurt) into the same flat a floor down, then along the corridor to the east
    // stairs, down to the ground floor and out of its back door into the alley. The second floor's door is boarded up.
    // Rooms wear what the packs have (sofa, TV, lockers, a fridge, tables, boxes, rubbish); a few dim lamps; zombies
    // wait on every floor, and the alley hold's horde can come out of the back door.
    private static void Apartment(GreyboxBuilder b)
    {
        const float X0 = 87f, X1 = 104f, Z0 = -10f, Z1 = 10f, In0 = X0 + Wall, In1 = X1 - Wall, S0 = Z0 + Wall, N1 = Z1 - Wall;
        // Stair cores north of the corridor: west (roof to the top floor), east (third floor to the ground floor). Each
        // has a landing by the corridor, two flights side by side and a half landing at the far end.
        const float CoreZ = 1.5f, Landing = 1.2f, Near = CoreZ + Landing, Far = N1 - Landing;
        const float WestTo = 91.3f, EastFrom = 99.7f;
        const float HoleX0 = 100f, HoleX1 = 102.4f, HoleZ0 = -7.5f, HoleZ1 = -5f;
        const float Ceiling = 2.7f;

        // Outer walls; the back door through the east wall on the ground floor.
        b.Box("B4 서벽", X0, In0, 0f, 12f, Z0, Z1);
        b.Box("B4 남벽", In0, In1, 0f, 12f, Z0, S0);
        b.Box("B4 북벽", In0, In1, 0f, 12f, N1, Z1);
        b.Box("B4 동벽 남", In1, X1, 0f, 12f, Z0, -1.1f);
        b.Box("B4 동벽 북", In1, X1, 0f, 12f, 1.1f, Z1);
        b.Box("B4 뒷문 위", In1, X1, 2.3f, 12f, -1.1f, 1.1f);

        // Floors: the roof open over the west stairs, the top floor over the broken flat, the third and second floors over
        // the east stairs.
        Slab(b, "B4 지붕", 12f, In0, In1, S0, N1, In0, WestTo, Near, N1, null, null);
        Slab(b, "B4 4층 바닥", 9f, In0, In1, S0, N1, HoleX0, HoleX1, HoleZ0, HoleZ1, b.Concrete, "Tile");
        Slab(b, "B4 3층 바닥", 6f, In0, In1, S0, N1, EastFrom, In1, Near, N1, b.Concrete, "Tile");
        Slab(b, "B4 2층 바닥", 3f, In0, In1, S0, N1, EastFrom, In1, Near, N1, b.Concrete, "Tile");

        // West stairs under the roof's hut, roof to the top floor.
        Room(b, "B4 옥상 계단실", X0, WestTo + Wall, CoreZ - Wall, Z1, 12f, 2.7f, Side.South, 87.6f, 89.1f);
        b.Box("B4 서계단 동벽", WestTo, WestTo + Wall, 9f, 12f, CoreZ - Wall, N1);
        WallAlongX(b, "B4 서계단 남벽", In0, WestTo + Wall, CoreZ - Wall, CoreZ, 9f, 9f + Ceiling, (89.5f, 91.1f));
        Flights(b, "B4 서계단", In0, WestTo, Near, Far, N1, 12f, 9f);

        // East stairs, third floor to the ground floor: doors onto the corridor on the third and ground floors.
        b.Box("B4 동계단 서벽", EastFrom - Wall, EastFrom, 0f, 9f - Wall, CoreZ - Wall, N1);
        WallAlongX(b, "B4 동계단 1층 남벽", EastFrom - Wall, In1, CoreZ - Wall, CoreZ, 0f, Ceiling, (101.9f, 103.5f));
        b.Box("B4 동계단 2층 남벽 (판자로 막음)", EastFrom - Wall, In1, 3f, 3f + Ceiling, CoreZ - Wall, CoreZ);
        WallAlongX(b, "B4 동계단 3층 남벽", EastFrom - Wall, In1, CoreZ - Wall, CoreZ, 6f, 6f + Ceiling, (99.9f, 101.5f));
        Flights(b, "B4 동계단", EastFrom, In1, Near, Far, N1, 6f, 0f);

        // Top floor (9): the corridor, two flats each side. The south-west flat's door is blocked by a sofa.
        WallAlongX(b, "B4 4층 복도 북벽", WestTo + Wall, In1, CoreZ - Wall, CoreZ, 9f, 9f + Ceiling, (93f, 94.2f), (99f, 100.2f));
        WallAlongX(b, "B4 4층 복도 남벽", In0, In1, -1.5f, -1.2f, 9f, 9f + Ceiling, (90f, 91.2f), (99f, 100.2f));
        b.Box("B4 4층 남쪽 집 사이벽", 95.35f, 95.65f, 9f, 9f + Ceiling, S0, -1.5f);
        b.Box("B4 4층 북쪽 집 사이벽", 97.45f, 97.75f, 9f, 9f + Ceiling, CoreZ, N1);
        // Third floor (6): the north flat, the south-east flat the hole drops into.
        WallAlongX(b, "B4 3층 복도 북벽", In0, EastFrom - Wall, CoreZ - Wall, CoreZ, 6f, 6f + Ceiling, (93f, 94.2f));
        WallAlongX(b, "B4 3층 복도 남벽", In0, In1, -1.5f, -1.2f, 6f, 6f + Ceiling, (96.2f, 97.4f));
        b.Box("B4 3층 남쪽 집 사이벽", 95.35f, 95.65f, 6f, 6f + Ceiling, S0, -1.5f);
        // Ground floor (0): the corridor to the back door, the north flat, the south-east flat (the mail room).
        WallAlongX(b, "B4 1층 복도 북벽", In0, EastFrom - Wall, CoreZ - Wall, CoreZ, 0f, Ceiling, (93f, 94.2f));
        WallAlongX(b, "B4 1층 복도 남벽", In0, In1, -1.5f, -1.2f, 0f, Ceiling, (90f, 91.2f), (98f, 99.2f));
        b.Box("B4 1층 남쪽 집 사이벽", 95.35f, 95.65f, 0f, Ceiling, S0, -1.5f);

        b.Spot("B4Hole", new Vector3((HoleX0 + HoleX1) * .5f, 9f, (HoleZ0 + HoleZ1) * .5f));
        b.Spot("B4Lobby", new Vector3(100.5f, 0f, 0f));

        // Lamps, dim and warm (the building lost its mains; these are the survivors' work lights).
        foreach (var at in new[]
        {
            new Vector3(95.5f, 11.3f, 0f), new Vector3(89.3f, 14.3f, 6f), new Vector3(94.5f, 11.3f, 5.5f), new Vector3(100.5f, 11.3f, 5.5f),
            new Vector3(99.5f, 11.3f, -5.5f), new Vector3(92f, 8.3f, 0f), new Vector3(99.5f, 8.3f, -5.5f), new Vector3(101.7f, 8.3f, 5f),
            new Vector3(100.2f, 3.6f, 6f), new Vector3(97f, 2.3f, 0f), new Vector3(99.5f, 2.3f, -5.5f), new Vector3(92f, 2.3f, 6f),
        })
            b.Lamp(at, 7f, 1.1f);

        // Top floor: rubbish in the corridor, the sofa across the south-west door, a lived-in north-west flat, a kitchen
        // and a survivor's stash north-east, debris round the hole.
        foreach (var (x, z) in new[] { (89f, -.8f), (97.5f, .7f), (103f, -.6f) }) b.Prop("litter", new Vector3(x, 9f, z), x * 13f % 360f);
        foreach (var (x, z) in new[] { (92.5f, -.85f), (102.8f, .65f) }) b.Prop("trashbag", new Vector3(x, 9f, z), x * 7f % 360f);
        b.Prop("cardboard", new Vector3(101.5f, 9f, .85f), 20f);
        b.PropSolid("couch", new Vector3(90.6f, 9f, -.65f), 0f);
        b.Prop("couch", new Vector3(94.5f, 9f, 8.7f), 180f);
        b.Prop("oldtv", new Vector3(94.5f, 9f, 3.3f), 0f);
        b.PropSolid("worktable", new Vector3(95.6f, 9f, 5.6f), 90f);
        foreach (var (x, z) in new[] { (95f, 4.4f), (96.4f, 6.6f) }) b.Prop("stool", new Vector3(x, 9f, z), x * 17f % 360f);
        b.PropSolid("locker", new Vector3(92.1f, 9f, 8.9f), 90f);
        b.Prop("cardboard", new Vector3(96.6f, 9f, 9.1f), 5f);
        b.PropSolid("fridge", new Vector3(103.1f, 9f, 8.9f), 270f);
        b.PropSolid("worktable", new Vector3(100.6f, 9f, 6.2f), 0f);
        b.Prop("stool", new Vector3(100.4f, 9f, 4.9f), 40f);
        b.Prop("plasticdrum", new Vector3(98.4f, 9f, 9.1f));
        foreach (var (x, z) in new[] { (99.1f, 8.9f), (99.6f, 9.2f) }) b.Prop("jerrycan", new Vector3(x, 9f, z), x * 29f % 360f);
        b.Prop("cardboardflat", new Vector3(101f, 9f, 3f), 15f);
        foreach (var (x, z) in new[] { (99.4f, -6.2f), (103f, -6.6f), (101.3f, -8.3f) }) b.Prop("brickdebris", new Vector3(x, 9f, z), x * 19f % 360f);
        b.Prop("planks", new Vector3(101.2f, 9f, -4.4f), 30f);
        b.Prop("rubble", new Vector3(97.5f, 9f, -8.4f), 60f);
        // Third floor: what fell through, a stripped north flat.
        b.Prop("brickdebris", new Vector3(101.2f, 6f, -6.3f), 110f);
        b.Prop("rubble", new Vector3(102.6f, 6f, -5f), 200f);
        foreach (var (x, z) in new[] { (97f, -8.8f), (98.2f, -9f) }) b.Prop("plasticcrate", new Vector3(x, 6f, z), x * 11f % 360f);
        b.Prop("trashbag", new Vector3(91f, 6f, .8f), 70f);
        b.Prop("litter", new Vector3(98.5f, 6f, -.7f), 140f);
        b.Prop("couch", new Vector3(91.5f, 6f, 8.7f), 180f);
        b.Prop("oldtv", new Vector3(91.5f, 6f, 3.2f), 10f);
        b.PropSolid("wireshelf", new Vector3(98.5f, 6f, 8.8f), 0f);
        foreach (var (x, z) in new[] { (96.5f, 3.4f), (97.3f, 4.1f), (88.5f, 6.5f) }) b.Prop("cardboard", new Vector3(x, 6f, z), x * 23f % 360f);
        // The boarded second-floor door, boxes on the landings.
        b.Prop("planks", new Vector3(102.6f, 3f, 1.8f), 90f);
        b.Prop("cardboard", new Vector3(100.4f, 3f, 1.9f), 30f);
        // Ground floor: the mail room's boxes (electric boxes on the wall), rubbish to the back door.
        foreach (var (x, z) in new[] { (100f, -9.35f), (102f, -9.35f) }) b.Prop("electricbox", new Vector3(x, 0f, z), 0f);
        foreach (var (x, z) in new[] { (97f, -2.5f), (103f, -8f) }) b.Prop("cardboard", new Vector3(x, 0f, z), x * 31f % 360f);
        b.Prop("trashcan", new Vector3(96f, 0f, -.9f), 0f);
        b.Prop("cardboardflat", new Vector3(102.5f, 0f, .2f), 75f);
        b.Prop("litter", new Vector3(99f, 0f, .7f), 20f);
        b.Prop("trashbag", new Vector3(92f, 0f, 8.5f), 160f);

        // Zombies on every floor, a few supplies, and the building's own sound.
        b.EnemySpawn("B4 4층 복도", new Vector3(102.5f, 9f, 0f));
        b.EnemySpawn("B4 4층 북서 집", new Vector3(94f, 9f, 6.5f));
        b.EnemySpawn("B4 4층 북동 집", new Vector3(101f, 9f, 4f));
        b.EnemySpawn("B4 4층 남동 집", new Vector3(97.5f, 9f, -4f));
        b.EnemySpawn("B4 3층 남동 집", new Vector3(97.5f, 6f, -7.5f));
        b.EnemySpawn("B4 3층 복도", new Vector3(90f, 6f, 0f));
        b.EnemySpawn("B4 3층 북쪽 집", new Vector3(93f, 6f, 6f));
        b.EnemySpawn("B4 동계단 2층", new Vector3(102.7f, 3f, 2.1f));
        b.EnemySpawn("B4 1층 복도", new Vector3(95f, 0f, 0f));
        b.EnemySpawn("B4 1층 우편함 방", new Vector3(101f, 0f, -6f));
        b.EnemySpawn("B4 1층 북쪽 집", new Vector3(93f, 0f, 6f));
        b.Pickup(new Vector3(101.5f, 9f, 7.6f));
        b.Pickup(new Vector3(90.5f, 6f, 7.4f), PickupSpot.Roll.Gun);
        b.Pickup(new Vector3(99f, 0f, -.5f));
        b.Ambience("Ch1.Flats", In0, In1, 0f, 12f, S0, N1);
    }

    // Two flights per floor side by side, from a landing by the corridor (near) to a half landing at the far end and
    // back, from top down to bottom (3 m a floor), with a wall between them.
    private static void Flights(GreyboxBuilder b, string name, float x0, float x1, float near, float far, float end,
        float top, float bottom)
    {
        float half = (x1 - x0) * .5f, a = x0 + half * .5f, c = x0 + half * 1.5f;
        int floor = 0;
        for (float y = top; y > bottom + .01f; y -= 3f, floor++)
        {
            float mid = y - 1.5f;
            b.Ramp($"{name} {floor * 2 + 1}", new Vector3(a, mid, far), new Vector3(a, y, near), half - .1f, b.Concrete);
            b.Box($"{name} 참 {floor + 1}", x0, x1, mid - Wall, mid, far, end, b.Concrete, "Concrete");
            b.Ramp($"{name} {floor * 2 + 2}", new Vector3(c, y - 3f, near), new Vector3(c, mid, far), half - .1f, b.Concrete);
        }
        b.Box(name + " 가운데 벽", x0 + half - .05f, x0 + half + .05f, bottom, top + 1f, near, far);
    }

    // A floor slab with its top at y over x0..x1, z0..z1, open over the rectangle hx0..hx1, hz0..hz1.
    private static void Slab(GreyboxBuilder b, string name, float y, float x0, float x1, float z0, float z1,
        float hx0, float hx1, float hz0, float hz1, Material material, string surface)
    {
        float y0 = y - Wall;
        if (hz0 > z0) b.Box(name + " 남", x0, x1, y0, y, z0, hz0, material, surface);
        if (hz1 < z1) b.Box(name + " 북", x0, x1, y0, y, hz1, z1, material, surface);
        if (hx0 > x0) b.Box(name + " 서", x0, hx0, y0, y, hz0, hz1, material, surface);
        if (hx1 < x1) b.Box(name + " 동", hx1, x1, y0, y, hz0, hz1, material, surface);
    }

    // A wall along x (z0..z1 thick) from y0 to y1 with door openings 2.3 m high at the given spans (west to east).
    private static void WallAlongX(GreyboxBuilder b, string name, float x0, float x1, float z0, float z1, float y0, float y1,
        params (float from, float to)[] doors)
    {
        float x = x0;
        int part = 0;
        foreach (var (from, to) in doors)
        {
            if (from > x) b.Box($"{name} {++part}", x, from, y0, y1, z0, z1);
            b.Box($"{name} 문 위 {part}", from, to, y0 + 2.3f, y1, z0, z1);
            x = to;
        }
        if (x1 > x) b.Box($"{name} {++part}", x, x1, y0, y1, z0, z1);
    }

    // Chapter 1's streets, roofs, alley and building site in the packs' props (map dressing stage 3, 2026-11-12): a
    // police car and barricaded street ends, poles and lamps, water tanks and air conditioners on the roofs, rubbish in
    // the alley, the building site's materials and machines. Big things block (PropSolid) and stand clear of the
    // routes and the spawns; small things are only seen.
    private static void DressRooftops(GreyboxBuilder b)
    {
        // Roofs.
        b.PropSolid("watertank", new Vector3(30.5f, 16f, -6.5f), 0f);
        foreach (var x in new[] { 72f, 74.2f }) b.PropSolid("acunit", new Vector3(x, 13f, -8.4f), 0f);
        b.PropSolid("watertank", new Vector3(101.5f, 12f, -7.3f), 30f);
        // Street 1 under the zipline: barricaded ends, a police car, cones round the first car, rubbish.
        foreach (var z in new[] { -21f, 22f })
            foreach (var x in new[] { 51.5f, 55f, 58.5f }) b.Prop("jersey", new Vector3(x, 0f, z), 0f);
        b.PropSolid("policecar", new Vector3(54f, 0f, 12f), 75f);
        foreach (var (x, z) in new[] { (49.8f, -7f), (51f, -7.4f), (50.2f, -3.2f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 37f % 360f);
        b.Prop("trafficdrum", new Vector3(60.8f, 0f, 7f));
        b.Prop("powerpole", new Vector3(48.4f, 0f, -16f), 90f);
        b.Prop("powerpole", new Vector3(48.4f, 0f, 18f), 90f);
        b.Prop("streetlamp", new Vector3(61.6f, 0f, -10f), 270f);
        b.Prop("streetlamp", new Vector3(61.6f, 0f, 12f), 270f);
        foreach (var (x, z) in new[] { (48f, 3f), (62f, -3f), (49f, 9.5f) }) b.Prop("trashbag", new Vector3(x, 0f, z), x * 13f % 360f);
        foreach (var (x, z) in new[] { (53f, 1.5f), (57.5f, -8f), (56f, 16f) }) b.Prop("litter", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Prop("cardboard", new Vector3(62.2f, 0f, -14f), 15f);
        // The alley.
        b.Prop("trashcan", new Vector3(116f, 0f, 3.4f), 0f);
        b.Prop("cardboard", new Vector3(133f, 0f, 3.2f), 40f);
        b.Prop("planks", new Vector3(130.5f, 0f, -3.6f), 90f);
        b.Prop("electricbox", new Vector3(135.5f, 0f, 3.65f), 180f);
        foreach (var (x, z) in new[] { (112f, -1f), (126f, -.5f) }) b.Prop("litter", new Vector3(x, 0f, z), x * 11f % 360f);
        // Street 2 and the building site's gate.
        b.PropSolid("policecar", new Vector3(165f, 0f, -12f), 30f);
        b.Prop("jersey", new Vector3(168.4f, 0f, -6f), 90f);
        b.Prop("jersey", new Vector3(168.4f, 0f, 6f), 90f);
        foreach (var (x, z) in new[] { (169f, -2.5f), (169f, 2.5f), (167.5f, .8f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 23f % 360f);
        b.Prop("warninglight", new Vector3(169.3f, 0f, -4.2f), 270f);
        b.Prop("streetlamp", new Vector3(143f, 0f, -20f), 0f);
        b.Prop("streetlamp", new Vector3(165f, 0f, 22f), 180f);
        b.Prop("trashbag", new Vector3(144.5f, 0f, 9f), 60f);
        b.Prop("litter", new Vector3(157.8f, 0f, 9.3f), 10f);
        // The building site: bricks and bags by the gate, pipes, a mixer and a generator by the building, toilets and a
        // water tank in the north corner, tarps, boards and drums.
        b.PropSolid("bricks", new Vector3(173.5f, 0f, -6f), 0f);
        b.PropSolid("bricks", new Vector3(176.5f, 0f, -14f), 90f);
        foreach (var (x, z) in new[] { (175.5f, -6.8f), (176.4f, -7.3f) }) b.Prop("cementbags", new Vector3(x, 0f, z), x * 17f % 360f);
        b.PropSolid("concretepipe", new Vector3(205f, 0f, 4f), 0f);
        b.PropSolid("concretepipe", new Vector3(205f, 0f, 7f), 0f);
        b.PropSolid("concretemixer", new Vector3(207f, 0f, -4f), 90f);
        b.PropSolid("generator", new Vector3(208f, 0f, 11f), 180f);
        b.PropSolid("portapotty", new Vector3(172.5f, 0f, 24f), 90f);
        b.PropSolid("portapotty", new Vector3(174.5f, 0f, 24f), 90f);
        b.PropSolid("watertankportable", new Vector3(179f, 0f, 24f), 0f);
        b.Prop("tarp", new Vector3(178f, 0f, -22f), 20f);
        b.Prop("plywood", new Vector3(202f, 0f, -18f), 70f);
        b.Prop("woodpallet", new Vector3(203.5f, 0f, -20.5f), 10f);
        foreach (var (x, z) in new[] { (209f, -12f), (209.7f, -12.6f) }) b.Prop("oilbarrel", new Vector3(x, 0f, z), x * 29f % 360f);
        b.Prop("wheelbarrow", new Vector3(180f, 0f, -3.5f), 20f);
        b.Prop("propane", new Vector3(209f, 0f, 13.5f), 0f);
        foreach (var (x, z) in new[] { (201f, -10.5f), (201.5f, 10f) }) b.Prop("brickdebris", new Vector3(x, 0f, z), x * 19f % 360f);
        DenseRooftops(b);
    }

    // Map dressing stage 5 (the user, 2026-11-12: "맵에 밀도가 없어"): the chapter's floors filled, kept off its ways. On
    // the roofs, clutter along the inside of the long parapets (the run across each roof keeps to its middle, |z| < 4);
    // in the streets, rubbish and bins along both kerbs past the barricades and abandoned cars askew in the lanes; at the
    // buildings' feet and in the gaps the jumps cross, rubbish seen from above. The city beyond the edge and the
    // buildings' own walls and windows are tables' work (MapSkyline, MapSurfaces, MapWindows).
    private static void DenseRooftops(GreyboxBuilder b)
    {
        const float In = 10f - Wall - .6f, Foot = 10.6f;
        string[] roof = { "!acunit", "oilbarrel", "cardboard", "trashbag", "brickdebris", "plasticcrate", "tire", "pallet", "cinderblock", "gascylinder" };
        string[] street = { "trashbag", "cardboard", "litter", "box", "tire", "plasticcrate", "trashcan", "!dumpster", "rubblebag", "oilbarrel" };
        string[] rubbish = { "trashbag", "cardboard", "box", "litter", "brickdebris" };
        // Roofs: B1 (18 m), B2 (16 m, past its water tank), B3 (13 m, clear of its AC units and dish), B4 (12 m, south side).
        b.Clutter(new Vector3(1.2f, 18f, In), new Vector3(23f, 18f, In), 3f, roof);
        b.Clutter(new Vector3(1.2f, 18f, -In), new Vector3(23f, 18f, -In), 3f, roof);
        b.Clutter(new Vector3(28.2f, 16f, In), new Vector3(46f, 16f, In), 3f, roof);
        b.Clutter(new Vector3(33f, 16f, -In), new Vector3(46f, 16f, -In), 3f, roof);
        b.Clutter(new Vector3(64.2f, 13f, In), new Vector3(79f, 13f, In), 3f, roof);
        b.Clutter(new Vector3(64.2f, 13f, -In), new Vector3(70.5f, 13f, -In), 3f, roof);
        b.Clutter(new Vector3(88.5f, 12f, -In), new Vector3(99.5f, 12f, -In), 3f, roof);
        // Street 1 (between B2 and B3): both kerbs past the barricades, cars left askew beyond them.
        foreach (float x in new[] { 47.6f, 62.4f })
        {
            b.Clutter(new Vector3(x, 0f, -47f), new Vector3(x, 0f, -14f), 3f, street);
            b.Clutter(new Vector3(x, 0f, 14f), new Vector3(x, 0f, 47f), 3f, street);
        }
        foreach (var (x, z, yaw) in new[] { (52.5f, -32f, 18f), (58f, -41f, 160f), (53.5f, 34f, 200f), (59f, 42f, 10f) })
            b.PropSolid("car", new Vector3(x, 0f, z), yaw);
        // Street 2 (between the warehouse and the building site's fence): both kerbs, cars.
        b.Clutter(new Vector3(141.6f, 0f, -47f), new Vector3(141.6f, 0f, -5f), 3f, street);
        b.Clutter(new Vector3(141.6f, 0f, 5f), new Vector3(141.6f, 0f, 47f), 3f, street);
        b.Clutter(new Vector3(169.3f, 0f, -47f), new Vector3(169.3f, 0f, -8f), 3f, street);
        b.Clutter(new Vector3(169.3f, 0f, 8f), new Vector3(169.3f, 0f, 47f), 3f, street);
        foreach (var (x, z, yaw) in new[] { (150f, -30f, 75f), (157f, -42f, 120f), (152f, 33f, 260f), (160f, 41f, 15f) })
            b.PropSolid("car", new Vector3(x, 0f, z), yaw);
        // The buildings' feet (seen from the roofs) and the square west of B1 with two wrecks.
        b.Clutter(new Vector3(-.6f, 0f, -9f), new Vector3(-.6f, 0f, 9f), 3f, street);
        foreach (var (x0, x1) in new[] { (1f, 23f), (28f, 46f), (64f, 79f) })
        {
            b.Clutter(new Vector3(x0, 0f, Foot), new Vector3(x1, 0f, Foot), 3.5f, street);
            b.Clutter(new Vector3(x0, 0f, -Foot), new Vector3(x1, 0f, -Foot), 3.5f, street);
        }
        b.PropSolid("car", new Vector3(-14f, 0f, 22f), 35f);
        b.PropSolid("car", new Vector3(-19f, 0f, -27f), 110f);
        // The gaps the jumps cross: B1 to B2, and under the swing bar (clear of its posts and the ladder back up).
        b.Clutter(new Vector3(25.5f, 0f, -9f), new Vector3(25.5f, 0f, 9f), 2.5f, rubbish);
        b.Clutter(new Vector3(83.5f, 0f, -9f), new Vector3(83.5f, 0f, -2.6f), 2.5f, rubbish);
        // The alley, along its walls clear of the bins, the pallet and the shutter's boxes (small things only: the hold's
        // horde comes through here).
        b.Clutter(new Vector3(104.6f, 0f, -3.6f), new Vector3(116.5f, 0f, -3.6f), 2.5f, "litter", "cardboard", "trashbag");
        b.Clutter(new Vector3(109.5f, 0f, 3.6f), new Vector3(115f, 0f, 3.6f), 2.5f, "litter", "cardboard", "trashbag");
    }

    // Chapter 2's jam on the deck, the road below, the filling station and the army checkpoint in the packs' props (map
    // dressing stage 3): luggage left in the jam, cones by the wreck and the broken end, lamps along the road, a burned-
    // out army lorry and police cars before the toll, an army lorry and guard towers at the checkpoint, barbed wire on its
    // sandbags and HESCO walls to its sides.
    private static void DressOverpass(GreyboxBuilder b)
    {
        const float Deck = 8f;
        foreach (var (x, z) in new[] { (12.5f, -5.4f), (13.8f, -4.6f), (114.5f, 2f) }) b.Prop("cone", new Vector3(x, Deck, z), x * 31f % 360f);
        b.Prop("warninglight", new Vector3(11.2f, Deck, -6.1f), 0f);
        foreach (var (x, z, yaw) in new[] { (26f, 3.4f, 40f), (51.5f, -3.2f, 120f), (77.5f, 3.2f, 200f) }) b.Prop("suitcase", new Vector3(x, Deck, z), yaw);
        b.Prop("bag", new Vector3(65.5f, Deck, 2.2f), 70f);
        foreach (var (x, z) in new[] { (22f, .3f), (47f, 1.2f), (72f, -.4f), (103f, .9f) }) b.Prop("litter", new Vector3(x, Deck, z), x * 13f % 360f);
        b.Prop("trashbag", new Vector3(33.5f, Deck, 6.1f), 30f);
        foreach (var (x, z) in new[] { (28f, 6.2f), (52f, -6.2f), (90f, 5.8f) }) b.Prop("rubble", new Vector3(x, Deck, z), x * 11f % 360f);
        // The broken end: barriers and a warning light before the drop.
        b.Prop("jersey", new Vector3(115.5f, Deck, -4.5f), 90f);
        b.Prop("jersey", new Vector3(115.5f, Deck, 4.2f), 90f);
        b.Prop("sawhorse", new Vector3(114f, Deck, 0f), 90f);
        b.Prop("warninglight", new Vector3(113.5f, Deck, -2.2f), 90f);
        // The road below.
        foreach (var (x, z, yaw) in new[] { (115f, -23.5f, 0f), (140f, 23.5f, 180f), (170f, -23.5f, 0f), (185f, 23.5f, 180f) })
            b.Prop("streetlamp", new Vector3(x, 0f, z), yaw);
        b.Prop("roadplate", new Vector3(130f, 0f, 6f), 10f);
        b.Prop("roadplate", new Vector3(171f, 0f, -2f), 80f);
        b.PropSolid("wrecktruck", new Vector3(184f, 0f, 15f), 30f);
        b.PropSolid("policecar", new Vector3(199.5f, 0f, -11f), 95f);
        b.PropSolid("policecar", new Vector3(200f, 0f, 11.5f), 85f);
        foreach (var (x, z) in new[] { (196.5f, -4.5f), (196.5f, 4.5f), (197f, -13.5f), (197f, 13.5f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 17f % 360f);
        foreach (var z in new[] { -2.6f, 2.6f }) b.Prop("trafficdrum", new Vector3(195.5f, 0f, z));
        // The filling station.
        foreach (var (x, z) in new[] { (156.5f, 23f), (157.3f, 23.6f) }) b.Prop("drum", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Prop("gascylinder", new Vector3(155.5f, 0f, 23.2f));
        b.Prop("tirestack", new Vector3(169.5f, 0f, 9.5f), 20f);
        b.Prop("jersey", new Vector3(144f, 0f, 6.6f), 0f);
        b.Prop("jersey", new Vector3(150f, 0f, 6.6f), 0f);
        foreach (var (x, z) in new[] { (148f, 10.5f), (152f, 16f) }) b.Prop("litter", new Vector3(x, 0f, z), x * 19f % 360f);
        b.Prop("trashbag", new Vector3(157.8f, 0f, 12.5f), 120f);
        // The checkpoint: barbed wire along the sandbags' top, an army lorry, two guard towers, HESCO walls.
        for (float z = -19f; z <= -4f; z += 1.7f) b.Prop("barbedwire", new Vector3(204.5f, 1f, z), 90f);
        for (float z = 4f; z <= 19f; z += 1.7f) b.Prop("barbedwire", new Vector3(204.5f, 1f, z), 90f);
        b.PropSolid("militarytruck", new Vector3(217f, 0f, -8f), 0f);
        b.PropSolid("guardtower", new Vector3(226.5f, 0f, 19f), 180f);
        b.PropSolid("guardtower", new Vector3(226.5f, 0f, -19f), 0f);
        b.Dress(b.Box("HESCO 남", 220.5f, 222.4f, 0f, 2f, -24f, -17f, b.Wood, "Tarp"), "hesco");
        b.Dress(b.Box("HESCO 북", 220.5f, 222.4f, 0f, 2f, 17f, 24f, b.Wood, "Tarp"), "hesco");
        DenseOverpass(b);
    }

    // Chapter 2's density (map dressing stage 5, the user 2026-11-13: "2~4장 진행"): litter along the deck's rails, clear
    // of the wreck's cones, the ladder's gap and the broken end's barriers; rubbish along the sound barriers on the road
    // below, before the toll gate; debris along the fenced pit's walls.
    private static void DenseOverpass(GreyboxBuilder b)
    {
        const float Deck = 8f;
        string[] deck = { "litter", "trashbag", "cardboard", "brickdebris", "tire", "plasticcrate" };
        string[] road = { "trashbag", "cardboard", "litter", "tire", "rubblebag", "oilbarrel", "box" };
        string[] pit = { "rubble", "brickdebris", "tire", "litter", "cinderblock" };
        b.Clutter(new Vector3(18f, Deck, 6.2f), new Vector3(112f, Deck, 6.2f), 4.5f, deck);
        b.Clutter(new Vector3(18f, Deck, -6.2f), new Vector3(77f, Deck, -6.2f), 4.5f, deck);
        b.Clutter(new Vector3(83f, Deck, -6.2f), new Vector3(112f, Deck, -6.2f), 4.5f, deck);
        foreach (float z in new[] { 24.4f, -24.4f }) b.Clutter(new Vector3(103f, 0f, z), new Vector3(186f, 0f, z), 4f, road);
        b.Clutter(new Vector3(99.3f, 0f, -38f), new Vector3(99.3f, 0f, 38f), 4f, pit);
        foreach (float z in new[] { 38.3f, -38.3f }) b.Clutter(new Vector3(-17f, 0f, z), new Vector3(98f, 0f, z), 4.5f, pit);
    }

    // Chapter 3's concourse, platform, tunnel, depot, river tunnel and far bank in the packs' props (map dressing
    // stage 3): rubbish and boxes where people sheltered, debris on the tracks, the depot's shelves, lockers, drums and
    // workbench, and on the far bank the army's evacuation point (barbed wire on the sandbags, a lorry, HESCO, barriers).
    private static void DressSubway(GreyboxBuilder b)
    {
        const float Bank = 8f;
        // Concourse.
        foreach (var (x, z) in new[] { (-6.5f, -11.2f), (28.5f, 11.2f) }) b.Prop("trashbag", new Vector3(x, 0f, z), x * 13f % 360f);
        foreach (var (x, z) in new[] { (5f, -3f), (19f, 4f), (24f, -2f) }) b.Prop("litter", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Prop("cardboard", new Vector3(6.5f, 0f, 6.2f), 25f);
        b.Prop("plasticcrate", new Vector3(8.6f, 0f, 7.2f), 80f);
        b.Prop("planks", new Vector3(-4f, 0f, 11.4f), 0f);
        // Platform and tracks.
        foreach (var (x, z) in new[] { (35f, -11f), (86f, -10f) }) b.Prop("brickdebris", new Vector3(x, 0f, z), x * 11f % 360f);
        foreach (var (x, z) in new[] { (58f, -1f), (75f, -11f) }) b.Prop("litter", new Vector3(x, 0f, z), x * 17f % 360f);
        b.Prop("cardboard", new Vector3(44f, 1.1f, 11.2f), 10f);
        b.Prop("trashbag", new Vector3(66f, 1.1f, 11.3f), 140f);
        b.Prop("plasticcrate", new Vector3(80f, 1.1f, 11.2f), 15f);
        foreach (var (x, z) in new[] { (50f, 3f), (68f, 2.5f) }) b.Prop("litter", new Vector3(x, 1.1f, z), x * 19f % 360f);
        b.Prop("bag", new Vector3(61f, 1.1f, 7.5f), 200f);
        // Tunnel.
        foreach (var (x, z) in new[] { (100f, -5.4f), (118f, 1.4f), (135f, -5.3f) }) b.Prop("rubble", new Vector3(x, 0f, z), x * 23f % 360f);
        b.Prop("planks", new Vector3(122f, 0f, 1.3f), 0f);
        b.Prop("corrugated", new Vector3(155f, 0f, 1.4f), 0f);
        b.Prop("drum", new Vector3(165f, 0f, -5.4f), 40f);
        // Depot.
        b.PropSolid("palletload", new Vector3(201f, 0f, -18.5f), 0f);
        foreach (var (x, z) in new[] { (212f, 17.5f), (212.8f, 18.2f), (211.4f, 18.4f) }) b.Prop("drum", new Vector3(x, 0f, z), x * 29f % 360f);
        foreach (var (x, z) in new[] { (216.5f, -8f), (216.8f, -9.2f) }) b.Prop("gascylinder", new Vector3(x, 0f, z), x * 7f % 360f);
        b.Prop("plasticcrate", new Vector3(171.5f, 0f, -18.5f), 0f);
        foreach (var x in new[] { 176f, 179f }) b.PropSolid("wireshelf", new Vector3(x, 0f, -19.4f), 0f);
        foreach (var z in new[] { 4.4f, 5f }) b.PropSolid("locker", new Vector3(171f, 0f, z), 90f);
        b.PropSolid("worktable", new Vector3(198f, 0f, -12f), 0f);
        b.Prop("stool", new Vector3(198.8f, 0f, -11f), 30f);
        b.Prop("toolbox", new Vector3(199.5f, 0f, -12.9f), 10f);
        b.Prop("handtruck", new Vector3(203f, 0f, 8f), 60f);
        b.Prop("tire", new Vector3(215.5f, 0f, 6f), 0f);
        b.Prop("tirestack", new Vector3(213f, 0f, 7.5f), 0f);
        b.PropSolid("spool", new Vector3(208f, 0f, 18f), 0f);
        // River tunnel.
        b.Prop("electricbox", new Vector3(230f, 0f, 2.7f), 180f);
        b.Prop("electricbox", new Vector3(270f, 0f, -2.7f), 0f);
        foreach (var x in new[] { 246f, 292f }) b.Prop("litter", new Vector3(x, 0f, .5f), x * 11f % 360f);
        // Far bank: the army's evacuation point.
        for (float z = -15.2f; z <= -8.8f; z += 1.6f) b.Prop("barbedwire", new Vector3(326.5f, Bank + 1.1f, z), 90f);
        b.PropSolid("militarytruck", new Vector3(338.5f, Bank, 16f), 90f);
        b.Dress(b.Box("HESCO 광장", 336f, 337.9f, Bank, Bank + 2f, -24f, -14f, b.Wood, "Tarp"), "hesco");
        b.Prop("jersey", new Vector3(336.5f, Bank, -6.5f), 0f);
        b.Prop("jersey", new Vector3(336.5f, Bank, 6.5f), 0f);
        b.Prop("warninglight", new Vector3(337.5f, Bank, -5.6f), 90f);
        foreach (var (x, z) in new[] { (327.5f, -17.5f), (328.6f, -18f) }) b.Prop("woodcrate", new Vector3(x, Bank, z), x * 13f % 360f);
        b.Prop("streetlamp", new Vector3(323f, Bank, 10f), 90f);
        b.Prop("streetlamp", new Vector3(323f, Bank, -20f), 90f);
        DenseSubway(b);
    }

    // Chapter 3's density (map dressing stage 5): rubbish along the concourse's walls (clear of the kiosk and the vending
    // machines) and the platform's back wall, debris on the track bed and in the dark tunnel (clear of the fuel can near
    // its end), along the depot's north wall past the office's can, in the service tunnel and round the far bank's square.
    private static void DenseSubway(GreyboxBuilder b)
    {
        const float Bank = 8f, Top = .01f;
        string[] hall = { "litter", "cardboard", "trashbag", "box", "plasticcrate" };
        string[] track = { "brickdebris", "litter", "rubble", "trashbag" };
        string[] tunnel = { "rubble", "brickdebris", "litter", "tire" };
        string[] depot = { "oilbarrel", "plasticcrate", "cardboard", "tire", "pallet" };
        string[] square = { "trashbag", "cardboard", "litter", "tire", "box" };
        b.Clutter(new Vector3(-8f, 0f, -11.4f), new Vector3(8.5f, 0f, -11.4f), 3.5f, hall);
        b.Clutter(new Vector3(13.5f, 0f, -11.4f), new Vector3(29f, 0f, -11.4f), 3.5f, hall);
        b.Clutter(new Vector3(-8f, 0f, 11.4f), new Vector3(4f, 0f, 11.4f), 3.5f, hall);
        b.Clutter(new Vector3(9f, 0f, 11.4f), new Vector3(29f, 0f, 11.4f), 3.5f, hall);
        b.Clutter(new Vector3(31f, 1.1f, 11.4f), new Vector3(89f, 1.1f, 11.4f), 4f, hall);
        b.Clutter(new Vector3(31f, Top, -11.4f), new Vector3(89f, Top, -11.4f), 5f, track);
        b.Clutter(new Vector3(92f, Top, -5.5f), new Vector3(168f, Top, -5.5f), 5f, tunnel);
        b.Clutter(new Vector3(92f, Top, 1.5f), new Vector3(156f, Top, 1.5f), 5f, tunnel);
        b.Clutter(new Vector3(164f, Top, 1.5f), new Vector3(168f, Top, 1.5f), 5f, tunnel);
        b.Clutter(new Vector3(183f, Top, 19.3f), new Vector3(216f, Top, 19.3f), 4f, depot);
        foreach (float z in new[] { 2.5f, -2.5f })
            b.Clutter(new Vector3(220f, Top, z), new Vector3(298f, Top, z), 6f, "litter", "cardboard", "brickdebris");
        foreach (float z in new[] { 23.4f, -23.4f }) b.Clutter(new Vector3(323f, Bank, z), new Vector3(343f, Bank, z), 3.5f, square);
    }

    // Chapter 4's cargo yard, landside road, terminal, apron and the transport's evacuation point in the packs' props
    // (map dressing stage 3). The runway's middle (x 245-315), where the airliner is pushed back, is left clear.
    private static void DressAirport(GreyboxBuilder b)
    {
        const float Floor = 4f;
        // Cargo yard.
        foreach (var (x, z) in new[] { (14f, 9f), (14.7f, 9.6f), (13.6f, 10.2f) }) b.Prop("drum", new Vector3(x, 0f, z), x * 7f % 360f);
        b.PropSolid("palletload", new Vector3(16f, 0f, -6f), 0f);
        foreach (var (x, z) in new[] { (10f, 7.5f), (11.2f, 7.9f) }) b.Prop("woodcrate", new Vector3(x, 0f, z), x * 13f % 360f);
        b.Prop("plasticcrate", new Vector3(2.5f, 0f, 8.6f), 30f);
        b.Prop("tirestack", new Vector3(-4f, 0f, 18f), 0f);
        foreach (var (x, z) in new[] { (15f, -10.5f), (15.6f, -11f) }) b.Prop("gascylinder", new Vector3(x, 0f, z), x * 11f % 360f);
        b.Prop("streetlamp", new Vector3(18.5f, 0f, -20f), 270f);
        b.Prop("streetlamp", new Vector3(18.5f, 0f, 20f), 270f);
        // Landside road and the departures curb.
        b.PropSolid("policecar", new Vector3(30f, 0f, 22f), 10f);
        foreach (var z in new[] { -30f, -27f, -24f }) b.Prop("jersey", new Vector3(34f, 0f, z), 90f);
        foreach (var (x, z) in new[] { (31f, -6f), (32f, 6f), (40f, -2f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 17f % 360f);
        b.Prop("trafficdrum", new Vector3(36f, 0f, 9f));
        foreach (var z in new[] { -30f, -10f, 10f, 30f }) b.Prop("streetlamp", new Vector3(46f, 0f, z), 90f);
        foreach (var (x, z) in new[] { (57f, 6f), (58.5f, -9f) }) b.Prop("suitcase", new Vector3(x, Floor, z), x * 31f % 360f);
        b.Prop("trashbag", new Vector3(59.3f, Floor, 15f), 60f);
        // Terminal: what people dropped.
        foreach (var (x, z) in new[] { (70f, -5f), (84f, 12f), (101f, -18f), (118f, 5f), (150f, -8f), (165f, 14f) })
            b.Prop("litter", new Vector3(x, Floor, z), x * 7f % 360f);
        foreach (var (x, z) in new[] { (61f, 38.5f), (120f, -38.5f), (178f, 38.5f) }) b.Prop("trashbag", new Vector3(x, Floor, z), x * 11f % 360f);
        foreach (var (x, z) in new[] { (77f, 25f), (133f, -14f) }) b.Prop("cardboard", new Vector3(x, Floor, z), x * 13f % 360f);
        b.Prop("plasticbarrier", new Vector3(125f, Floor, -16f), 90f);
        b.Prop("plasticbarrier", new Vector3(125f, Floor, 16f), 90f);
        // Apron.
        foreach (var (x, z) in new[] { (205f, -20f), (206.2f, -19.4f), (226f, 12f), (227f, 12.8f) }) b.Prop("cone", new Vector3(x, 0f, z), x * 19f % 360f);
        foreach (var (x, z) in new[] { (196f, 30f), (196.8f, 30.6f) }) b.Prop("drum", new Vector3(x, 0f, z), x * 23f % 360f);
        b.PropSolid("palletload", new Vector3(234f, 0f, -32f), 0f);
        b.Prop("tirestack", new Vector3(238f, 0f, -48f), 0f);
        b.PropSolid("militarytruck", new Vector3(228f, 0f, -30f), 90f);
        b.Prop("streetlamp", new Vector3(208f, 0f, 38f), 180f);
        b.Prop("streetlamp", new Vector3(236f, 0f, -38f), 0f);
        // The transport's evacuation point: HESCO walls and sandbags round the hold, barbed wire, guard towers, lorries.
        b.Dress(b.Box("HESCO 수송기 남", 318f, 334f, 0f, 2f, -30f, -28.1f, b.Wood, "Tarp"), "hesco");
        b.Dress(b.Box("HESCO 수송기 북", 318f, 334f, 0f, 2f, 28.1f, 30f, b.Wood, "Tarp"), "hesco");
        b.Dress(b.Box("모래주머니 수송기 남", 312f, 313f, 0f, 1.2f, -20f, -12f, b.Wood, "Tarp"), "sandbags");
        b.Dress(b.Box("모래주머니 수송기 북", 312f, 313f, 0f, 1.2f, 12f, 20f, b.Wood, "Tarp"), "sandbags");
        for (float x = 319f; x <= 333f; x += 1.7f) b.Prop("barbedwire", new Vector3(x, 0f, -27f), 0f);
        for (float x = 319f; x <= 333f; x += 1.7f) b.Prop("barbedwire", new Vector3(x, 0f, 27f), 0f);
        b.PropSolid("guardtower", new Vector3(316f, 0f, -34f), 0f);
        b.PropSolid("guardtower", new Vector3(316f, 0f, 34f), 180f);
        b.PropSolid("militarytruck", new Vector3(350f, 0f, -18f), 0f);
        b.PropSolid("wrecktruck", new Vector3(352f, 0f, 20f), 200f);
        b.Prop("streetlamp", new Vector3(312f, 0f, -26f), 90f);
        b.Prop("streetlamp", new Vector3(312f, 0f, 26f), 270f);
        DenseAirport(b);
    }

    // Chapter 4's density (map dressing stage 5): what was dropped along the terminal's long walls (clear of the vending
    // machines, the café and the duty-free shop), outside the landside walls, along the cargo yard's river edge (clear
    // of the pier) and the airfield's walls by the apron.
    private static void DenseAirport(GreyboxBuilder b)
    {
        const float Floor = 4f;
        string[] hall = { "litter", "cardboard", "trashbag", "box" };
        string[] yard = { "tire", "pallet", "oilbarrel", "plasticcrate", "cardboard" };
        string[] apron = { "tire", "oilbarrel", "pallet", "cardboard", "trashbag" };
        b.Clutter(new Vector3(62f, Floor, 39.4f), new Vector3(139f, Floor, 39.4f), 5f, hall);
        b.Clutter(new Vector3(164f, Floor, 39.4f), new Vector3(179f, Floor, 39.4f), 5f, hall);
        b.Clutter(new Vector3(62f, Floor, -39.4f), new Vector3(94f, Floor, -39.4f), 5f, hall);
        b.Clutter(new Vector3(100f, Floor, -39.4f), new Vector3(126f, Floor, -39.4f), 5f, hall);
        b.Clutter(new Vector3(136f, Floor, -39.4f), new Vector3(179f, Floor, -39.4f), 5f, hall);
        foreach (float z in new[] { 50f, -50f })
            b.Clutter(new Vector3(59.4f, 0f, z - 8f), new Vector3(59.4f, 0f, z + 8f), 4f, "trashbag", "litter", "cardboard", "tire");
        b.Clutter(new Vector3(-20.3f, 0f, -56f), new Vector3(-20.3f, 0f, -9f), 5f, yard);
        b.Clutter(new Vector3(-20.3f, 0f, 9f), new Vector3(-20.3f, 0f, 56f), 5f, yard);
        foreach (float z in new[] { 58.3f, -58.3f }) b.Clutter(new Vector3(184f, 0f, z), new Vector3(356f, 0f, z), 6f, apron);
    }

    // A stopped car, 4.5 m long across x, at <paramref name="y"/> (the road it stands on), dressed as one of
    // the abandoned cars (Tables/MapArt car, picked by place).
    private static void Car(GreyboxBuilder b, float x, float y, float z)
        => b.Dress(b.Box("차", x - 2.25f, x + 2.25f, y, y + 1.5f, z - .95f, z + .95f, b.Metal, "Metal"), "car");

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
